using Microsoft.Data.Sqlite;

namespace AsterismApp;

public sealed record CachedSemanticEmbedding(
    string NotePath,
    string NoteTitle,
    string ChunkKey,
    string ContentHash,
    float[] Vector);

public sealed class SemanticIndexStore
{
    private readonly string _connectionString;

    public SemanticIndexStore(string? databasePath = null)
    {
        databasePath ??= ApplicationDataPaths.SemanticIndexFile;
        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("인덱스 파일 경로가 올바르지 않습니다.", nameof(databasePath));
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        EnsureSchema();
    }

    public IReadOnlyDictionary<string, CachedSemanticEmbedding> Load(string workspace)
    {
        var result = new Dictionary<string, CachedSemanticEmbedding>(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT note_path, note_title, chunk_key, content_hash, vector
            FROM embeddings
            WHERE workspace = $workspace AND model_version = $model;
            """;
        command.Parameters.AddWithValue("$workspace", workspace);
        command.Parameters.AddWithValue("$model", LocalEmbeddingModel.Version);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            var chunkKey = reader.GetString(2);
            var bytes = (byte[])reader[4];
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            result[CacheKey(path, chunkKey)] = new CachedSemanticEmbedding(
                path,
                reader.GetString(1),
                chunkKey,
                reader.GetString(3),
                vector);
        }
        return result;
    }

    public void Synchronize(string workspace, IReadOnlyCollection<CachedSemanticEmbedding> embeddings)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var clearOldModels = connection.CreateCommand())
        {
            clearOldModels.Transaction = transaction;
            clearOldModels.CommandText = "DELETE FROM embeddings WHERE workspace = $workspace AND model_version <> $model;";
            clearOldModels.Parameters.AddWithValue("$workspace", workspace);
            clearOldModels.Parameters.AddWithValue("$model", LocalEmbeddingModel.Version);
            clearOldModels.ExecuteNonQuery();
        }
        using (var deleteCurrent = connection.CreateCommand())
        {
            deleteCurrent.Transaction = transaction;
            deleteCurrent.CommandText = "DELETE FROM embeddings WHERE workspace = $workspace AND model_version = $model;";
            deleteCurrent.Parameters.AddWithValue("$workspace", workspace);
            deleteCurrent.Parameters.AddWithValue("$model", LocalEmbeddingModel.Version);
            deleteCurrent.ExecuteNonQuery();
        }
        foreach (var embedding in embeddings)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO embeddings(workspace, note_path, note_title, chunk_key, content_hash, model_version, vector)
                VALUES($workspace, $path, $title, $chunk, $hash, $model, $vector);
                """;
            var bytes = new byte[embedding.Vector.Length * sizeof(float)];
            Buffer.BlockCopy(embedding.Vector, 0, bytes, 0, bytes.Length);
            command.Parameters.AddWithValue("$workspace", workspace);
            command.Parameters.AddWithValue("$path", embedding.NotePath);
            command.Parameters.AddWithValue("$title", embedding.NoteTitle);
            command.Parameters.AddWithValue("$chunk", embedding.ChunkKey);
            command.Parameters.AddWithValue("$hash", embedding.ContentHash);
            command.Parameters.AddWithValue("$model", LocalEmbeddingModel.Version);
            command.Parameters.AddWithValue("$vector", bytes);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static string CacheKey(string path, string chunkKey) => $"{path}\u001f{chunkKey}";

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS embeddings(
                workspace TEXT NOT NULL,
                note_path TEXT NOT NULL,
                note_title TEXT NOT NULL,
                chunk_key TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                model_version TEXT NOT NULL,
                vector BLOB NOT NULL,
                PRIMARY KEY(workspace, note_path, chunk_key)
            );
            CREATE INDEX IF NOT EXISTS ix_embeddings_workspace ON embeddings(workspace, model_version);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
