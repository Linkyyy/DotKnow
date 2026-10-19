using System.Windows.Input;
using System.Windows.Interop;
using TypeLingo.Core;

namespace TypeLingo.App.Windows;

internal sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("TypeLingo Hotkeys") { ParentWindow = new nint(-3) });
    private readonly List<int> registered = [];
    public event Action<int>? Pressed;
    public HotkeyService() => source.AddHook(Hook);
    public string? Register(HotkeySettings settings)
    {
        foreach (var id in registered) Native.UnregisterHotKey(source.Handle, id);
        registered.Clear();
        var combinations = new HashSet<(uint, uint)>();
        var bindings = new[] { settings.Replace, settings.Toggle, settings.Copy };
        for (var i = 0; i < bindings.Length; i++)
        {
            if (!TryParse(bindings[i], out var modifiers, out var key) || !combinations.Add((modifiers, key)))
            { Clear(); return "快捷键格式无效或重复，例如 Ctrl+Alt+Enter。"; }
            if (!Native.RegisterHotKey(source.Handle, i + 1, modifiers | 0x4000, key))
            { Clear(); return $"快捷键 {bindings[i]} 已被占用，相关快捷键未启用。请修改设置。"; }
            registered.Add(i + 1);
        }
        return null;
    }
    private void Clear() { foreach (var id in registered) Native.UnregisterHotKey(source.Handle, id); registered.Clear(); }
    internal static bool TryParse(string text, out uint modifiers, out uint key)
    {
        modifiers = key = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch { "ctrl" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u, _ => 0u };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        if (!Enum.TryParse<Key>(parts[^1], true, out var parsed) || parsed is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl) return false;
        key = (uint)KeyInterop.VirtualKeyFromKey(parsed);
        return key != 0;
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.WmHotkey) { handled = true; Pressed?.Invoke((int)wParam); }
        return 0;
    }
    public void Dispose() { Clear(); source.Dispose(); }
}
