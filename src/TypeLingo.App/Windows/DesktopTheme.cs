using Microsoft.Win32;
using System.Windows.Media;
using TypeLingo.Core;

namespace TypeLingo.App.Windows;

internal static class DesktopTheme
{
    public static event Action? Changed;
    public static bool ApplyResources(OverlaySettings configuration)
    {
        var dark = AppearanceTheme.IsDark(configuration.Theme, IsDark);
        var palette = AppearancePalettes.Resolve(configuration.ColorTheme, configuration.Theme, dark);
        foreach (var pair in palette.Resources)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
            brush.Freeze();
            System.Windows.Application.Current.Resources[pair.Key] = brush;
        }
        Changed?.Invoke();
        return dark;
    }
    public static bool IsDark => IsThemeDark("AppsUseLightTheme");
    public static bool IsTaskbarDark => IsThemeDark("SystemUsesLightTheme");
    private static bool IsThemeDark(string name)
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", name, 1) is int value && value == 0; }
        catch (Exception) { return false; }
    }
}
