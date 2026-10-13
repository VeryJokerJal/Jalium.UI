using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Automation;

/// <summary>
/// A query-local snapshot of the clips actually pushed by Visual.RenderDirect.
/// Coordinates remain local to the queried element; no state survives a query.
/// </summary>
internal sealed class AutomationVisibility
{
    private readonly List<Rect> _rectangles = [];
    private readonly List<Region> _regions = [];
    private bool _empty;

    internal AutomationVisibility(UIElement owner)
    {
        Matrix matrix = owner.GetRenderMatrix();
        if (!Finite(matrix) || !matrix.TryInvert(out Matrix inverse) || !owner.IsVisible)
        { _empty = true; return; }

        Visual? child = null;
        for (Visual? current = owner; current != null; child = current, current = current.VisualParent)
        {
            if (current is not UIElement element) continue;
            if (element.Visibility != Visibility.Visible || Styling.CssDisplayProperties.IsExitInert(element))
            { _empty = true; return; }
            Matrix toOwner = Matrix.Multiply(element.GetRenderMatrix(), inverse);
            if (child != null || element.LayoutClipIncludesSelf) Add(element.GetLayoutClip(), toOwner);
            if (child != null)
            {
                Add(element.GetChildLayoutClip(), toOwner);
                Add(element.GetAdditionalChildLayoutClip(child), toOwner);
            }
            // The native client clips all drawing, even with ClipToBounds=false.
            if (current is IWindowHost)
            {
                Add(new RectangleGeometry(new Rect(element.RenderSize)), toOwner);
                break;
            }
        }
    }

    internal static IReadOnlyList<Rect> ClipRectangles(UIElement owner, IReadOnlyList<Rect> rectangles)
    {
        if (rectangles.Count == 0) return [];
        var visibility = new AutomationVisibility(owner);
        var result = new List<Rect>(rectangles.Count);
        foreach (Rect rectangle in rectangles)
            if (visibility.TryClip(rectangle, out Rect clipped)) result.Add(clipped);
        return result;
    }

    internal bool Contains(Point point) => !_empty && double.IsFinite(point.X) && double.IsFinite(point.Y)
        && _rectangles.All(rectangle => rectangle.Contains(point)) && _regions.All(region => region.Contains(point));

    internal bool TryClip(Rect rectangle, out Rect clipped) => Sweep(rectangle, Matrix.Identity, default, false, out clipped, out _);

    internal bool TryClip(Rect rectangle, Matrix destination, out Rect clipped) =>
        Sweep(rectangle, destination, default, false, out clipped, out _);

    internal bool TryGetPoint(Rect rectangle, Point preferred, out Point point) =>
        Sweep(rectangle, Matrix.Identity, preferred, true, out _, out point);

    private void Add(Geometry? geometry, Matrix toOwner)
    {
        if (_empty || geometry == null) return;
        if (geometry.Bounds.IsEmpty) { _empty = true; return; }
        Matrix transform = geometry.Transform is { } value ? Matrix.Multiply(value.Value, toOwner) : toOwner;
        if (!Finite(transform)) { _empty = true; return; }
        if (geometry is RectangleGeometry { RadiusX: 0, RadiusY: 0, HasPerCornerRadii: false } rectangle
            && transform.M12 == 0 && transform.M21 == 0)
        {
            if (!Valid(rectangle.Rect)) { _empty = true; return; }
            Point first = transform.Transform(new Point(rectangle.Rect.Left, rectangle.Rect.Top));
            Point last = transform.Transform(new Point(rectangle.Rect.Right, rectangle.Rect.Bottom));
            var bounds = new Rect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
            if (Valid(bounds)) _rectangles.Add(bounds); else _empty = true;
            return;
        }

        // Use the rendering flattener, including child geometry transforms and
        // winding. Curve error is at most 1/8 DIP in this element's space.
        double scale = Math.Sqrt(toOwner.M11 * toOwner.M11 + toOwner.M12 * toOwner.M12
            + toOwner.M21 * toOwner.M21 + toOwner.M22 * toOwner.M22);
        var container = new GeometryGroup(); container.Children.Add(geometry);
        PathGeometry flattened = RenderTargetDrawingContext.FlattenGeometryGroup(container, .125 / Math.Max(scale, .01));
        FillRule rule = geometry switch
        {
            GeometryGroup group => group.FillRule,
            PathGeometry path => path.FillRule,
            StreamGeometry stream => stream.FillRule,
            _ => flattened.FillRule
        };
        var region = new Region(flattened, rule, toOwner);
        if (region.Edges.Count == 0) _empty = true; else _regions.Add(region);
    }

