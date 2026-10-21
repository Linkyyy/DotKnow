using System.Runtime.InteropServices;
using System.Windows;
using DotKnow.App.Windows;

namespace DotKnow.UiSmoke;

// 仅用于独立验收窗口的鼠标操作；生产程序不模拟输入或读取剪贴板。
internal sealed class PointerFixture : IDisposable
{
    private readonly Native.Point original;
    private bool pressed;
    public PointerFixture() { Native.GetCursorPos(out original); }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    public static void Move(Point point)
    {
        if (!SetCursorPos((int)Math.Round(point.X), (int)Math.Round(point.Y)))
            throw new InvalidOperationException("验收桌面无法移动鼠标指针");
        mouse_event(0x0001, 0, 0, 0, 0);
    }
    public void Press() { pressed = true; mouse_event(GetSystemMetrics(23) == 0 ? 0x0002u : 0x0008u, 0, 0, 0, 0); }
    public void Release() { if (pressed) mouse_event(GetSystemMetrics(23) == 0 ? 0x0004u : 0x0010u, 0, 0, 0, 0); pressed = false; }
    public void Dispose() { Release(); SetCursorPos(original.X, original.Y); }
}
