namespace Jalium.UI.Media;

/// <summary>Builds the filled region of a flattened stroke, then resolves overlaps.</summary>
internal static class GeometryStroke
{
    private sealed record Run(List<Point> Points, bool Closed, HashSet<int> Smooth,
        double Phase = 0, bool Start = true, bool End = true);

    internal static PathGeometry Widen(PathGeometry path, Pen pen, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(pen);
        var result = new PathGeometry { FillRule = FillRule.Nonzero };
        if (!(pen.Thickness > 0) || !double.IsFinite(pen.Thickness)) return result;
        var half = pen.Thickness / 2;
        var arcTolerance = Math.Max(1e-6, tolerance);
        var pattern = pen.DashStyle.Dashes.Select(value => value * pen.Thickness).ToArray();
        if (pattern.Any(value => value < 0 || !double.IsFinite(value)))
            throw new ArgumentException("Dash lengths must be finite and nonnegative.", nameof(pen));
        if (pattern.Length % 2 != 0) pattern = [.. pattern, .. pattern];
        var period = pattern.Sum();
        if (!double.IsFinite(period) || !double.IsFinite(pen.DashStyle.Offset))
            throw new ArgumentException("The dash pattern and offset must be finite.", nameof(pen));

        foreach (var figure in path.Figures)
        foreach (var run in Runs(figure))
        {
            if (period > 0)
                Dash(run);
            else
                Stroke(run, run.Start ? pen.StartLineCap : pen.DashCap, run.End ? pen.EndLineCap : pen.DashCap);
        }
        return GeometryBoolean.Outline(result);

        void Polygon(IEnumerable<Point> points)
        {
            var vertices = points.ToArray();
            if (vertices.Length < 3) return;
            // Give every piece the same winding before unioning. Translate the
            // area calculation to keep small strokes stable at large offsets.
            var origin = vertices[0];
            double area = 0;
            for (var i = 1; i < vertices.Length - 1; i++)
                area += Cross(Subtract(vertices[i], origin), Subtract(vertices[i + 1], origin));
            if (area == 0) return;
            if (area < 0) Array.Reverse(vertices);
            var contour = new PathFigure { StartPoint = vertices[0], IsClosed = true, IsFilled = true };
            foreach (var point in vertices.Skip(1)) contour.Segments.Add(new LineSegment(point));
            result.Figures.Add(contour);
        }

        void Sector(Point center, double start, double sweep)
        {
            var step = 2 * Math.Acos(Math.Clamp(1 - arcTolerance / half, -1, 1));
            var count = (int)Math.Clamp(Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-6)), 2, 65536);
            var points = new List<Point> { center };
            for (var i = 0; i <= count; i++)
            {
                var angle = start + sweep * i / count;
                points.Add(new Point(center.X + half * Math.Cos(angle), center.Y + half * Math.Sin(angle)));
            }
            Polygon(points);
        }

        void Cap(Point center, Point outward, PenLineCap cap)
        {
            var normal = new Point(-outward.Y * half, outward.X * half);
            var a = Add(center, normal);
            var b = Subtract(center, normal);
            var extension = Multiply(outward, half);
            switch (cap)
            {
                case PenLineCap.Square:
                    Polygon([a, Add(a, extension), Add(b, extension), b]);
                    break;
                case PenLineCap.Triangle:
                    Polygon([a, Add(center, extension), b]);
                    break;
                case PenLineCap.Round:
                    Sector(center, Math.Atan2(outward.Y, outward.X) - Math.PI / 2, Math.PI);
                    break;
            }
        }

        void Stroke(Run run, PenLineCap startCap, PenLineCap endCap)
        {
            var points = run.Points;
            var closed = run.Closed;
            if (points.Count == 0) return;
            var cleaned = new List<Point> { points[0] };
            var smooth = new HashSet<int>();
            if (run.Smooth.Contains(0)) smooth.Add(0);
            for (var index = 1; index < points.Count; index++)
            {
                if (points[index] != cleaned[^1]) cleaned.Add(points[index]);
                if (run.Smooth.Contains(index)) smooth.Add(cleaned.Count - 1);
            }
            if (closed && cleaned.Count > 1 && cleaned[^1] == cleaned[0])
            {
                if (smooth.Remove(cleaned.Count - 1)) smooth.Add(0);
                cleaned.RemoveAt(cleaned.Count - 1);
            }
            if (cleaned.Count == 1)
            {
                if (!closed)
                {
                    Cap(cleaned[0], new Point(-1, 0), startCap);
                    Cap(cleaned[0], new Point(1, 0), endCap);
                }
                return;
            }
            var count = closed ? cleaned.Count : cleaned.Count - 1;
            var directions = new Point[count];
            for (var i = 0; i < count; i++)
            {
                var a = cleaned[i];
                var b = cleaned[(i + 1) % cleaned.Count];
                var delta = Subtract(b, a);
                var length = Length(delta);
                directions[i] = Multiply(delta, 1 / length);
                var normal = new Point(-directions[i].Y * half, directions[i].X * half);
                Polygon([Add(a, normal), Add(b, normal), Subtract(b, normal), Subtract(a, normal)]);
            }
            for (var i = closed ? 0 : 1; i < (closed ? cleaned.Count : cleaned.Count - 1); i++)
            {
                var before = directions[(i + count - 1) % count];
                var after = directions[i];
                var turn = Cross(before, after);
                // Smooth segment joins use a rounded connection, independently
                // of the pen's corner style (including flattened curve edges).
                var join = smooth.Contains(i) ? PenLineJoin.Round : pen.LineJoin;
                if (Math.Abs(turn) < 1e-12)
                {
                    if (before.X * after.X + before.Y * after.Y < 0 && join == PenLineJoin.Round)
                        Sector(cleaned[i], 0, 2 * Math.PI);
                    continue;
                }
                var side = turn > 0 ? -half : half;
                var a = Add(cleaned[i], new Point(-before.Y * side, before.X * side));
                var b = Add(cleaned[i], new Point(-after.Y * side, after.X * side));
                if (join == PenLineJoin.Round)
                {
                    var start = Math.Atan2(a.Y - cleaned[i].Y, a.X - cleaned[i].X);
                    Sector(cleaned[i], start, Math.Atan2(turn, before.X * after.X + before.Y * after.Y));
                }
                else if (join == PenLineJoin.Miter)
                {
                    var miter = Add(a, Multiply(before, Cross(Subtract(b, a), after) / turn));
                    if (Length(Subtract(miter, cleaned[i])) <= half * Math.Max(1, pen.MiterLimit))
                        Polygon([cleaned[i], a, miter, b]);
                    else
                        Polygon([cleaned[i], a, b]);
                }
                else Polygon([cleaned[i], a, b]);
            }
            if (!closed)
            {
                Cap(cleaned[0], Multiply(directions[0], -1), startCap);
                Cap(cleaned[^1], directions[^1], endCap);
            }
        }

        void Dash(Run run)
        {
            var runLength = 0.0;
            for (var edge = 1; edge < run.Points.Count; edge++)
                runLength += Length(Subtract(run.Points[edge], run.Points[edge - 1]));
            if (run.Closed) runLength += Length(Subtract(run.Points[0], run.Points[^1]));
            if (runLength / period * pattern.Length > 100_000)
                throw new ArgumentException("The stroke dash pattern exceeds the supported complexity.", nameof(pen));
            var phase = (pen.DashStyle.Offset * pen.Thickness + run.Phase) % period;
            if (!double.IsFinite(phase)) throw new ArgumentException("The scaled dash offset must be finite.", nameof(pen));
            if (phase < 0) phase += period;
            var index = 0;
            while (phase > 0 && phase >= pattern[index])
            {
                phase -= pattern[index];
                index = (index + 1) % pattern.Length;
            }
            var remaining = pattern[index] - phase;
            var pieces = new List<Run>();
            Run? active = null;
            var points = run.Closed ? new List<Point>(run.Points) { run.Points[0] } : run.Points;
            void Finish()
            {
                if (active is not null) pieces.Add(active);
                active = null;
            }
            for (var edge = 1; edge < points.Count; edge++)
            {
                var cursor = points[edge - 1];
                var vector = Subtract(points[edge], cursor);
                var length = Length(vector);
                if (length == 0) continue;
                var direction = Multiply(vector, 1 / length);
                while (length > 1e-10)
                {
                    // Zero-length dashes are dots when the dash cap has area.
                    while (remaining <= 0)
                    {
                        if (index % 2 == 0 && pattern[index] == 0) pieces.Add(new Run([cursor], false, []));
                        index = (index + 1) % pattern.Length;
                        remaining = pattern[index];
                    }
                    if (index % 2 != 0) Finish();
                    var step = Math.Min(length, remaining);
                    var next = step == length ? points[edge] : Add(cursor, Multiply(direction, step));
                    if (index % 2 == 0)
                    {
                        active ??= new Run([cursor], false, []);
                        if (cursor == points[edge - 1] && run.Smooth.Contains(edge - 1))
                            active.Smooth.Add(active.Points.Count - 1);
                        active.Points.Add(next);
                    }
                    cursor = next;
                    length -= step;
                    remaining -= step;
                }
            }
            Finish();
            // A dash crossing the seam of a closed figure has a join, not caps.
            if (run.Closed && pieces.Count > 1 && pieces[0].Points[0] == points[0] && pieces[^1].Points[^1] == points[0])
            {
                AppendRun(pieces[^1], pieces[0]);
                pieces.RemoveAt(0);
            }
            foreach (var piece in pieces)
            {
                var closed = run.Closed && piece.Points.Count > 1 && piece.Points[0] == piece.Points[^1];
                var first = run.Start && piece.Points[0] == points[0] ? pen.StartLineCap : pen.DashCap;
                var last = run.End && piece.Points[^1] == points[^1] ? pen.EndLineCap : pen.DashCap;
                Stroke(piece with { Closed = closed }, first, last);
            }
        }
    }

    private static IEnumerable<Run> Runs(PathFigure figure)
    {
        var runs = new List<Run>();
        List<Point>? active = null;
        HashSet<int> smooth = [];
        var current = figure.StartPoint;
        var hasGap = false;
        var distance = 0.0;
        var runPhase = 0.0;
        var atStart = true;
        var runStart = false;
        void Edge(Point point, bool stroked, bool smoothJoin)
        {
            if (stroked)
            {
                if (active is null)
                {
                    active = [current];
                    runPhase = distance;
                    runStart = atStart && !figure.IsClosed;
                }
                if (smoothJoin) smooth.Add(active.Count - 1);
                active.Add(point);
            }
            else
            {
                if (active is not null) runs.Add(new Run(active, false, smooth, runPhase, runStart, false));
                active = null;
                smooth = [];
                hasGap = true;
            }
            distance += Length(Subtract(point, current));
            current = point;
            atStart = false;
        }
        foreach (var segment in figure.Segments)
        {
            var first = true;
            foreach (var point in segment.GetPoints())
            {
                Edge(point, segment.IsStroked, first && segment.IsSmoothJoin);
                first = false;
            }
        }
        if (figure.IsClosed) Edge(figure.StartPoint, true, false);
        if (active is not null) runs.Add(new Run(active, figure.IsClosed && !hasGap, smooth,
            runPhase, runStart, !figure.IsClosed));
        if (figure.IsClosed && hasGap && runs.Count > 1 &&
            runs[0].Points[0] == figure.StartPoint && runs[^1].Points[^1] == figure.StartPoint)
        {
            AppendRun(runs[^1], runs[0]);
            runs.RemoveAt(0);
        }
        return runs;
    }

    private static void AppendRun(Run target, Run source)
    {
        var offset = target.Points.Count - 1;
        target.Points.AddRange(source.Points.Skip(1));
        foreach (var index in source.Smooth) target.Smooth.Add(offset + index);
    }

    private static Point Add(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    private static Point Subtract(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    private static Point Multiply(Point point, double value) => new(point.X * value, point.Y * value);
    private static double Cross(Point a, Point b) => a.X * b.Y - a.Y * b.X;
    private static double Length(Point point) => Math.Sqrt(point.X * point.X + point.Y * point.Y);
}
