using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TypeLingo.Core;

namespace TypeLingo.Infrastructure;

public sealed class SettingsService(string directory, IDataProtector protector)
{
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };
    public string DirectoryPath { get; } = directory;
    private AppSettings current = new();
    public AppSettings Current { get => Volatile.Read(ref current); private set => Volatile.Write(ref current, value); }
    public string? LoadWarning { get; private set; }
    public bool HasKey => HasKeyFor(Current.AI);
    private string KeyPath(AiSettings ai) => Path.Combine(DirectoryPath, "api-key." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ai.Provider + "\n" + ai.BaseUrl.TrimEnd('/')))) + ".dpapi");
    private string? ExistingKeyPath(AiSettings ai)
    {
        if (File.Exists(KeyPath(ai))) return KeyPath(ai);
        var legacy = Path.Combine(DirectoryPath, "api-key.dpapi");
        return ai.Provider == "DeepSeek" && ai.BaseUrl.TrimEnd('/') == "https://api.deepseek.com" && File.Exists(legacy) ? legacy : null;
    }
    public bool HasKeyFor(AiSettings ai) => ExistingKeyPath(ai) != null;
    public void Load()
    {
        var file = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(file)) return;
        try
        {
            var text = File.ReadAllText(file);
            var candidate = JsonSerializer.Deserialize<AppSettings>(text) ?? throw new JsonException();
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("Overlay", out var overlay) && !overlay.TryGetProperty("Theme", out _))
                candidate = candidate with { Overlay = candidate.Overlay with { Theme = candidate.Overlay.DarkMode ? "dark" : "light" } };
            candidate.Validate(); Current = candidate;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or IOException or UnauthorizedAccessException or NullReferenceException)
        {
            Current = new AppSettings { Translation = new() { Enabled = false } };
            LoadWarning = "配置损坏或版本不兼容，已暂停读取；原配置文件未覆盖。请检查后重新保存。";
        }
    }
    public void Save(AppSettings settings, string? newKey = null)
    {
        settings.Validate(); Directory.CreateDirectory(DirectoryPath);
        if (!string.IsNullOrWhiteSpace(newKey))
            AtomicWrite(KeyPath(settings.AI), protector.Protect(Encoding.UTF8.GetBytes(newKey.Trim())));
        AtomicWrite(Path.Combine(DirectoryPath, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(settings, json));
        Current = settings;
    }
    public void SetPaused(bool paused) => Current = Current with { Translation = Current.Translation with { Enabled = !paused } };
    public string ReadKey() => ReadKey(Current.AI);
    public string ReadKey(AiSettings ai)
    {
        try { return ExistingKeyPath(ai) is { } path ? Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path))) : ""; }
        catch (System.Security.Cryptography.CryptographicException) { throw new TranslationException(TranslationError.Authentication, "当前 Windows 用户无法解密 API Key，请重新保存。"); }
    }
    public void DeleteKey(AiSettings? ai = null)
    {
        ai ??= Current.AI;
        if (ExistingKeyPath(ai) is { } path) File.Delete(path);
        // 同时移除当前 DeepSeek 配置的旧版兼容文件，防止删除后再次回退读取。
        if (ai.Provider == "DeepSeek" && ai.BaseUrl.TrimEnd('/') == "https://api.deepseek.com") File.Delete(Path.Combine(DirectoryPath, "api-key.dpapi"));
    }
    private static void AtomicWrite(string file, byte[] bytes)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
