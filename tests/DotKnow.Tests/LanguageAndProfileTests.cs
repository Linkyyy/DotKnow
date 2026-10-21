using System.Text.Json;
using DotKnow.Core;
using DotKnow.Infrastructure;

namespace DotKnow.Tests;

public sealed class LanguageAndProfileTests
{
    [Theory][InlineData("zh-en", "zh", "en")][InlineData("en-zh", "en", "zh")][InlineData("zh-ja", "zh", "ja")]
    [InlineData("ja-zh", "ja", "zh")][InlineData("en-ja", "en", "ja")][InlineData("ja-en", "ja", "en")]
    [InlineData("auto-ja", "auto", "ja")][InlineData("auto-en", "auto", "en")][InlineData("auto-zh", "auto", "zh")]
    public void DirectionPresets_PreserveStyleAndOtherSettings(string id, string source, string target)
    {
        var original = new TranslationSettings { Mode = "business", DebounceMilliseconds = 650 };
        var translated = TranslationPresets.Apply(id, original);
        Assert.Equal(source, translated.SourceLanguage); Assert.Equal(target, translated.TargetLanguage);
        Assert.Equal("business", translated.Mode); Assert.Equal(650, translated.DebounceMilliseconds);
        Assert.Equal(id, TranslationPresets.Find(source, target));
    }
    [Fact] public void EveryUiMessage_HasEnglishAndJapaneseTranslations_AndMatchingPlaceholders()
    {
        Assert.True(UiCatalog.Keys.Count() > 150);
        foreach (var key in UiCatalog.Keys)
            foreach (var language in new[] { "en-US", "ja-JP" })
            {
                var translated = UiCatalog.Get(language, key); Assert.False(string.IsNullOrWhiteSpace(translated));
                var placeholders = System.Text.RegularExpressions.Regex.Matches(key, @"\{\d+\}").Select(x => x.Value).Order().ToArray();
                Assert.Equal(placeholders, System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+\}").Select(x => x.Value).Order().ToArray());
            }
    }
    [Fact] public void UiLanguage_DoesNotChangeTranslationRequestOrCacheKey()
    {
        var chinese = CoordinatorTests.Config; var english = chinese with { UiLanguage = "en-US" };
        Assert.Equal(TranslationRequest.Create(chinese, "原文"), TranslationRequest.Create(english, "原文"));
        Assert.Equal("中译日", UiCatalog.Get("zh-CN", "中译日")); Assert.Equal("Chinese → Japanese", UiCatalog.Get("en-US", "中译日"));
        Assert.Equal("中国語 → 日本語", UiCatalog.Get("ja-JP", "中译日"));
        Assert.Contains("HTTP 402", UiCatalog.Get("en-US", "API 拒绝请求（HTTP 402），请检查模型、地址和账户余额。"));
    }
    [Fact] public void KeyProfiles_IsolateProviderAndUrl_AndPersistLanguageAndDirection()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var settings = new SettingsService(directory, new TestProtector());
            var deepseek = CoordinatorTests.Config; var openai = deepseek with { AI = ProviderCatalog.Defaults("OpenAI"), UiLanguage = "ja-JP", Translation = TranslationPresets.Apply("zh-en", deepseek.Translation) };
            settings.Save(deepseek, "deepseek-test-key"); Assert.False(settings.HasKeyFor(openai.AI)); Assert.Equal("", settings.ReadKey(openai.AI));
            settings.Save(openai, "openai-test-key"); Assert.Equal("deepseek-test-key", settings.ReadKey(deepseek.AI)); Assert.Equal("openai-test-key", settings.ReadKey(openai.AI));
            Assert.Equal("", settings.ReadKey(openai.AI with { BaseUrl = "https://other.example/v1" }));
            settings.Load(); Assert.Equal("ja-JP", settings.Current.UiLanguage); Assert.Equal("en", settings.Current.Translation.TargetLanguage);
            settings.DeleteKey(openai.AI); Assert.False(settings.HasKeyFor(openai.AI)); Assert.True(settings.HasKeyFor(deepseek.AI));
            Assert.DoesNotContain("test-key", File.ReadAllText(Path.Combine(directory, "settings.json")));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void LegacyKey_IsOnlyAvailableToOriginalDeepSeekEndpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            var protector = new TestProtector(); File.WriteAllBytes(Path.Combine(directory, "api-key.dpapi"), protector.Protect("legacy-test-key"u8.ToArray()));
            var settings = new SettingsService(directory, protector);
            Assert.Equal("legacy-test-key", settings.ReadKey(new AiSettings())); Assert.Equal("", settings.ReadKey(ProviderCatalog.Defaults("OpenAI")));
            Assert.Equal("", settings.ReadKey(new AiSettings { BaseUrl = "https://other.example" }));
            settings.DeleteKey(); Assert.False(settings.HasKey);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void PlatformLinks_AreFixedOfficialHttpsDestinations()
    {
        Assert.Equal(5, ProviderCatalog.All.Count);
        var hosts = new[] { "platform.deepseek.com", "platform.openai.com", "console.anthropic.com", "aistudio.google.com", "bailian.console.aliyun.com" };
        Assert.All(ProviderCatalog.All, item => { var uri = new Uri(item.PlatformUrl); Assert.Equal("https", uri.Scheme); Assert.Contains(uri.Host, hosts); });
    }
}
