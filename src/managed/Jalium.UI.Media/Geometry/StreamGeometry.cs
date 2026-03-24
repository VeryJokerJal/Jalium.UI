namespace Jalium.UI.Media;

/// <summary>
/// Defines a geometric shape described using a StreamGeometryContext.
/// This geometry is lighter-weight than PathGeometry and is optimized for describing
/// geometries that don't need modification after creation.
/// </summary>
public sealed class StreamGeometry : Geometry
{
    private PathGeometry? _pathGeometry;
    private bool _isOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamGeometry"/> class.
    /// </summary>
    public StreamGeometry()
    {
        _pathGeometry = new PathGeometry();
    }

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule
    {
        get => _pathGeometry?.FillRule ?? FillRule.EvenOdd;
        set
        {
            if (_pathGeometry != null)
            {
                _pathGeometry.FillRule = value;
            }
        }
    }

    /// <inheritdoc />
    public override Rect Bounds => _pathGeometry?.Bounds ?? Rect.Empty;

    /// <summary>
    /// Opens the StreamGeometry for populating.
    /// </summary>
    /// <returns>A StreamGeometryContext that can be used to describe the geometry.</returns>
    public StreamGeometryContext Open()
    {
        if (_isOpen)
        {
            throw new InvalidOperationException("StreamGeometry is already open.");
        }

        _isOpen = true;
        _pathGeometry = new PathGeometry { FillRule = FillRule };
        return new StreamGeometryContext(this);
    }

    /// <summary>
    /// Removes all figures from the geometry.
    /// </summary>
    public void Clear()
    {
        if (_isOpen)
        {
            throw new InvalidOperationException("Cannot clear while StreamGeometry is open.");
        }

        _pathGeometry?.Figures.Clear();
    }

    /// <summary>
    /// Returns true if this geometry is empty.
    /// </summary>
    public bool IsEmpty()
    {
        return _pathGeometry == null || _pathGeometry.Figures.Count == 0;
    }

    /// <summary>
    /// Returns true if this geometry may have curved segments.
    /// </summary>
    public bool MayHaveCurves()
    {
        if (_pathGeometry == null) return false;

        foreach (var figure in _pathGeometry.Figures)
        {
            foreach (var segment in figure.Segments)
            {
                if (segment is BezierSegment ||
                    segment is QuadraticBezierSegment ||
                    segment is PolyBezierSegment ||
                    segment is PolyQuadraticBezierSegment ||
                    segment is ArcSegment)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the underlying PathGeometry.
    /// </summary>
    public PathGeometry? GetPathGeometry() => _pathGeometry;

    /// <inheritdoc />
    public override Geometry Clone()
    {
        var clone = new StreamGeometry();
        if (_pathGeometry != null)
        {
            var clonedPath = _pathGeometry.ClonePathGeometry();
            clone._pathGeometry = clonedPath;
        }
        clone.Transform = Transform;
        return clone;
    }

    /// <summary>
    /// Called by StreamGeometryContext when it is closed.
    /// </summary>
    internal void Close(PathFigure? currentFigure)
    {
        if (currentFigure != null && _pathGeometry != null)
        {
            _pathGeometry.Figures.Add(currentFigure);
        }

        _isOpen = false;
    }
}

/// <summary>
/// Describes a geometry using drawing commands. This class is used with a StreamGeometry
/// object to create a lightweight geometry that does not support data binding, animation, or modification.
/// </summary>
public sealed class StreamGeometryContext : IDisposable
{
    private readonly StreamGeometry _owner;
    private PathFigure? _currentFigure;
    private bool _isClosed;

    internal StreamGeometryContext(StreamGeometry owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Starts a new figure at the specified point.
    /// </summary>
    /// <param name="startPoint">The starting point for the new figure.</param>
    /// <param name="isFilled">true if the figure should be filled; otherwise, false.</param>
    /// <param name="isClosed">true if the figure should be closed; otherwise, false.</param>
    public void BeginFigure(Point startPoint, bool isFilled, bool isClosed)
    {
        ThrowIfClosed();

        // Save previous figure if any
        if (_currentFigure != null)
        {
            _owner.GetPathGeometry()?.Figures.Add(_currentFigure);
        }

        _currentFigure = new PathFigure
        {
            StartPoint = startPoint,
            IsFilled = isFilled,
            IsClosed = isClosed
        };
    }

    /// <summary>
    /// Draws a straight line to the specified point.
    /// </summary>
    /// <param name="point">The destination point.</param>
    /// <param name="isStroked">true if the line should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public void LineTo(Point point, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new LineSegment(point, isStroked));
    }

    /// <summary>
    /// Draws one or more connected straight lines.
    /// </summary>
    /// <param name="points">The collection of points that specify the lines to draw.</param>
    /// <param name="isStroked">true if the lines should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new PolyLineSegment(points, isStroked));
    }

    /// <summary>
    /// Draws a cubic Bezier curve to the specified point.
    /// </summary>
    /// <param name="point1">The first control point.</param>
    /// <param name="point2">The second control point.</param>
    /// <param name="point3">The destination point.</param>
    /// <param name="isStroked">true if the curve should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public void BezierTo(Point point1, Point point2, Point point3, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new BezierSegment(point1, point2, point3, isStroked));
    }

    /// <summary>
    /// Draws one or more connected cubic Bezier curves.
    /// </summary>
    /// <param name="points">The collection of points (in groups of three) that specify the curves.</param>
    /// <param name="isStroked">true if the curves should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public void PolyBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        var segment = new PolyBezierSegment { IsStroked = isStroked };
        segment.Points.AddRange(points);
        _currentFigure!.Segments.Add(segment);
    }

