using DotKnow.Core;

namespace DotKnow.Tests;

public sealed class SelectionDismissalTests
{
    private static InputSnapshot Selection(long revision = 1) => new("wechat:window:selection", "同一段文字",
        new(10, 10, 100, 20), CanWrite: false, IsSelection: true, SelectionRevision: revision);

    [Fact] public void DismissedSelection_StaysClosedAcrossNullAndFocusChanges()
    {
        var gate = new SelectionDismissal(); gate.Dismiss(Selection());
        Assert.True(gate.Allows(null));
        Assert.True(gate.Allows(Selection() with { Identity = "another-app" }));
        Assert.False(gate.Allows(Selection() with { Bounds = new(90, 80, 200, 30) }));
        Assert.False(gate.Allows(Selection(0)));
    }

    [Fact] public void ReselectOrExplicitShortcut_CanReopenTheSameText()
    {
        var gate = new SelectionDismissal(); gate.Dismiss(Selection());
        Assert.True(gate.Allows(Selection(2))); Assert.True(gate.Allows(Selection(2)));
        gate.Dismiss(Selection(2)); Assert.True(gate.Allows(Selection(2), explicitRequest: true));
        gate.Dismiss(Selection(2)); Assert.True(gate.Allows(Selection(2) with { Text = "另一段文字" }));
    }

    [Fact] public void ClosingAnotherApplication_DoesNotForgetPreviousDismissals()
    {
        var gate = new SelectionDismissal(); var other = Selection() with { Identity = "other-window" };
        gate.Dismiss(Selection()); gate.Dismiss(other);
        Assert.False(gate.Allows(Selection())); Assert.False(gate.Allows(other));
    }
}
