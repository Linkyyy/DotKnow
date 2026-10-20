using System.Security.Cryptography;
using System.Text.Json;
using TypeLingo.Core;

namespace TypeLingo.Infrastructure;

/// <summary>只保存用户主动收藏的内容；与可清除的翻译缓存独立，全文使用当前用户加密。</summary>
public sealed class FavoriteStore(string directory, IDataProtector protector, Func<DateTimeOffset>? clock = null)
{
    private sealed record Document(int Version, List<FavoriteEntry> Entries);
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private string FilePath => Path.Combine(directory, "favorites.dpapi");
    public IReadOnlyList<FavoriteEntry> GetAll()
    {
        lock (gate) return Read().OrderByDescending(x => x.CreatedAt).ToArray();
    }
    public IReadOnlyList<FavoriteEntry> GetDue()
    {
        lock (gate) return Read().Where(x => x.DueAt <= now()).OrderBy(x => x.DueAt).ToArray();
    }
    public FavoriteEntry Add(Preview preview)
    {
        if (string.IsNullOrWhiteSpace(preview.Snapshot.Text) || string.IsNullOrWhiteSpace(preview.Result.Text) ||
            preview.Snapshot.Text.Length > 20000 || preview.Result.Text.Length > 40000)
            throw new ArgumentException("无法收藏空白或过长的内容。");
        lock (gate)
        {
            var entries = Read(); var request = preview.Request;
            var existing = entries.FirstOrDefault(x => x.Original == preview.Snapshot.Text && x.Translation == preview.Result.Text &&
                x.SourceLanguage == request.SourceLanguage && x.TargetLanguage == request.TargetLanguage && x.Mode == request.Mode &&
                x.TaskId == request.TaskId && x.Question == request.Question);
            if (existing != null) return existing;
            if (entries.Count >= 5000) throw new InvalidOperationException("收藏已达 5000 条，请先删除部分内容。");
            var time = now();
            var entry = new FavoriteEntry(Guid.NewGuid(), preview.Snapshot.Text, preview.Result.Text,
                request.SourceLanguage, request.TargetLanguage, request.Mode, time, time, TaskId: request.TaskId, Question: request.Question);
            entries.Add(entry); Write(entries); return entry;
        }
    }
    public bool Delete(Guid id)
    {
        lock (gate)
        {
            var entries = Read(); if (entries.RemoveAll(x => x.Id == id) == 0) return false;
            Write(entries); return true;
        }
    }
    public FavoriteEntry? Review(Guid id, bool remembered)
    {
        lock (gate)
        {
            var entries = Read(); var index = entries.FindIndex(x => x.Id == id);
            if (index < 0) return null;
            var reviewed = entries[index].Review(remembered, now()); entries[index] = reviewed;
            Write(entries); return reviewed;
        }
    }
    private List<FavoriteEntry> Read()
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var data = JsonSerializer.Deserialize<Document>(protector.Unprotect(File.ReadAllBytes(FilePath)));
            if (data is not { Version: 1, Entries: not null } || data.Entries.Count > 5000 ||
                data.Entries.Any(x => x == null || x.Id == Guid.Empty || string.IsNullOrWhiteSpace(x.Original) ||
                    string.IsNullOrWhiteSpace(x.Translation) || x.ReviewCount < 0 || x.IntervalDays < 0) ||
                data.Entries.Select(x => x.Id).Distinct().Count() != data.Entries.Count) throw new JsonException();
            return data.Entries;
        }
        catch (Exception e) when (e is JsonException or CryptographicException)
        { throw new IOException("收藏文件无法读取，原文件已保留。请检查本地文件或 Windows 用户。", e); }
    }
    private void Write(List<FavoriteEntry> entries)
    {
        Directory.CreateDirectory(directory);
        var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new Document(1, entries)));
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
