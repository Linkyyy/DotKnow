namespace DotKnow.Core;

/// <summary>A hidden window stays hidden across focus changes and temporary missing UIA snapshots.</summary>
public sealed class SelectionDismissal
{
    private readonly List<InputSnapshot> dismissed = [];

    public void Dismiss(InputSnapshot? snapshot)
    {
        if (snapshot == null) return;
        dismissed.RemoveAll(x => x.SameContent(snapshot));
        dismissed.Add(snapshot);
        if (dismissed.Count > 32) dismissed.RemoveAt(0);
    }

    public bool Allows(InputSnapshot? snapshot, bool explicitRequest = false)
    {
        if (snapshot == null) return true;
        var previous = dismissed.FirstOrDefault(x => x.SameContent(snapshot));
        if (previous == null) return true;
        if (!explicitRequest && snapshot.SelectionRevision <= previous.SelectionRevision) return false;
        dismissed.Remove(previous);
        return true;
    }
}
