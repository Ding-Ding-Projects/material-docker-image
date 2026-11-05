namespace MaterialDockerImage.Pages;

/// <summary>The side of a node rectangle a connector leaves from or enters through.</summary>
public enum PortSide { Left, Top, Right, Bottom }

/// <summary>A point in graph content coordinates (y grows downward).</summary>
public readonly record struct GraphPoint(double X, double Y);

/// <summary>An axis-aligned rectangle in graph content coordinates.</summary>
public readonly record struct GraphRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>One drawing step of a rounded route: a straight line, or a quarter arc that turns a corner.</summary>
public readonly record struct RouteSegment(GraphPoint End, bool IsArc, double Radius, bool Clockwise);

/// <summary>Scale and offset that map graph content into the minimap.</summary>
public readonly record struct MinimapScale(double Scale, double OffsetX, double OffsetY);

/// <summary>
/// Framework-free geometry for the Canvas destination: orthogonal (elbow) connector routes with rounded corners,
/// label anchors, arrow directions, and the minimap's content-to-map transform. Kept free of WPF types so it can be
/// checked headlessly.
/// </summary>
public static class CanvasRouting
{
    private const double Epsilon = 0.5;

    private static (double X, double Y) Direction(PortSide side) => side switch
    {
        PortSide.Left => (-1, 0), PortSide.Right => (1, 0), PortSide.Top => (0, -1), _ => (0, 1)
    };

    private static bool AheadOrEqual(double from, double to, double direction) => (to - from) * direction >= -Epsilon;

    /// <summary>
    /// Orthogonal route from <paramref name="from"/>, leaving through <paramref name="fromSide"/>, to <paramref name="to"/>,
    /// entering through <paramref name="toSide"/>. Every segment is horizontal or vertical. The simplest shape that moves
    /// forward is preferred (straight, one corner, then a centred two-corner jog); otherwise the route steps out by
    /// <paramref name="stub"/> and goes around.
    /// </summary>
    public static IReadOnlyList<GraphPoint> Elbow(GraphPoint from, PortSide fromSide, GraphPoint to, PortSide toSide, double stub = 20)
    {
        var dOut = Direction(fromSide);
        var entry = Direction(toSide);
        var dIn = (X: -entry.X, Y: -entry.Y);
        var hOut = dOut.Y == 0;
        var hIn = dIn.Y == 0;
        var points = new List<GraphPoint> { from };

        if (hOut && hIn && dOut == dIn && Math.Abs(from.Y - to.Y) < Epsilon && (to.X - from.X) * dOut.X > Epsilon)
        { points.Add(to); return Simplify(points); }
        if (!hOut && !hIn && dOut == dIn && Math.Abs(from.X - to.X) < Epsilon && (to.Y - from.Y) * dOut.Y > Epsilon)
        { points.Add(to); return Simplify(points); }

        if (hOut && !hIn && AheadOrEqual(from.X, to.X, dOut.X) && (to.Y - from.Y) * dIn.Y > Epsilon)
        { points.Add(new(to.X, from.Y)); points.Add(to); return Simplify(points); }
        if (!hOut && hIn && AheadOrEqual(from.Y, to.Y, dOut.Y) && (to.X - from.X) * dIn.X > Epsilon)
        { points.Add(new(from.X, to.Y)); points.Add(to); return Simplify(points); }

        if (hOut && hIn && dOut == dIn && (to.X - from.X) * dOut.X > 2 * Epsilon)
        {
            var mx = (from.X + to.X) / 2;
            points.Add(new(mx, from.Y)); points.Add(new(mx, to.Y)); points.Add(to);
            return Simplify(points);
        }
        if (!hOut && !hIn && dOut == dIn && (to.Y - from.Y) * dOut.Y > 2 * Epsilon)
        {
            var my = (from.Y + to.Y) / 2;
            points.Add(new(from.X, my)); points.Add(new(to.X, my)); points.Add(to);
            return Simplify(points);
        }

        // Fallback: step out of the source, step back from the target, and connect the two stubs around the nodes.
        var p1 = new GraphPoint(from.X + dOut.X * stub, from.Y + dOut.Y * stub);
        var p2 = new GraphPoint(to.X - dIn.X * stub, to.Y - dIn.Y * stub);
        points.Add(p1);
        if (hOut && hIn)
        {
            if (dOut != dIn)
            {
                var mx = dOut.X > 0 ? Math.Max(p1.X, p2.X) : Math.Min(p1.X, p2.X);
                points.Add(new(mx, p1.Y)); points.Add(new(mx, p2.Y));
            }
            else
            {
                var my = (from.Y + to.Y) / 2;
                points.Add(new(p1.X, my)); points.Add(new(p2.X, my));
            }
        }
        else if (!hOut && !hIn)
        {
            if (dOut != dIn)
            {
                var my = dOut.Y > 0 ? Math.Max(p1.Y, p2.Y) : Math.Min(p1.Y, p2.Y);
                points.Add(new(p1.X, my)); points.Add(new(p2.X, my));
            }
            else
            {
                var mx = (from.X + to.X) / 2;
                points.Add(new(mx, p1.Y)); points.Add(new(mx, p2.Y));
            }
        }
        else if (hOut) points.Add(new(p2.X, p1.Y));
        else points.Add(new(p1.X, p2.Y));
        points.Add(p2);
        points.Add(to);
        return Simplify(points);
    }

