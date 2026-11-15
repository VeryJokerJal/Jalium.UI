namespace Jalium.UI.Styling;

internal sealed partial class CssFloatSpace
{
    private readonly record struct Affine(double Slope, double Constant)
    {
        internal double At(double y) => Slope * y + Constant;
    }

    /// <summary>
    /// The first lower position that can contain an unbreakable line. Between
    /// contour vertices (also shifted by the line height), the left envelope is
    /// a maximum of affine functions and the right a minimum. Their difference
    /// is concave, so even a narrow opening between opposing/concave shapes is
    /// found without pixel stepping or assuming an entire contour is monotonic.
    /// </summary>
    internal double NextFit(double width, double y, double height, double required, int count = int.MaxValue,
        bool useShapes = true)
    {
        if (!useShapes) return NextBottom(y, count);
        if (_regions.Take(count).Any(region => region.Shape?.IsRaster == true))
            return NextRasterFit(width, y, height, required, count);
        if (!double.IsFinite(width) || !double.IsFinite(required) ||
            !_regions.Take(count).Any(region => region.Shape is not null)) return NextBottom(y, count);
        height = Math.Max(height, .000001);
        var events = new SortedSet<double> { y };
        foreach (var region in _regions.Take(count))
        {
            if (region.Bottom <= y || region.Width <= 0 || region.Height <= 0) continue;
            Add(region.Y); Add(region.Y - height); Add(region.Bottom);
            if (region.Shape is { } shape)
                foreach (var edge in shape.Edges)
                {
                    Add(region.Y + edge.A.Y); Add(region.Y + edge.A.Y - height);
                    Add(region.Y + edge.B.Y); Add(region.Y + edge.B.Y - height);
                }
        }
        void Add(double value) { if (double.IsFinite(value) && value > y) events.Add(value); }
        bool Fits(double value)
        { var band = Band(width, value, height, count, useShapes: true); return band.Right - band.Left + .000001 >= required; }
        var positions = events.ToArray();
        for (var index = 1; index < positions.Length; index++)
        {
            var begin = positions[index - 1]; var end = positions[index];
            if (begin > y + .000001 && Fits(begin)) return begin;
            if (end - begin > .000001)
            {
                var left = new List<Affine> { new(0, 0) };
                var negativeRight = new List<Affine> { new(0, -width) };
                var mid = begin + (end - begin) / 2;
                foreach (var region in _regions.Take(count))
                {
                    if (region.Bottom <= mid || region.Y >= mid + height || region.Height <= 0 || region.Width <= 0 ||
                        region.X >= width || region.Right <= 0) continue;
                    var isLeft = region.Side == CssFloatSide.Left;
                    var list = isLeft ? left : negativeRight;
                    if (region.Shape is not { } shape)
                    { list.Add(new(0, isLeft ? region.Right : -region.X)); continue; }
                    var localY = mid - region.Y;
                    foreach (var edge in shape.Edges)
                    {
                        if (edge.Bottom <= localY || edge.Top >= localY + height) continue;
                        // Select the extreme endpoint of the segment/line-strip
                        // intersection. Event boundaries keep this choice stable.
                        var high = isLeft == (edge.Slope >= 0);
                        var variable = high ? localY + height < edge.Bottom : localY > edge.Top;
                        var slope = variable ? edge.Slope : 0;
                        var value = variable ? edge.X(localY + (high ? height : 0))
                            : edge.X(high ? edge.Bottom : edge.Top);
                        var constant = region.X + value - slope * mid;
                        list.Add(isLeft ? new(slope, constant) : new(-slope, -constant));
                    }
                }
                var target = -required;
                double Loss(double position) => left.Max(line => line.At(position)) + negativeRight.Max(line => line.At(position));
                (double Low, double High) Slope(List<Affine> lines, double position)
                {
                    var maximum = double.NegativeInfinity; var low = 0d; var high = 0d;
                    foreach (var line in lines)
                    {
                        var value = line.At(position);
                        if (value > maximum + 1e-9) { maximum = value; low = high = line.Slope; }
                        else if (Math.Abs(value - maximum) <= 1e-9)
                        { low = Math.Min(low, line.Slope); high = Math.Max(high, line.Slope); }
                    }
                    return (low, high);
                }
                var lo = begin; var hi = end;
                for (var pass = 0; pass < 48 && hi - lo > .0000001; pass++)
                {
                    var position = lo + (hi - lo) / 2;
                    var a = Slope(left, position); var b = Slope(negativeRight, position);
                    if (a.Low + b.Low > 0) hi = position;
                    else if (a.High + b.High < 0) lo = position;
                    else { lo = hi = position; break; }
                }
                var minimum = lo + (hi - lo) / 2;
                if (Loss(minimum) <= target + .000001)
                {
                    lo = begin; hi = minimum;
                    for (var pass = 0; pass < 48 && hi - lo > .0000001; pass++)
                    {
                        var position = lo + (hi - lo) / 2;
                        if (Loss(position) <= target) hi = position; else lo = position;
                    }
                    var candidate = Math.Max(y + .000001, hi);
                    if (Fits(candidate)) return candidate;
                }
            }
            if (Fits(end)) return end;
        }
        return NextBottom(y, count);
    }
}
