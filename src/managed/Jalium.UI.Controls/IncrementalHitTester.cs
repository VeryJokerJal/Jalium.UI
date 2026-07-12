using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Ink;

/// <summary>
/// Dynamically performs hit testing on an <see cref="InkCanvas"/>.
/// </summary>
public abstract class IncrementalHitTester
{
    /// <summary>
    /// Gets a value indicating whether the IncrementalHitTester is valid.
    /// </summary>
    public bool IsValid { get; private set; } = true;

    /// <summary>
    /// Adds a point to the IncrementalHitTester.
    /// </summary>
    public void AddPoint(Point point)
    {
        AddPoints(new[] { point });
    }

    /// <summary>
    /// Adds points to the IncrementalHitTester.
    /// </summary>
    public void AddPoints(IEnumerable<Point> points)
    {
        if (!IsValid)
            throw new InvalidOperationException("This IncrementalHitTester is no longer valid.");

        AddPointsCore(points);
    }

    /// <summary>Adds stylus points to the incremental hit tester.</summary>
    public void AddPoints(StylusPointCollection stylusPoints)
    {
        ArgumentNullException.ThrowIfNull(stylusPoints);
        AddPoints(stylusPoints.Select(static point => point.ToPoint()));
    }

    /// <summary>
    /// When overridden in a derived class, adds points to the hit tester.
    /// </summary>
    protected abstract void AddPointsCore(IEnumerable<Point> points);

    /// <summary>
    /// Releases resources used by the IncrementalHitTester.
    /// </summary>
    public void EndHitTesting()
    {
        IsValid = false;
    }
}

/// <summary>
/// Dynamically hit tests a <see cref="StrokeCollection"/> with a lasso.
/// </summary>
public class IncrementalLassoHitTester : IncrementalHitTester
{
    private readonly StrokeCollection _strokes;
    private readonly List<Point> _lassoPoints = new();
    private readonly HashSet<Stroke> _selected = new(ReferenceEqualityComparer.Instance);
    private readonly int _percentageWithinLasso;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncrementalLassoHitTester"/> class.
    /// </summary>
    internal IncrementalLassoHitTester(StrokeCollection strokes, int percentageWithinLasso = 80)
    {
        _strokes = strokes;
        _percentageWithinLasso = percentageWithinLasso;
    }

    /// <summary>
    /// Occurs when the lasso path selects or deselects an ink <see cref="Stroke"/>.
    /// </summary>
    public event LassoSelectionChangedEventHandler? SelectionChanged;

    /// <inheritdoc />
    protected override void AddPointsCore(IEnumerable<Point> points)
    {
        _lassoPoints.AddRange(points);
        // Perform lasso hit testing against strokes
        PerformHitTest();
    }

    private void PerformHitTest()
    {
        if (_lassoPoints.Count < 3) return;

        var newlySelected = new StrokeCollection();
        var newlyDeselected = new StrokeCollection();
        var current = new HashSet<Stroke>(ReferenceEqualityComparer.Instance);

        foreach (var stroke in _strokes)
        {
            if (stroke.HitTest(_lassoPoints, _percentageWithinLasso))
                current.Add(stroke);
        }

        foreach (Stroke stroke in current)
        {
            if (!_selected.Contains(stroke))
                newlySelected.Add(stroke);
        }
        foreach (Stroke stroke in _selected)
        {
            if (!current.Contains(stroke))
                newlyDeselected.Add(stroke);
        }

        _selected.Clear();
        _selected.UnionWith(current);
        if (newlySelected.Count > 0 || newlyDeselected.Count > 0)
            OnSelectionChanged(new LassoSelectionChangedEventArgs(newlySelected, newlyDeselected));
    }

    /// <summary>Raises <see cref="SelectionChanged"/>.</summary>
    protected void OnSelectionChanged(LassoSelectionChangedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        SelectionChanged?.Invoke(this, eventArgs);
    }
}

/// <summary>
/// Dynamically hit tests a <see cref="StrokeCollection"/> with an eraser path.
/// </summary>
public class IncrementalStrokeHitTester : IncrementalHitTester
{
    private readonly StrokeCollection _strokes;
    private readonly StylusShape _eraserShape;
    private Point? _lastPoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncrementalStrokeHitTester"/> class.
    /// </summary>
    internal IncrementalStrokeHitTester(StrokeCollection strokes, StylusShape eraserShape)
    {
        _strokes = strokes;
        _eraserShape = eraserShape;
    }

    /// <summary>
    /// Occurs when the eraser path intersects an ink <see cref="Stroke"/>.
    /// </summary>
    public event StrokeHitEventHandler? StrokeHit;

