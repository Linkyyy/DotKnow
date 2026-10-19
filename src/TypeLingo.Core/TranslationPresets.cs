namespace TypeLingo.Core;

public sealed record TranslationPreset(string Id, string Label, string SourceLanguage, string TargetLanguage);

public static class TranslationPresets
{
    // 翻译方向与界面语言、翻译风格分别保存，切换界面语言不会改变要翻译的语言。
    public static IReadOnlyList<TranslationPreset> All { get; } = Array.AsReadOnly(new[]
    {
        new TranslationPreset("auto-ja", "自动识别 → 日语", "auto", "ja"),
        new TranslationPreset("auto-en", "自动识别 → 英语", "auto", "en"),
        new TranslationPreset("auto-zh", "自动识别 → 中文", "auto", "zh"),
        new TranslationPreset("zh-en", "中译英", "zh", "en"),
        new TranslationPreset("en-zh", "英译中", "en", "zh"),
        new TranslationPreset("zh-ja", "中译日", "zh", "ja"),
        new TranslationPreset("ja-zh", "日译中", "ja", "zh"),
        new TranslationPreset("en-ja", "英译日", "en", "ja"),
        new TranslationPreset("ja-en", "日译英", "ja", "en")
    });
    public static TranslationSettings Apply(string id, TranslationSettings settings)
    {
        var preset = All.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("不支持所选翻译方向。");
        return settings with { SourceLanguage = preset.SourceLanguage, TargetLanguage = preset.TargetLanguage };
    }
    public static string Find(string source, string target) => All.FirstOrDefault(x => x.SourceLanguage == source && x.TargetLanguage == target)?.Id ?? "custom";
}
