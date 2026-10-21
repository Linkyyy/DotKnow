namespace DotKnow.Core;

/// <summary>A hidden window stays hidden across focus changes and temporary missing UIA snapshots.</summary>
public sealed class SelectionDismissal
{
    private readonly List<InputSnapshot> dismissed = [];

    public void Dismiss(InputSnapshot? snapshot)
    {
        if (snapshot == null) return;
        dismissed.RemoveAll(x => SameSelection(x, snapshot));
        dismissed.Add(snapshot);
        if (dismissed.Count > 32) dismissed.RemoveAt(0);
    }

    public bool Allows(InputSnapshot? snapshot, bool explicitRequest = false)
    {
        if (snapshot == null) return true;
        var previous = dismissed.FirstOrDefault(x => SameSelection(x, snapshot));
        if (previous == null) return true;
        if (!explicitRequest && snapshot.SelectionRevision <= previous.SelectionRevision) return false;
        dismissed.Remove(previous);
        return true;
    }
    private static bool SameSelection(InputSnapshot first, InputSnapshot second) =>
        first.SelectionSource.Length > 0 && second.SelectionSource.Length > 0
            ? first.SelectionSource == second.SelectionSource && first.Text == second.Text
            : first.SameContent(second);
}
