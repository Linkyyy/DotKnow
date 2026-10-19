namespace TypeLingo.Core;

public sealed record AppSettings
{
    public string UiLanguage { get; init; } = "zh-CN";
    public bool PrivacyConsent { get; init; }
    public AiSettings AI { get; init; } = new();
    public TranslationSettings Translation { get; init; } = new();
    public CacheSettings Cache { get; init; } = new();
    public OverlaySettings Overlay { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public string[] AllowedApplications { get; init; } = [];
    public string[] ExcludedApplications { get; init; } = ["1password", "keepass", "keepassxc", "bitwarden"];

    public void Validate()
    {
        if (!UiCatalog.SupportedLanguages.Contains(UiLanguage))
            throw new ArgumentException("不支持所选界面语言。");
        _ = ProviderCatalog.Get(AI.Provider);
        if (string.IsNullOrWhiteSpace(AI.Model))
            throw new ArgumentException("请选择 API 服务商并填写模型名称。");
        if (!Uri.TryCreate(AI.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("API Base URL 必须是不含凭证、查询参数或片段的 HTTPS 地址。");
        if (AI.TimeoutSeconds is < 1 or > 120 || AI.MaxRetries is < 0 or > 4)
            throw new ArgumentException("超时范围为 1–120 秒，重试范围为 0–4。");
        if (Translation.DebounceMilliseconds is < 100 or > 10000 ||
            Translation.MinimumRequestIntervalMilliseconds is < 100 or > 30000 ||
            Translation.MaxInputCharacters is < 1 or > 20000)
            throw new ArgumentException("防抖、请求间隔或文本长度不在支持范围内。");
        if (!new[] { "auto", "zh", "ja", "en" }.Contains(Translation.SourceLanguage) ||
            !new[] { "zh", "ja", "en" }.Contains(Translation.TargetLanguage) ||
            !new[] { "natural", "literal", "business", "keigo" }.Contains(Translation.Mode))
            throw new ArgumentException("不支持所选语言或翻译风格。");
        if (Cache.MemoryMaxEntries is < 1 or > 50000 || Cache.MemoryTTLMinutes is < 1 or > 1440 ||
            Cache.DiskMaxEntries is < 1 or > 100000 || Cache.DiskTTLDays is < 1 or > 365)
            throw new ArgumentException("缓存容量或有效期不在支持范围内。");
        if (!double.IsFinite(Overlay.FontSize) || !double.IsFinite(Overlay.MaxWidth) || !double.IsFinite(Overlay.Opacity) ||
            Overlay.FontSize is < 10 or > 32 || Overlay.MaxWidth is < 240 or > 800 ||
            Overlay.Opacity is < .5 or > 1 || !new[] { "right", "below", "left", "above" }.Contains(Overlay.Position))
            throw new ArgumentException("悬浮窗参数不在支持范围内。");
        if (AllowedApplications.Concat(ExcludedApplications).Any(x =>
                string.IsNullOrWhiteSpace(x) || x.IndexOfAny(['/', '\\', ':', '*']) >= 0))
            throw new ArgumentException("应用列表仅填写进程名，例如 notepad 或 chrome。");
    }
}

public sealed record AiSettings
{
    public string Provider { get; init; } = "DeepSeek";
    public string BaseUrl { get; init; } = "https://api.deepseek.com";
    // 模型可显式配置；启用前用用户自己的账户查询 /models 确认可用性。
    public string Model { get; init; } = "deepseek-flash";
    public int TimeoutSeconds { get; init; } = 20;
    public int MaxRetries { get; init; } = 2;
}
public sealed record TranslationSettings
{
    public bool Enabled { get; init; } = true;
    public int DebounceMilliseconds { get; init; } = 500;
    public int MinimumRequestIntervalMilliseconds { get; init; } = 700;
    public int MaxInputCharacters { get; init; } = 2000;
    public string SourceLanguage { get; init; } = "auto";
    public string TargetLanguage { get; init; } = "ja";
    public string Mode { get; init; } = "natural";
}
public sealed record CacheSettings
{
    public bool MemoryEnabled { get; init; } = true;
    public int MemoryMaxEntries { get; init; } = 500;
    public int MemoryTTLMinutes { get; init; } = 30;
    public bool DiskEnabled { get; init; }
    public int DiskMaxEntries { get; init; } = 5000;
    public int DiskTTLDays { get; init; } = 7;
}
public sealed record OverlaySettings
{
    public bool DarkMode { get; init; } = true;
    public double FontSize { get; init; } = 16;
    public double Opacity { get; init; } = .97;
    public double MaxWidth { get; init; } = 380;
    public string Position { get; init; } = "right";
}
public sealed record HotkeySettings
{
    public string Replace { get; init; } = "Ctrl+Alt+Enter";
    public string Toggle { get; init; } = "Ctrl+Alt+Space";
    public string Copy { get; init; } = "Ctrl+Alt+C";
}

public static class PrivacyGuard
{
    public static bool Allows(AppSettings settings, string processName, bool password, bool sensitive)
    {
        static string Normalize(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        bool Has(string[] names) => names.Any(n => string.Equals(Normalize(n), Normalize(processName), StringComparison.OrdinalIgnoreCase));
        return settings.PrivacyConsent && settings.Translation.Enabled && !password && !sensitive &&
               Has(settings.AllowedApplications) && !Has(settings.ExcludedApplications);
    }
}
