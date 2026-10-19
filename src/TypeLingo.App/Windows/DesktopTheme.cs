using Microsoft.Win32;

namespace TypeLingo.App.Windows;

internal static class DesktopTheme
{
    public static bool IsDark
    {
        get
        {
            try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0; }
            catch (Exception) { return false; }
        }
    }
}
