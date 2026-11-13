using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssFloatShape
{
    private readonly Raster? _raster;
    internal bool IsRaster => _raster is not null;
    private CssFloatShape(Raster raster) { _raster = raster; _hash = raster.GetHashCode(); }

    internal static CssFloatShape FromPixels(CssShapeImagePixels pixels, double threshold, Rect image,
        Size marginBox, double margin)
    {
        if (marginBox.Width <= 0 || marginBox.Height <= 0 || image.Width <= 0 || image.Height <= 0) return new([]);
        var boxes = new List<Rect>(); var previous = new Dictionary<(int Left, int Right), int>();
        var sx = image.Width / pixels.Width; var sy = image.Height / pixels.Height;
        var rows = pixels.Runs(threshold);
        for (var y = 0; y < rows.Length; y++)
        {
            var row = rows[y]; var selected = new HashSet<int>();
            // For one-sided wrapping only the extreme intervals on each side
            // of the clipping domain can contribute. Keep the nearest interval
            // just outside each side as well: shape-margin may bring it inside.
            var leftIndex = LowerBound(row, 0, useRight: true);
            var rightIndex = LowerBound(row, marginBox.Width, useRight: false);
            foreach (var i in new[] { leftIndex - 1, leftIndex, rightIndex - 1, rightIndex })
                if (i >= 0 && i < row.Length / 2) selected.Add(i);
            var next = new Dictionary<(int Left, int Right), int>();
            foreach (var i in selected.Order())
            {
                var left = image.X + row[i * 2] * sx; var right = image.X + row[i * 2 + 1] * sx;
                if (right + margin < 0 || left - margin > marginBox.Width) continue;
                var key = (row[i * 2], row[i * 2 + 1]);
                if (previous.TryGetValue(key, out var index))
                {
                    var box = boxes[index]; boxes[index] = new(box.X, box.Y, box.Width, image.Y + (y + 1) * sy - box.Y); next[key] = index;
                }
                else { next[key] = boxes.Count; boxes.Add(new(left, image.Y + y * sy, right - left, sy)); }
            }
            previous = next;
            int LowerBound(int[] runs, double x, bool useRight)
            {
                var lo = 0; var hi = runs.Length / 2;
                while (lo < hi)
                {
                    var mid = lo + (hi - lo) / 2;
                    if (image.X + runs[mid * 2 + (useRight ? 1 : 0)] * sx < x) lo = mid + 1; else hi = mid;
                }
                return lo;
            }
        }
        return new(new Raster(boxes.ToArray(), marginBox, margin));
    }

    private Edge[] BuildRasterEdges()
    {
        var raster = _raster!;
        var path = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var rect in raster.Boxes)
        {
            var figure = new PathFigure { StartPoint = rect.TopLeft, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(rect.TopRight, true));
            figure.Segments.Add(new LineSegment(rect.BottomRight, true));
            figure.Segments.Add(new LineSegment(rect.BottomLeft, true));
            path.Figures.Add(figure);
        }
        // NextFit's affine envelope uses a conservative flattened outline.
        // Ordinary line bands retain the exact circular distance below.
        return Create(path, 0, 0, raster.Size.Width, raster.Size.Height,
            raster.Margin > 0 ? raster.Margin + Tolerance * 2 : 0).Edges;
    }

    // Optimistic constraints over every possible line top in [a,b]. An
    // individual circular rectangle has concave right support and convex left
    // support while active, so its endpoint extrema bound the whole interval.
    // Taking maxima/minima preserves a safe bound across disjoint pixel runs.
    internal (double LeftUpper, double RightLower) BoundsOverPositions(double a, double b,
        double height, double clipLeft, double clipRight)
    {
        var leftUpper = clipRight; var rightLower = clipLeft;
        if (_raster is { } raster)
        {
            foreach (var box in raster.Boxes)
                if (raster.BoxBand(box, a, height, out var first) && raster.BoxBand(box, b, height, out var last))
                    Include(first.Left, first.Right, last.Left, last.Right);
        }
        else
            foreach (var edge in Edges)
            {
                if (edge.Bottom <= b || edge.Top >= a + height) continue;
                var firstA = edge.X(Math.Max(a, edge.Top)); var firstB = edge.X(Math.Min(a + height, edge.Bottom));
                var lastA = edge.X(Math.Max(b, edge.Top)); var lastB = edge.X(Math.Min(b + height, edge.Bottom));
                Include(Math.Min(firstA, firstB), Math.Max(firstA, firstB), Math.Min(lastA, lastB), Math.Max(lastA, lastB));
            }
        return (leftUpper, rightLower);
        void Include(double l1, double r1, double l2, double r2)
        {
            l1 = Math.Max(l1, clipLeft); l2 = Math.Max(l2, clipLeft);
            r1 = Math.Min(r1, clipRight); r2 = Math.Min(r2, clipRight);
            if (l1 > r1 || l2 > r2) return;
            leftUpper = Math.Min(leftUpper, Math.Max(l1, l2));
            rightLower = Math.Max(rightLower, Math.Min(r1, r2));
        }
    }

    private sealed class Raster(Rect[] boxes, Size size, double margin) : IEquatable<Raster>
    {
        internal Rect[] Boxes => boxes;
        internal Size Size => size;
        internal double Margin => margin;
        public bool Equals(Raster? other) => other is not null && Size == other.Size && Margin == other.Margin && Boxes.AsSpan().SequenceEqual(other.Boxes);
        public override bool Equals(object? other) => other is Raster raster && Equals(raster);
        public override int GetHashCode() { var hash = new HashCode(); hash.Add(Size); hash.Add(Margin); foreach (var box in Boxes) hash.Add(box); return hash.ToHashCode(); }

        internal (double Left, double Right, bool Active) Band(double y, double height)
        {
            var left = double.PositiveInfinity; var right = double.NegativeInfinity;
            foreach (var box in Boxes)
            {
                if (!BoxBand(box, y, height, out var band)) continue;
                left = Math.Min(left, band.Left); right = Math.Max(right, band.Right);
            }
            return (left, right, left <= right);
        }

        internal bool BoxBand(Rect box, double y, double height, out (double Left, double Right) band)
        {
            band = default;
            var top = Math.Max(0, y); var bottom = Math.Min(Size.Height, y + height);
            if (top >= bottom) return false;
            var distance = Math.Max(0, Math.Max(box.Top - bottom, top - box.Bottom));
            if (Margin == 0 ? box.Bottom <= top || box.Top >= bottom : distance >= Margin) return false;
            var outset = Margin == 0 ? 0 : Margin * Math.Sqrt(Math.Max(0, 1 - Math.Pow(distance / Margin, 2)));
            var left = Math.Max(0, box.Left - outset); var right = Math.Min(Size.Width, box.Right + outset);
            band = (left, right); return left <= right;
        }
    }
}
