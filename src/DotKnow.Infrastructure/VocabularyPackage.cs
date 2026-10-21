using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotKnow.Core;

namespace DotKnow.Infrastructure;

public sealed record VocabularyEntry
{
    public string SourceLanguage { get; init; } = "";
    public string TargetLanguage { get; init; } = "";
    public string SourceText { get; init; } = "";
    public string TranslatedText { get; init; } = "";
    public string Style { get; init; } = "natural";
    public string PromptVersion { get; init; } = "1";
    public string Origin { get; init; } = "imported";
    public bool IsAmbiguous { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new[] { SourceLanguage, TargetLanguage, SourceText, Style, PromptVersion }))));
    public static VocabularyEntry From(TranslationRequest request, string text) => new()
    {
        SourceLanguage = request.SourceLanguage, TargetLanguage = request.TargetLanguage,
        SourceText = request.Text, TranslatedText = text, Style = request.Mode, PromptVersion = request.PromptVersion
    };
    internal bool IsValid => new[] { "zh", "en", "ja", "auto" }.Contains(SourceLanguage) &&
        new[] { "zh", "en", "ja" }.Contains(TargetLanguage) && SourceLanguage != TargetLanguage &&
        new[] { "natural", "literal", "business", "keigo" }.Contains(Style) &&
        !string.IsNullOrWhiteSpace(SourceText) && SourceText.Length <= 20000 &&
        TranslatedText != null && TranslatedText.Length <= 40000 &&
        (IsAmbiguous || IsDeleted || !string.IsNullOrWhiteSpace(TranslatedText)) &&
        !string.IsNullOrWhiteSpace(PromptVersion) && PromptVersion.Length <= 32;
}

public sealed record VocabularyImportReport(int Added, int Duplicates, int Conflicts, int Invalid, int Preserved);
public sealed record VocabularyStatistics(int Available, int Ambiguous, int Learned, int Manual, int Imported);

/// <summary>只读数据，不解压执行上传包中的脚本；限制条数、文件大小并核对提供的 SHA-256。</summary>
internal sealed class VocabularyPackage
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal const int MaxEntries = 250000;
    private const long MaxFileBytes = 64 * 1024 * 1024;
    internal Dictionary<string, VocabularyEntry> Entries { get; } = new(StringComparer.Ordinal);
    internal int Invalid { get; private set; }
    internal int Duplicates { get; private set; }
    internal int Conflicts { get; private set; }
    internal static Stream OpenBundled() => typeof(VocabularyPackage).Assembly.GetManifestResourceStream("DotKnow.Vocabulary.zip")
        ?? throw new IOException("内置词库资源缺失。");

    internal static VocabularyPackage Read(Stream stream, bool zip, CancellationToken token)
    {
        var result = new VocabularyPackage();
        if (!zip) { result.ReadLines(stream, false, token); return result; }
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 256 || archive.Entries.Sum(x => x.Length) > 128 * 1024 * 1024 ||
            archive.Entries.Any(x => x.Length > MaxFileBytes || !SafePath(x.FullName)) ||
            archive.Entries.GroupBy(x => x.FullName, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("词库包过大或包含不安全路径。");
        var manifest = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("词库包缺少 manifest.json。");
        using var manifestStream = manifest.Open();
        using var document = JsonDocument.Parse(manifestStream);
        if (!document.RootElement.TryGetProperty("formatVersion", out var version) ||
            !new[] { "1", "1.0" }.Contains(version.ToString())) throw new InvalidDataException("不支持此词库格式版本。");
        var entriesName = "entries.jsonl";
        var ambiguousName = "ambiguous.jsonl";
        if (document.RootElement.TryGetProperty("files", out var files))
        {
            if (files.TryGetProperty("entries", out var name)) entriesName = name.GetString() ?? "";
            if (files.TryGetProperty("ambiguous", out name)) ambiguousName = name.GetString() ?? "";
        }
        if (!SafePath(entriesName) || !SafePath(ambiguousName)) throw new InvalidDataException("词库文件路径无效。");
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        if (archive.GetEntry("checksums.sha256") is { } checks)
        {
            using var reader = new StreamReader(checks.Open(), Encoding.UTF8, true);
            while (reader.ReadLine() is { } line)
            {
                token.ThrowIfCancellationRequested();
                var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit)) throw new InvalidDataException("词库校验文件无效。");
                var path = parts[1].TrimStart('*', ' ');
                if (!SafePath(path) || !checksums.TryAdd(path, parts[0])) throw new InvalidDataException("词库校验路径无效。");
                var entry = archive.GetEntry(path) ?? throw new InvalidDataException("词库校验文件缺失。");
                using var content = entry.Open();
                if (!Convert.ToHexString(SHA256.HashData(content)).Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("词库文件校验失败，未导入。");
            }
            if (!checksums.ContainsKey("manifest.json") || !checksums.ContainsKey(entriesName) ||
                (archive.GetEntry(ambiguousName) != null && !checksums.ContainsKey(ambiguousName)))
                throw new InvalidDataException("词库校验未覆盖必要文件。");
        }
        var entries = archive.GetEntry(entriesName) ?? throw new InvalidDataException("词库包缺少 entries.jsonl。");
        using (var content = entries.Open()) result.ReadLines(content, false, token);
        if (archive.GetEntry(ambiguousName) is { } ambiguous)
            using (var content = ambiguous.Open()) result.ReadLines(content, true, token);
        return result;
    }

    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') &&
        !path.Contains('\\') && !path.Contains(':') && path.Split('/').All(x => x != ".." && x != ".");
    private void ReadLines(Stream stream, bool ambiguous, CancellationToken token)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, leaveOpen: true);
        long characters = 0; var lines = 0;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            characters += line.Length;
            if (++lines > MaxEntries || characters > MaxFileBytes) throw new InvalidDataException("词库超过支持的容量。");
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 100000) { Invalid++; continue; }
            VocabularyEntry? entry;
            try { entry = JsonSerializer.Deserialize<VocabularyEntry>(line, Json); }
            catch (JsonException) { Invalid++; continue; }
            if (entry == null) { Invalid++; continue; }
            entry = entry with { Origin = "imported", IsAmbiguous = ambiguous || entry.IsAmbiguous, IsDeleted = false };
            if (!entry.IsValid) { Invalid++; continue; }
            if (Entries.TryGetValue(entry.Key, out var previous))
            {
                if (!previous.IsAmbiguous && !entry.IsAmbiguous && previous.TranslatedText == entry.TranslatedText) Duplicates++;
                else { Entries[entry.Key] = previous with { IsAmbiguous = true }; Conflicts++; }
            }
            else Entries.Add(entry.Key, entry);
            if (Entries.Count > MaxEntries) throw new InvalidDataException("词库超过支持的容量。");
        }
    }
}
