namespace AsterismApp;

public sealed record SemanticIndexResult(
    IReadOnlyDictionary<string, List<SemanticSuggestion>> SuggestionsByPath,
    IReadOnlyDictionary<string, List<string>> GraphLinks,
    int EmbeddedChunkCount,
    int ReusedChunkCount);

public sealed class SemanticLinkService : IDisposable
{
    private const double MinimumSimilarity = .84;
    private const int SuggestionsPerNote = 4;
    private const int GraphConnectionsPerNote = 2;

    private readonly LocalEmbeddingModel _model;
    private readonly SemanticIndexStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SemanticLinkService(LocalEmbeddingModel? model = null, SemanticIndexStore? store = null)
    {
        _model = model ?? new LocalEmbeddingModel();
        _store = store ?? new SemanticIndexStore();
    }

    public async Task<SemanticIndexResult> BuildAsync(
        string workspace,
        IReadOnlyList<NoteInfo> notes,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Build(workspace, notes, cancellationToken), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SemanticIndexResult Build(string workspace, IReadOnlyList<NoteInfo> notes, CancellationToken cancellationToken)
    {
        workspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
        var cached = _store.Load(workspace);
        var current = new List<CachedSemanticEmbedding>();
        var pending = new List<(NoteInfo Note, SemanticChunk Chunk)>();
        var reusedCount = 0;

        foreach (var note in notes)
        {
            foreach (var chunk in SemanticTextChunker.Split(note))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = SemanticIndexStore.CacheKey(note.Path, chunk.Key);
                if (cached.TryGetValue(key, out var embedding) && embedding.ContentHash == chunk.ContentHash)
                {
                    current.Add(embedding with { NoteTitle = note.Title });
                    reusedCount++;
                }
                else
                {
                    pending.Add((note, chunk));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        pending.Sort((left, right) => left.Chunk.Text.Length.CompareTo(right.Chunk.Text.Length));
        var newVectors = _model.EmbedMany(pending.Select(item => item.Chunk.Text).ToList());
        for (var index = 0; index < pending.Count; index++)
        {
            var item = pending[index];
            current.Add(new CachedSemanticEmbedding(
                item.Note.Path,
                item.Note.Title,
                item.Chunk.Key,
                item.Chunk.ContentHash,
                newVectors[index]));
        }
        cancellationToken.ThrowIfCancellationRequested();
        _store.Synchronize(workspace, current);

        var distinctNotes = notes
            .GroupBy(note => note.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var vectorsByPath = current
            .GroupBy(embedding => embedding.NotePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Vector).ToList(), StringComparer.OrdinalIgnoreCase);
        var scored = distinctNotes.ToDictionary(
            note => note.Path,
            _ => new List<(NoteInfo Note, double Score)>(),
            StringComparer.OrdinalIgnoreCase);

        for (var left = 0; left < distinctNotes.Count; left++)
        {
            if (!vectorsByPath.TryGetValue(distinctNotes[left].Path, out var leftVectors)) continue;
            for (var right = left + 1; right < distinctNotes.Count; right++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!vectorsByPath.TryGetValue(distinctNotes[right].Path, out var rightVectors)) continue;
                var score = MaximumSimilarity(leftVectors, rightVectors);
                if (score < MinimumSimilarity) continue;
                scored[distinctNotes[left].Path].Add((distinctNotes[right], score));
                scored[distinctNotes[right].Path].Add((distinctNotes[left], score));
            }
        }

        var suggestions = scored.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderByDescending(item => item.Score)
                .Take(SuggestionsPerNote)
                .Select(item => new SemanticSuggestion(item.Note, item.Score))
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
        var graphLinks = distinctNotes.ToDictionary(
            note => note.Title,
            note => suggestions[note.Path]
                .Take(GraphConnectionsPerNote)
                .Select(suggestion => suggestion.Note.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
        return new SemanticIndexResult(suggestions, graphLinks, pending.Count, reusedCount);
    }

    private static double MaximumSimilarity(IReadOnlyList<float[]> left, IReadOnlyList<float[]> right)
    {
        var maximum = double.MinValue;
        foreach (var leftVector in left)
            foreach (var rightVector in right)
            {
                var length = Math.Min(leftVector.Length, rightVector.Length);
                var score = 0d;
                for (var index = 0; index < length; index++) score += leftVector[index] * rightVector[index];
                maximum = Math.Max(maximum, score);
            }
        return maximum;
    }

    public void Dispose()
    {
        _model.Dispose();
        _gate.Dispose();
    }
}
