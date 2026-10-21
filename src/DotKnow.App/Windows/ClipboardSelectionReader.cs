using System.Runtime.InteropServices;
using System.Windows;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DotKnow.App.Windows;

/// <summary>One user-authorized copy. Never selects all, pastes, writes to the app, or reads stale clipboard text.</summary>
internal static class ClipboardSelectionReader
{
    internal static Task<string?> ReadAsync(nint window, int maxCharacters, Func<bool> stillAllowed, Action<string> status, Action<string>? diagnostics = null)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            string? result = null;
            uint copiedSequence = 0;
            IDataObject? previous = null;
            var stage = "initial";
            var initialized = OleInitialize(0) >= 0;
            try
            {
                if (!initialized || !Valid()) return;
                // A hotkey is delivered before its modifiers are released. Do not send Ctrl+C with Alt/Shift held.
                var releaseDeadline = Environment.TickCount64 + 600;
                while (ModifiersDown() && Environment.TickCount64 < releaseDeadline && Valid()) Thread.Sleep(15);
                if (ModifiersDown() || !Valid()) return;
                var originalSequence = GetClipboardSequenceNumber();
                var saved = OleGetClipboard(out previous);
                stage = $"backup:{saved:X8}";
                if (saved < 0 || !Valid() || GetClipboardSequenceNumber() != originalSequence) return;
                var input = new[] { Key(0x11), Key(0x43), Key(0x43, true), Key(0x11, true) };
                var sent = SendInput((uint)input.Length, input, Marshal.SizeOf<Input>());
                stage = $"copy:{sent}";
                if (sent == 0) return;
                if (sent != input.Length)
                {
                    var release = new[] { Key(0x43, true), Key(0x11, true) };
                    SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
                }
                var deadline = Environment.TickCount64 + 700;
                while (Environment.TickCount64 < deadline)
                {
                    var sequence = GetClipboardSequenceNumber();
                    if (sequence != originalSequence)
                    {
                        Native.GetWindowThreadProcessId(GetClipboardOwner(), out var ownerPid);
                        Native.GetWindowThreadProcessId(window, out var targetPid);
                        stage = $"owner:{ownerPid}/{targetPid}";
                        if (ownerPid == 0 || ownerPid != targetPid) return;
                        copiedSequence = sequence;
                        if (!Valid()) return;
                        try
                        {
                            if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                            {
                                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                                // Delayed OLE rendering may advance the sequence during GetText itself.
                                // Accept it only while the same app still owns the clipboard and the gesture is unchanged.
                                var renderedSequence = GetClipboardSequenceNumber();
                                Native.GetWindowThreadProcessId(GetClipboardOwner(), out var renderedOwner);
                                stage = $"read:owner={renderedOwner == targetPid}; stable={renderedSequence == sequence}; length={text.Length}";
                                if (renderedOwner != targetPid || !Valid()) return;
                                copiedSequence = renderedSequence;
                                if (!string.IsNullOrWhiteSpace(text) && text.Length <= maxCharacters) result = text;
                                return;
                            }
                        }
                        catch (COMException) { }
                    }
                    if (!Valid()) return;
                    Thread.Sleep(15);
                }
            }
            catch (Exception e) when (e is COMException or ExternalException or InvalidOperationException) { }
            finally
            {
                // Preserve concurrent user/app copies. Keep the original OLE data object, including non-text formats.
                if (copiedSequence != 0 && GetClipboardSequenceNumber() == copiedSequence)
                {
                    var restored = false;
                    for (var attempt = 0; attempt < 5 && GetClipboardSequenceNumber() == copiedSequence; attempt++)
                    {
                        if (OleSetClipboard(previous) >= 0) { restored = previous == null || OleFlushClipboard() >= 0; break; }
                        Thread.Sleep(15);
                    }
                    if (!restored) status("微信选区已读取，但剪贴板恢复失败，请检查剪贴板内容。");
                }
                if (previous != null && Marshal.IsComObject(previous)) Marshal.ReleaseComObject(previous);
                if (initialized) OleUninitialize();
                diagnostics?.Invoke($"{stage}; result={result != null}; foreground={Native.GetForegroundWindow() == window}; modifiers={ModifiersDown()}; copied={copiedSequence != 0}");
                completion.TrySetResult(result);
            }
            bool Valid() => Native.GetForegroundWindow() == window && stillAllowed() && !Native.IsSelecting && !Native.IsComposing(window);
        }) { IsBackground = true, Name = "DotKnow selection copy" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    private static bool ModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(Native.KeyDown);
    private static Input Key(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0 } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    { [FieldOffset(0)] internal KeyboardInput Keyboard; [FieldOffset(0)] internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int OleGetClipboard(out IDataObject? data);
    [DllImport("ole32.dll")] private static extern int OleSetClipboard(IDataObject? data);
    [DllImport("ole32.dll")] private static extern int OleFlushClipboard();
}
