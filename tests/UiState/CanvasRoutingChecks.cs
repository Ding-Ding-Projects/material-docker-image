using MaterialDockerImage.Pages;

/// <summary>Checks for the Canvas elbow connector routing, stage layout and minimap transform.</summary>
public static class CanvasRoutingChecks
{
    static int count;

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        count++;
        Console.WriteLine("PASS " + name);
    }

    static bool Near(double a, double b) => Math.Abs(a - b) < 0.01;
    static bool Near(GraphPoint a, GraphPoint b) => Near(a.X, b.X) && Near(a.Y, b.Y);

    public static int Run()
    {
        count = 0;
        var sides = Enum.GetValues<PortSide>();
        var samples = new[] { new GraphPoint(300, 100), new GraphPoint(-40, 260), new GraphPoint(100, 100), new GraphPoint(100, -80), new GraphPoint(20, 20) };
        var allOrthogonal = true; var allEndpoints = true; var allShort = true;
        foreach (var fs in sides) foreach (var ts in sides) foreach (var to in samples)
        {
            var route = CanvasRouting.Elbow(new(0, 0), fs, to, ts);
            allOrthogonal &= CanvasRouting.IsOrthogonal(route);
            allEndpoints &= Near(route[0], new(0, 0)) && Near(route[^1], to);
            allShort &= route.Count <= 6;
        }
        Check(allOrthogonal, "every elbow route is made of horizontal and vertical segments");
        Check(allEndpoints, "every elbow route starts and ends at the requested points");
        Check(allShort, "elbow routes use at most four corners");

        var straight = CanvasRouting.Elbow(new(0, 50), PortSide.Right, new(200, 50), PortSide.Left);
        Check(straight.Count == 2, "aligned right-to-left link is a straight line");
        var oneCorner = CanvasRouting.Elbow(new(290, 150), PortSide.Right, new(590, 260), PortSide.Top);
        Check(oneCorner.Count == 3 && Near(oneCorner[1], new(590, 150)), "right-to-top link turns once, as on the design board");
        var upward = CanvasRouting.Elbow(new(290, 540), PortSide.Right, new(590, 470), PortSide.Bottom);
        Check(upward.Count == 3 && Near(upward[1], new(590, 540)) && CanvasRouting.EndDirection(upward).Y < 0, "right-to-bottom link turns once and arrives travelling upward");
        var jog = CanvasRouting.Elbow(new(0, 0), PortSide.Right, new(200, 100), PortSide.Left);
        Check(jog.Count == 4 && Near(jog[1].X, 100) && Near(jog[2].X, 100), "offset right-to-left link jogs at the midpoint");
        var backward = CanvasRouting.Elbow(new(300, 0), PortSide.Right, new(0, 200), PortSide.Left);
        Check(backward[1].X > 300 && backward[^2].X < 0, "backward link steps out of the source and into the target before turning");

        var rounded = CanvasRouting.Round(oneCorner, 12);
        Check(rounded.Count(s => s.IsArc) == 1 && rounded.Single(s => s.IsArc).Clockwise, "a right-then-down corner is one clockwise arc");
        Check(Near(rounded[0].End, new(578, 150)) && Near(rounded[1].End, new(590, 162)), "the corner arc starts and ends one radius from the corner");
        var tight = CanvasRouting.Round(new GraphPoint[] { new(0, 0), new(10, 0), new(10, 6), new(40, 6) }, 12);
        Check(tight.Where(s => s.IsArc).All(s => s.Radius <= 3.01), "corner radius shrinks to half of a short segment");
        Check(Near(CanvasRouting.Round(upward, 12)[^1].End, upward[^1]), "rounded route still ends at the arrow tip");

        var label = CanvasRouting.LabelAnchor(oneCorner);
        Check(label.Horizontal && Near(label.Anchor, new(318, 150)), "label sits 28 px along the first long horizontal run");
        var vertical = CanvasRouting.LabelAnchor(new GraphPoint[] { new(165, 250), new(165, 420) });
        Check(!vertical.Horizontal && Near(vertical.Anchor, new(165, 335)), "label of a vertical link sits beside its midpoint");

        var rects = CanvasRouting.LayeredLayout(3, new[] { (0, 1, true), (0, 2, false), (1, 2, false) }, 250, 176, 180, 96, 40, 76, out var w, out var h);
        Check(Near(rects[0].X, rects[1].X) && rects[1].Y >= rects[0].Bottom + 96, "a FROM child stays in its parent's column, below it");
        Check(rects[2].X > rects[0].Right && rects[2].Y > rects[0].Y && rects[2].Y < rects[1].Y, "a COPY receiver moves right and centres on the stages feeding it");
        Check(w >= rects[2].Right && h >= rects[1].Bottom, "layout size covers every node");
        var overlap = false;
        var many = CanvasRouting.LayeredLayout(6, new[] { (0, 5, false), (1, 5, false), (2, 5, false), (3, 4, false) }, 250, 176, 180, 96, 40, 76, out _, out _);
        for (var i = 0; i < many.Count; i++) for (var j = i + 1; j < many.Count; j++)
            overlap |= Near(many[i].X, many[j].X) && many[i].Y < many[j].Bottom && many[j].Y < many[i].Bottom;
        Check(!overlap, "stage nodes never overlap");
        Check(CanvasRouting.LayeredLayout(0, Array.Empty<(int, int, bool)>(), 250, 176, 180, 96, 40, 76, out var ew, out var eh).Count == 0 && ew == 0 && eh == 0, "an empty graph lays out to nothing");

        var down = CanvasRouting.StageRoute(rects[0], rects[1], true, 0, 1, 0);
        Check(down.Count == 2 && Near(down[0].Y, rects[0].Bottom) && down[^1].Y < rects[1].Y, "neighbouring FROM link runs straight down between the cards");
        var around = CanvasRouting.StageRoute(rects[0], rects[1], false, 0, 1, 0);
        Check(around.Min(p => p.X) < rects[0].X && CanvasRouting.IsOrthogonal(around), "non-neighbouring FROM link goes around the left side");
        var intoTop = CanvasRouting.StageRoute(new(40, 100, 250, 150), new(470, 260, 250, 210), false, 0, 1, 0);
        Check(Near(intoTop[0], new(290, 175)) && intoTop[^1].Y < 260 && CanvasRouting.EndDirection(intoTop).Y > 0, "COPY link from above enters the receiver's top");
        var a = CanvasRouting.StageRoute(new(40, 100, 250, 150), new(470, 260, 250, 210), false, 0, 2, 0);
        var b = CanvasRouting.StageRoute(new(40, 0, 250, 150), new(470, 260, 250, 210), false, 1, 2, 0);
        Check(!Near(a[^1].X, b[^1].X), "two links entering the same edge arrive at different points");

        var map = CanvasRouting.MinimapFit(1000, 500, 158, 108, 8);
        Check(Near(map.Scale, 0.142) && Near(map.OffsetX, 8), "minimap fits wide content to its width");
        var back = CanvasRouting.FromMap(new(map.OffsetX + 71, map.OffsetY + 35.5), map);
        Check(Near(back, new(500, 250)), "minimap points map back to content coordinates");
        var mapped = CanvasRouting.ToMap(new(100, 50, 200, 100), map);
        Check(Near(mapped.Width, 28.4) && Near(mapped.X, map.OffsetX + 14.2), "content rectangles scale into the minimap");
        var visible = CanvasRouting.VisibleContent(208, 8, 400, 300, 2, 8);
        Check(Near(visible.X, 100) && Near(visible.Y, 0) && Near(visible.Width, 200) && Near(visible.Height, 150), "the viewport is measured in content coordinates at the current zoom");
        var (hOff, vOff) = CanvasRouting.ScrollToCenter(new(300, 200), 400, 300, 2, 8);
        Check(Near(hOff, 408) && Near(vOff, 258), "centering scrolls the point to the middle of the viewport");
        Check(CanvasRouting.ScrollToCenter(new(0, 0), 400, 300, 1, 8) == (0, 0), "centering never scrolls to a negative offset");
        var clipped = CanvasRouting.Clip(new(-50, -50, 2000, 2000), new(0, 0, 1000, 500));
        Check(clipped == new GraphRect(0, 0, 1000, 500), "the viewport box is clipped to the drawing");
        return count;
    }
}
