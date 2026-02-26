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
    /// Draws a rounded rectangle with potentially non-uniform corner radii.
    /// </summary>
    /// <param name="brush">The brush to fill with, or null for no fill.</param>
    /// <param name="pen">The pen for the outline, or null for no outline.</param>
    /// <param name="rectangle">The rectangle to draw.</param>
    /// <param name="cornerRadius">The corner radius for each corner.</param>
    public void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, CornerRadius cornerRadius)
    {
        // Fast path: no corner radius
        if (cornerRadius.TopLeft == 0 && cornerRadius.TopRight == 0 &&
            cornerRadius.BottomLeft == 0 && cornerRadius.BottomRight == 0)
        {
            DrawRectangle(brush, pen, rectangle);
            return;
        }

        // Fast path: uniform corner radius
        if (cornerRadius.TopLeft == cornerRadius.TopRight &&
            cornerRadius.TopLeft == cornerRadius.BottomRight &&
            cornerRadius.TopLeft == cornerRadius.BottomLeft)
        {
            DrawRoundedRectangle(brush, pen, rectangle, cornerRadius.TopLeft, cornerRadius.TopLeft);
            return;
        }

        // Non-uniform corner radius: use PathGeometry
        var geometry = CreateRoundedRectGeometry(rectangle, cornerRadius);
        DrawGeometry(brush, pen, geometry);
    }

    /// <summary>
    /// Draws a content area border: fills rect with bottom-only rounded corners,
    /// strokes a U-shape (left + bottom + right, no top) with smooth arcs.
    /// </summary>
    public virtual void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        if (fillBrush != null)
            DrawRoundedRectangle(fillBrush, null, rectangle, new CornerRadius(0, 0, bottomRightRadius, bottomLeftRadius));
        if (strokePen != null)
        {
            double x = rectangle.X, y = rectangle.Y;
            double w = rectangle.Width, h = rectangle.Height;
            double bl = Math.Min(bottomLeftRadius, Math.Min(w, h) / 2);
            double br = Math.Min(bottomRightRadius, Math.Min(w, h) / 2);

            var figure = new PathFigure
            {
                StartPoint = new Point(x, y),
                IsClosed = false,
                IsFilled = false,
            };

            const double k = 0.5522847498; // cubic Bézier approximation of quarter circle

            // Left edge: top → bottom-left arc start
            figure.Segments.Add(new LineSegment(new Point(x, y + h - bl)));

            // Bottom-left arc (cubic Bézier)
            if (bl > 0)
            {
                figure.Segments.Add(new BezierSegment(
                    new Point(x, y + h - bl * (1 - k)),
                    new Point(x + bl * (1 - k), y + h),
                    new Point(x + bl, y + h)));
            }

            // Bottom edge
            figure.Segments.Add(new LineSegment(new Point(x + w - br, y + h)));

            // Bottom-right arc (cubic Bézier)
            if (br > 0)
            {
                figure.Segments.Add(new BezierSegment(
                    new Point(x + w - br * (1 - k), y + h),
                    new Point(x + w, y + h - br * (1 - k)),
                    new Point(x + w, y + h - br)));
            }

            // Right edge: bottom-right → top
            figure.Segments.Add(new LineSegment(new Point(x + w, y)));

            var pathGeometry = new PathGeometry();
            pathGeometry.Figures.Add(figure);
            DrawGeometry(null, strokePen, pathGeometry);
        }
    }

    /// <summary>
    /// Creates a PathGeometry for a rounded rectangle with non-uniform corner radii.
    /// </summary>
    private static PathGeometry CreateRoundedRectGeometry(Rect rect, CornerRadius cornerRadius)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure();

        double x = rect.X;
        double y = rect.Y;
        double w = rect.Width;
        double h = rect.Height;

        // Clamp corner radii to half the minimum dimension
        double maxRadius = Math.Min(w, h) / 2;
        double tl = Math.Min(cornerRadius.TopLeft, maxRadius);
        double tr = Math.Min(cornerRadius.TopRight, maxRadius);
        double br = Math.Min(cornerRadius.BottomRight, maxRadius);
        double bl = Math.Min(cornerRadius.BottomLeft, maxRadius);

        // Start at top-left corner, after the arc
        figure.StartPoint = new Point(x + tl, y);
        figure.IsClosed = true;
        figure.IsFilled = true;

        // Top edge
        figure.Segments.Add(new LineSegment(new Point(x + w - tr, y), true));

        // Top-right corner
        if (tr > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w, y + tr),
                new Size(tr, tr),
                0, false, SweepDirection.Clockwise, true));
        }

        // Right edge
        figure.Segments.Add(new LineSegment(new Point(x + w, y + h - br), true));

        // Bottom-right corner
        if (br > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + w - br, y + h),
                new Size(br, br),
                0, false, SweepDirection.Clockwise, true));
        }

        // Bottom edge
        figure.Segments.Add(new LineSegment(new Point(x + bl, y + h), true));

        // Bottom-left corner
        if (bl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x, y + h - bl),
                new Size(bl, bl),
                0, false, SweepDirection.Clockwise, true));
        }

        // Left edge
        figure.Segments.Add(new LineSegment(new Point(x, y + tl), true));

        // Top-left corner
        if (tl > 0)
        {
            figure.Segments.Add(new ArcSegment(
                new Point(x + tl, y),
                new Size(tl, tl),
                0, false, SweepDirection.Clockwise, true));
        }

        geometry.Figures.Add(figure);
        return geometry;
    }

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
public sealed class DashStyle
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
public sealed class FormattedText
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

    /// <summary>
    /// Creates a Geometry from a path markup mini-language string.
    /// </summary>
    /// <param name="source">The path data string (e.g., "M 0,0 L 100,100 Z").</param>
    /// <returns>A PathGeometry parsed from the string.</returns>
    public static Geometry Parse(string source)
    {
        return PathMarkupParser.Parse(source);
    }

    /// <summary>
    /// Gets an empty Geometry.
    /// </summary>
    public static Geometry Empty { get; } = new GeometryGroup();
}

