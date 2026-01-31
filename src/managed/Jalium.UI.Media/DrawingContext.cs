namespace Jalium.UI.Media;

/// <summary>
/// Describes visual content using draw, push, and pop commands.
/// </summary>
public abstract class DrawingContext : IDisposable, IClipDrawingContext
{
    /// <summary>
    /// Pushes a clip region onto the clip stack (IClipDrawingContext implementation).
    /// </summary>
    /// <param name="clipGeometry">The clipping geometry (Geometry or Rect).</param>
    void IClipDrawingContext.PushClip(object clipGeometry)
    {
        if (clipGeometry is Geometry geometry)
        {
            PushClip(geometry);
        }
        else if (clipGeometry is Rect rect)
        {
            PushClip(new RectangleGeometry(rect));
        }
    }

    /// <summary>
    /// Pops the most recent clip from the clip stack (IClipDrawingContext implementation).
    /// </summary>
    void IClipDrawingContext.Pop()
    {
        Pop();
    }

    /// <summary>
    /// Draws a line between two points.
    /// </summary>
    /// <param name="pen">The pen to use.</param>
    /// <param name="point0">The start point.</param>
    /// <param name="point1">The end point.</param>
    public abstract void DrawLine(Pen pen, Point point0, Point point1);

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    public abstract void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle);

    /// <summary>
    /// Draws a rounded rectangle.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    /// <param name="radiusX">The X radius of the corners.</param>
    /// <param name="radiusY">The Y radius of the corners.</param>
    public abstract void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY);

    /// <summary>
    /// Draws an ellipse.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="center">The center point.</param>
    /// <param name="radiusX">The X radius.</param>
    /// <param name="radiusY">The Y radius.</param>
    public abstract void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY);

    /// <summary>
    /// Draws text at the specified location.
    /// </summary>
    /// <param name="formattedText">The formatted text to draw.</param>
    /// <param name="origin">The top-left origin point.</param>
    public abstract void DrawText(FormattedText formattedText, Point origin);

    /// <summary>
    /// Draws a geometry.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="geometry">The geometry to draw.</param>
    public abstract void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry);

    /// <summary>
    /// Draws an image.
    /// </summary>
    /// <param name="imageSource">The image to draw.</param>
    /// <param name="rectangle">The destination rectangle.</param>
    public abstract void DrawImage(ImageSource imageSource, Rect rectangle);

    /// <summary>
    /// Draws a backdrop effect.
    /// </summary>
    /// <param name="rectangle">The area to apply the effect to.</param>
    /// <param name="effect">The backdrop effect to apply. Can be BlurEffect, AcrylicEffect, MicaEffect, or custom implementations.</param>
    /// <param name="cornerRadius">The corner radius for the effect area.</param>
    public abstract void DrawBackdropEffect(
        Rect rectangle,
        IBackdropEffect effect,
        CornerRadius cornerRadius);

    /// <summary>
    /// Pushes a transform onto the transform stack.
    /// </summary>
    /// <param name="transform">The transform to push.</param>
    public abstract void PushTransform(Transform transform);

    /// <summary>
    /// Pushes a clip region onto the clip stack.
    /// </summary>
    /// <param name="clipGeometry">The clipping geometry.</param>
    public abstract void PushClip(Geometry clipGeometry);

    /// <summary>
    /// Pushes an opacity value onto the opacity stack.
    /// </summary>
    /// <param name="opacity">The opacity (0.0 - 1.0).</param>
    public abstract void PushOpacity(double opacity);

    /// <summary>
    /// Pops the most recent transform, clip, or opacity.
    /// </summary>
    public abstract void Pop();

    /// <summary>
    /// Closes the drawing context.
    /// </summary>
    public abstract void Close();

    /// <inheritdoc />
    public void Dispose()
    {
        Close();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Describes how a shape is outlined.
/// </summary>
public class Pen
{
    /// <summary>
    /// Gets or sets the brush for the stroke.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the stroke thickness.
    /// </summary>
    public double Thickness { get; set; } = 1;

    /// <summary>
    /// Gets or sets the line cap style for the start of the line.
    /// </summary>
    public PenLineCap StartLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>
    /// Gets or sets the line cap style for the end of the line.
    /// </summary>
    public PenLineCap EndLineCap { get; set; } = PenLineCap.Flat;

    /// <summary>
    /// Gets or sets the dash cap style.
    /// </summary>
    public PenLineCap DashCap { get; set; } = PenLineCap.Flat;

    /// <summary>
    /// Gets or sets the line join style.
    /// </summary>
    public PenLineJoin LineJoin { get; set; } = PenLineJoin.Miter;

    /// <summary>
    /// Gets or sets the miter limit.
    /// </summary>
    public double MiterLimit { get; set; } = 10;

    /// <summary>
    /// Gets or sets the dash style.
    /// </summary>
    public DashStyle? DashStyle { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    public Pen()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Pen"/> class.
    /// </summary>
    /// <param name="brush">The brush for the stroke.</param>
    /// <param name="thickness">The stroke thickness.</param>
    public Pen(Brush brush, double thickness)
    {
        Brush = brush;
        Thickness = thickness;
    }
}

/// <summary>
/// Describes the dash pattern of a stroke.
/// </summary>
public class DashStyle
{
    /// <summary>
    /// Gets the collection of dash lengths.
    /// </summary>
    public List<double> Dashes { get; } = new();

    /// <summary>
    /// Gets or sets the offset to start the dash pattern.
    /// </summary>
    public double Offset { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DashStyle"/> class.
    /// </summary>
    public DashStyle()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DashStyle"/> class.
    /// </summary>
    /// <param name="dashes">The dash array.</param>
    /// <param name="offset">The dash offset.</param>
    public DashStyle(IEnumerable<double> dashes, double offset = 0)
    {
        Dashes.AddRange(dashes);
        Offset = offset;
    }

    /// <summary>
    /// Gets a solid (no dashes) style.
    /// </summary>
    public static DashStyle Solid => new();

    /// <summary>
    /// Gets a dash style.
    /// </summary>
    public static DashStyle Dash => new(new[] { 2.0, 2.0 });

    /// <summary>
    /// Gets a dot style.
    /// </summary>
    public static DashStyle Dot => new(new[] { 0.0, 2.0 });

    /// <summary>
    /// Gets a dash-dot style.
    /// </summary>
    public static DashStyle DashDot => new(new[] { 2.0, 2.0, 0.0, 2.0 });

    /// <summary>
    /// Gets a dash-dot-dot style.
    /// </summary>
    public static DashStyle DashDotDot => new(new[] { 2.0, 2.0, 0.0, 2.0, 0.0, 2.0 });
}

/// <summary>
/// Specifies the shape at the end of a line.
/// </summary>
public enum PenLineCap
{
    /// <summary>
    /// A cap that does not extend past the end point.
    /// </summary>
    Flat,

    /// <summary>
    /// A semicircular cap.
    /// </summary>
    Round,

    /// <summary>
    /// A triangular cap.
    /// </summary>
    Triangle,

    /// <summary>
    /// A square cap that extends past the end point.
    /// </summary>
    Square
}

/// <summary>
/// Specifies how lines are joined.
/// </summary>
public enum PenLineJoin
{
    /// <summary>
    /// Lines are joined with a sharp corner.
    /// </summary>
    Miter,

    /// <summary>
    /// Lines are joined with a beveled corner.
    /// </summary>
    Bevel,

    /// <summary>
    /// Lines are joined with a rounded corner.
    /// </summary>
    Round
}

/// <summary>
/// Represents formatted text for rendering and measurement.
/// </summary>
public class FormattedText
{
    /// <summary>
    /// Gets the text content.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FontFamily { get; }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    public double FontSize { get; }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the maximum width for text wrapping.
    /// </summary>
    public double MaxTextWidth { get; set; } = double.MaxValue;

    /// <summary>
    /// Gets or sets the maximum height.
    /// </summary>
    public double MaxTextHeight { get; set; } = double.MaxValue;

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming Trimming { get; set; } = TextTrimming.None;

    /// <summary>
    /// Gets or sets the font weight (400 = normal, 700 = bold).
    /// </summary>
    public int FontWeight { get; set; } = 400;

    /// <summary>
    /// Gets or sets the font style (0 = normal, 1 = italic, 2 = oblique).
    /// </summary>
    public int FontStyle { get; set; } = 0;

    /// <summary>
    /// Gets or sets the font stretch (5 = normal). Values 1-9 map to DirectWrite font stretch.
    /// </summary>
    public int FontStretch { get; set; } = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedText"/> class.
    /// </summary>
    public FormattedText(string text, string fontFamily, double fontSize)
    {
        Text = text;
        FontFamily = fontFamily;
        FontSize = fontSize;
    }

    /// <summary>
    /// Gets the width of the text layout.
    /// </summary>
    public double Width { get; internal set; }

    /// <summary>
    /// Gets the height of the text layout.
    /// </summary>
    public double Height { get; internal set; }

    /// <summary>
    /// Gets the natural line height (ascent + descent + line gap).
    /// This is the WPF-style line height based on actual font metrics.
    /// </summary>
    public double LineHeight { get; internal set; }

    /// <summary>
    /// Gets the font ascent (distance from baseline to top of the tallest glyph).
    /// </summary>
    public double Ascent { get; internal set; }

    /// <summary>
    /// Gets the font descent (distance from baseline to bottom of the lowest glyph).
    /// </summary>
    public double Descent { get; internal set; }

    /// <summary>
    /// Gets the recommended line gap between lines.
    /// </summary>
    public double LineGap { get; internal set; }

    /// <summary>
    /// Gets the baseline offset from the top of the line.
    /// </summary>
    public double Baseline { get; internal set; }

    /// <summary>
    /// Gets the number of lines in the text layout.
    /// </summary>
    public int LineCount { get; internal set; } = 1;

    /// <summary>
    /// Gets whether this text has been measured using native text measurement.
    /// </summary>
    public bool IsMeasured { get; internal set; }
}

/// <summary>
/// Specifies how text is trimmed when it overflows the edge of its container.
/// </summary>
public enum TextTrimming
{
    /// <summary>
    /// Text is not trimmed.
    /// </summary>
    None,

    /// <summary>
    /// Text is trimmed at a character boundary. An ellipsis (...) is drawn in place of remaining text.
    /// </summary>
    CharacterEllipsis,

    /// <summary>
    /// Text is trimmed at a word boundary. An ellipsis (...) is drawn in place of remaining text.
    /// </summary>
    WordEllipsis
}

/// <summary>
/// Base class for geometry objects.
/// </summary>
public abstract class Geometry
{
    /// <summary>
    /// Gets the bounding box of this geometry.
    /// </summary>
    public abstract Rect Bounds { get; }
}

/// <summary>
/// Represents a rectangle geometry with optional rounded corners.
/// </summary>
public class RectangleGeometry : Geometry
{
    /// <summary>
    /// Gets or sets the rectangle.
    /// </summary>
    public Rect Rect { get; set; }

    /// <summary>
    /// Gets or sets the X radius of the corners.
    /// </summary>
    public double RadiusX { get; set; }

    /// <summary>
    /// Gets or sets the Y radius of the corners.
    /// </summary>
    public double RadiusY { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class.
    /// </summary>
    public RectangleGeometry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    public RectangleGeometry(Rect rect)
    {
        Rect = rect;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RectangleGeometry"/> class with rounded corners.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radiusX">The X radius of the corners.</param>
    /// <param name="radiusY">The Y radius of the corners.</param>
    public RectangleGeometry(Rect rect, double radiusX, double radiusY)
    {
        Rect = rect;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    /// <inheritdoc />
    public override Rect Bounds => Rect;
}

/// <summary>
/// Represents an ellipse geometry.
/// </summary>
public class EllipseGeometry : Geometry
{
    /// <summary>
    /// Gets or sets the center point.
    /// </summary>
    public Point Center { get; set; }

    /// <summary>
    /// Gets or sets the X radius.
    /// </summary>
    public double RadiusX { get; set; }

    /// <summary>
    /// Gets or sets the Y radius.
    /// </summary>
    public double RadiusY { get; set; }

    /// <inheritdoc />
    public override Rect Bounds => new Rect(
        Center.X - RadiusX,
        Center.Y - RadiusY,
        RadiusX * 2,
        RadiusY * 2);
}

/// <summary>
/// Represents a line geometry.
/// </summary>
public class LineGeometry : Geometry
{
    /// <summary>
    /// Gets or sets the start point of the line.
    /// </summary>
    public Point StartPoint { get; set; }

    /// <summary>
    /// Gets or sets the end point of the line.
    /// </summary>
    public Point EndPoint { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineGeometry"/> class.
    /// </summary>
    public LineGeometry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineGeometry"/> class.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="endPoint">The end point.</param>
    public LineGeometry(Point startPoint, Point endPoint)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    /// <inheritdoc />
    public override Rect Bounds => new Rect(
        Math.Min(StartPoint.X, EndPoint.X),
        Math.Min(StartPoint.Y, EndPoint.Y),
        Math.Abs(EndPoint.X - StartPoint.X),
        Math.Abs(EndPoint.Y - StartPoint.Y));
}

/// <summary>
/// Represents a composite geometry made up of other geometry objects.
/// </summary>
public class GeometryGroup : Geometry
{
    /// <summary>
    /// Gets the collection of child geometries.
    /// </summary>
    public List<Geometry> Children { get; } = new();

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (Children.Count == 0) return Rect.Empty;

            var result = Children[0].Bounds;
            for (int i = 1; i < Children.Count; i++)
            {
                result = result.Union(Children[i].Bounds);
            }
            return result;
        }
    }
}

/// <summary>
/// Represents a geometry that is the combination of two geometries.
/// </summary>
public class CombinedGeometry : Geometry
{
    /// <summary>
    /// Gets or sets the first geometry.
    /// </summary>
    public Geometry? Geometry1 { get; set; }

    /// <summary>
    /// Gets or sets the second geometry.
    /// </summary>
    public Geometry? Geometry2 { get; set; }

    /// <summary>
    /// Gets or sets the method used to combine the two geometries.
    /// </summary>
    public GeometryCombineMode GeometryCombineMode { get; set; } = GeometryCombineMode.Union;

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            var bounds1 = Geometry1?.Bounds ?? Rect.Empty;
            var bounds2 = Geometry2?.Bounds ?? Rect.Empty;

            return GeometryCombineMode switch
            {
                GeometryCombineMode.Union => bounds1.Union(bounds2),
                GeometryCombineMode.Intersect => bounds1.Intersect(bounds2),
                GeometryCombineMode.Exclude => bounds1,
                GeometryCombineMode.Xor => bounds1.Union(bounds2),
                _ => bounds1.Union(bounds2)
            };
        }
    }
}

/// <summary>
/// Specifies the method used to combine two geometries.
/// </summary>
public enum GeometryCombineMode
{
    /// <summary>
    /// The two geometries are combined by taking their union.
    /// </summary>
    Union,

    /// <summary>
    /// The two geometries are combined by taking their intersection.
    /// </summary>
    Intersect,

    /// <summary>
    /// The second geometry is excluded from the first.
    /// </summary>
    Exclude,

    /// <summary>
    /// The two geometries are combined by taking the area that exists in the first geometry but not the second and the area that exists in the second geometry but not the first.
    /// </summary>
    Xor
}

/// <summary>
/// Specifies how the interior of a closed path is filled.
/// </summary>
public enum FillRule
{
    /// <summary>
    /// A point is inside if a ray from that point crosses an odd number of boundaries.
    /// </summary>
    EvenOdd,

    /// <summary>
    /// A point is inside if the winding number is non-zero.
    /// </summary>
    Nonzero
}

/// <summary>
/// Represents a complex shape composed of arcs, curves, lines, and rectangles.
/// </summary>
public class PathGeometry : Geometry
{
    /// <summary>
    /// Gets the collection of path figures.
    /// </summary>
    public List<PathFigure> Figures { get; } = new();

    /// <summary>
    /// Gets or sets the fill rule for this geometry.
    /// </summary>
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (Figures.Count == 0) return Rect.Empty;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var figure in Figures)
            {
                UpdateBounds(figure.StartPoint, ref minX, ref minY, ref maxX, ref maxY);

                foreach (var segment in figure.Segments)
                {
                    foreach (var point in segment.GetPoints())
                    {
                        UpdateBounds(point, ref minX, ref minY, ref maxX, ref maxY);
                    }
                }
            }

            if (minX > maxX || minY > maxY) return Rect.Empty;
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }

    private static void UpdateBounds(Point pt, ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (pt.X < minX) minX = pt.X;
        if (pt.Y < minY) minY = pt.Y;
        if (pt.X > maxX) maxX = pt.X;
        if (pt.Y > maxY) maxY = pt.Y;
    }
}

/// <summary>
/// Represents a subsection of a path geometry.
/// </summary>
public class PathFigure
{
    /// <summary>
    /// Gets or sets the start point of the figure.
    /// </summary>
    public Point StartPoint { get; set; }

    /// <summary>
    /// Gets the collection of segments in this figure.
    /// </summary>
    public List<PathSegment> Segments { get; } = new();

    /// <summary>
    /// Gets or sets whether this figure is closed.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Gets or sets whether this figure is filled.
    /// </summary>
    public bool IsFilled { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathFigure"/> class.
    /// </summary>
    public PathFigure()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PathFigure"/> class.
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <param name="segments">The segments.</param>
    /// <param name="isClosed">Whether the figure is closed.</param>
    public PathFigure(Point startPoint, IEnumerable<PathSegment> segments, bool isClosed)
    {
        StartPoint = startPoint;
        Segments.AddRange(segments);
        IsClosed = isClosed;
    }
}

/// <summary>
/// Base class for path segments.
/// </summary>
public abstract class PathSegment
{
    /// <summary>
    /// Gets or sets whether the segment is stroked.
    /// </summary>
    public bool IsStroked { get; set; } = true;

    /// <summary>
    /// Gets the points that define this segment.
    /// </summary>
    public abstract IEnumerable<Point> GetPoints();
}

/// <summary>
/// Represents a straight line segment.
/// </summary>
public class LineSegment : PathSegment
{
    /// <summary>
    /// Gets or sets the end point of the line segment.
    /// </summary>
    public Point Point { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineSegment"/> class.
    /// </summary>
    public LineSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineSegment"/> class.
    /// </summary>
    /// <param name="point">The end point.</param>
    /// <param name="isStroked">Whether the segment is stroked.</param>
    public LineSegment(Point point, bool isStroked = true)
    {
        Point = point;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point;
    }
}

/// <summary>
/// Represents a series of connected line segments.
/// </summary>
public class PolyLineSegment : PathSegment
{
    /// <summary>
    /// Gets the collection of points defining the poly line.
    /// </summary>
    public List<Point> Points { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PolyLineSegment"/> class.
    /// </summary>
    public PolyLineSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PolyLineSegment"/> class.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="isStroked">Whether the segment is stroked.</param>
    public PolyLineSegment(IEnumerable<Point> points, bool isStroked = true)
    {
        Points.AddRange(points);
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;
}

/// <summary>
/// Represents an elliptical arc segment.
/// </summary>
public class ArcSegment : PathSegment
{
    /// <summary>
    /// Gets or sets the end point of the arc.
    /// </summary>
    public Point Point { get; set; }

    /// <summary>
    /// Gets or sets the size (radii) of the arc.
    /// </summary>
    public Size Size { get; set; }

    /// <summary>
    /// Gets or sets the rotation angle in degrees.
    /// </summary>
    public double RotationAngle { get; set; }

    /// <summary>
    /// Gets or sets whether the arc spans more than 180 degrees.
    /// </summary>
    public bool IsLargeArc { get; set; }

    /// <summary>
    /// Gets or sets the sweep direction.
    /// </summary>
    public SweepDirection SweepDirection { get; set; } = SweepDirection.Counterclockwise;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcSegment"/> class.
    /// </summary>
    public ArcSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArcSegment"/> class.
    /// </summary>
    public ArcSegment(Point point, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweepDirection, bool isStroked = true)
    {
        Point = point;
        Size = size;
        RotationAngle = rotationAngle;
        IsLargeArc = isLargeArc;
        SweepDirection = sweepDirection;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point;
    }
}

/// <summary>
/// Specifies the sweep direction of an arc.
/// </summary>
public enum SweepDirection
{
    /// <summary>
    /// Arcs are drawn counterclockwise.
    /// </summary>
    Counterclockwise,

    /// <summary>
    /// Arcs are drawn clockwise.
    /// </summary>
    Clockwise
}

/// <summary>
/// Represents a cubic Bezier curve segment.
/// </summary>
public class BezierSegment : PathSegment
{
    /// <summary>
    /// Gets or sets the first control point.
    /// </summary>
    public Point Point1 { get; set; }

    /// <summary>
    /// Gets or sets the second control point.
    /// </summary>
    public Point Point2 { get; set; }

    /// <summary>
    /// Gets or sets the end point.
    /// </summary>
    public Point Point3 { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BezierSegment"/> class.
    /// </summary>
    public BezierSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BezierSegment"/> class.
    /// </summary>
    public BezierSegment(Point point1, Point point2, Point point3, bool isStroked = true)
    {
        Point1 = point1;
        Point2 = point2;
        Point3 = point3;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point1;
        yield return Point2;
        yield return Point3;
    }
}

/// <summary>
/// Represents a series of cubic Bezier curve segments.
/// </summary>
public class PolyBezierSegment : PathSegment
{
    /// <summary>
    /// Gets the collection of points (in groups of 3: control1, control2, end).
    /// </summary>
    public List<Point> Points { get; } = new();

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;
}

/// <summary>
/// Represents a quadratic Bezier curve segment.
/// </summary>
public class QuadraticBezierSegment : PathSegment
{
    /// <summary>
    /// Gets or sets the control point.
    /// </summary>
    public Point Point1 { get; set; }

    /// <summary>
    /// Gets or sets the end point.
    /// </summary>
    public Point Point2 { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuadraticBezierSegment"/> class.
    /// </summary>
    public QuadraticBezierSegment()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QuadraticBezierSegment"/> class.
    /// </summary>
    public QuadraticBezierSegment(Point point1, Point point2, bool isStroked = true)
    {
        Point1 = point1;
        Point2 = point2;
        IsStroked = isStroked;
    }

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints()
    {
        yield return Point1;
        yield return Point2;
    }
}

/// <summary>
/// Represents a series of quadratic Bezier curve segments.
/// </summary>
public class PolyQuadraticBezierSegment : PathSegment
{
    /// <summary>
    /// Gets the collection of points (in groups of 2: control, end).
    /// </summary>
    public List<Point> Points { get; } = new();

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;
}

// ImageSource is defined in ImageSource.cs