    /// <summary>
    /// Draws a quadratic Bezier curve to the specified point.
    /// </summary>
    /// <param name="point1">The control point.</param>
    /// <param name="point2">The destination point.</param>
    /// <param name="isStroked">true if the curve should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public void QuadraticBezierTo(Point point1, Point point2, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new QuadraticBezierSegment(point1, point2, isStroked));
    }

    /// <summary>
    /// Draws one or more connected quadratic Bezier curves.
    /// </summary>
    /// <param name="points">The collection of points (in groups of two) that specify the curves.</param>
    /// <param name="isStroked">true if the curves should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the joins should be smooth; otherwise, false.</param>
    public void PolyQuadraticBezierTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        var segment = new PolyQuadraticBezierSegment { IsStroked = isStroked };
        segment.Points.AddRange(points);
        _currentFigure!.Segments.Add(segment);
    }

    /// <summary>
    /// Draws an arc to the specified point.
    /// </summary>
    /// <param name="point">The destination point.</param>
    /// <param name="size">The size of the arc (the x and y radius of the ellipse).</param>
    /// <param name="rotationAngle">The rotation angle of the ellipse in degrees.</param>
    /// <param name="isLargeArc">true if the arc should be greater than 180 degrees; otherwise, false.</param>
    /// <param name="sweepDirection">The direction to draw the arc.</param>
    /// <param name="isStroked">true if the arc should be stroked; otherwise, false.</param>
    /// <param name="isSmoothJoin">true if the join should be smooth; otherwise, false.</param>
    public void ArcTo(Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked, bool isSmoothJoin)
    {
        ThrowIfClosed();
        ThrowIfNoFigure();

        _currentFigure!.Segments.Add(new ArcSegment(point, size, rotationAngle, isLargeArc, sweepDirection, isStroked));
    }

    /// <summary>
    /// Closes the StreamGeometryContext and flushes its content so it can be rendered.
    /// </summary>
    public void Close()
    {
        if (_isClosed) return;

        _isClosed = true;
        _owner.Close(_currentFigure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
    }

    private void ThrowIfClosed()
    {
        if (_isClosed)
        {
            throw new InvalidOperationException("StreamGeometryContext is already closed.");
        }
    }

    private void ThrowIfNoFigure()
    {
        if (_currentFigure == null)
        {
            throw new InvalidOperationException("BeginFigure must be called before drawing segments.");
        }
    }
}
