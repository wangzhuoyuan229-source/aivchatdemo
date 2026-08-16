using System.Text.Json;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace ChatApp.Infrastructure.VectorStore;

/// <summary>
/// SQLite-backed vector store. Embeddings are persisted as BLOBs; search loads
/// the requested scope into an in-memory cache (invalidated on writes) and
/// computes cosine similarity. Suitable for ~10k-vector desktop workloads (<1s).
/// </summary>
public class SqliteVectorStore : IVectorStore
{
    private readonly string _connStr;
    private readonly ILogger<SqliteVectorStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, List<VectorRecord>> _cache = new();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public SqliteVectorStore(ILogger<SqliteVectorStore> logger)
        : this(logger, $"Data Source={AppPaths.DbPath}")
    {
    }

    internal SqliteVectorStore(ILogger<SqliteVectorStore> logger, string connectionString)
    {
        _connStr = connectionString;
        _logger = logger;
        EnsureTable();
    }

    private void EnsureTable()
    {
        try
        {
            using var c = new SqliteConnection(_connStr);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Vectors (
                Id TEXT PRIMARY KEY,
                Scope TEXT NOT NULL,
                Content TEXT NOT NULL,
                Embedding BLOB NOT NULL,
                Metadata TEXT NOT NULL DEFAULT '{}'
            );
            CREATE INDEX IF NOT EXISTS IX_Vectors_Scope ON Vectors(Scope);
            """;
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Vectors table.");
            throw;
        }
    }

    public Task UpsertAsync(VectorRecord record, CancellationToken ct = default) =>
        UpsertBatchAsync(new[] { record }, ct);

    public async Task UpsertBatchAsync(IEnumerable<VectorRecord> records, CancellationToken ct = default)
    {
        var batch = records.ToList();
        if (batch.Count == 0) return;

        await _gate.WaitAsync(ct);
        try
        {
            await using var connection = new SqliteConnection(_connStr);
            await connection.OpenAsync(ct);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "INSERT OR REPLACE INTO Vectors (Id,Scope,Content,Embedding,Metadata) VALUES (@id,@scope,@content,@emb,@meta);";
            var id = command.Parameters.Add("@id", SqliteType.Text);
            var scope = command.Parameters.Add("@scope", SqliteType.Text);
            var content = command.Parameters.Add("@content", SqliteType.Text);
            var embedding = command.Parameters.Add("@emb", SqliteType.Blob);
            var metadata = command.Parameters.Add("@meta", SqliteType.Text);

            foreach (var record in batch)
            {
                ct.ThrowIfCancellationRequested();
                id.Value = record.Id;
                scope.Value = record.Scope;
                content.Value = record.Content;
                embedding.Value = ToBytes(record.Embedding);
                metadata.Value = JsonSerializer.Serialize(
                    record.Metadata ?? new Dictionary<string, string>(), JsonOpts);
                await command.ExecuteNonQueryAsync(ct);
            }
            transaction.Commit();

            foreach (var record in batch)
            {
                if (!_cache.TryGetValue(record.Scope, out var list))
                {
                    list = new List<VectorRecord>();
                    _cache[record.Scope] = list;
                }
                var index = list.FindIndex(item => item.Id == record.Id);
                if (index >= 0) list[index] = record; else list.Add(record);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryVector,
        string scope,
        int topK,
        double minScore = 0,
        IReadOnlySet<string>? allowedIds = null,
        CancellationToken ct = default)
    {
        if (topK <= 0) return Array.Empty<VectorSearchHit>();
        var list = await GetScopeAsync(scope, ct);
        return Rank(list, queryVector, topK, minScore, allowedIds);
    }

    internal static IReadOnlyList<VectorSearchHit> Rank(
        IEnumerable<VectorRecord> records,
        float[] queryVector,
        int topK,
        double minScore,
        IReadOnlySet<string>? allowedIds)
    {
        if (topK <= 0) return Array.Empty<VectorSearchHit>();
        var hits = new List<VectorSearchHit>();
        foreach (var r in records)
        {
            if (allowedIds is not null && !allowedIds.Contains(r.Id)) continue;
            var score = Cosine(queryVector, r.Embedding);
            if (score > 0 && score >= minScore)
                hits.Add(new VectorSearchHit { Record = r, Score = score });
        }
        return hits.OrderByDescending(h => h.Score).Take(topK).ToList();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connStr);
            await c.OpenAsync(ct);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Scope FROM Vectors WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            var scopeObj = await cmd.ExecuteScalarAsync(ct);
            if (scopeObj is null) return;
            cmd.Parameters.Clear();
            cmd.CommandText = "DELETE FROM Vectors WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync(ct);
            if (scopeObj is string scope)
                _cache.Remove(scope);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteByScopeAsync(string scope, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var c = new SqliteConnection(_connStr);
            await c.OpenAsync(ct);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM Vectors WHERE Scope=@scope;";
            cmd.Parameters.AddWithValue("@scope", scope);
            await cmd.ExecuteNonQueryAsync(ct);
            _cache.Remove(scope);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<VectorRecord>> GetScopeAsync(string scope, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(scope, out var cached)) return cached;

            var list = new List<VectorRecord>();
            await using var c = new SqliteConnection(_connStr);
            await c.OpenAsync(ct);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Id, Content, Embedding, Metadata FROM Vectors WHERE Scope=@s;";
            cmd.Parameters.AddWithValue("@s", scope);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var meta = r.GetString(3);
                list.Add(new VectorRecord
                {
                    Id = r.GetString(0),
                    Scope = scope,
                    Content = r.GetString(1),
                    Embedding = ToFloats(r.GetFieldValue<byte[]>(2)),
                    Metadata = string.IsNullOrWhiteSpace(meta) ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(meta, JsonOpts) ?? new()
                });
            }
            _cache[scope] = list;
            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] ToBytes(float[] v)
    {
        var bytes = new byte[v.Length * sizeof(float)];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ToFloats(byte[] b)
    {
        if (b.Length == 0) return Array.Empty<float>();
        var v = new float[b.Length / sizeof(float)];
        Buffer.BlockCopy(b, 0, v, 0, b.Length);
        return v;
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