/// <summary>
/// Represents a rectangle geometry with optional rounded corners.
/// </summary>
public sealed class RectangleGeometry : Geometry
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
public sealed class EllipseGeometry : Geometry
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
public sealed class LineGeometry : Geometry
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
public sealed class GeometryGroup : Geometry
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
public sealed class CombinedGeometry : Geometry
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
public sealed class PathGeometry : Geometry
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

    /// <summary>
    /// Gets the point and tangent vector at the specified fraction of the path length.
    /// </summary>
    /// <param name="progress">A value between 0.0 and 1.0 indicating the fraction of the path.</param>
    /// <param name="point">The point at the specified fraction.</param>
    /// <param name="tangent">The tangent vector at the specified fraction (normalized).</param>
    public void GetPointAtFractionLength(double progress, out Point point, out Point tangent)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);

        if (Figures.Count == 0)
        {
            point = new Point(0, 0);
            tangent = new Point(1, 0);
            return;
        }

        // Calculate total path length
        var totalLength = GetTotalLength();
        if (totalLength <= 0)
        {
            point = Figures[0].StartPoint;
            tangent = new Point(1, 0);
            return;
        }

        var targetLength = progress * totalLength;
        var currentLength = 0.0;

        foreach (var figure in Figures)
        {
            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                var segmentLength = GetSegmentLength(currentPoint, segment);

                if (currentLength + segmentLength >= targetLength)
                {
                    // The target point is within this segment
                    var segmentProgress = segmentLength > 0
                        ? (targetLength - currentLength) / segmentLength
                        : 0;

                    GetPointOnSegment(currentPoint, segment, segmentProgress, out point, out tangent);
                    return;
                }

                currentLength += segmentLength;
                currentPoint = segment.GetEndPoint(currentPoint);
            }

            // Handle closed figures
            if (figure.IsClosed)
            {
                var closeLength = Distance(currentPoint, figure.StartPoint);
                if (currentLength + closeLength >= targetLength)
                {
                    var closeProgress = closeLength > 0
                        ? (targetLength - currentLength) / closeLength
                        : 0;
                    point = Lerp(currentPoint, figure.StartPoint, closeProgress);
                    var diff = new Point(figure.StartPoint.X - currentPoint.X, figure.StartPoint.Y - currentPoint.Y);
                    var len = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
                    tangent = len > 0 ? new Point(diff.X / len, diff.Y / len) : new Point(1, 0);
                    return;
                }
                currentLength += closeLength;
            }
        }

        // Return the end point
        var lastFigure = Figures[^1];
        if (lastFigure.Segments.Count > 0)
        {
            var lastPoint = lastFigure.StartPoint;
            foreach (var seg in lastFigure.Segments)
            {
                lastPoint = seg.GetEndPoint(lastPoint);
            }
            point = lastPoint;
        }
        else
        {
            point = lastFigure.StartPoint;
        }
        tangent = new Point(1, 0);
    }

    /// <summary>
    /// Gets the total length of the path.
    /// </summary>
    public double GetTotalLength()
    {
        var totalLength = 0.0;

        foreach (var figure in Figures)
        {
            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                totalLength += GetSegmentLength(currentPoint, segment);
                currentPoint = segment.GetEndPoint(currentPoint);
            }

            if (figure.IsClosed)
            {
                totalLength += Distance(currentPoint, figure.StartPoint);
            }
        }

        return totalLength;
    }

    private static double GetSegmentLength(Point startPoint, PathSegment segment)
    {
        return segment switch
        {
            LineSegment line => Distance(startPoint, line.Point),
            PolyLineSegment poly => GetPolyLineLength(startPoint, poly.Points),
            BezierSegment bezier => GetBezierLength(startPoint, bezier.Point1, bezier.Point2, bezier.Point3),
            QuadraticBezierSegment quad => GetQuadraticBezierLength(startPoint, quad.Point1, quad.Point2),
            PolyBezierSegment polyBezier => GetPolyBezierLength(startPoint, polyBezier.Points),
            PolyQuadraticBezierSegment polyQuad => GetPolyQuadraticBezierLength(startPoint, polyQuad.Points),
            ArcSegment arc => GetArcLength(startPoint, arc),
            _ => 0
        };
    }

    private static void GetPointOnSegment(Point startPoint, PathSegment segment, double progress, out Point point, out Point tangent)
    {
        switch (segment)
        {
            case LineSegment line:
                point = Lerp(startPoint, line.Point, progress);
                var lineDiff = new Point(line.Point.X - startPoint.X, line.Point.Y - startPoint.Y);
                var lineLen = Math.Sqrt(lineDiff.X * lineDiff.X + lineDiff.Y * lineDiff.Y);
                tangent = lineLen > 0 ? new Point(lineDiff.X / lineLen, lineDiff.Y / lineLen) : new Point(1, 0);
                break;

            case BezierSegment bezier:
                GetCubicBezierPointAndTangent(startPoint, bezier.Point1, bezier.Point2, bezier.Point3, progress, out point, out tangent);
                break;

            case QuadraticBezierSegment quad:
                GetQuadraticBezierPointAndTangent(startPoint, quad.Point1, quad.Point2, progress, out point, out tangent);
                break;

            case ArcSegment arc:
                GetArcPointAndTangent(startPoint, arc, progress, out point, out tangent);
                break;

            default:
                point = startPoint;
                tangent = new Point(1, 0);
                break;
        }
    }

    private static Point Lerp(Point a, Point b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static double Distance(Point a, Point b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static double GetPolyLineLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        foreach (var pt in points)
        {
            length += Distance(current, pt);
            current = pt;
        }
        return length;
    }

    private static double GetBezierLength(Point p0, Point p1, Point p2, Point p3, int samples = 20)
    {
        var length = 0.0;
        var prev = p0;
        for (var i = 1; i <= samples; i++)
        {
            var t = i / (double)samples;
            var pt = GetCubicBezierPoint(p0, p1, p2, p3, t);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static double GetQuadraticBezierLength(Point p0, Point p1, Point p2, int samples = 20)
    {
        var length = 0.0;
        var prev = p0;
        for (var i = 1; i <= samples; i++)
        {
            var t = i / (double)samples;
            var pt = GetQuadraticBezierPoint(p0, p1, p2, t);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static double GetPolyBezierLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        for (var i = 0; i + 2 < points.Count; i += 3)
        {
            length += GetBezierLength(current, points[i], points[i + 1], points[i + 2]);
            current = points[i + 2];
        }
        return length;
    }

    private static double GetPolyQuadraticBezierLength(Point start, IList<Point> points)
    {
        var length = 0.0;
        var current = start;
        for (var i = 0; i + 1 < points.Count; i += 2)
        {
            length += GetQuadraticBezierLength(current, points[i], points[i + 1]);
            current = points[i + 1];
        }
        return length;
    }

    private static double GetArcLength(Point start, ArcSegment arc)
    {
        // Approximate arc length using line segments
        var length = 0.0;
        var prev = start;
        const int samples = 20;
        for (var i = 1; i <= samples; i++)
        {
            GetArcPointAndTangent(start, arc, i / (double)samples, out var pt, out _);
            length += Distance(prev, pt);
            prev = pt;
        }
        return length;
    }

    private static Point GetCubicBezierPoint(Point p0, Point p1, Point p2, Point p3, double t)
    {
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;

        return new Point(
            uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y
        );
    }

    private static Point GetQuadraticBezierPoint(Point p0, Point p1, Point p2, double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
            u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y
        );
    }

    private static void GetCubicBezierPointAndTangent(Point p0, Point p1, Point p2, Point p3, double t, out Point point, out Point tangent)
    {
        point = GetCubicBezierPoint(p0, p1, p2, p3, t);

        // Derivative of cubic Bezier
        var u = 1 - t;
        var dx = 3 * u * u * (p1.X - p0.X) + 6 * u * t * (p2.X - p1.X) + 3 * t * t * (p3.X - p2.X);
        var dy = 3 * u * u * (p1.Y - p0.Y) + 6 * u * t * (p2.Y - p1.Y) + 3 * t * t * (p3.Y - p2.Y);

        var len = Math.Sqrt(dx * dx + dy * dy);
        tangent = len > 0 ? new Point(dx / len, dy / len) : new Point(1, 0);
    }

    private static void GetQuadraticBezierPointAndTangent(Point p0, Point p1, Point p2, double t, out Point point, out Point tangent)
    {
        point = GetQuadraticBezierPoint(p0, p1, p2, t);

        // Derivative of quadratic Bezier
        var dx = 2 * (1 - t) * (p1.X - p0.X) + 2 * t * (p2.X - p1.X);
        var dy = 2 * (1 - t) * (p1.Y - p0.Y) + 2 * t * (p2.Y - p1.Y);

        var len = Math.Sqrt(dx * dx + dy * dy);
        tangent = len > 0 ? new Point(dx / len, dy / len) : new Point(1, 0);
    }

    private static void GetArcPointAndTangent(Point start, ArcSegment arc, double progress, out Point point, out Point tangent)
    {
        // Simplified arc point calculation
        // For a complete implementation, this would need proper elliptical arc math
        point = Lerp(start, arc.Point, progress);
        var diff = new Point(arc.Point.X - start.X, arc.Point.Y - start.Y);
        var len = Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y);
        tangent = len > 0 ? new Point(diff.X / len, diff.Y / len) : new Point(1, 0);
    }
}

/// <summary>
/// Represents a subsection of a path geometry.
/// </summary>
public sealed class PathFigure
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

    /// <summary>
    /// Gets the end point of this segment given a start point.
    /// </summary>
    /// <param name="startPoint">The start point of the segment.</param>
    /// <returns>The end point of the segment.</returns>
    public abstract Point GetEndPoint(Point startPoint);
}

