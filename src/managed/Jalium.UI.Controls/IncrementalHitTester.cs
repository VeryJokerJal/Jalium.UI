using Jalium.UI.Controls.Ink;

namespace Jalium.UI.Controls;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="IncrementalLassoHitTester"/> class.
    /// </summary>
    internal IncrementalLassoHitTester(StrokeCollection strokes)
    {
        _strokes = strokes;
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

        var selectedStrokes = new StrokeCollection();
        var deselectedStrokes = new StrokeCollection();

        foreach (var stroke in _strokes)
        {
            bool isInside = IsStrokeInsideLasso(stroke);
            if (isInside)
                selectedStrokes.Add(stroke);
        }

        if (selectedStrokes.Count > 0 || deselectedStrokes.Count > 0)
        {
            SelectionChanged?.Invoke(this,
                new LassoSelectionChangedEventArgs(selectedStrokes, deselectedStrokes));
        }
    }

    private bool IsStrokeInsideLasso(Stroke stroke)
    {
        // Simplified: check if the stroke's bounding box center is inside the lasso polygon
        var bounds = stroke.GetBounds();
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return IsPointInPolygon(center, _lassoPoints);
    }

    private static bool IsPointInPolygon(Point point, List<Point> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                          (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// Dynamically hit tests a <see cref="StrokeCollection"/> with an eraser path.
/// </summary>
public class IncrementalStrokeHitTester : IncrementalHitTester
{
    private readonly StrokeCollection _strokes;
    private readonly StylusShape _eraserShape;

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
            foreach (var stroke in _strokes)
            {
                if (stroke.HitTest(point, _eraserShape.Width))
                {
                    StrokeHit?.Invoke(this, new StrokeHitEventArgs(stroke, point));
                }
            }
        }
    }
}

/// <summary>
/// Represents the shape of a stylus tip.
/// </summary>
public class StylusShape
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StylusShape"/> class.
    /// </summary>
    protected StylusShape(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Gets the width of the stylus shape.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the stylus shape.</summary>
    public double Height { get; }

    /// <summary>Gets or sets the rotation of the stylus shape.</summary>
    public double Rotation { get; set; }
}

/// <summary>
/// Represents a rectangular stylus tip.
/// </summary>
public class RectangleStylusShape : StylusShape
{
    /// <summary>
    /// Initializes a new instance with the specified width and height.
    /// </summary>
    public RectangleStylusShape(double width, double height) : base(width, height) { }

    /// <summary>
    /// Initializes a new instance with the specified width, height, and rotation.
    /// </summary>
    public RectangleStylusShape(double width, double height, double rotation) : base(width, height)
    {
        Rotation = rotation;
    }
}

/// <summary>
/// Represents an elliptical stylus tip.
/// </summary>
public class EllipseStylusShape : StylusShape
{
    /// <summary>
    /// Initializes a new instance with the specified width and height.
    /// </summary>
    public EllipseStylusShape(double width, double height) : base(width, height) { }

    /// <summary>
    /// Initializes a new instance with the specified width, height, and rotation.
    /// </summary>
    public EllipseStylusShape(double width, double height, double rotation) : base(width, height)
    {
        Rotation = rotation;
    }
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
    /// <summary>
    /// Initializes a new instance of the <see cref="StrokeHitEventArgs"/> class.
    /// </summary>
    public StrokeHitEventArgs(Stroke hitStroke, Point hitPoint)
    {
        HitStroke = hitStroke;
        HitPoint = hitPoint;
    }

    /// <summary>Gets the stroke that was hit.</summary>
    public Stroke HitStroke { get; }

    /// <summary>Gets the point at which the hit occurred.</summary>
    public Point HitPoint { get; }
}

/// <summary>
/// Represents the method that handles the StrokeHit event.
/// </summary>
public delegate void StrokeHitEventHandler(object sender, StrokeHitEventArgs e);
