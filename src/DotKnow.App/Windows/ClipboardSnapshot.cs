using System.Runtime.InteropServices;
using System.Text;

namespace DotKnow.App.Windows;

/// <summary>Materializes public clipboard formats before copy, without deserializing application objects.</summary>
internal sealed class ClipboardSnapshot : IDisposable
{
    private readonly List<(uint Format, nint Handle)> formats = [];
    internal uint Sequence { get; private set; }
    private ClipboardSnapshot() { }

    internal static ClipboardSnapshot? Capture(nint owner)
    {
        if (!TryOpen(owner)) return null;
        var snapshot = new ClipboardSnapshot();
        try
        {
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                var name = new StringBuilder(256);
                GetClipboardFormatName(format, name, name.Capacity);
                // These are OLE transport references, not content. Restored public formats get fresh wrappers.
                if (name.ToString() is "DataObject" or "Ole Private Data" or "DataObjectAttributes" or "DataObjectAttributesRequiringElevation") continue;
                var original = GetClipboardData(format);
                var duplicate = original == 0 ? 0 : OleDuplicateData(original, format, 2); // GMEM_MOVEABLE
                if (duplicate == 0) { snapshot.Dispose(); return null; }
                snapshot.formats.Add((format, duplicate));
            }
            snapshot.Sequence = GetClipboardSequenceNumber();
            return snapshot;
        }
        finally { CloseClipboard(); }
    }

    internal bool Restore(nint owner, uint expectedSequence)
    {
        if (!TryOpen(owner)) return false;
        try
        {
            // The check and replacement share the clipboard lock, so a concurrent copy cannot be overwritten.
            if (GetClipboardSequenceNumber() != expectedSequence) return true;
            if (!EmptyClipboard()) return false;
            var success = true;
            for (var i = 0; i < formats.Count; i++)
            {
                var (format, handle) = formats[i];
                if (SetClipboardData(format, handle) != 0) formats[i] = (format, 0); // ownership transfers to Windows
                else success = false;
            }
            return success;
        }
        finally { CloseClipboard(); }
    }

    public void Dispose()
    {
        foreach (var (format, handle) in formats)
        {
            if (handle == 0) continue;
            if (format is 2 or 9 or 0x82) DeleteObject(handle);
            else if (format is 14 or 0x8E) DeleteEnhMetaFile(handle);
            else
            {
                if (format is 3 or 0x83)
                {
                    var address = GlobalLock(handle);
                    if (address != 0)
                    {
                        var picture = Marshal.PtrToStructure<MetafilePicture>(address);
                        DeleteMetaFile(picture.Metafile); GlobalUnlock(handle);
                    }
                }
                GlobalFree(handle);
            }
        }
        formats.Clear();
    }
    private static bool TryOpen(nint owner)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        { if (OpenClipboard(owner)) return true; Thread.Sleep(15); }
        return false;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MetafilePicture { internal int Mode, X, Y; internal nint Metafile; }
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int length);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("ole32.dll")] private static extern nint OleDuplicateData(nint source, uint format, uint flags);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteMetaFile(nint value);
}
