using DotKnow.Core;

namespace DotKnow.Tests;

public sealed class HostedInputTests
{
    private static AppSettings Config => new() { PrivacyConsent = true, MonitorAllApplications = false, AllowedApplications = ["ms-teams"] };
    [Fact] public void RendererWithinAuthorizedForegroundHost_IsAllowedWithoutGlobalRendererPermission()
    {
        Assert.True(PrivacyGuard.AllowsHostedInput(Config, "ms-teams", "msedgewebview2", true));
        Assert.False(PrivacyGuard.Allows(Config, "msedgewebview2", false, false));
        Assert.False(PrivacyGuard.AllowsHostedInput(Config, "other-app", "msedgewebview2", true));
        Assert.False(PrivacyGuard.AllowsHostedInput(Config, "ms-teams", "msedgewebview2", false));
    }
    [Theory][InlineData("ms-teams")][InlineData("MSEDGEWEBVIEW2.exe")]
    public void HostAndRendererExclusions_TakePriority(string excluded)
    {
        var config = Config with { ExcludedApplications = [excluded] };
        Assert.False(PrivacyGuard.AllowsHostedInput(config, "ms-teams", "msedgewebview2", true));
    }
    [Theory][InlineData("consent")][InlineData("pause")][InlineData("password")][InlineData("sensitive")]
    public void HostedInput_PreservesPrivacyGates(string gate)
    {
        var config = Config;
        if (gate == "consent") config = config with { PrivacyConsent = false };
        if (gate == "pause") config = config with { Translation = config.Translation with { Enabled = false } };
        Assert.False(PrivacyGuard.AllowsHostedInput(config, "ms-teams", "msedgewebview2", true, gate == "password", gate == "sensitive"));
    }
    [Theory][InlineData("Loading")][InlineData("翻译完成后显示")][InlineData("window display timing")]
    public void PopupTiming_IsDiscoverableInGeneralSettings(string query)
    {
        Assert.Contains("general", SettingsNavigation.Search(query, query.Contains(' ') ? "en-US" : "zh-CN"));
    }
}