    /// <summary>Removes repeated points and middle points that lie on a straight run.</summary>
    public static IReadOnlyList<GraphPoint> Simplify(IReadOnlyList<GraphPoint> points)
    {
        var result = new List<GraphPoint>();
        foreach (var p in points)
        {
            if (result.Count > 0 && Same(result[^1], p)) continue;
            if (result.Count >= 2 && Collinear(result[^2], result[^1], p)) result[^1] = p;
            else result.Add(p);
        }
        return result;
    }

    private static bool Same(GraphPoint a, GraphPoint b) => Math.Abs(a.X - b.X) < Epsilon && Math.Abs(a.Y - b.Y) < Epsilon;

    private static bool Collinear(GraphPoint a, GraphPoint b, GraphPoint c) =>
        (Math.Abs(a.X - b.X) < Epsilon && Math.Abs(b.X - c.X) < Epsilon && (b.Y - a.Y) * (c.Y - b.Y) >= 0) ||
        (Math.Abs(a.Y - b.Y) < Epsilon && Math.Abs(b.Y - c.Y) < Epsilon && (b.X - a.X) * (c.X - b.X) >= 0);

    /// <summary>True when every segment of the route is horizontal or vertical.</summary>
    public static bool IsOrthogonal(IReadOnlyList<GraphPoint> points)
    {
        for (var i = 1; i < points.Count; i++)
            if (Math.Abs(points[i].X - points[i - 1].X) >= Epsilon && Math.Abs(points[i].Y - points[i - 1].Y) >= Epsilon) return false;
        return true;
    }

    /// <summary>
    /// Turns an orthogonal route into drawing steps with each corner rounded. A corner radius never exceeds half of
    /// either adjacent segment, so short jogs stay inside their own segments.
    /// </summary>
    public static IReadOnlyList<RouteSegment> Round(IReadOnlyList<GraphPoint> points, double radius)
    {
        var segments = new List<RouteSegment>();
        if (points.Count < 2) return segments;
        for (var i = 1; i < points.Count - 1; i++)
        {
            var a = points[i - 1]; var b = points[i]; var c = points[i + 1];
            var inLength = Length(a, b); var outLength = Length(b, c);
            var r = Math.Max(0, Math.Min(radius, Math.Min(inLength, outLength) / 2));
            var d1 = Unit(a, b); var d2 = Unit(b, c);
            if (r < Epsilon) { segments.Add(new(b, false, 0, false)); continue; }
            segments.Add(new(new(b.X - d1.X * r, b.Y - d1.Y * r), false, 0, false));
            var cross = d1.X * d2.Y - d1.Y * d2.X;
            segments.Add(new(new(b.X + d2.X * r, b.Y + d2.Y * r), true, r, cross > 0));
        }
        segments.Add(new(points[^1], false, 0, false));
        return segments;
    }

