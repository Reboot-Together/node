using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace AsterismApp;

public sealed class LocalEmbeddingModel : IDisposable
{
    public const string Version = "multilingual-e5-small-qint8-v1";
    private const int MaximumTokens = 512;
    private const int EmbeddingDimensions = 384;
    private const long ModelLength = 118308811;
    private const string ModelSha256 = "739c8f25bbe6d8a6001cd2f048701da9879140cc67d4e9327716111e869dd717";

    private readonly object _gate = new();
    private readonly string _assetsPath;
    private readonly string _modelDirectory;
    private InferenceSession? _session;
    private SentencePieceTokenizer? _tokenizer;

    public LocalEmbeddingModel(string? assetsPath = null, string? modelDirectory = null)
    {
        _assetsPath = assetsPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "SemanticModel");
        _modelDirectory = modelDirectory ?? ApplicationDataPaths.ModelDirectory;
    }

    public float[] Embed(string text)
        => EmbedMany([text], 1)[0];

    public IReadOnlyList<float[]> EmbedMany(IReadOnlyList<string> texts, int batchSize = 8)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        lock (_gate)
        {
            EnsureInitialized();
            var result = new List<float[]>(texts.Count);
            for (var offset = 0; offset < texts.Count; offset += batchSize)
            {
                var count = Math.Min(batchSize, texts.Count - offset);
                var tokenized = Enumerable.Range(offset, count).Select(index => Tokenize(texts[index])).ToList();
                var sequenceLength = tokenized.Max(tokens => tokens.Count);
                var inputIds = Enumerable.Repeat(1L, count * sequenceLength).ToArray(); // XLM-R <pad>
                var attentionMask = new long[inputIds.Length];
                var tokenTypeIds = new long[inputIds.Length];
                for (var batchIndex = 0; batchIndex < count; batchIndex++)
                    for (var tokenIndex = 0; tokenIndex < tokenized[batchIndex].Count; tokenIndex++)
                    {
                        var position = batchIndex * sequenceLength + tokenIndex;
                        inputIds[position] = tokenized[batchIndex][tokenIndex];
                        attentionMask[position] = 1;
                    }

                var dimensions = new long[] { count, sequenceLength };
                using var inputIdsValue = OrtValue.CreateTensorValueFromMemory(inputIds, dimensions);
                using var attentionMaskValue = OrtValue.CreateTensorValueFromMemory(attentionMask, dimensions);
                using var tokenTypeIdsValue = OrtValue.CreateTensorValueFromMemory(tokenTypeIds, dimensions);
                var inputs = new Dictionary<string, OrtValue>
                {
                    ["input_ids"] = inputIdsValue,
                    ["attention_mask"] = attentionMaskValue,
                    ["token_type_ids"] = tokenTypeIdsValue
                };
                using var runOptions = new RunOptions();
                using var outputs = _session!.Run(runOptions, inputs, _session.OutputNames);
                var hiddenState = outputs[0].GetTensorDataAsSpan<float>();

                for (var batchIndex = 0; batchIndex < count; batchIndex++)
                {
                    var embedding = new float[EmbeddingDimensions];
                    var tokenCount = tokenized[batchIndex].Count;
                    for (var tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
                    {
                        var start = (batchIndex * sequenceLength + tokenIndex) * EmbeddingDimensions;
                        for (var dimension = 0; dimension < EmbeddingDimensions; dimension++)
                            embedding[dimension] += hiddenState[start + dimension];
                    }

                    var magnitude = 0d;
                    for (var dimension = 0; dimension < EmbeddingDimensions; dimension++)
                    {
                        embedding[dimension] /= tokenCount;
                        magnitude += embedding[dimension] * embedding[dimension];
                    }
                    magnitude = Math.Sqrt(magnitude);
                    if (magnitude > 0)
                        for (var dimension = 0; dimension < EmbeddingDimensions; dimension++)
                            embedding[dimension] = (float)(embedding[dimension] / magnitude);
                    result.Add(embedding);
                }
            }
            return result;
        }
    }

    private IReadOnlyList<int> Tokenize(string text)
    {
        EnsureInitialized();
        var sentencePieceIds = _tokenizer!.EncodeToIds(
            "query: " + text,
            addBeginningOfSentence: false,
            addEndOfSentence: false,
            maxTokenCount: MaximumTokens - 2,
            out _,
            out _,
            considerPreTokenization: true,
            considerNormalization: true);
        var modelIds = new int[sentencePieceIds.Count + 2];
        modelIds[0] = 0; // XLM-R <s>
        // XLM-R reserves IDs 0-3 before the SentencePiece vocabulary; unknown is ID 3.
        for (var index = 0; index < sentencePieceIds.Count; index++)
            modelIds[index + 1] = sentencePieceIds[index] == 0 ? 3 : sentencePieceIds[index] + 1;
        modelIds[^1] = 2; // XLM-R </s>
        return modelIds;
    }

    private void EnsureInitialized()
    {
        if (_session is not null) return;
        var tokenizerPath = Path.Combine(_assetsPath, "sentencepiece.bpe.model");
        if (!File.Exists(tokenizerPath)) throw new FileNotFoundException("로컬 AI 토크나이저를 찾을 수 없습니다.", tokenizerPath);

        using (var tokenizerStream = File.OpenRead(tokenizerPath))
            _tokenizer = SentencePieceTokenizer.Create(tokenizerStream, addBeginningOfSentence: false, addEndOfSentence: false);

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2)
        };
        _session = new InferenceSession(EnsureAssembledModel(_assetsPath, _modelDirectory), options);
    }

    private static string EnsureAssembledModel(string assets, string modelDirectory)
    {
        Directory.CreateDirectory(modelDirectory);
        var modelPath = Path.Combine(modelDirectory, "multilingual-e5-small-qint8.onnx");
        if (File.Exists(modelPath) && new FileInfo(modelPath).Length == ModelLength) return modelPath;

        var temporaryPath = modelPath + ".tmp";
        using (var output = File.Create(temporaryPath))
        {
            foreach (var partName in new[] { "model.part1", "model.part2" })
            {
                var partPath = Path.Combine(assets, partName);
                if (!File.Exists(partPath)) throw new FileNotFoundException("로컬 AI 모델 조각을 찾을 수 없습니다.", partPath);
                using var input = File.OpenRead(partPath);
                input.CopyTo(output);
            }
        }
        bool modelIsValid;
        using (var verificationStream = File.OpenRead(temporaryPath))
            modelIsValid = verificationStream.Length == ModelLength
                && Convert.ToHexString(SHA256.HashData(verificationStream)).Equals(ModelSha256, StringComparison.OrdinalIgnoreCase);
        if (!modelIsValid)
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException("로컬 AI 모델 무결성 검사에 실패했습니다.");
        }
        File.Move(temporaryPath, modelPath, true);
        return modelPath;
    }

    public void Dispose()
        => Unload();

    public void Unload()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
        }
    }
}
