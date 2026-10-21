namespace DotKnow.Core;

/// <summary>快捷键仅授权当前一次内容；编辑、焦点变化、暂停后必须重新触发。</summary>
public sealed class PreviewActivation
{
    private InputSnapshot? requested;
    private string mode = "selection";
    public bool ShouldCapture(string triggerMode)
    {
        if (mode != triggerMode) { Reset(); mode = triggerMode; }
        return mode != "hotkey" || requested != null;
    }
    public InputSnapshot? Filter(InputSnapshot? snapshot, string triggerMode, bool explicitRequest = false)
    {
        ShouldCapture(triggerMode);
        if (snapshot is { IsSensitive: true } or { IsComposing: true } || string.IsNullOrWhiteSpace(snapshot?.Text)) snapshot = null;
        if (mode == "selection") return snapshot is { IsSelection: true } ? snapshot : null;
        if (mode != "hotkey") return snapshot;
        if (explicitRequest) requested = snapshot;
        if (snapshot == null || requested == null || !requested.SameContent(snapshot))
        { Reset(); return null; }
        return snapshot;
    }
    public void Reset() => requested = null;
}
