using TypeLingo.Core;

namespace TypeLingo.Tests;

public sealed class PlacementTests
{
    [Fact] public void Default_PrefersRight() => Assert.Equal((312d, 100d), OverlayPlacement.Place(new(100, 100, 200, 40), new(0, 0, 1920, 1080), 380, 220));
    [Fact] public void RightEdge_FallsBelow() => Assert.Equal((800d, 152d), OverlayPlacement.Place(new(800, 100, 200, 40), new(0, 0, 1200, 900), 380, 220));
    [Fact] public void Preference_IsApplied() => Assert.Equal((100d, 152d), OverlayPlacement.Place(new(100, 100, 200, 40), new(0, 0, 1920, 1080), 380, 220, "below"));
    [Fact] public void NegativeMonitorOrigin_IsPreserved() => Assert.Equal((-888d, 100d), OverlayPlacement.Place(new(-1200, 100, 300, 40), new(-1920, 0, 1920, 1080), 380, 220));
    [Fact] public void UnknownAnchor_UsesClampedScreenCorner() => Assert.Equal((800d, 660d), OverlayPlacement.Place(new(0, 0, 0, 0), new(0, 0, 1200, 900), 380, 220));
    [Fact] public void OversizedOverlay_DoesNotThrow() => Assert.Equal((0d, 0d), OverlayPlacement.Place(new(1, 1, 200, 50), new(0, 0, 200, 100), 380, 220));
}
