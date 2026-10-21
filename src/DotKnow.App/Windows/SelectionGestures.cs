using System.Runtime.InteropServices;

namespace DotKnow.App.Windows;

internal sealed record SelectionGesture(long Id, long SelectionRevision, nint Window, Native.Point Point, bool IsSelection);

/// <summary>Hooks record pointer gestures, including assistive pointer input; text access stays on the observer thread.</summary>
internal sealed class SelectionGestures : IDisposable
{
    private readonly HookProc mouseCallback, keyboardCallback;
    private readonly nint mouseHook, keyboardHook;
    private SelectionGesture current = new(0, 0, 0, default, false);
    private long id, revision;
    private Native.Point down, previousClick;
    private nint downWindow;
    private long previousClickAt;
    private nint previousWindow;
    private bool pressed;
    internal SelectionGesture Current => Volatile.Read(ref current);
    internal event Action? Changed;

    internal SelectionGestures()
    {
        mouseCallback = Mouse; keyboardCallback = Keyboard;
        var module = GetModuleHandle(null);
        mouseHook = SetWindowsHookEx(14, mouseCallback, module, 0);
        keyboardHook = SetWindowsHookEx(13, keyboardCallback, module, 0);
    }
    private bool ExternalWindow(nint window)
    {
        Native.GetWindowThreadProcessId(window, out var pid);
        return pid != 0 && pid != Environment.ProcessId;
    }
    private void Record(nint window, Native.Point point, bool selection)
    {
        Volatile.Write(ref current, new(++id, selection ? ++revision : 0, window, point, selection));
        Changed?.Invoke();
    }
    private nint Mouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<MouseData>(data);
            // Accept assistive pointer input as well as a physical mouse.
            var window = Native.GetForegroundWindow();
            var swapped = System.Windows.Forms.SystemInformation.MouseButtonsSwapped;
            if (message == (swapped ? 0x204 : 0x201) && ExternalWindow(window))
            { down = value.Point; downWindow = window; pressed = true; Record(window, value.Point, false); }
            else if (message == (swapped ? 0x205 : 0x202) && pressed)
            {
                pressed = false;
                if (window == downWindow && ExternalWindow(window))
                {
                    var distance = Math.Abs(value.Point.X - down.X) + Math.Abs(value.Point.Y - down.Y);
                    var doubleClick = previousWindow == window && Environment.TickCount64 - previousClickAt <= System.Windows.Forms.SystemInformation.DoubleClickTime &&
                        Math.Abs(down.X - previousClick.X) < 5 && Math.Abs(down.Y - previousClick.Y) < 5;
                    Record(window, value.Point, distance >= 5 || doubleClick);
                    previousClick = value.Point; previousClickAt = distance < 5 ? Environment.TickCount64 : 0; previousWindow = window;
                }
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    private nint Keyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<KeyboardData>(data);
            if ((value.Flags & 0x10) == 0 && value.Key is not (0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5))
            {
                var window = Native.GetForegroundWindow();
                if (ExternalWindow(window) && Native.GetCursorPos(out var point))
                {
                    var selecting = (message == 0x101 || message == 0x105) && value.Key is >= 0x23 and <= 0x28 && Native.KeyDown(0x10);
                    if (selecting || message == 0x100 || message == 0x104) Record(window, point, selecting);
                }
            }
        }
        return CallNextHookEx(0, code, message, data);
    }
    public void Dispose()
    {
        if (mouseHook != 0) UnhookWindowsHookEx(mouseHook);
        if (keyboardHook != 0) UnhookWindowsHookEx(keyboardHook);
    }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { internal Native.Point Point; internal uint Mouse, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { internal uint Key, Scan, Flags, Time; internal nuint Extra; }
}
