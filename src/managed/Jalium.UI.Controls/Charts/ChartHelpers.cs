namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Provides static utility methods for chart calculations.
/// </summary>
public static class ChartHelpers
{
    /// <summary>
    /// Computes a "nice" number for tick spacing. A nice number is 1, 2, or 5 times a power of 10.
    /// </summary>
    /// <param name="range">The data range.</param>
    /// <param name="round">If true, round to nearest nice number; otherwise, ceiling.</param>
    /// <returns>A nice number suitable for tick spacing.</returns>
    public static double NiceNumber(double range, bool round)
    {
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range))
            return 1.0;

        var exponent = Math.Floor(Math.Log10(range));
        var fraction = range / Math.Pow(10, exponent);

        double niceFraction;
        if (round)
        {
            niceFraction = fraction switch
            {
                < 1.5 => 1.0,
                < 3.0 => 2.0,
                < 7.0 => 5.0,
                _ => 10.0
            };
        }
        else
        {
            niceFraction = fraction switch
            {
                <= 1.0 => 1.0,
                <= 2.0 => 2.0,
                <= 5.0 => 5.0,
                _ => 10.0
            };
        }

        return niceFraction * Math.Pow(10, exponent);
    }

    /// <summary>
    /// Computes an automatic data range with 5% padding on each side.
    /// </summary>
    /// <param name="values">The data values to compute a range for.</param>
    /// <param name="min">The computed minimum.</param>
    /// <param name="max">The computed maximum.</param>
    public static void ComputeAutoRange(IEnumerable<double> values, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        bool hasValues = false;

        foreach (var v in values)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                continue;
            hasValues = true;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        if (!hasValues)
        {
            min = 0;
            max = 1;
            return;
        }

        if (Math.Abs(max - min) < 1e-10)
        {
            // All values are the same
            if (Math.Abs(min) < 1e-10)
            {
                min = -1;
                max = 1;
            }
            else
            {
                var absVal = Math.Abs(min);
                min -= absVal * 0.05;
                max += absVal * 0.05;
            }
            return;
        }

        var padding = (max - min) * 0.05;
        min -= padding;
        max += padding;
    }

    /// <summary>
    /// Linearly interpolates between two values.
    /// </summary>
    public static double Lerp(double a, double b, double t)
    {
        return a + (b - a) * t;
    }

    /// <summary>
    /// Maps a value from one range to another.
    /// </summary>
    public static double MapValue(double value, double fromMin, double fromMax, double toMin, double toMax)
    {
        var fromRange = fromMax - fromMin;
        if (Math.Abs(fromRange) < 1e-15)
            return (toMin + toMax) / 2.0;

        var t = (value - fromMin) / fromRange;
        return toMin + t * (toMax - toMin);
    }

    /// <summary>
    /// Determines whether a point lies inside a polygon using the ray-casting algorithm.
    /// </summary>
    public static bool PointInPolygon(Point p, Point[] polygon)
    {
        if (polygon == null || polygon.Length < 3)
            return false;

        bool inside = false;
        int j = polygon.Length - 1;

        for (int i = 0; i < polygon.Length; i++)
        {
            if ((polygon[i].Y > p.Y) != (polygon[j].Y > p.Y) &&
                p.X < (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    /// <summary>
    /// Computes the minimum distance from a point to a line segment.
    /// </summary>
    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lenSq = dx * dx + dy * dy;

        if (lenSq < 1e-15)
        {
            // Segment is a point
            var ddx = p.X - a.X;
            var ddy = p.Y - a.Y;
            return Math.Sqrt(ddx * ddx + ddy * ddy);
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var projX = a.X + t * dx;
        var projY = a.Y + t * dy;
        var distX = p.X - projX;
        var distY = p.Y - projY;
        return Math.Sqrt(distX * distX + distY * distY);
    }

    /// <summary>
    /// Simplifies a polyline using the Ramer-Douglas-Peucker algorithm.
    /// </summary>
    /// <param name="points">The input points.</param>
    /// <param name="tolerance">The maximum allowed perpendicular distance.</param>
    /// <returns>The simplified array of points.</returns>
    public static Point[] SimplifyPoints(Point[] points, double tolerance)
    {
        if (points == null || points.Length < 3)
            return points ?? Array.Empty<Point>();

        if (tolerance <= 0)
            return (Point[])points.Clone();

        var keep = new bool[points.Length];
        keep[0] = true;
        keep[points.Length - 1] = true;

        SimplifyRecursive(points, 0, points.Length - 1, tolerance, keep);

        var result = new List<Point>();
        for (int i = 0; i < points.Length; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }

        return result.ToArray();
    }

    private static void SimplifyRecursive(Point[] points, int startIndex, int endIndex, double tolerance, bool[] keep)
    {
        if (endIndex - startIndex < 2)
            return;

        double maxDist = 0;
        int maxIndex = startIndex;

        var a = points[startIndex];
        var b = points[endIndex];

        for (int i = startIndex + 1; i < endIndex; i++)
        {
            var dist = DistanceToSegment(points[i], a, b);
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist > tolerance)
        {
            keep[maxIndex] = true;
            SimplifyRecursive(points, startIndex, maxIndex, tolerance, keep);
            SimplifyRecursive(points, maxIndex, endIndex, tolerance, keep);
        }
    }
}