/// <summary>
/// Represents a straight line segment.
/// </summary>
public sealed class LineSegment : PathSegment
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

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point;
}

/// <summary>
/// Represents a series of connected line segments.
/// </summary>
public sealed class PolyLineSegment : PathSegment
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

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;
}

/// <summary>
/// Represents an elliptical arc segment.
/// </summary>
public sealed class ArcSegment : PathSegment
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

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point;
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
public sealed class BezierSegment : PathSegment
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

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point3;
}

/// <summary>
/// Represents a series of cubic Bezier curve segments.
/// </summary>
public sealed class PolyBezierSegment : PathSegment
{
    /// <summary>
    /// Gets the collection of points (in groups of 3: control1, control2, end).
    /// </summary>
    public List<Point> Points { get; } = new();

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;
}

/// <summary>
/// Represents a quadratic Bezier curve segment.
/// </summary>
public sealed class QuadraticBezierSegment : PathSegment
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

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Point2;
}

/// <summary>
/// Represents a series of quadratic Bezier curve segments.
/// </summary>
public sealed class PolyQuadraticBezierSegment : PathSegment
{
    /// <summary>
    /// Gets the collection of points (in groups of 2: control, end).
    /// </summary>
    public List<Point> Points { get; } = new();

    /// <inheritdoc />
    public override IEnumerable<Point> GetPoints() => Points;

    /// <inheritdoc />
    public override Point GetEndPoint(Point startPoint) => Points.Count > 0 ? Points[^1] : startPoint;
}

// ImageSource is defined in ImageSource.cs
