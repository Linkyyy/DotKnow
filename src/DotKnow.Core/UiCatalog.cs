using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotKnow.Core;

public static class UiCatalog
{
    public static IReadOnlyList<string> SupportedLanguages { get; } = Array.AsReadOnly(new[] { "zh-CN", "en-US", "ja-JP" });
    private static readonly Dictionary<string, Dictionary<string, string>> messages = Load();
    public static IEnumerable<string> Keys => messages.Keys;
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        using var stream = typeof(UiCatalog).Assembly.GetManifestResourceStream("DotKnow.Core.UiCatalog.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }
    public static bool Contains(string key) => messages.ContainsKey(key);
    public static string Get(string language, string text)
    {
        if (language == "zh-CN" || !SupportedLanguages.Contains(language)) return text;
        if (messages.TryGetValue(text, out var localized)) return localized[language];
        // 仅匹配已定义的系统消息模板，不对原文或译文做任何界面本地化处理。
        foreach (var template in dynamicMessages)
        {
            var match = template.Pattern.Match(text);
            if (match.Success) return Format(language, template.Key, match.Groups[1].Value);
        }
        return text;
    }
    public static string Format(string language, string key, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, Get(language, key), values);
    private static readonly (Regex Pattern, string Key)[] dynamicMessages =
    [
        (new(@"^快捷键 (.+) 已被占用，相关快捷键未启用。请修改设置。$", RegexOptions.CultureInvariant), "快捷键 {0} 已被占用，相关快捷键未启用。请修改设置。"),
        (new(@"^API 拒绝请求（HTTP (\d+)），请检查模型、地址和账户余额。$", RegexOptions.CultureInvariant), "API 拒绝请求（HTTP {0}），请检查模型、地址和账户余额。")
    ];
}
