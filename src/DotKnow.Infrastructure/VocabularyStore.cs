using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DotKnow.Core;

namespace DotKnow.Infrastructure;

/// <summary>公开内置词库只读加载；个人增量在独立 SQLite 中逐条 DPAPI 加密，无 TTL 或自动淘汰。</summary>
public sealed class VocabularyStore(string directory, IDataProtector protector, bool includeBuiltin = true) : ITranslationMemory, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object initializationGate = new();
    private Task? initialization;
    private SqliteConnection? database;
    private Dictionary<string, VocabularyEntry> builtin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VocabularyEntry> personal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VocabularyEntry> entries = new(StringComparer.Ordinal);
    private bool builtinEnabled = true;
    public Task InitializeAsync(CancellationToken token = default)
    {
        Task task;
        lock (initializationGate) task = initialization ??= Task.Run(InitializeCoreAsync);
        return task.WaitAsync(token);
    }
    private async Task InitializeCoreAsync()
    {
        if (includeBuiltin)
        {
            using var resource = VocabularyPackage.OpenBundled();
            builtin = VocabularyPackage.Read(resource, true, default).Entries.ToDictionary(x => x.Key, x => x.Value with { Origin = "builtin" }, StringComparer.Ordinal);
        }
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "vocabulary.db"), Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync();
            using var setup = connection.CreateCommand();
            setup.CommandText = "PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS vocabulary(key TEXT PRIMARY KEY, value BLOB NOT NULL); CREATE TABLE IF NOT EXISTS vocabulary_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);";
            await setup.ExecuteNonQueryAsync();
            using var metadata = connection.CreateCommand(); metadata.CommandText = "SELECT value FROM vocabulary_metadata WHERE key='builtin_enabled'";
            builtinEnabled = (await metadata.ExecuteScalarAsync() as string) != "0";
            using var command = connection.CreateCommand(); command.CommandText = "SELECT key,value FROM vocabulary";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var entry = JsonSerializer.Deserialize<VocabularyEntry>(protector.Unprotect((byte[])reader[1]), VocabularyPackage.Json);
                if (entry == null || !entry.IsValid || entry.Key != reader.GetString(0) || personal.Count >= VocabularyPackage.MaxEntries)
                    throw new InvalidDataException("离线题库记录损坏，原文件已保留。");
                personal.Add(entry.Key, entry);
            }
            database = connection; Rebuild();
        }
        catch { connection.Dispose(); throw; }
    }
    private void Rebuild()
    {
        entries.Clear();
        if (builtinEnabled) foreach (var pair in builtin) entries.Add(pair.Key, pair.Value);
        foreach (var pair in personal)
            if (pair.Value.IsDeleted) entries.Remove(pair.Key); else entries[pair.Key] = pair.Value;
    }
    public async Task<TranslationResult?> FindAsync(TranslationRequest request, CancellationToken token)
    {
        if (!request.IsTranslation) return null;
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var query = VocabularyEntry.From(request, "");
            if (request.SourceLanguage != "auto")
                return entries.TryGetValue(query.Key, out var item) && !item.IsAmbiguous ? new(item.TranslatedText) : null;
            entries.TryGetValue(query.Key, out var automatic);
            if (automatic is { Origin: "manual", IsAmbiguous: false }) return new(automatic.TranslatedText);
            var candidates = new List<VocabularyEntry>();
            foreach (var source in new[] { "zh", "en", "ja" }.Where(x => x != request.TargetLanguage))
            {
                var key = (query with { SourceLanguage = source }).Key;
                if (personal.TryGetValue(key, out var deleted) && deleted.IsDeleted) return null;
                if (entries.TryGetValue(key, out var item))
                { if (item.IsAmbiguous) return null; candidates.Add(item); }
            }
            var answers = candidates.Select(x => x.TranslatedText).Distinct(StringComparer.Ordinal).ToArray();
            if (answers.Length > 1) return null;
            if (answers.Length == 1) return new(answers[0]);
            return automatic != null && !automatic.IsAmbiguous ? new(automatic.TranslatedText) : null;
        }
        finally { gate.Release(); }
    }
    public async Task LearnAsync(TranslationRequest request, TranslationResult result, CancellationToken token)
    {
        if (!request.IsTranslation) return;
        var entry = VocabularyEntry.From(request, result.Text) with { Origin = "learned" };
        if (!entry.IsValid) return;
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // 自动积累不覆盖导入、人工修改、歧义隔离或用户已删除的查询。
            if (entries.ContainsKey(entry.Key) || personal.ContainsKey(entry.Key)) return;
            // 在线自动源语言没有可靠的检测结果，不能用它绕过已有词义冲突或遮蔽明确方向的译文。
            if (entry.SourceLanguage == "auto" && new[] { "zh", "en", "ja" }.Any(source =>
                entries.ContainsKey((entry with { SourceLanguage = source }).Key) || personal.ContainsKey((entry with { SourceLanguage = source }).Key))) return;
            CheckCapacity(1);
            await SaveAsync(new Dictionary<string, VocabularyEntry?> { [entry.Key] = entry }, null, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task<VocabularyImportReport> ImportAsync(string file, CancellationToken token = default)
    {
        if (new FileInfo(file).Length > 64 * 1024 * 1024) throw new InvalidDataException("词库包超过 64 MB。");
        using var stream = File.OpenRead(file);
        var package = VocabularyPackage.Read(stream, Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase), token);
        if (package.Entries.Count == 0) throw new InvalidDataException("词库没有有效条目，未导入。");
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var added = 0; var duplicates = package.Duplicates; var conflicts = package.Conflicts; var preserved = 0;
            var changes = new Dictionary<string, VocabularyEntry?>();
            foreach (var pair in package.Entries)
            {
                token.ThrowIfCancellationRequested(); var incoming = pair.Value;
                if (entries.TryGetValue(pair.Key, out var previous))
                {
                    if (previous.Origin == "manual") { preserved++; continue; }
                    if (!previous.IsAmbiguous && !incoming.IsAmbiguous && previous.TranslatedText == incoming.TranslatedText)
                    { duplicates++; continue; }
                    incoming = incoming with { IsAmbiguous = true }; conflicts++;
                }
                else if (incoming.IsAmbiguous) conflicts++; else added++;
                changes[pair.Key] = incoming;
            }
            CheckCapacity(changes.Keys.Count(x => !entries.ContainsKey(x) && !personal.ContainsKey(x)));
            await SaveAsync(changes, null, token).ConfigureAwait(false);
            return new(added, duplicates, conflicts, package.Invalid, preserved);
        }
        finally { gate.Release(); }
    }
    private void CheckCapacity(int additions)
    {
        if (entries.Count + personal.Keys.Count(x => !entries.ContainsKey(x)) + additions > VocabularyPackage.MaxEntries)
            throw new InvalidDataException("题库已达 250000 条上限，请先整理后重试；未自动删除内容。");
    }
    public async Task<VocabularyStatistics> StatisticsAsync(CancellationToken token = default)
    {
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try { return new(entries.Values.Count(x => !x.IsAmbiguous), entries.Values.Count(x => x.IsAmbiguous),
            entries.Values.Count(x => x.Origin == "learned"), entries.Values.Count(x => x.Origin == "manual"), entries.Values.Count(x => x.Origin == "imported")); }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<VocabularyEntry>> SearchAsync(string query, CancellationToken token = default)
    {
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try { return entries.Values.Where(x => x.SourceText.Contains(query, StringComparison.OrdinalIgnoreCase) || x.TranslatedText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SourceText, StringComparer.Ordinal).ThenBy(x => x.TargetLanguage).Take(100).ToArray(); }
        finally { gate.Release(); }
    }
    public async Task EditAsync(VocabularyEntry expected, string translation, CancellationToken token = default)
    {
        var edited = expected with { TranslatedText = translation, IsAmbiguous = false, IsDeleted = false, Origin = "manual", UpdatedAt = DateTimeOffset.UtcNow };
        if (!edited.IsValid) throw new ArgumentException("译文不能为空或超过 40000 字符。");
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!entries.TryGetValue(expected.Key, out var current) || current != expected) throw new InvalidOperationException("条目已变化，请刷新后重试。");
            await SaveAsync(new Dictionary<string, VocabularyEntry?> { [expected.Key] = edited }, null, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(VocabularyEntry expected, CancellationToken token = default)
    {
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!entries.TryGetValue(expected.Key, out var current) || current != expected) throw new InvalidOperationException("条目已变化，请刷新后重试。");
            await SaveAsync(new Dictionary<string, VocabularyEntry?> { [expected.Key] = expected with { IsDeleted = true } }, null, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task ClearAsync(CancellationToken token = default)
    {
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try { await SaveAsync(personal.Keys.ToDictionary(x => x, _ => (VocabularyEntry?)null), false, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async Task RestoreBuiltinAsync(CancellationToken token = default)
    {
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        try { await SaveAsync(personal.Where(x => x.Value.IsDeleted && builtin.ContainsKey(x.Key)).ToDictionary(x => x.Key, _ => (VocabularyEntry?)null), true, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    public async Task ExportAsync(string file, CancellationToken token = default)
    {
        if (!Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("题库导出文件必须为 ZIP。");
        await InitializeAsync(token).ConfigureAwait(false); await gate.WaitAsync(token).ConfigureAwait(false);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                Write("manifest.json", JsonSerializer.Serialize(new { formatVersion = "1.0", packageId = "dotknow-personal-vocabulary", packageVersion = "1.0", files = new { entries = "entries.jsonl", ambiguous = "ambiguous.jsonl" }, note = "用户导出的题库为明文；auto 表示仅适用于自动源语言查询的在线积累。" }));
                foreach (var ambiguous in new[] { false, true })
                {
                    using var writer = new StreamWriter(archive.CreateEntry(ambiguous ? "ambiguous.jsonl" : "entries.jsonl").Open(), new UTF8Encoding(false));
                    foreach (var entry in entries.Values.Where(x => x.IsAmbiguous == ambiguous))
                    { token.ThrowIfCancellationRequested(); writer.WriteLine(JsonSerializer.Serialize(entry, VocabularyPackage.Json)); }
                }
                // 导出含原始内置数据时附上原作者声明，个人新增与修改并不声称经原作者审核。
                using var original = VocabularyPackage.OpenBundled(); using var attribution = new ZipArchive(original, ZipArchiveMode.Read);
                foreach (var item in attribution.Entries.Where(x => x.FullName.StartsWith("licenses/", StringComparison.Ordinal)))
                { using var source = item.Open(); using var destination = archive.CreateEntry(item.FullName).Open(); source.CopyTo(destination); }
                Write("README.zh-CN.md", "DotKnow 本地题库导出。文件包含明文原文、译文与个人积累，请妥善保管。内置词库由用户提供，署名与许可证见 licenses/。个人修改及在线结果未作人工审核。歧义条目不会自动命中。\n");
                void Write(string name, string value) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(value); }
            }
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                var hashes = new List<string>();
                foreach (var item in archive.Entries)
                { token.ThrowIfCancellationRequested(); using var source = item.Open(); hashes.Add(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant() + "  " + item.FullName); }
                using var writer = new StreamWriter(archive.CreateEntry("checksums.sha256").Open(), new UTF8Encoding(false));
                foreach (var line in hashes) writer.WriteLine(line);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
    private async Task SaveAsync(Dictionary<string, VocabularyEntry?> changes, bool? enabled, CancellationToken token)
    {
        using var transaction = database!.BeginTransaction();
        using var upsert = database.CreateCommand(); upsert.Transaction = transaction;
        upsert.CommandText = "INSERT INTO vocabulary(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value";
        upsert.Parameters.Add("$key", SqliteType.Text); upsert.Parameters.Add("$value", SqliteType.Blob);
        foreach (var pair in changes)
        {
            token.ThrowIfCancellationRequested();
            if (pair.Value == null)
            {
                using var delete = database.CreateCommand(); delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM vocabulary WHERE key=$key"; delete.Parameters.AddWithValue("$key", pair.Key);
                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            else
            {
                upsert.Parameters["$key"].Value = pair.Key;
                upsert.Parameters["$value"].Value = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(pair.Value, VocabularyPackage.Json));
                await upsert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        if (enabled != null)
        {
            using var metadata = database.CreateCommand(); metadata.Transaction = transaction;
            metadata.CommandText = "INSERT INTO vocabulary_metadata(key,value) VALUES('builtin_enabled',$value) ON CONFLICT(key) DO UPDATE SET value=$value";
            metadata.Parameters.AddWithValue("$value", enabled.Value ? "1" : "0"); await metadata.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested(); transaction.Commit();
        foreach (var pair in changes) if (pair.Value == null) personal.Remove(pair.Key); else personal[pair.Key] = pair.Value;
        if (enabled != null) builtinEnabled = enabled.Value;
        Rebuild();
    }
    public void Dispose() => database?.Dispose();
}
