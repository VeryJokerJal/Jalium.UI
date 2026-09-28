namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    private readonly record struct ClipEdge(Point Start, Point End)
    {
        internal double XAt(double y) => Start.X +
            (y - Start.Y) * (End.X - Start.X) / (End.Y - Start.Y);

        internal double Slope => (End.X - Start.X) / (End.Y - Start.Y);
    }

    private readonly record struct ClipCrossing(double X, ClipEdge Edge);

    /// <summary>
    /// Composes SVG silhouettes after flattening. Each horizontal band is split at
    /// every vertex and edge crossing, so its covered intervals have straight sides.
    /// The resulting trapezoids have disjoint interiors and can use one native path
    /// fill rule without canceling opposite windings or filling an individual hole.
    /// </summary>
    private static PathGeometry? UnionClipPaths(IReadOnlyList<PathGeometry> parts)
        => ComposeClipPaths(parts, intersect: false);

    private static PathGeometry? IntersectClipPaths(PathGeometry first, PathGeometry second)
        => ComposeClipPaths([first, second], intersect: true);

    private static PathGeometry? ComposeClipPaths(IReadOnlyList<PathGeometry> parts, bool intersect)
    {
        const int maxEdges = 512;
        const int maxBands = 16384;
        var edges = new List<ClipEdge>();
        var levels = new List<double>();
        foreach (var part in parts)
        foreach (var figure in part.Figures)
        {
            if (!figure.IsFilled) continue;
            var first = figure.StartPoint;
            var previous = first;
            foreach (var segment in figure.Segments)
            foreach (var point in segment.GetPoints())
            {
                if (!AddEdge(previous, point)) return null;
                previous = point;
            }
            if (!AddEdge(previous, first)) return null;
        }

        if (edges.Count == 0) return new PathGeometry();
        for (var left = 0; left < edges.Count; left++)
        for (var right = left + 1; right < edges.Count; right++)
        {
            var a = edges[left];
            var b = edges[right];
            var low = Math.Max(Math.Min(a.Start.Y, a.End.Y), Math.Min(b.Start.Y, b.End.Y));
            var high = Math.Min(Math.Max(a.Start.Y, a.End.Y), Math.Max(b.Start.Y, b.End.Y));
            if (high <= low) continue;
            var slopeDifference = a.Slope - b.Slope;
            if (Math.Abs(slopeDifference) < 1e-12) continue;
            var y = (b.Start.X - a.Start.X + a.Slope * a.Start.Y -
                b.Slope * b.Start.Y) / slopeDifference;
            if (y > low && y < high && double.IsFinite(y))
            {
                if (levels.Count >= maxBands) return null;
                levels.Add(y);
            }
        }

        levels.Sort();
        var path = new PathGeometry { FillRule = FillRule.Nonzero };
        var crossings = new List<ClipCrossing>(edges.Count);
        for (var band = 1; band < levels.Count; band++)
        {
            var y0 = levels[band - 1];
            var y1 = levels[band];
            if (y1 <= y0) continue;
            var middle = y0 + (y1 - y0) / 2;
            crossings.Clear();
            foreach (var edge in edges)
                if (middle > Math.Min(edge.Start.Y, edge.End.Y) &&
                    middle < Math.Max(edge.Start.Y, edge.End.Y))
                    crossings.Add(new ClipCrossing(edge.XAt(middle), edge));
            crossings.Sort((a, b) => a.X.CompareTo(b.X));

            var firstCovered = -1;
            for (var index = 0; index < crossings.Count - 1; index++)
            {
                var left = crossings[index].X;
                var right = crossings[index + 1].X;
                if (right <= left) continue;
                var sample = new Point(left + (right - left) / 2, middle);
                var covered = intersect;
                foreach (var part in parts)
                {
                    var contains = part.FillContains(sample);
                    if (intersect && !contains || !intersect && contains)
                    {
                        covered = contains;
                        break;
                    }
                }
                if (covered && firstCovered < 0) firstCovered = index;
                if (!covered && firstCovered >= 0)
                {
                    AddBand(firstCovered, index);
                    firstCovered = -1;
                }
            }
            if (firstCovered >= 0) AddBand(firstCovered, crossings.Count - 1);

            void AddBand(int first, int last)
            {
                var left = crossings[first].Edge;
                var right = crossings[last].Edge;
                var figure = new PathFigure
                {
                    StartPoint = new Point(left.XAt(y0), y0),
                    IsClosed = true,
                    IsFilled = true,
                };
                figure.Segments.Add(new LineSegment(new Point(right.XAt(y0), y0)));
                figure.Segments.Add(new LineSegment(new Point(right.XAt(y1), y1)));
                figure.Segments.Add(new LineSegment(new Point(left.XAt(y1), y1)));
                path.Figures.Add(figure);
            }
        }
        return path;

        bool AddEdge(Point start, Point end)
        {
            if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) ||
                !double.IsFinite(end.X) || !double.IsFinite(end.Y)) return false;
            if (start.Y == end.Y) return true;
            if (edges.Count >= maxEdges || levels.Count >= maxBands) return false;
            edges.Add(new ClipEdge(start, end));
            levels.Add(start.Y);
            levels.Add(end.Y);
            return true;
        }
    }
}
