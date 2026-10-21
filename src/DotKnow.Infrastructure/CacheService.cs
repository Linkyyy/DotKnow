using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DotKnow.Core;

namespace DotKnow.Infrastructure;

public interface IDataProtector
{
    byte[] Protect(byte[] value);
    byte[] Unprotect(byte[] value);
}

public sealed class CacheService(Func<AppSettings> settings, string databasePath, IDataProtector protector,
    TimeProvider? clock = null) : ITranslationCache, IDisposable
{
    private sealed record Entry(string Key, TranslationResult Result, DateTimeOffset Expires);
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, LinkedListNode<Entry>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> lru = new();
    private SqliteConnection? database;

    private async Task<SqliteConnection> DatabaseAsync(CancellationToken token)
    {
        if (database != null) return database;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await database.OpenAsync(token);
        using var command = database.CreateCommand();
        command.CommandText = "PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS translations (key TEXT PRIMARY KEY, value BLOB NOT NULL, expires INTEGER NOT NULL, used INTEGER NOT NULL);";
        await command.ExecuteNonQueryAsync(token);
        return database;
    }

    public async Task<TranslationResult?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var config = settings().Cache;
            if (config.MemoryEnabled && entries.TryGetValue(key, out var node))
            {
                if (node.Value.Expires > time.GetUtcNow())
                { lru.Remove(node); lru.AddFirst(node); return node.Value.Result; }
                lru.Remove(node); entries.Remove(key);
            }
            if (!config.DiskEnabled) return null;
            using var command = (await DatabaseAsync(cancellationToken)).CreateCommand();
            command.CommandText = "DELETE FROM translations WHERE expires <= $now; SELECT value FROM translations WHERE key=$key;";
            command.Parameters.AddWithValue("$now", time.GetUtcNow().ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$key", key);
            var bytes = await command.ExecuteScalarAsync(cancellationToken) as byte[];
            if (bytes == null) return null;
            TranslationResult? result;
            try { result = JsonSerializer.Deserialize<TranslationResult>(protector.Unprotect(bytes)); }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException) { return null; }
            if (result != null && config.MemoryEnabled) PutMemory(key, result, config);
            return result;
        }
        finally { gate.Release(); }
    }

    public async Task PutAsync(string key, TranslationResult result, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var config = settings().Cache;
            if (config.MemoryEnabled) PutMemory(key, result, config);
            if (!config.DiskEnabled) return;
            using var command = (await DatabaseAsync(cancellationToken)).CreateCommand();
            command.CommandText = """
                DELETE FROM translations WHERE expires <= $now;
                INSERT INTO translations(key,value,expires,used) VALUES($key,$value,$expires,$now)
                    ON CONFLICT(key) DO UPDATE SET value=$value,expires=$expires,used=$now;
                DELETE FROM translations WHERE key NOT IN (SELECT key FROM translations ORDER BY used DESC, rowid DESC LIMIT $limit);
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", protector.Protect(JsonSerializer.SerializeToUtf8Bytes(result)));
            command.Parameters.AddWithValue("$expires", time.GetUtcNow().AddDays(config.DiskTTLDays).ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$now", time.GetUtcNow().ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$limit", config.DiskMaxEntries);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    private void PutMemory(string key, TranslationResult result, CacheSettings config)
    {
        if (entries.Remove(key, out var old)) lru.Remove(old);
        entries[key] = lru.AddFirst(new Entry(key, result, time.GetUtcNow().AddMinutes(config.MemoryTTLMinutes)));
        while (entries.Count > config.MemoryMaxEntries)
        { var last = lru.Last!; entries.Remove(last.Value.Key); lru.RemoveLast(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            entries.Clear(); lru.Clear();
            if (database == null && !File.Exists(databasePath)) return;
            using var command = (await DatabaseAsync(cancellationToken)).CreateCommand();
            command.CommandText = "DELETE FROM translations; VACUUM;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }
    public void Dispose() { entries.Clear(); lru.Clear(); database?.Dispose(); }
}
