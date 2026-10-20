using System.Drawing;
using System.Windows;

namespace TypeLingo.App.Windows;

internal static class BrandAssets
{
    // 托盘使用透明单色主标；任务栏深色用白标，浅色用黑标。
    internal static Icon CreateTrayIcon(bool darkTaskbar)
    {
        var name = darkTaskbar ? "typelingo-tray-white.ico" : "typelingo-tray-black.ico";
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/TypeLingo;component/Assets/{name}", UriKind.Absolute)).Stream;
        using var source = new Icon(stream, 32, 32);
        return (Icon)source.Clone();
    }
}
