namespace Jalium.UI.Styling;

internal sealed partial class CssFloatSpace
{
    // Search the original pixel intervals, without tessellating every pixel
    // corner into an arc. Interval bounds discard only positions proven unable
    // to fit, so concave/opposing shapes need no monotonic-contour assumption.
    private double NextRasterFit(double width, double y, double height, double required, int count)
    {
        if (!double.IsFinite(width) || !double.IsFinite(required)) return NextBottom(y, count);
        var regions = _regions.Take(count).Where(region => region.Bottom > y && region.Width > 0 && region.Height > 0).ToArray();
        if (regions.Length == 0) return double.PositiveInfinity;
        if (required > width + .000001) return regions.Max(region => region.Bottom);
        var bottom = regions.Max(region => region.Bottom);
        if (!double.IsFinite(bottom)) return NextBottom(y, count);
        height = Math.Max(height, .000001);
        bool Fits(double top)
        { var band = Band(width, top, height, count, useShapes: true); return band.Right - band.Left + .000001 >= required; }
        var pending = new Stack<(double Start, double End)>(); pending.Push((y, bottom));
        while (pending.TryPop(out var range))
        {
            var (a, b) = range;
            if (Fits(a)) return a;
            var left = 0d; var right = width;
            foreach (var region in regions)
            {
                if (region.Shape is { } shape)
                {
                    var bounds = shape.BoundsOverPositions(a - region.Y, b - region.Y, height, -region.X, width - region.X);
                    if (region.Side == CssFloatSide.Left) left = Math.Max(left, region.X + bounds.RightLower);
                    else right = Math.Min(right, region.X + bounds.LeftUpper);
                }
                else if (region.Bottom > b && region.Y < a + height && region.X < width && region.Right > 0)
                {
                    if (region.Side == CssFloatSide.Left) left = Math.Max(left, region.Right);
                    else right = Math.Min(right, region.X);
                }
            }
            if (right - left + .000001 < required) continue;
            var mid = a + (b - a) / 2;
            if (b - a <= .0000001 || mid == a || mid == b)
            {
                if (Fits(mid)) return mid;
                if (Fits(b)) return b;
                continue;
            }
            pending.Push((mid, b)); pending.Push((a, mid));
        }
        return bottom;
    }
}
