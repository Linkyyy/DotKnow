using System.Runtime.InteropServices;

namespace TypeLingo.App.Windows;

internal static class Native
{
    internal const int WmHotkey = 0x0312, WmMouseActivate = 0x21, MaNoActivate = 3;
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint id, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)] private static extern int ImmGetCompositionStringW(nint context, uint index, nint buffer, uint size);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal Rect CaretRect;
    }
    internal static bool IsComposing(nint foreground)
    {
        var thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var hwnd = GetGUIThreadInfo(thread, ref info) && info.Focus != 0 ? info.Focus : foreground;
        var context = ImmGetContext(hwnd);
        if (context == 0) return false; // 现代 TSF 控件可能不公开 IMM 组合输入上下文。
        try { return ImmGetCompositionStringW(context, 8, 0, 0) > 0; }
        finally { ImmReleaseContext(hwnd, context); }
    }
    internal static bool CanVerifyCompositionForWrite(nint foreground, string className, int nativeHandle)
    {
        var thread = GetWindowThreadProcessId(foreground, out _);
        var primaryLanguage = (long)GetKeyboardLayout(thread) & 0x3ff;
        if (primaryLanguage is not (0x04 or 0x11 or 0x12)) return true;
        // 仅使用 TSF 的虚拟控件不提供可靠的跨进程组合输入状态。
        // 在东亚语言输入布局下，即使存在 ValuePattern 也只能预览和复制。
        if (nativeHandle == 0 || !(className.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
            className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase))) return false;
        var context = ImmGetContext(new nint(nativeHandle));
        if (context == 0) return false;
        try { return ImmGetCompositionStringW(context, 8, 0, 0) == 0; }
        finally { ImmReleaseContext(new nint(nativeHandle), context); }
    }
}