    /// <inheritdoc />
    protected override void AddPointsCore(IEnumerable<Point> points)
    {
        foreach (var point in points)
        {
            Point[] path = _lastPoint is Point previous ? [previous, point] : [point];
            foreach (var stroke in _strokes)
            {
                if (stroke.HitTest(path, _eraserShape))
                {
                    OnStrokeHit(new StrokeHitEventArgs(stroke, point, _eraserShape));
                }
            }
            _lastPoint = point;
        }
    }

    /// <summary>Raises <see cref="StrokeHit"/>.</summary>
    protected void OnStrokeHit(StrokeHitEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        StrokeHit?.Invoke(this, eventArgs);
    }
}

/// <summary>
/// Represents the shape of a stylus tip.
/// </summary>
public abstract class StylusShape
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StylusShape"/> class.
    /// </summary>
    internal StylusShape(StylusTip stylusTip, double width, double height, double rotation)
    {
        if (!double.IsFinite(width) || width < DrawingAttributes.MinWidth || width > DrawingAttributes.MaxWidth)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height < DrawingAttributes.MinHeight || height > DrawingAttributes.MaxHeight)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (!double.IsFinite(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));

        StylusTip = stylusTip;
        Width = width;
        Height = height;
        Rotation = rotation % 360.0;
    }

    internal StylusTip StylusTip { get; }

    /// <summary>Gets the width of the stylus shape.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the stylus shape.</summary>
    public double Height { get; }

    /// <summary>Gets the clockwise rotation, in degrees, of the stylus shape.</summary>
    public double Rotation { get; }
}

/// <summary>
/// Represents a rectangular stylus tip.
/// </summary>
public sealed class RectangleStylusShape : StylusShape
{
    /// <summary>
    /// Initializes a new instance with the specified width and height.
    /// </summary>
    public RectangleStylusShape(double width, double height)
        : this(width, height, 0.0) { }

    /// <summary>
    /// Initializes a new instance with the specified width, height, and rotation.
    /// </summary>
    public RectangleStylusShape(double width, double height, double rotation)
        : base(StylusTip.Rectangle, width, height, rotation) { }
}

/// <summary>
/// Represents an elliptical stylus tip.
/// </summary>
public sealed class EllipseStylusShape : StylusShape
{
    /// <summary>
    /// Initializes a new instance with the specified width and height.
    /// </summary>
    public EllipseStylusShape(double width, double height)
        : this(width, height, 0.0) { }

    /// <summary>
    /// Initializes a new instance with the specified width, height, and rotation.
    /// </summary>
    public EllipseStylusShape(double width, double height, double rotation)
        : base(StylusTip.Ellipse, width, height, rotation) { }
}

/// <summary>
/// Provides data for the <see cref="IncrementalLassoHitTester.SelectionChanged"/> event.
/// </summary>
public class LassoSelectionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LassoSelectionChangedEventArgs"/> class.
    /// </summary>
    public LassoSelectionChangedEventArgs(StrokeCollection selectedStrokes, StrokeCollection deselectedStrokes)
    {
        SelectedStrokes = selectedStrokes;
        DeselectedStrokes = deselectedStrokes;
    }

    /// <summary>Gets the strokes that are selected.</summary>
    public StrokeCollection SelectedStrokes { get; }

    /// <summary>Gets the strokes that are deselected.</summary>
    public StrokeCollection DeselectedStrokes { get; }
}

/// <summary>
/// Represents the method that handles the SelectionChanged event.
/// </summary>
public delegate void LassoSelectionChangedEventHandler(object sender, LassoSelectionChangedEventArgs e);

/// <summary>
/// Provides data for the <see cref="IncrementalStrokeHitTester.StrokeHit"/> event.
/// </summary>
public class StrokeHitEventArgs : EventArgs
{
    private readonly StylusShape _eraserShape;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeHitEventArgs"/> class.
    /// </summary>
    public StrokeHitEventArgs(Stroke hitStroke, Point hitPoint)
        : this(hitStroke, hitPoint, new EllipseStylusShape(1.0, 1.0))
    {
    }

    internal StrokeHitEventArgs(Stroke hitStroke, Point hitPoint, StylusShape eraserShape)
    {
        HitStroke = hitStroke ?? throw new ArgumentNullException(nameof(hitStroke));
        HitPoint = hitPoint;
        _eraserShape = eraserShape ?? throw new ArgumentNullException(nameof(eraserShape));
    }

    /// <summary>Gets the stroke that was hit.</summary>
    public Stroke HitStroke { get; }

    /// <summary>Gets the point at which the hit occurred.</summary>
    public Point HitPoint { get; }

    /// <summary>Gets the fragments left after point-erasing the hit stroke.</summary>
    public StrokeCollection GetPointEraseResults() => HitStroke.GetEraseResult(
        [HitPoint],
        _eraserShape);
}

/// <summary>
/// Represents the method that handles the StrokeHit event.
/// </summary>
public delegate void StrokeHitEventHandler(object sender, StrokeHitEventArgs e);
