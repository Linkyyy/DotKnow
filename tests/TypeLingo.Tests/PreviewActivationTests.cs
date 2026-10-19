using System.Text.Json;
using TypeLingo.Core;
using TypeLingo.Infrastructure;

namespace TypeLingo.Tests;

public sealed class PreviewActivationTests
{
    private static InputSnapshot Input(string text = "输入") => new("control", text, new(10, 10, 100, 30));
    [Fact] public async Task ManualMode_DoesNotRequestBeforeShortcut_AndEditRequiresAnotherShortcut()
    {
        var gate = new PreviewActivation(); using var f = new CoordinatorTests.Fixture();
        Assert.False(gate.ShouldCapture("hotkey"));
        await f.Coordinator.ObserveAsync(gate.Filter(Input(), "hotkey"));
        Assert.Equal(0, f.Provider.Calls);
        await f.Coordinator.ObserveAsync(gate.Filter(Input(), "hotkey", true));
        Assert.Equal(1, f.Provider.Calls); Assert.NotNull(f.Coordinator.CurrentPreview);
        await f.Coordinator.ObserveAsync(gate.Filter(Input("修改后"), "hotkey"));
        Assert.Null(f.Coordinator.CurrentPreview); Assert.False(gate.ShouldCapture("hotkey"));
        await f.Coordinator.ObserveAsync(gate.Filter(Input("继续输入"), "hotkey"));
        Assert.Equal(1, f.Provider.Calls);
        await f.Coordinator.ObserveAsync(gate.Filter(Input("继续输入"), "hotkey", true));
        Assert.Equal(2, f.Provider.Calls);
    }
    [Fact] public void ManualMode_FocusChangePauseAndModeSwitchInvalidatePermission()
    {
        var gate = new PreviewActivation();
        Assert.NotNull(gate.Filter(Input(), "hotkey", true));
        Assert.Null(gate.Filter(Input() with { Identity = "other" }, "hotkey"));
        Assert.False(gate.ShouldCapture("hotkey"));
        gate.Filter(Input(), "hotkey", true); gate.Reset(); Assert.False(gate.ShouldCapture("hotkey"));
        gate.Filter(Input(), "hotkey", true); gate.ShouldCapture("typing"); Assert.False(gate.ShouldCapture("hotkey"));
    }
    [Fact] public void SelectionMode_OnlyAcceptsActualNonWritableSelection()
    {
        var gate = new PreviewActivation();
        Assert.Null(gate.Filter(Input(), "selection"));
        Assert.Null(gate.Filter(Input() with { IsSelection = true }, "selection"));
        var selected = Input() with { IsSelection = true, CanWrite = false };
        Assert.Equal(selected, gate.Filter(selected, "selection"));
        Assert.Null(gate.Filter(selected with { Text = " " }, "selection"));
        Assert.Null(gate.Filter(selected with { IsSensitive = true }, "selection"));
        Assert.Null(gate.Filter(selected with { IsComposing = true }, "selection"));
        Assert.Null(gate.Filter(null, "selection"));
    }
    [Fact] public void CompactPlacement_AvoidsTheEditorWhenAnotherSideFits()
    {
        var editor = new InputRect(0, 200, 700, 500); var caret = new InputRect(300, 400, 10, 20);
        var p = OverlayPlacement.Place(caret, new(0, 0, 1200, 900), 280, 60, "above", editor);
        Assert.True(p.X >= editor.X + editor.Width || p.Y + 60 <= editor.Y || p.Y >= editor.Y + editor.Height);
    }
    [Fact] public void FullscreenEditor_StillKeepsCaretUncovered()
    {
        var caret = new InputRect(500, 400, 10, 20);
        var p = OverlayPlacement.Place(caret, new(-100, 0, 1200, 900), 280, 60, "above", new(-100, 0, 1200, 900));
        Assert.False(p.X < 510 && p.X + 280 > 500 && p.Y < 420 && p.Y + 60 > 400);
        Assert.InRange(p.X, -100, 820); Assert.InRange(p.Y, 0, 840);
    }
    [Theory][InlineData("light", true, false)][InlineData("dark", false, true)][InlineData("system", true, true)][InlineData("system", false, false)]
    public void Theme_ResolvesExplicitAndSystemModes(string theme, bool system, bool expected) => Assert.Equal(expected, AppearanceTheme.IsDark(theme, system));
    [Theory][InlineData(true)][InlineData(false)]
    public void OldTheme_MigratesWithoutChangingAppearance(bool dark)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Overlay\":{\"DarkMode\":" + (dark ? "true" : "false") + "}}");
            var service = new SettingsService(directory, new TestProtector()); service.Load();
            Assert.Null(service.LoadWarning); Assert.Equal(dark ? "dark" : "light", service.Current.Overlay.Theme);
            Assert.Equal("typing", service.Current.Overlay.TriggerMode); Assert.Equal("Ctrl+Alt+T", service.Current.Hotkeys.Preview);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact] public void NullOverlayConfiguration_PausesWithoutCrashingDuringMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"PrivacyConsent\":true,\"Overlay\":null}");
            var service = new SettingsService(directory, new TestProtector()); service.Load();
            Assert.NotNull(service.LoadWarning); Assert.False(service.Current.Translation.Enabled); Assert.False(service.Current.PrivacyConsent);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory][InlineData("hotkey")][InlineData("selection")][InlineData("typing")]
    public void NewPreferences_RoundTripAndValidate(string trigger)
    {
        var s = new AppSettings { Overlay = new() { Theme = "system", TriggerMode = trigger, Opacity = .6, MaxWidth = 180, AutoHideSeconds = 0 } };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!; restored.Validate(); Assert.Equal(s.Overlay, restored.Overlay);
        Assert.Throws<ArgumentException>(() => (s with { Overlay = s.Overlay with { TriggerMode = "unknown" } }).Validate());
        Assert.Throws<ArgumentException>(() => (s with { Overlay = s.Overlay with { AutoHideSeconds = -1 } }).Validate());
    }
}
