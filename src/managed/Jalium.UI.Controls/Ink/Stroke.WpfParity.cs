using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Ink;

public partial class Stroke
{
    /// <summary>Adds or replaces custom property data on this stroke.</summary>
    public void AddPropertyData(Guid propertyDataId, object propertyData)
    {
        InkPropertyData.Validate(propertyDataId, propertyData);
        if (_propertyData.TryGetValue(propertyDataId, out object? previous) &&
            InkPropertyData.ValuesEqual(previous, propertyData))
        {
            return;
        }

        object stored = InkPropertyData.CloneValue(propertyData);
        _propertyData[propertyDataId] = stored;
        OnPropertyDataChanged(new PropertyDataChangedEventArgs(
            propertyDataId,
            stored,
            previous));
    }

    /// <summary>Removes custom property data from this stroke.</summary>
    public void RemovePropertyData(Guid propertyDataId)
    {
        if (!_propertyData.Remove(propertyDataId, out object? previous))
            throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));

        OnPropertyDataChanged(new PropertyDataChangedEventArgs(
            propertyDataId,
            null,
            previous));
    }

    /// <summary>Gets a custom property-data value.</summary>
    public object GetPropertyData(Guid propertyDataId)
    {
        if (_propertyData.TryGetValue(propertyDataId, out object? value))
            return value;
        throw new ArgumentException("The property identifier was not found.", nameof(propertyDataId));
    }

    /// <summary>Gets all custom property-data identifiers.</summary>
    public Guid[] GetPropertyDataIds() => _propertyData.Keys.ToArray();

    /// <summary>Returns whether custom data with the identifier is present.</summary>
    public bool ContainsPropertyData(Guid propertyDataId) => _propertyData.ContainsKey(propertyDataId);

    /// <summary>Transforms every stored stylus point and optionally the stylus tip.</summary>
    public virtual void Transform(Matrix transformMatrix, bool applyToStylusTip)
    {
        if (transformMatrix.IsIdentity)
            return;
        if (!IsFinite(transformMatrix) || !transformMatrix.HasInverse)
            throw new ArgumentException("The transform matrix must be finite and invertible.", nameof(transformMatrix));

        _stylusPoints.Transform(
            transformMatrix.M11,
            transformMatrix.M12,
            transformMatrix.M21,
            transformMatrix.M22,
            transformMatrix.OffsetX,
            transformMatrix.OffsetY);

        if (applyToStylusTip)
        {
            transformMatrix.OffsetX = 0;
            transformMatrix.OffsetY = 0;
            Matrix tipTransform = Matrix.Multiply(
                _drawingAttributes.StylusTipTransform,
                transformMatrix);
            if (tipTransform.HasInverse && IsFinite(tipTransform))
                _drawingAttributes.StylusTipTransform = tipTransform;
        }
    }

    /// <summary>Returns a smoothed, pressure-interpolated copy of the stroke points.</summary>
    public StylusPointCollection GetBezierStylusPoints()
    {
        if (_stylusPoints.Count < 2)
            return _stylusPoints;

        StylusPointDescription description = _stylusPoints.Description;
        var result = new StylusPointCollection(description);
        for (int segment = 0; segment < _stylusPoints.Count - 1; segment++)
        {
            StylusPoint p0 = _stylusPoints[Math.Max(0, segment - 1)];
            StylusPoint p1 = _stylusPoints[segment];
            StylusPoint p2 = _stylusPoints[segment + 1];
            StylusPoint p3 = _stylusPoints[Math.Min(_stylusPoints.Count - 1, segment + 2)];

            double length = Distance(p1.ToPoint(), p2.ToPoint());
            int steps = Math.Clamp((int)Math.Ceiling(length / 4.0), 2, 32);
            int firstStep = segment == 0 ? 0 : 1;
            for (int step = firstStep; step <= steps; step++)
            {
                double t = (double)step / steps;
                double t2 = t * t;
                double t3 = t2 * t;
                double x = 0.5 * ((2 * p1.X) +
                    (-p0.X + p2.X) * t +
                    (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                    (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                double y = 0.5 * ((2 * p1.Y) +
                    (-p0.Y + p2.Y) * t +
                    (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                float pressure = (float)(p1.PressureFactor +
                    (p2.PressureFactor - p1.PressureFactor) * t);
                result.Add(new StylusPoint(
                    x,
                    y,
                    pressure,
                    description,
                    p1.GetUnpackedAdditionalValues(description)));
            }
        }

        return result;
    }

    /// <summary>Performs WPF-compatible one-pixel tap hit testing.</summary>
    public bool HitTest(Point point) => HitTest(point, 1.0);

    /// <summary>Tests what percentage of the sampled stroke lies inside a rectangle.</summary>
    public bool HitTest(Rect bounds, int percentageWithinBounds)
    {
        ValidatePercentage(percentageWithinBounds, nameof(percentageWithinBounds));
        if (percentageWithinBounds == 0)
            return true;
        if (bounds.IsEmpty)
            return false;

        return PercentageInside(point => bounds.Contains(point)) >= percentageWithinBounds;
    }

    /// <summary>Tests what percentage of the sampled stroke lies inside a lasso.</summary>
    public bool HitTest(IEnumerable<Point> lassoPoints, int percentageWithinLasso)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        ValidatePercentage(percentageWithinLasso, nameof(percentageWithinLasso));
        if (percentageWithinLasso == 0)
            return true;

        List<Point> polygon = MaterializePoints(lassoPoints);
        if (polygon.Count < 3)
            return false;
        return PercentageInside(point => PointInPolygon(point, polygon)) >= percentageWithinLasso;
    }

    /// <summary>Tests this stroke against an eraser path and stylus shape.</summary>
    public bool HitTest(IEnumerable<Point> path, StylusShape stylusShape)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stylusShape);

        List<Point> eraserPath = MaterializePoints(path);
        if (eraserPath.Count == 0)
            return false;

        double eraserRadius = Math.Max(stylusShape.Width, stylusShape.Height) / 2.0;
        (double strokeHalfWidth, double strokeHalfHeight) = GetTransformedTipHalfExtents(_drawingAttributes);
        double radius = eraserRadius + Math.Max(strokeHalfWidth, strokeHalfHeight);

        if (_stylusPoints.Count == 1)
            return DistanceToPath(_stylusPoints[0].ToPoint(), eraserPath) <= radius;

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            Point start = _stylusPoints[i].ToPoint();
            Point end = _stylusPoints[i + 1].ToPoint();
            if (eraserPath.Count == 1)
            {
                if (DistanceToLineSegment(eraserPath[0], start, end) <= radius)
                    return true;
                continue;
            }

            for (int j = 0; j < eraserPath.Count - 1; j++)
            {
                if (DistanceBetweenSegments(start, end, eraserPath[j], eraserPath[j + 1]) <= radius)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Returns stroke fragments contained by the rectangle.</summary>
    public StrokeCollection GetClipResult(Rect bounds)
    {
        if (bounds.IsEmpty)
            return new StrokeCollection();
        return SplitByPredicate(point => bounds.Contains(point), keepMatching: true);
    }

    /// <summary>Returns stroke fragments contained by the lasso.</summary>
    public StrokeCollection GetClipResult(IEnumerable<Point> lassoPoints)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        List<Point> polygon = MaterializePoints(lassoPoints);
        if (polygon.Count == 0)
            throw new ArgumentException("The lasso must contain at least one point.", nameof(lassoPoints));
        if (polygon.Count < 3)
            return new StrokeCollection();

        return SplitByPredicate(point => PointInPolygon(point, polygon), keepMatching: true);
    }

    /// <summary>Returns stroke fragments outside the rectangle.</summary>
    public StrokeCollection GetEraseResult(Rect bounds)
    {
        if (bounds.IsEmpty)
            return new StrokeCollection([Clone()]);
        return SplitByPredicate(point => bounds.Contains(point), keepMatching: false);
    }

    /// <summary>Returns stroke fragments outside the lasso.</summary>
    public StrokeCollection GetEraseResult(IEnumerable<Point> lassoPoints)
    {
        ArgumentNullException.ThrowIfNull(lassoPoints);
        List<Point> polygon = MaterializePoints(lassoPoints);
        if (polygon.Count == 0)
            throw new ArgumentException("The lasso must contain at least one point.", nameof(lassoPoints));
        if (polygon.Count < 3)
            return new StrokeCollection([Clone()]);

        return SplitByPredicate(point => PointInPolygon(point, polygon), keepMatching: false);
    }

    /// <summary>Returns stroke fragments not touched by an eraser path.</summary>
    public StrokeCollection GetEraseResult(IEnumerable<Point> eraserPath, StylusShape eraserShape)
    {
        ArgumentNullException.ThrowIfNull(eraserPath);
        ArgumentNullException.ThrowIfNull(eraserShape);
        List<Point> path = MaterializePoints(eraserPath);
        if (path.Count == 0)
            return new StrokeCollection([Clone()]);

        double eraserRadius = Math.Max(eraserShape.Width, eraserShape.Height) / 2.0;
        (double halfWidth, double halfHeight) = GetTransformedTipHalfExtents(_drawingAttributes);
        double radius = eraserRadius + Math.Max(halfWidth, halfHeight);
        return SplitByPredicate(
            point => DistanceToPath(point, path) <= radius,
            keepMatching: false);
    }

    internal IReadOnlyDictionary<Guid, object> PropertyData => _propertyData;

    internal void LoadPropertyData(Guid id, object value) => _propertyData[id] = value;

    private StrokeCollection SplitByPredicate(Func<Point, bool> predicate, bool keepMatching)
    {
        var result = new StrokeCollection();
        var run = new List<StylusPoint>();

        void FlushRun()
        {
            if (run.Count == 0)
                return;
            result.Add(CreateFragment(run));
            run = new List<StylusPoint>();
        }

        IEnumerable<StylusPoint> samples = EnumerateDenseSamples();
        foreach (StylusPoint sample in samples)
        {
            bool keep = predicate(sample.ToPoint()) == keepMatching;
            if (!keep)
            {
                FlushRun();
                continue;
            }

            if (run.Count == 0 || run[^1] != sample)
                run.Add(sample);
        }
        FlushRun();
        return result;
    }

    private IEnumerable<StylusPoint> EnumerateDenseSamples()
    {
        if (_stylusPoints.Count == 1)
        {
            yield return _stylusPoints[0];
            yield break;
        }

        for (int i = 0; i < _stylusPoints.Count - 1; i++)
        {
            StylusPoint start = _stylusPoints[i];
            StylusPoint end = _stylusPoints[i + 1];
            StylusPointDescription description = _stylusPoints.Description;
            int[] additionalValues = start.GetUnpackedAdditionalValues(description);
            int steps = Math.Clamp((int)Math.Ceiling(Distance(start.ToPoint(), end.ToPoint())), 1, 128);
            int first = i == 0 ? 0 : 1;
            for (int step = first; step <= steps; step++)
            {
                double t = (double)step / steps;
                yield return new StylusPoint(
                    start.X + (end.X - start.X) * t,
                    start.Y + (end.Y - start.Y) * t,
                    (float)(start.PressureFactor + (end.PressureFactor - start.PressureFactor) * t),
                    description,
                    additionalValues);
            }
        }
    }

    private Stroke CreateFragment(IEnumerable<StylusPoint> points)
    {
        var fragment = new Stroke(new StylusPointCollection(points), _drawingAttributes.Clone())
        {
            _taperMode = _taperMode,
        };
        foreach ((Guid id, object value) in _propertyData)
            fragment._propertyData.Add(id, InkPropertyData.CloneValue(value));
        return fragment;
    }

    private double PercentageInside(Func<Point, bool> contains)
    {
        if (_stylusPoints.Count == 1)
            return contains(_stylusPoints[0].ToPoint()) ? 100 : 0;

        double total = 0;
        double inside = 0;
        for (int i = 0; i < _stylusPoints.Count; i++)
        {
            double weight = 0;
            if (i > 0)
                weight += Distance(_stylusPoints[i - 1].ToPoint(), _stylusPoints[i].ToPoint()) / 2;
            if (i + 1 < _stylusPoints.Count)
                weight += Distance(_stylusPoints[i].ToPoint(), _stylusPoints[i + 1].ToPoint()) / 2;
            if (weight == 0)
                weight = 1;
            total += weight;
            if (contains(_stylusPoints[i].ToPoint()))
                inside += weight;
        }
        return total == 0 ? 0 : inside * 100 / total;
    }

    private static (double HalfWidth, double HalfHeight) GetTransformedTipHalfExtents(
        DrawingAttributes attributes)
    {
        double halfWidth = attributes.Width / 2;
        double halfHeight = attributes.Height / 2;
        Matrix matrix = attributes.StylusTipTransform;

        if (attributes.StylusTip == StylusTip.Rectangle)
        {
            return (
                Math.Abs(matrix.M11) * halfWidth + Math.Abs(matrix.M21) * halfHeight,
                Math.Abs(matrix.M12) * halfWidth + Math.Abs(matrix.M22) * halfHeight);
        }

        return (
            Math.Sqrt(Math.Pow(matrix.M11 * halfWidth, 2) + Math.Pow(matrix.M21 * halfHeight, 2)),
            Math.Sqrt(Math.Pow(matrix.M12 * halfWidth, 2) + Math.Pow(matrix.M22 * halfHeight, 2)));
    }

    private static List<Point> MaterializePoints(IEnumerable<Point> points)
    {
        var result = new List<Point>();
        foreach (Point point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                throw new ArgumentException("Ink geometry points must be finite.", nameof(points));
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        }
        if (result.Count > 1 && result[0] == result[^1])
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static bool PointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[j];
            Point b = polygon[i];
            if (DistanceToLineSegment(point, a, b) <= 1e-9)
                return true;

            if ((b.Y > point.Y) != (a.Y > point.Y) &&
                point.X < (a.X - b.X) * (point.Y - b.Y) / (a.Y - b.Y) + b.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double DistanceToPath(Point point, IReadOnlyList<Point> path)
    {
        if (path.Count == 1)
            return Distance(point, path[0]);
        double minimum = double.PositiveInfinity;
        for (int i = 0; i < path.Count - 1; i++)
            minimum = Math.Min(minimum, DistanceToLineSegment(point, path[i], path[i + 1]));
        return minimum;
    }

    private static double DistanceBetweenSegments(Point a, Point b, Point c, Point d)
    {
        if (SegmentsIntersect(a, b, c, d))
            return 0;
        return Math.Min(
            Math.Min(DistanceToLineSegment(a, c, d), DistanceToLineSegment(b, c, d)),
            Math.Min(DistanceToLineSegment(c, a, b), DistanceToLineSegment(d, a, b)));
    }

    private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
    {
        double o1 = Cross(a, b, c);
        double o2 = Cross(a, b, d);
        double o3 = Cross(c, d, a);
        double o4 = Cross(c, d, b);
        if (((o1 < 0 && o2 > 0) || (o1 > 0 && o2 < 0)) &&
            ((o3 < 0 && o4 > 0) || (o3 > 0 && o4 < 0)))
        {
            return true;
        }

        const double epsilon = 1e-9;
        return (Math.Abs(o1) <= epsilon && IsPointOnSegment(c, a, b)) ||
            (Math.Abs(o2) <= epsilon && IsPointOnSegment(d, a, b)) ||
            (Math.Abs(o3) <= epsilon && IsPointOnSegment(a, c, d)) ||
            (Math.Abs(o4) <= epsilon && IsPointOnSegment(b, c, d));
    }

    private static bool IsPointOnSegment(Point point, Point start, Point end) =>
        point.X >= Math.Min(start.X, end.X) - 1e-9 &&
        point.X <= Math.Max(start.X, end.X) + 1e-9 &&
        point.Y >= Math.Min(start.Y, end.Y) - 1e-9 &&
        point.Y <= Math.Max(start.Y, end.Y) + 1e-9;

    private static double Cross(Point a, Point b, Point c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double Distance(Point first, Point second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsFinite(Matrix matrix) =>
        double.IsFinite(matrix.M11) && double.IsFinite(matrix.M12) &&
        double.IsFinite(matrix.M21) && double.IsFinite(matrix.M22) &&
        double.IsFinite(matrix.OffsetX) && double.IsFinite(matrix.OffsetY);

    private static void ValidatePercentage(int percentage, string parameterName)
    {
        if (percentage is < 0 or > 100)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
