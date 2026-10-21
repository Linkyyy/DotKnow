using System.Globalization;
using System.Text.Json;
using DotKnow.Core;
using DotKnow.Infrastructure;
using Xunit;

namespace DotKnow.Tests;

public sealed class AppearancePaletteTests
{
    public static IEnumerable<object[]> Combinations => from palette in AppearancePalettes.All
        from mode in new[] { "light", "dark", "system" } select new object[] { palette.Id, mode };

    [Theory, MemberData(nameof(Combinations))]
    public void PaletteAndMode_AreIndependentAndPersistAcrossReload(string palette, string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "DotKnow-appearance-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(directory, new TestProtector());
            var settings = new AppSettings { Overlay = new() { ColorTheme = palette, Theme = mode } };
            service.Save(settings);
            var loaded = new SettingsService(directory, new TestProtector()); loaded.Load();
            Assert.Null(loaded.LoadWarning); Assert.Equal(settings.Overlay, loaded.Current.Overlay);
            Assert.Same(mode == "dark" ? AppearancePalettes.Get(palette).Dark : AppearancePalettes.Get(palette).Light,
                AppearancePalettes.Resolve(palette, mode, false));
            Assert.Same(mode == "light" ? AppearancePalettes.Get(palette).Light : AppearancePalettes.Get(palette).Dark,
                AppearancePalettes.Resolve(palette, mode, true));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExistingSettingsWithoutPalette_KeepTheirModeAndUsePaper()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Overlay\":{\"Theme\":\"dark\"}}")!;
        old.Validate();
        Assert.Equal("dark", old.Overlay.Theme); Assert.Equal("paper", old.Overlay.ColorTheme);
        Assert.Throws<ArgumentException>(() => (old with { Overlay = old.Overlay with { ColorTheme = "missing" } }).Validate());
    }

    [Fact]
    public void EveryPalette_HasCompleteResourcesAndReadableTextPairs()
    {
        var keys = AppearancePalettes.All[0].Light.Resources.Keys.Order().ToArray();
        foreach (var palette in AppearancePalettes.All)
        foreach (var colors in new[] { palette.Light, palette.Dark })
        {
            Assert.Equal(keys, colors.Resources.Keys.Order().ToArray());
            foreach (var value in colors.Resources.Values) Assert.Matches("^#[0-9A-F]{6}$", value);
            var pairs = new[] { (colors.Text, colors.Canvas), (colors.Text, colors.Card), (colors.Text, colors.Input),
                (colors.Text, colors.Selection), (colors.Muted, colors.Canvas), (colors.Muted, colors.Sidebar),
                (colors.Muted, colors.Card), (colors.Muted, colors.AccentSurface), (colors.AccentText, colors.Accent),
                (colors.HeroText, colors.Hero), (colors.HeroMuted, colors.Hero), (colors.HighlightText, colors.Highlight),
                (colors.HeroHighlight ?? colors.Highlight, colors.Hero) };
            foreach (var (foreground, background) in pairs)
                Assert.True(Contrast(foreground, background) >= 4.5,
                    $"{palette.Id}: {foreground} on {background} needs 4.5:1 contrast; got {Contrast(foreground, background):F2}");
        }
    }

    [Theory, InlineData("浅纸", "zh-CN"), InlineData("Forest", "en-US"), InlineData("ラベンダー", "ja-JP")]
    public void Search_FindsPaletteControlsInAppearance(string query, string language) =>
        Assert.Contains("appearance", SettingsNavigation.Search(query, language));

    private static double Contrast(string first, string second)
    {
        static double Luminance(string value)
        {
            var components = new[] { 1, 3, 5 }.Select(i => int.Parse(value.AsSpan(i, 2), NumberStyles.HexNumber) / 255d)
                .Select(v => v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4)).ToArray();
            return components[0] * .2126 + components[1] * .7152 + components[2] * .0722;
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
