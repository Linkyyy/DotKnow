namespace TypeLingo.Core;

public static class OverlayPlacement
{
    // 全部坐标均为屏幕物理像素，包含位于主屏左侧等情况产生的负坐标。
    public static (double X, double Y) Place(InputRect anchor, InputRect area, double width, double height, string preference = "right")
    {
        const double gap = 12;
        var positions = new Dictionary<string, (double X, double Y)>
        {
            ["right"] = (anchor.X + anchor.Width + gap, anchor.Y),
            ["below"] = (anchor.X, anchor.Y + anchor.Height + gap),
            ["left"] = (anchor.X - width - gap, anchor.Y),
            ["above"] = (anchor.X, anchor.Y - height - gap)
        };
        var chosen = (X: area.X + area.Width - width - 20, Y: area.Y + area.Height - height - 20);
        if (anchor.Width > 0 && anchor.Height > 0)
            foreach (var name in new[] { preference, "right", "below", "left", "above" }.Distinct())
            {
                if (!positions.TryGetValue(name, out var p)) continue;
                if (p.X >= area.X && p.Y >= area.Y && p.X + width <= area.X + area.Width && p.Y + height <= area.Y + area.Height)
                { chosen = p; break; }
            }
        return (Math.Clamp(chosen.X, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(chosen.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height)));
    }
}
