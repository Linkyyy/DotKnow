using System.Text.Json;
using TypeLingo.Core;

namespace TypeLingo.Tests;

public sealed class ApplicationScopeTests
{
    [Theory][InlineData("chrome")][InlineData("notepad")][InlineData("new-unlisted-app")]
    public void AllApplications_AllowsProcessesWithoutManualAllowlist(string process)
    {
        var settings = new AppSettings { PrivacyConsent = true };
        Assert.True(settings.MonitorAllApplications); Assert.Empty(settings.AllowedApplications);
        Assert.True(PrivacyGuard.Allows(settings, process, false, false));
    }
    [Theory][InlineData(true, false, false, true)][InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)][InlineData(false, false, false, false)]
    public void AllApplications_StillRequiresConsentAndRejectsSensitiveControls(bool consent, bool password, bool sensitive, bool allowed)
    {
        var settings = new AppSettings { PrivacyConsent = consent, MonitorAllApplications = true };
        Assert.Equal(allowed, PrivacyGuard.Allows(settings, "chrome", password, sensitive));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void ExclusionsAndPause_OverrideBothScopeModes(bool all)
    {
        var settings = new AppSettings { PrivacyConsent = true, MonitorAllApplications = all, AllowedApplications = ["keepass", "chrome"], ExcludedApplications = ["keepass"] };
        Assert.False(PrivacyGuard.Allows(settings, "KEEPASS.exe", false, false));
        Assert.False(PrivacyGuard.Allows(settings with { Translation = settings.Translation with { Enabled = false } }, "chrome", false, false));
    }
    [Fact] public void SpecificApplications_ModeKeepsAllowlistBehavior()
    {
        var settings = new AppSettings { PrivacyConsent = true, MonitorAllApplications = false, AllowedApplications = ["chrome"] };
        Assert.True(PrivacyGuard.Allows(settings, "chrome.exe", false, false));
        Assert.False(PrivacyGuard.Allows(settings, "notepad", false, false));
    }
    [Fact] public void Scope_PersistsAndOldSettingsKeepConsentBoundary()
    {
        var original = new AppSettings { PrivacyConsent = true, MonitorAllApplications = false };
        Assert.False(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(original))!.MonitorAllApplications);
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"PrivacyConsent\":false}")!;
        Assert.True(legacy.MonitorAllApplications); Assert.False(PrivacyGuard.Allows(legacy, "chrome", false, false));
    }
    [Theory][InlineData("密钥", "zh-CN", "ai")][InlineData("password", "en-US", "privacy")]
    [InlineData("字体", "zh-CN", "appearance")][InlineData("缓存", "zh-CN", "cache")]
    [InlineData("privacy", "en-US", "privacy")][InlineData("界面语言", "zh-CN", "general")]
    public void Search_FindsTheRelevantSettingsPage(string query, string language, string page) =>
        Assert.Contains(page, SettingsNavigation.Search(query, language));
    [Fact] public void Search_HandlesEmptyAndUnknownQueries()
    {
        Assert.Equal(SettingsNavigation.Sections.Count, SettingsNavigation.Search("  ", "zh-CN").Count);
        Assert.Empty(SettingsNavigation.Search("不存在的设置项 xyzzy", "zh-CN"));
    }
}