    private bool Sweep(Rect rectangle, Matrix destination, Point preferred, bool findPoint, out Rect bounds, out Point point)
    {
        bounds = Rect.Empty; point = new(double.NaN, double.NaN);
        if (_empty || !Valid(rectangle) || !Finite(destination)) return false;
        foreach (Rect clip in _rectangles) rectangle = Rect.Intersect(rectangle, clip);
        if (!Valid(rectangle)) return false;
        if (_regions.Count == 0)
        {
            bounds = Project(new(rectangle.Left, rectangle.Top), new(rectangle.Right, rectangle.Top),
                new(rectangle.Right, rectangle.Bottom), new(rectangle.Left, rectangle.Bottom));
            point = new(Interior(preferred.X, rectangle.Left, rectangle.Right), Interior(preferred.Y, rectangle.Top, rectangle.Bottom));
            return true;
        }

        foreach (Region region in _regions) rectangle = Rect.Intersect(rectangle, region.Bounds);
        if (!Valid(rectangle)) return false;
        var edges = _regions.SelectMany(region => region.Edges).Where(edge => edge.HighY > rectangle.Top && edge.LowY < rectangle.Bottom).ToArray();
        var cuts = new SortedSet<double> { rectangle.Top, rectangle.Bottom };
        foreach (Edge edge in edges)
        {
            Cut(edge.LowY); Cut(edge.HighY);
            if (edge.Slope != 0)
            {
                Cut(edge.A.Y + (rectangle.Left - edge.A.X) / edge.Slope);
                Cut(edge.A.Y + (rectangle.Right - edge.A.X) / edge.Slope);
            }
        }
        // Edge order is constant between vertices and pairwise crossings. This
        // gives continuous filled intervals, without pixel sampling that misses
        // narrow slivers, concave shapes or holes between sample positions.
        for (int i = 0; i < edges.Length; i++)
            for (int j = i + 1; j < edges.Length; j++)
            {
                Edge left = edges[i], right = edges[j];
                double start = Math.Max(left.LowY, right.LowY), end = Math.Min(left.HighY, right.HighY);
                double delta = left.Slope - right.Slope;
                if (start < end && delta != 0)
                {
                    double crossing = start + (right.X(start) - left.X(start)) / delta;
                    if (crossing > start && crossing < end) Cut(crossing);
                }
            }

        double[] levels = cuts.ToArray();
        double distance = double.PositiveInfinity;
        for (int band = 1; band < levels.Length; band++)
        {
            double top = levels[band - 1], bottom = levels[band], middle = top + (bottom - top) / 2;
            if (middle <= top || middle >= bottom) continue;
            var spans = Intervals(middle);
            foreach (Interval span in spans)
            {
                double left = Math.Max(rectangle.Left, Math.Min(span.Left.X(top), span.Left.X(bottom)));
                double right = Math.Min(rectangle.Right, Math.Max(span.Right.X(top), span.Right.X(bottom)));
                if (right <= left) continue;
                // Project the clipped trapezoid, not its local AABB. Rotating
                // that AABB would expand the result outside an ancestor clip.
                var part = Project(new(Math.Max(rectangle.Left, span.Left.X(top)), top),
                    new(Math.Min(rectangle.Right, span.Right.X(top)), top),
                    new(Math.Min(rectangle.Right, span.Right.X(bottom)), bottom),
                    new(Math.Max(rectangle.Left, span.Left.X(bottom)), bottom));
                bounds = bounds.IsEmpty ? part : Rect.Union(bounds, part);
            }
            if (!findPoint || spans.Count == 0) continue;
            double y = Interior(preferred.Y, top, bottom);
            foreach (Interval span in Intervals(y))
            {
                double x = Interior(preferred.X, span.Left.X(y), span.Right.X(y));
                double candidate = (x - preferred.X) * (x - preferred.X) + (y - preferred.Y) * (y - preferred.Y);
                if (candidate < distance) { distance = candidate; point = new(x, y); }
            }
        }
        return Valid(bounds) && (!findPoint || double.IsFinite(point.X));

        void Cut(double y) { if (double.IsFinite(y) && y > rectangle.Top && y < rectangle.Bottom) cuts.Add(y); }
        Rect Project(Point a, Point b, Point c, Point d)
        {
            Point[] points = [destination.Transform(a), destination.Transform(b), destination.Transform(c), destination.Transform(d)];
            if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) return Rect.Empty;
            double left = points.Min(point => point.X), top = points.Min(point => point.Y);
            return new(left, top, points.Max(point => point.X) - left, points.Max(point => point.Y) - top);
        }
        List<Interval> Intervals(double y)
        {
            List<Interval> spans = [new(Edge.Vertical(rectangle.Left), Edge.Vertical(rectangle.Right))];
            foreach (Region region in _regions)
            {
                var clipped = new List<Interval>();
                List<Interval> mask = region.Intervals(y);
                int i = 0, j = 0;
                while (i < spans.Count && j < mask.Count)
                {
                    Interval source = spans[i], clip = mask[j];
                    Edge left = source.Left.X(y) >= clip.Left.X(y) ? source.Left : clip.Left;
                    Edge right = source.Right.X(y) <= clip.Right.X(y) ? source.Right : clip.Right;
                    if (right.X(y) > left.X(y)) clipped.Add(new(left, right));
                    if (source.Right.X(y) < clip.Right.X(y)) i++; else j++;
                }
                spans = clipped;
                if (spans.Count == 0) break;
            }
            return spans;
        }
    }

    private static double Interior(double value, double start, double end)
    {
        double inset = Math.Min((end - start) / 4, 1e-6);
        return Math.Clamp(value, start + inset, end - inset);
    }
    private static bool Valid(Rect rectangle) => !rectangle.IsEmpty && rectangle.Width > 0 && rectangle.Height > 0
        && double.IsFinite(rectangle.Left) && double.IsFinite(rectangle.Top) && double.IsFinite(rectangle.Right) && double.IsFinite(rectangle.Bottom);
    private static bool Finite(Matrix matrix) => double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12)
        && double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) && double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);

    private readonly record struct Edge(Point A, Point B)
    {
        internal double LowY => Math.Min(A.Y, B.Y);
        internal double HighY => Math.Max(A.Y, B.Y);
        internal double Slope => (B.X - A.X) / (B.Y - A.Y);
        internal double X(double y) => A.X + (y - A.Y) * Slope;
        internal static Edge Vertical(double x) => new(new(x, 0), new(x, 1));
    }
    private readonly record struct Interval(Edge Left, Edge Right);

    private sealed class Region
    {
        internal List<Edge> Edges { get; } = [];
        internal Rect Bounds { get; private set; } = Rect.Empty;
        private readonly FillRule _rule;
        internal Region(PathGeometry path, FillRule rule, Matrix transform)
        {
            _rule = rule;
            foreach (PathFigure figure in path.Figures)
            {
                if (!figure.IsFilled) continue;
                var points = new List<Point> { transform.Transform(figure.StartPoint) };
                foreach (PathSegment segment in figure.Segments)
                    if (segment is LineSegment line) points.Add(transform.Transform(line.Point));
                    else if (segment is PolyLineSegment poly) points.AddRange(poly.Points.Select(transform.Transform));
                if (points.Count < 3 || points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) continue;
                for (int i = 0; i < points.Count; i++)
                {
                    Point first = points[i], last = points[(i + 1) % points.Count];
                    if (first.Y != last.Y) Edges.Add(new(first, last));
                }
                double left = points.Min(point => point.X), top = points.Min(point => point.Y);
                var bounds = new Rect(left, top, points.Max(point => point.X) - left, points.Max(point => point.Y) - top);
                Bounds = Bounds.IsEmpty ? bounds : Rect.Union(Bounds, bounds);
            }
        }
        internal bool Contains(Point point) => Intervals(point.Y).Any(span => point.X >= span.Left.X(point.Y) && point.X <= span.Right.X(point.Y));
        internal List<Interval> Intervals(double y)
        {
            var crossings = Edges.Where(edge => edge.LowY <= y && edge.HighY > y).OrderBy(edge => edge.X(y)).ToArray();
            var result = new List<Interval>();
            int winding = 0;
            Edge? start = null;
            foreach (Edge edge in crossings)
            {
                bool before = _rule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;
                winding += edge.B.Y > edge.A.Y ? 1 : -1;
                bool after = _rule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;
                if (!before && after) start = edge;
                else if (before && !after && start is { } left && edge.X(y) > left.X(y)) result.Add(new(left, edge));
            }
            return result;
        }
    }
}