    private static double Length(GraphPoint a, GraphPoint b) => Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y);

    private static (double X, double Y) Unit(GraphPoint a, GraphPoint b)
    {
        var length = Length(a, b);
        return length < Epsilon ? (0, 0) : ((b.X - a.X) / length, (b.Y - a.Y) / length);
    }

    /// <summary>Direction of travel of the final segment, used to point the arrowhead.</summary>
    public static (double X, double Y) EndDirection(IReadOnlyList<GraphPoint> points) =>
        points.Count < 2 ? (1, 0) : Unit(points[^2], points[^1]);

    /// <summary>
    /// Where a connector's label pill sits: on the first horizontal run that is long enough, a little past its start,
    /// or else beside the middle of the longest vertical run. <c>Horizontal</c> tells the caller which.
    /// </summary>
    public static (GraphPoint Anchor, bool Horizontal) LabelAnchor(IReadOnlyList<GraphPoint> points, double inset = 28, double minimumRun = 120)
    {
        if (points.Count < 2) return (points.Count == 1 ? points[0] : new(0, 0), true);
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i];
            if (Math.Abs(a.Y - b.Y) < Epsilon && Math.Abs(b.X - a.X) >= minimumRun)
                return (new(Math.Min(a.X, b.X) + inset, a.Y), true);
        }
        var best = 1; var bestLength = -1.0;
        for (var i = 1; i < points.Count; i++)
        {
            var length = Length(points[i - 1], points[i]);
            if (length > bestLength) { bestLength = length; best = i; }
        }
        var p = points[best - 1]; var q = points[best];
        return (new((p.X + q.X) / 2, (p.Y + q.Y) / 2), Math.Abs(p.Y - q.Y) < Epsilon);
    }

    /// <summary>
    /// Places stage nodes in columns. A link marked <c>KeepColumn</c> (a stage built FROM an earlier stage) keeps the
    /// child in its parent's column, below it; any other link (COPY --from) moves the receiver one column right. In
    /// columns after the first, a node is centred on the stages that feed it when there is room, as on the design
    /// board; nodes never overlap. Edges must point from a lower to a higher index; others are ignored.
    /// </summary>
    public static IReadOnlyList<GraphRect> LayeredLayout(int count, IReadOnlyList<(int From, int To, bool KeepColumn)> edges,
        double nodeWidth, double nodeHeight, double columnGap, double rowGap, double left, double top, out double width, out double height)
    {
        var column = new int[count];
        var usable = edges.Where(e => e.From >= 0 && e.To < count && e.From < e.To).ToList();
        for (var pass = 0; pass <= count; pass++)
        {
            var changed = false;
            foreach (var e in usable)
            {
                var wanted = column[e.From] + (e.KeepColumn ? 0 : 1);
                if (column[e.To] < wanted && wanted < count) { column[e.To] = wanted; changed = true; }
            }
            if (!changed) break;
        }
        var y = new double[count];
        var columns = count == 0 ? 0 : column.Max() + 1;
        for (var c = 0; c < columns; c++)
        {
            var bottom = double.NegativeInfinity;
            for (var i = 0; i < count; i++)
            {
                if (column[i] != c) continue;
                var desired = top;
                var feeders = usable.Where(e => e.To == i && column[e.From] < c).Select(e => y[e.From] + nodeHeight / 2).ToList();
                if (feeders.Count > 0) desired = Math.Max(top, feeders.Average() - nodeHeight / 2);
                y[i] = double.IsNegativeInfinity(bottom) ? desired : Math.Max(desired, bottom + rowGap);
                bottom = y[i] + nodeHeight;
            }
        }
        var rects = new GraphRect[count];
        for (var i = 0; i < count; i++) rects[i] = new(left + column[i] * (nodeWidth + columnGap), y[i], nodeWidth, nodeHeight);
        width = count == 0 ? 0 : rects.Max(r => r.Right) + left;
        height = count == 0 ? 0 : rects.Max(r => r.Bottom) + top;
        return rects;
    }

    /// <summary>
    /// Route between two stage rectangles. Same-column links run straight down from the parent's bottom to the
    /// child's top when they are neighbours, and around the left side otherwise. Links to a column on the right
    /// leave the source's right side and enter the target's top or bottom (or its left side when the source's centre
    /// lines up with it), spreading several entries across the edge by <paramref name="entryIndex"/>.
    /// </summary>
    public static IReadOnlyList<GraphPoint> StageRoute(GraphRect source, GraphRect target, bool sameColumnNeighbours, int entryIndex, int entryCount, double laneOffset)
    {
        if (Math.Abs(source.X - target.X) < Epsilon)
        {
            if (sameColumnNeighbours && target.Y > source.Bottom)
                return Elbow(new(source.CenterX, source.Bottom), PortSide.Bottom, new(target.CenterX, target.Y - 1), PortSide.Top);
            return Elbow(new(source.X, source.CenterY + laneOffset), PortSide.Left, new(target.X - 1, target.CenterY + laneOffset), PortSide.Left, 20);
        }
        var startY = source.CenterY + laneOffset;
        if (target.X > source.Right)
        {
            if (startY > target.Y + 20 && startY < target.Bottom - 20)
                return Elbow(new(source.Right, startY), PortSide.Right, new(target.X - 1, startY), PortSide.Left);
            var spread = Math.Min(28, (target.Width - 40) / Math.Max(1, entryCount));
            var entryX = target.CenterX + (entryIndex - (entryCount - 1) / 2.0) * spread;
            return startY <= target.Y
                ? Elbow(new(source.Right, startY), PortSide.Right, new(entryX, target.Y - 1), PortSide.Top)
                : Elbow(new(source.Right, startY), PortSide.Right, new(entryX, target.Bottom + 1), PortSide.Bottom);
        }
        return Elbow(new(source.Right, startY), PortSide.Right, new(target.X - 1, target.CenterY + laneOffset), PortSide.Left);
    }

    /// <summary>Fits content of the given size into a minimap of the given size, centred, with an inner inset.</summary>
    public static MinimapScale MinimapFit(double contentWidth, double contentHeight, double mapWidth, double mapHeight, double inset)
    {
        var innerW = Math.Max(1, mapWidth - 2 * inset);
        var innerH = Math.Max(1, mapHeight - 2 * inset);
        if (contentWidth <= 0 || contentHeight <= 0) return new(1, inset, inset);
        var scale = Math.Min(innerW / contentWidth, innerH / contentHeight);
        return new(scale, inset + (innerW - contentWidth * scale) / 2, inset + (innerH - contentHeight * scale) / 2);
    }

    public static GraphRect ToMap(GraphRect content, MinimapScale map) =>
        new(map.OffsetX + content.X * map.Scale, map.OffsetY + content.Y * map.Scale, content.Width * map.Scale, content.Height * map.Scale);

    public static GraphPoint FromMap(GraphPoint point, MinimapScale map) =>
        new((point.X - map.OffsetX) / map.Scale, (point.Y - map.OffsetY) / map.Scale);

    /// <summary>The visible part of the graph in content coordinates, from scroll offsets measured in zoomed pixels.</summary>
    public static GraphRect VisibleContent(double horizontalOffset, double verticalOffset, double viewportWidth, double viewportHeight, double zoom, double padding)
    {
        zoom = zoom <= 0 ? 1 : zoom;
        return new((horizontalOffset - padding) / zoom, (verticalOffset - padding) / zoom, viewportWidth / zoom, viewportHeight / zoom);
    }

    /// <summary>Scroll offsets that centre the viewport on a content point; never negative.</summary>
    public static (double Horizontal, double Vertical) ScrollToCenter(GraphPoint content, double viewportWidth, double viewportHeight, double zoom, double padding)
    {
        zoom = zoom <= 0 ? 1 : zoom;
        return (Math.Max(0, content.X * zoom + padding - viewportWidth / 2), Math.Max(0, content.Y * zoom + padding - viewportHeight / 2));
    }

    /// <summary>Clips <paramref name="rect"/> to <paramref name="bounds"/>; an empty result keeps zero size at the nearest edge.</summary>
    public static GraphRect Clip(GraphRect rect, GraphRect bounds)
    {
        var x = Math.Clamp(rect.X, bounds.X, bounds.Right);
        var y = Math.Clamp(rect.Y, bounds.Y, bounds.Bottom);
        var right = Math.Clamp(rect.Right, bounds.X, bounds.Right);
        var bottom = Math.Clamp(rect.Bottom, bounds.Y, bounds.Bottom);
        return new(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
