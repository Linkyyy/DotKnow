using Microsoft.Win32;
using System.Windows.Media;
using TypeLingo.Core;

namespace TypeLingo.App.Windows;

internal static class DesktopTheme
{
    public static bool ApplyResources(OverlaySettings configuration)
    {
        var dark = AppearanceTheme.IsDark(configuration.Theme, IsDark);
        var palette = new Dictionary<string, (string Light, string Dark)>
        {
            ["CanvasBrush"] = ("#FFFFFF", "#202020"), ["SurfaceBrush"] = ("#FFFFFF", "#2B2B2B"),
            ["CardBrush"] = ("#FFFFFF", "#2B2B2B"), ["BorderBrush"] = ("#E5E5E5", "#3A3A3A"),
            ["HoverBrush"] = ("#F5F6F7", "#343434"), ["SelectionBrush"] = ("#EBEEF1", "#383838"),
            ["TextBrush"] = ("#1A1A1A", "#F5F6F7"), ["MutedBrush"] = ("#666666", "#B8B8B8"),
            ["AccentBrush"] = ("#17191D", "#F0F1F2"), ["AccentTextBrush"] = ("#FFFFFF", "#17191D"),
            ["AccentSurfaceBrush"] = ("#F5F6F7", "#292B30")
        };
        foreach (var pair in palette) System.Windows.Application.Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? pair.Value.Dark : pair.Value.Light));
        return dark;
    }
    public static bool IsDark
    {
        get
        {
            try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
            catch (Exception) { return false; }
        }
    }
}
