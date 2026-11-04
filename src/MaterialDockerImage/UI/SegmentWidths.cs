namespace MaterialDockerImage.UI;

/// <summary>Width allocation for SegmentPanel, kept free of WPF so the rules can be checked on their own.</summary>
public static class SegmentWidths
{
    public static double[] Allocate(IReadOnlyList<double> natural, double available)
    {
        var count = natural.Count;
        if (count == 0) return [];
        var widest = natural.Max();
        var total = natural.Sum();
        if (double.IsInfinity(available) || widest * count <= available)
        {
            var each = double.IsInfinity(available) ? widest : available / count;
            return Enumerable.Repeat(each, count).ToArray();
        }
        if (total <= available)
        {
            var extra = (available - total) / count;
            return natural.Select(w => w + extra).ToArray();
        }
        var scale = available / total;
        return natural.Select(w => w * scale).ToArray();
    }
}
