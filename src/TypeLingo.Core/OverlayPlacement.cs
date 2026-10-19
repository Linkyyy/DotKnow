namespace TypeLingo.Core;

public static class OverlayPlacement
{
    // 全部坐标均为屏幕物理像素，包含位于主屏左侧等情况产生的负坐标。
    public static (double X, double Y) Place(InputRect anchor, InputRect area, double width, double height, string preference = "right", InputRect? avoid = null)
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
        (double X, double Y) Clamp((double X, double Y) p) =>
            (Math.Clamp(p.X, area.X, Math.Max(area.X, area.X + area.Width - width)),
             Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height)));
        if (avoid == null) return Clamp(chosen);
        double Overlap((double X, double Y) p, InputRect r) =>
            Math.Max(0, Math.Min(p.X + width, r.X + r.Width) - Math.Max(p.X, r.X)) *
            Math.Max(0, Math.Min(p.Y + height, r.Y + r.Height) - Math.Max(p.Y, r.Y));
        // 先保留用户位置偏好；容纳不下时尝试输入框外侧和屏幕边缘，优先保护光标或选区。
        var candidates = new List<(double X, double Y)> { chosen };
        candidates.AddRange(new[] { preference, "right", "below", "left", "above" }.Distinct().Where(positions.ContainsKey).Select(x => positions[x]));
        candidates.AddRange(new[] { (avoid.X + avoid.Width + gap, anchor.Y), (avoid.X - width - gap, anchor.Y),
            (anchor.X, avoid.Y - height - gap), (anchor.X, avoid.Y + avoid.Height + gap),
            (area.X + area.Width - width - gap, area.Y + gap), (area.X + gap, area.Y + area.Height - height - gap) });
        return candidates.Select(Clamp).OrderBy(p => Overlap(p, anchor) * 1000 + Overlap(p, avoid)).First();
    }
}
