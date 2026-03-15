using System.Linq;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// A DrawingContext implementation that renders to a RenderTarget.
/// </summary>
public sealed class RenderTargetDrawingContext : DrawingContext, IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext, IEffectDrawingContext
{
    private const int MaxBrushCacheSize = 256;
    private const int MaxTextFormatCacheSize = 64;
    private const int MaxBitmapCacheSize = 64;
    private const long MaxBitmapCacheBytes = 128L * 1024 * 1024;
    private const long MediumMemoryPressureWorkingSetBytes = 400L * 1024 * 1024;
    private const long HighMemoryPressureWorkingSetBytes = 550L * 1024 * 1024;
    private const long MediumPressureBitmapCacheBytes = 64L * 1024 * 1024;
    private const long HighPressureBitmapCacheBytes = 32L * 1024 * 1024;

    private readonly RenderTarget _renderTarget;
    private readonly RenderContext _context;
    private readonly Dictionary<Brush, NativeBrush> _brushCache = new();
    private readonly Dictionary<string, NativeTextFormat> _textFormatCache = new();
    private readonly Dictionary<ImageSource, BitmapCacheEntry> _bitmapCache = new();
    private readonly Stack<DrawingState> _stateStack = new();
    private readonly Stack<Rect?> _clipBoundsStack = new();
    private long _bitmapCacheBytes;
    private long _bitmapCacheSequence;
    private bool _closed;

    private sealed class BitmapCacheEntry
    {
        public BitmapCacheEntry(NativeBitmap bitmap, long estimatedBytes, long lastAccessSequence)
        {
            Bitmap = bitmap;
            EstimatedBytes = estimatedBytes;
            LastAccessSequence = lastAccessSequence;
        }

        public NativeBitmap Bitmap { get; }
        public long EstimatedBytes { get; }
        public long LastAccessSequence { get; set; }
    }

    /// <summary>
    /// Gets the underlying render target.
    /// </summary>
    public RenderTarget RenderTarget => _renderTarget;

    /// <summary>
    /// Gets or sets the current transform offset for child rendering.
    /// </summary>
    public Point Offset { get; set; }

    /// <inheritdoc />
    public Rect? CurrentClipBounds => _clipBoundsStack.Count > 0 ? _clipBoundsStack.Peek() : null;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderTargetDrawingContext"/> class.
    /// </summary>
    /// <param name="renderTarget">The render target to draw on.</param>
    /// <param name="context">The render context for creating resources.</param>
    public RenderTargetDrawingContext(RenderTarget renderTarget, RenderContext context)
    {
        _renderTarget = renderTarget ?? throw new ArgumentNullException(nameof(renderTarget));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private static (float RadiusX, float RadiusY) NormalizeRoundedRectRadii(float width, float height, double radiusX, double radiusY)
    {
        static double Sanitize(double radius) =>
            double.IsFinite(radius) && radius > 0 ? radius : 0;

        var halfWidth = Math.Max(0f, width) / 2f;
        var halfHeight = Math.Max(0f, height) / 2f;
        var normalizedRadiusX = (float)Math.Min(Sanitize(radiusX), halfWidth);
        var normalizedRadiusY = (float)Math.Min(Sanitize(radiusY), halfHeight);
        return (normalizedRadiusX, normalizedRadiusY);
    }

    private static float SnapCoordinate(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0f;
        }

        var rounded = Math.Round(value);
        if (Math.Abs(value - rounded) < 0.0001)
        {
            return (float)rounded;
        }

        var halfPixel = Math.Floor(value) + 0.5;
        if (Math.Abs(value - halfPixel) < 0.0001)
        {
            return (float)halfPixel;
        }

        return (float)rounded;
    }

    /// <inheritdoc />
    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        if (_closed || pen?.Brush == null) return;

        var brush = GetNativeBrush(pen.Brush);
        if (brush == null) return;

        // Round to pixel boundaries to prevent sub-pixel jittering
        _renderTarget.DrawLine(
            (float)Math.Round(point0.X + Offset.X),
            (float)Math.Round(point0.Y + Offset.Y),
            (float)Math.Round(point1.X + Offset.X),
            (float)Math.Round(point1.Y + Offset.Y),
            brush,
            (float)pen.Thickness);
    }

    /// <inheritdoc />
    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
    {
        if (_closed) return;

        // Preserve intentional half-pixel alignment for odd-width strokes.
        var x = SnapCoordinate(rectangle.X + Offset.X);
        var y = SnapCoordinate(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush, x, y, width, height);
            if (nativeBrush != null)
            {
                _renderTarget.FillRectangle(x, y, width, height, nativeBrush);
            }
        }

        // Stroke – snap all four edges so the stroke is uniform on every side.
        // The fill keeps the original width/height to avoid shrinking backgrounds.
        if (pen?.Brush != null)
        {
            var strokeRight = SnapCoordinate(rectangle.X + rectangle.Width + Offset.X);
            var strokeBottom = SnapCoordinate(rectangle.Y + rectangle.Height + Offset.Y);
            var strokeW = strokeRight - x;
            var strokeH = strokeBottom - y;
            var strokeBrush = GetNativeBrush(pen.Brush, x, y, strokeW, strokeH);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRectangle(x, y, strokeW, strokeH, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        if (_closed) return;

        // Preserve intentional half-pixel alignment for odd-width strokes.
        var x = SnapCoordinate(rectangle.X + Offset.X);
        var y = SnapCoordinate(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var (rx, ry) = NormalizeRoundedRectRadii(width, height, radiusX, radiusY);

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush, x, y, width, height);
            if (nativeBrush != null)
            {
                _renderTarget.FillRoundedRectangle(x, y, width, height, rx, ry, nativeBrush);
            }
        }

        // Stroke – snap all four edges so the stroke is uniform on every side.
        // The fill keeps the original width/height to avoid shrinking backgrounds.
        if (pen?.Brush != null)
        {
            var strokeRight = SnapCoordinate(rectangle.X + rectangle.Width + Offset.X);
            var strokeBottom = SnapCoordinate(rectangle.Y + rectangle.Height + Offset.Y);
            var strokeW = strokeRight - x;
            var strokeH = strokeBottom - y;
            var (strokeRx, strokeRy) = NormalizeRoundedRectRadii(strokeW, strokeH, radiusX, radiusY);
            var strokeBrush = GetNativeBrush(pen.Brush, x, y, strokeW, strokeH);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRoundedRectangle(x, y, strokeW, strokeH, strokeRx, strokeRy, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawContentBorder(Brush? fillBrush, Pen? strokePen, Rect rectangle,
        double bottomLeftRadius, double bottomRightRadius)
    {
        if (_closed) return;

        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var right = (float)Math.Round(rectangle.X + rectangle.Width + Offset.X);
        var bottom = (float)Math.Round(rectangle.Y + rectangle.Height + Offset.Y);
        var w = right - x;
        var h = bottom - y;
        var bl = (float)bottomLeftRadius;
        var br = (float)bottomRightRadius;

        var nativeFill = fillBrush != null ? GetNativeBrush(fillBrush, x, y, w, h) : null;
        var nativeStroke = strokePen?.Brush != null ? GetNativeBrush(strokePen.Brush, x, y, w, h) : null;
        var strokeWidth = strokePen != null ? (float)strokePen.Thickness : 0f;

        // Always use managed BezierSegment path (native D2D arc direction is inverted)
        base.DrawContentBorder(fillBrush, strokePen, rectangle, bottomLeftRadius, bottomRightRadius);
    }

    /// <inheritdoc />
    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        if (_closed) return;

        // Round to pixel boundaries to prevent sub-pixel jittering
        var cx = (float)Math.Round(center.X + Offset.X);
        var cy = (float)Math.Round(center.Y + Offset.Y);
        var rx = (float)radiusX;
        var ry = (float)radiusY;

        // Bounding box for gradient brush coordinate conversion
        float bx = cx - rx, by = cy - ry, bw = rx * 2, bh = ry * 2;

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush, bx, by, bw, bh);
            if (nativeBrush != null)
            {
                _renderTarget.FillEllipse(cx, cy, rx, ry, nativeBrush);
            }
        }

        // Stroke
        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush, bx, by, bw, bh);
            if (strokeBrush != null)
            {
                _renderTarget.DrawEllipse(cx, cy, rx, ry, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawText(FormattedText formattedText, Point origin)
    {
        if (_closed || formattedText == null || string.IsNullOrEmpty(formattedText.Text)) return;

        var brush = formattedText.Foreground != null ? GetNativeBrush(formattedText.Foreground) : null;
        if (brush == null) return;

        var format = GetTextFormat(
            formattedText.FontFamily,
            formattedText.FontSize,
            formattedText.FontWeight,
            formattedText.FontStyle);
        if (format == null) return;

        // Round text coordinates to pixel boundaries to prevent sub-pixel jittering
        // DirectWrite renders text using sub-pixel positioning which can cause visual instability
        var x = (float)Math.Round(origin.X + Offset.X);
        var y = (float)Math.Round(origin.Y + Offset.Y);
        var width = (float)formattedText.MaxTextWidth;
        var height = (float)formattedText.MaxTextHeight;

        if (width <= 0 || float.IsInfinity(width) || float.IsNaN(width)) width = 10000;
        if (height <= 0 || float.IsInfinity(height) || float.IsNaN(height)) height = 10000;

        _renderTarget.DrawText(formattedText.Text, format, x, y, width, height, brush);
    }

    /// <inheritdoc />
    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        if (_closed || geometry == null) return;

        // Handle geometry types
        if (geometry is RectangleGeometry rectGeom)
        {
            if (rectGeom.RadiusX > 0 || rectGeom.RadiusY > 0)
            {
                DrawRoundedRectangle(brush, pen, rectGeom.Rect, rectGeom.RadiusX, rectGeom.RadiusY);
            }
            else
            {
                DrawRectangle(brush, pen, rectGeom.Rect);
            }
        }
        else if (geometry is EllipseGeometry ellipseGeom)
        {
            DrawEllipse(brush, pen, ellipseGeom.Center, ellipseGeom.RadiusX, ellipseGeom.RadiusY);
        }
        else if (geometry is LineGeometry lineGeom)
        {
            if (pen != null)
            {
                DrawLine(pen, lineGeom.StartPoint, lineGeom.EndPoint);
            }
        }
        else if (geometry is GeometryGroup group)
        {
            foreach (var child in group.Children)
            {
                DrawGeometry(brush, pen, child);
            }
        }
        else if (geometry is CombinedGeometry combined)
        {
            // For combined geometry, we can only render the union as approximation
            // Full CSG operations would require native D2D geometry operations
            if (combined.Geometry1 != null) DrawGeometry(brush, pen, combined.Geometry1);
            if (combined.Geometry2 != null) DrawGeometry(brush, pen, combined.Geometry2);
        }
        else if (geometry is StreamGeometry streamGeom)
        {
            var inner = streamGeom.GetPathGeometry();
            if (inner != null)
                DrawPathGeometry(brush, pen, inner);
        }
        else if (geometry is PathGeometry pathGeom)
        {
            DrawPathGeometry(brush, pen, pathGeom);
        }
    }

    private void DrawPathGeometry(Brush? brush, Pen? pen, PathGeometry pathGeom)
    {
        foreach (var figure in pathGeom.Figures)
        {
            // Check if figure has any bezier/quadratic segments → use native path API
            bool hasCurves = false;
            foreach (var segment in figure.Segments)
            {
                if (segment is BezierSegment or QuadraticBezierSegment)
                {
                    hasCurves = true;
                    break;
                }
            }

            if (hasCurves)
            {
                DrawPathFigureNative(brush, pen, figure, pathGeom.FillRule);
            }
            else
            {
                DrawPathFigurePolygon(brush, pen, figure, pathGeom.FillRule);
            }
        }
    }

    /// <summary>
    /// Renders a path figure using the native FillPath/StrokePath API with real bezier curves.
    /// </summary>
    private void DrawPathFigureNative(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule)
    {
        // Build command buffer: tag 0 = LineTo [0,x,y], tag 1 = BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey]
        var cmds = new List<float>();
        var ox = Offset.X;
        var oy = Offset.Y;
        var currentPoint = figure.StartPoint;

        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment lineSeg)
            {
                cmds.Add(0f);
                cmds.Add((float)(lineSeg.Point.X + ox));
                cmds.Add((float)(lineSeg.Point.Y + oy));
                currentPoint = lineSeg.Point;
            }
            else if (segment is PolyLineSegment polyLine)
            {
                foreach (var pt in polyLine.Points)
                {
                    cmds.Add(0f);
                    cmds.Add((float)(pt.X + ox));
                    cmds.Add((float)(pt.Y + oy));
                    currentPoint = pt;
                }
            }
            else if (segment is BezierSegment bezier)
            {
                cmds.Add(1f);
                cmds.Add((float)(bezier.Point1.X + ox));
                cmds.Add((float)(bezier.Point1.Y + oy));
                cmds.Add((float)(bezier.Point2.X + ox));
                cmds.Add((float)(bezier.Point2.Y + oy));
                cmds.Add((float)(bezier.Point3.X + ox));
                cmds.Add((float)(bezier.Point3.Y + oy));
                currentPoint = bezier.Point3;
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                // Promote quadratic to cubic: cp1 = start + 2/3*(ctrl-start), cp2 = end + 2/3*(ctrl-end)
                // We need the current point for this; track it
                // For simplicity, approximate as cubic with control points
                cmds.Add(1f);
                cmds.Add((float)(quad.Point1.X + ox));
                cmds.Add((float)(quad.Point1.Y + oy));
                cmds.Add((float)(quad.Point1.X + ox));
                cmds.Add((float)(quad.Point1.Y + oy));
                cmds.Add((float)(quad.Point2.X + ox));
                cmds.Add((float)(quad.Point2.Y + oy));
                currentPoint = quad.Point2;
            }
            else if (segment is ArcSegment arc)
            {
                // Fall back to line approximation for arcs
                var arcPoints = GetArcPoints(currentPoint, arc);
                foreach (var pt in arcPoints)
                {
                    cmds.Add(0f);
                    cmds.Add((float)(pt.X + ox));
                    cmds.Add((float)(pt.Y + oy));
                }
                currentPoint = arc.Point;
            }
        }

        if (cmds.Count == 0) return;

        float startX = (float)(figure.StartPoint.X + ox);
        float startY = (float)(figure.StartPoint.Y + oy);
        var cmdArray = cmds.ToArray();

        if (brush != null && figure.IsFilled)
        {
            var nativeBrush = GetNativeBrush(brush);
            if (nativeBrush != null)
            {
                int rule = fillRule == FillRule.Nonzero ? 1 : 0;
                _renderTarget.FillPath(startX, startY, cmdArray, nativeBrush, rule);
            }
        }

        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush);
            if (strokeBrush != null)
            {
                _renderTarget.StrokePath(startX, startY, cmdArray, strokeBrush, (float)pen.Thickness, figure.IsClosed);
            }
        }
    }

    /// <summary>
    /// Renders a path figure as a polygon (line segments only, for figures without curves).
    /// </summary>
    private void DrawPathFigurePolygon(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule)
    {
        var points = new List<Point> { figure.StartPoint };
        var currentPoint = figure.StartPoint;
        bool hasArc = false;

        foreach (var segment in figure.Segments)
        {
            if (segment is LineSegment lineSeg)
            {
                points.Add(lineSeg.Point);
                currentPoint = lineSeg.Point;
            }
            else if (segment is PolyLineSegment polyLine)
            {
                foreach (var point in polyLine.Points)
                {
                    points.Add(point);
                    currentPoint = point;
                }
            }
            else if (segment is ArcSegment arc)
            {
                hasArc = true;
                var arcPoints = GetArcPoints(currentPoint, arc);
                points.AddRange(arcPoints);
                currentPoint = arc.Point;
            }
        }

        var pointArray = new float[points.Count * 2];
        for (int i = 0; i < points.Count; i++)
        {
            var px = points[i].X + Offset.X;
            var py = points[i].Y + Offset.Y;

            // Integer snapping on arc-generated points turns small radii into beveled corners.
            // Keep subpixel coordinates for arc paths; keep legacy snapping for straight polygons.
            if (!hasArc)
            {
                px = Math.Round(px);
                py = Math.Round(py);
            }

            pointArray[i * 2] = (float)px;
            pointArray[i * 2 + 1] = (float)py;
        }

        if (brush != null && figure.IsFilled && points.Count >= 3)
        {
            int rule = fillRule == FillRule.Nonzero ? 1 : 0;
            var nativeBrush = GetNativeBrush(brush);
            if (nativeBrush != null)
            {
                _renderTarget.FillPolygon(pointArray, nativeBrush, rule);
            }
        }

        if (pen?.Brush != null && points.Count >= 2)
        {
            var strokeBrush = GetNativeBrush(pen.Brush);
            if (strokeBrush != null)
            {
                _renderTarget.DrawPolygon(pointArray, strokeBrush, (float)pen.Thickness, figure.IsClosed);
            }
        }
    }

    private List<Point> GetBezierPoints(Point p0, Point p1, Point p2, Point p3)
    {
        const int segments = 16;
        var points = new List<Point>();
        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1 - t;
            double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
            points.Add(new Point(x, y));
        }
        return points;
    }

    private List<Point> GetQuadBezierPoints(Point p0, Point p1, Point p2)
    {
        const int segments = 12;
        var points = new List<Point>();
        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1 - t;
            double x = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
            double y = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
            points.Add(new Point(x, y));
        }
        return points;
    }

    private List<Point> GetArcPoints(Point start, ArcSegment arc)
    {
        var points = new List<Point>();
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        // Handle degenerate cases
        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            points.Add(end);
            return points;
        }

        // Convert endpoint parameterization to center parameterization
        // Based on SVG arc implementation algorithm
        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;

        var rotationAngle = arc.RotationAngle * Math.PI / 180;
        var cosAngle = Math.Cos(rotationAngle);
        var sinAngle = Math.Sin(rotationAngle);

        var x1p = cosAngle * dx + sinAngle * dy;
        var y1p = -sinAngle * dx + cosAngle * dy;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;

        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sqrtLambda = Math.Sqrt(lambda);
            rx *= sqrtLambda;
            ry *= sqrtLambda;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Calculate center point
        // Per SVG spec: sign is positive when fA != fS
        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1 : -1;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);

        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;

        var cx = cosAngle * cxp - sinAngle * cyp + (start.X + end.X) / 2;
        var cy = sinAngle * cxp + cosAngle * cyp + (start.Y + end.Y) / 2;

        // Calculate start and end angles
        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);

        var deltaAngle = endAngle - startAngle;

        // Adjust delta angle based on sweep direction
        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0)
            deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0)
            deltaAngle -= 2 * Math.PI;

        // Generate arc points
        const int segments = 8;
        for (int i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var angle = startAngle + deltaAngle * t;

            var px = rx * Math.Cos(angle);
            var py = ry * Math.Sin(angle);

            var x = cosAngle * px - sinAngle * py + cx;
            var y = sinAngle * px + cosAngle * py + cy;

            points.Add(new Point(x, y));
        }

        return points;
    }

    /// <inheritdoc />
    public override void DrawImage(ImageSource imageSource, Rect rectangle)
    {
        if (_closed || imageSource == null) return;

        var bitmap = GetNativeBitmap(imageSource);
        if (bitmap == null) return;

        // Round to pixel boundaries to prevent sub-pixel jittering
        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        _renderTarget.DrawBitmap(bitmap, x, y, width, height, 1.0f);
    }

    /// <inheritdoc />
    public override void DrawBackdropEffect(
        Rect rectangle,
        IBackdropEffect effect,
        CornerRadius cornerRadius)
    {
        if (_closed) return;

        // Check if there's any effect to apply
        if (effect == null || !effect.HasEffect) return;

        // Round to pixel boundaries to prevent sub-pixel jittering
        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var normalizedCornerRadius = cornerRadius.Normalize(rectangle.Width, rectangle.Height);

        // Convert IBackdropEffect to native parameters
        // Build backdrop filter string based on effect properties
        var backdropFilter = BuildBackdropFilterString(effect);

        // Build material string based on blur type
        var material = effect.BlurType switch
        {
            BackdropBlurType.Frosted => "acrylic",
            _ when effect.TintOpacity > 0 => "acrylic",
            _ => string.Empty
        };

        // Convert tint color from ARGB uint to hex string
        var tintColorArgb = effect.TintColorArgb;
        var materialTint = tintColorArgb != 0
            ? $"#{(tintColorArgb >> 16) & 0xFF:X2}{(tintColorArgb >> 8) & 0xFF:X2}{tintColorArgb & 0xFF:X2}"
            : string.Empty;

        _renderTarget.DrawBackdropFilter(
            x, y, width, height,
            backdropFilter,
            material,
            materialTint,
            effect.TintOpacity,
            effect.BlurRadius,
            (float)normalizedCornerRadius.TopLeft,
            (float)normalizedCornerRadius.TopRight,
            (float)normalizedCornerRadius.BottomRight,
            (float)normalizedCornerRadius.BottomLeft);
    }

    /// <summary>
    /// Begins capturing content into an offscreen bitmap for transition shader effects.
    /// Converts local bounds to screen coordinates using the current Offset.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    /// <param name="localBounds">The transition area in local coordinates.</param>
    public void BeginTransitionCapture(int slot, Rect localBounds)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.BeginTransitionCapture(slot, x, y,
            (float)localBounds.Width, (float)localBounds.Height);
    }

    /// <summary>
    /// Ends capturing content for a transition slot and restores the main render target.
    /// </summary>
    /// <param name="slot">0 = old content, 1 = new content.</param>
    public void EndTransitionCapture(int slot)
    {
        if (_closed) return;
        _renderTarget.EndTransitionCapture(slot);
    }

    /// <summary>
    /// Draws the transition shader effect blending old and new content bitmaps.
    /// </summary>
    /// <param name="localBounds">The transition area in local coordinates.</param>
    /// <param name="progress">Transition progress (0.0 - 1.0).</param>
    /// <param name="mode">Shader mode index (0-9).</param>
    public void DrawTransitionShader(Rect localBounds, float progress, int mode)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.DrawTransitionShader(x, y,
            (float)localBounds.Width, (float)localBounds.Height, progress, mode);
    }

    /// <summary>
    /// Draws a previously captured transition bitmap.
    /// </summary>
    public void DrawCapturedTransition(int slot, Rect localBounds, float opacity)
    {
        if (_closed) return;
        var x = (float)(localBounds.X + Offset.X);
        var y = (float)(localBounds.Y + Offset.Y);
        _renderTarget.DrawCapturedTransition(slot, x, y,
            (float)localBounds.Width, (float)localBounds.Height, opacity);
    }

    /// <summary>
    /// Draws a liquid glass effect at the specified rectangle.
    /// </summary>
    public void DrawLiquidGlass(
        Rect rectangle,
        float cornerRadius,
        float blurRadius = 8f,
        float refractionAmount = 60f,
        float chromaticAberration = 0f,
        float tintR = 0.08f, float tintG = 0.08f, float tintB = 0.08f,
        float tintOpacity = 0.3f,
        float lightX = -1f, float lightY = -1f,
        float highlightBoost = 0f,
        int shapeType = 0,
        float shapeExponent = 4f,
        int neighborCount = 0,
        float fusionRadius = 30f,
        ReadOnlySpan<float> neighborData = default)
    {
        if (_closed) return;

        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        _renderTarget.DrawLiquidGlass(
            x, y, width, height,
            cornerRadius, blurRadius,
            refractionAmount, chromaticAberration,
            tintR, tintG, tintB, tintOpacity,
            lightX, lightY, highlightBoost,
            shapeType, shapeExponent,
            neighborCount, fusionRadius, neighborData);
    }

    /// <summary>
    /// Builds a CSS-style backdrop filter string from the effect properties.
    /// </summary>
    private static string BuildBackdropFilterString(IBackdropEffect effect)
    {
        var parts = new List<string>();

        if (effect.BlurRadius > 0)
        {
            parts.Add($"blur({effect.BlurRadius}px)");
        }

        if (Math.Abs(effect.Brightness - 1.0f) > 0.001f)
        {
            parts.Add($"brightness({effect.Brightness})");
        }

        if (Math.Abs(effect.Contrast - 1.0f) > 0.001f)
        {
            parts.Add($"contrast({effect.Contrast})");
        }

        if (Math.Abs(effect.Saturation - 1.0f) > 0.001f)
        {
            parts.Add($"saturate({effect.Saturation})");
        }

        if (effect.Grayscale > 0)
        {
            parts.Add($"grayscale({effect.Grayscale})");
        }

        if (effect.Sepia > 0)
        {
            parts.Add($"sepia({effect.Sepia})");
        }

        if (effect.Invert > 0)
        {
            parts.Add($"invert({effect.Invert})");
        }

        if (Math.Abs(effect.HueRotation) > 0.001f)
        {
            parts.Add($"hue-rotate({effect.HueRotation}rad)");
        }

        if (Math.Abs(effect.Opacity - 1.0f) > 0.001f)
        {
            parts.Add($"opacity({effect.Opacity})");
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc />
    public override void PushTransform(Transform transform)
    {
        if (_closed) return;

        if (transform is TranslateTransform translate)
        {
            // Translation: handled via managed Offset (existing fast path)
            _stateStack.Push(new DrawingState(DrawingStateType.Transform, Offset));
            Offset = new Point(Offset.X + translate.X, Offset.Y + translate.Y);
        }
        else
        {
            // Non-translate transform: push native D2D1 matrix.
            // Drawing operations add managed Offset to coordinates before native,
            // so we compose: T(-offset) * transform * T(+offset) to apply
            // the transform in local space while coordinates are in screen space.
            var m = transform.Value;
            var ox = Offset.X;
            var oy = Offset.Y;

            // step 1: T(-offset) * transform
            // M' = T(-ox,-oy) * M
            var m11 = m.M11;
            var m12 = m.M12;
            var m21 = m.M21;
            var m22 = m.M22;
            var dx = -ox * m11 + -oy * m21 + m.OffsetX;
            var dy = -ox * m12 + -oy * m22 + m.OffsetY;

            // step 2: result * T(+offset)
            var finalDx = dx + ox;
            var finalDy = dy + oy;

            _renderTarget.PushTransform(new float[]
            {
                (float)m11, (float)m12,
                (float)m21, (float)m22,
                (float)finalDx, (float)finalDy
            });
            _stateStack.Push(new DrawingState(DrawingStateType.NativeTransform, Point.Zero));
        }
    }

    /// <inheritdoc />
    public override void PushClip(Geometry clipGeometry)
    {
        if (_closed || clipGeometry == null) return;

        var bounds = clipGeometry.Bounds;
        // Snap clip edges to pixel grid by expanding outward (Floor start, Ceiling end).
        // Drawing operations (DrawRoundedRectangle etc.) pixel-snap their origin via Math.Round,
        // so the drawn stroke can land up to 0.5px outside the mathematical clip region.
        // Expanding to full-pixel boundaries ensures the clip always contains the entire
        // pixel-snapped content, preventing asymmetric border thickness artifacts.
        var exactLeft = bounds.X + Offset.X;
        var exactTop = bounds.Y + Offset.Y;
        var exactRight = exactLeft + bounds.Width;
        var exactBottom = exactTop + bounds.Height;

        var x = (float)Math.Floor(exactLeft);
        var y = (float)Math.Floor(exactTop);
        var w = (float)Math.Ceiling(exactRight) - x;
        var h = (float)Math.Ceiling(exactBottom) - y;

        var clipRect = new Rect(exactLeft, exactTop, Math.Max(0, exactRight - exactLeft), Math.Max(0, exactBottom - exactTop));
        Rect? effectiveClip = clipRect;
        if (_clipBoundsStack.Count > 0)
        {
            var parentClip = _clipBoundsStack.Peek();
            effectiveClip = parentClip.HasValue ? parentClip.Value.Intersect(clipRect) : clipRect;
        }
        _clipBoundsStack.Push(effectiveClip);

        if (clipGeometry is RectangleGeometry rectGeom && (rectGeom.RadiusX > 0 || rectGeom.RadiusY > 0))
        {
            var (rx, ry) = NormalizeRoundedRectRadii(w, h, rectGeom.RadiusX, rectGeom.RadiusY);
            _renderTarget.PushRoundedRectClip(x, y, w, h, rx, ry);
        }
        else
        {
            _renderTarget.PushClip(x, y, w, h);
        }

        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <inheritdoc />
    public override void PushOpacity(double opacity)
    {
        if (_closed) return;

        _renderTarget.PushOpacity((float)opacity);
        _stateStack.Push(new DrawingState(DrawingStateType.Opacity, Point.Zero));
    }

    /// <summary>
    /// Punches a transparent rectangular hole using the current offset and clip stack.
    /// </summary>
    public void PunchTransparentRect(Rect rectangle)
    {
        if (_closed) return;

        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)Math.Round(rectangle.Width);
        var height = (float)Math.Round(rectangle.Height);

        if (width <= 0 || height <= 0)
            return;

        _renderTarget.PunchTransparentRect(x, y, width, height);
    }

    /// <summary>
    /// Pops the most recent opacity from the opacity stack.
    /// </summary>
    public void PopOpacity()
    {
        if (_closed) return;

        // Pop from our state stack if the top is opacity
        if (_stateStack.Count > 0 && _stateStack.Peek().Type == DrawingStateType.Opacity)
        {
            _stateStack.Pop();
        }
        _renderTarget.PopOpacity();
    }

    /// <inheritdoc />
    public override void Pop()
    {
        if (_closed || _stateStack.Count == 0) return;

        var state = _stateStack.Pop();
        switch (state.Type)
        {
            case DrawingStateType.Transform:
                Offset = state.SavedOffset;
                break;
            case DrawingStateType.NativeTransform:
                _renderTarget.PopTransform();
                break;
            case DrawingStateType.Clip:
                if (_clipBoundsStack.Count > 0)
                {
                    _clipBoundsStack.Pop();
                }
                _renderTarget.PopClip();
                break;
            case DrawingStateType.Opacity:
                _renderTarget.PopOpacity();
                break;
            case DrawingStateType.ViewportOnly:
                if (_clipBoundsStack.Count > 0)
                {
                    _clipBoundsStack.Pop();
                }
                // No native PopClip — ViewportOnly only affects managed culling
                break;
        }
    }

    /// <summary>
    /// Pushes a dirty region clip that restricts D2D rendering AND managed viewport
    /// culling to the specified rectangle. Uses the native PushClip for GPU-side
    /// clipping and updates <see cref="CurrentClipBounds"/> for
    /// <see cref="Visual.ShouldRenderChild"/> viewport culling.
    /// </summary>
    internal void PushDirtyRegionClip(Rect dirtyRegion)
    {
        if (_closed) return;

        var x = (float)Math.Floor(dirtyRegion.X);
        var y = (float)Math.Floor(dirtyRegion.Y);
        var w = (float)Math.Ceiling(dirtyRegion.X + dirtyRegion.Width) - x;
        var h = (float)Math.Ceiling(dirtyRegion.Y + dirtyRegion.Height) - y;

        Rect? effectiveClip = dirtyRegion;
        if (_clipBoundsStack.Count > 0)
        {
            var parentClip = _clipBoundsStack.Peek();
            effectiveClip = parentClip.HasValue ? parentClip.Value.Intersect(dirtyRegion) : dirtyRegion;
        }
        _clipBoundsStack.Push(effectiveClip);

        // Push D2D clip with ALIASED mode — hard pixel boundary, no semi-transparent
        // edge artifacts. PER_PRIMITIVE mode creates anti-aliased clip edges that
        // produce visible seam lines when the clip boundary intersects opaque content.
        _renderTarget.PushClipAliased(x, y, w, h);
        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <summary>
    /// Pops a dirty region clip previously pushed by <see cref="PushDirtyRegionClip"/>.
    /// </summary>
    internal void PopDirtyRegionClip()
    {
        if (_closed) return;
        Pop();
    }

    // ========================================================================
    // Element Effect Capture & Rendering
    // ========================================================================

    /// <summary>
    /// Begins capturing element content into an offscreen bitmap for effect processing.
    /// </summary>
    public void BeginEffectCapture(float x, float y, float w, float h)
    {
        if (_closed) return;
        _renderTarget.BeginEffectCapture(x, y, w, h);
    }

    /// <summary>
    /// Ends capturing element content and restores the main render target.
    /// </summary>
    public void EndEffectCapture()
    {
        if (_closed) return;
        _renderTarget.EndEffectCapture();
    }

    /// <summary>
    /// Applies the given element effect to the captured content and draws the result.
    /// Dispatches to the appropriate native rendering method based on concrete effect type.
    /// </summary>
    public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h)
    {
        if (_closed || effect == null) return;

        if (effect is Media.Effects.BlurEffect blur)
        {
            _renderTarget.DrawBlurEffect(x, y, w, h, (float)blur.Radius);
        }
        else if (effect is Media.Effects.DropShadowEffect shadow)
        {
            var color = shadow.Color;
            _renderTarget.DrawDropShadowEffect(x, y, w, h,
                (float)shadow.BlurRadius,
                (float)shadow.OffsetX,
                (float)shadow.OffsetY,
                color.R / 255f, color.G / 255f, color.B / 255f,
                (float)shadow.Opacity);
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        _closed = true;
        // Note: Don't dispose cached resources here - they may be reused
    }

    /// <summary>
    /// Clears all cached resources.
    /// </summary>
    public void ClearCache()
    {
        foreach (var brush in _brushCache.Values)
        {
            brush.Dispose();
        }
        _brushCache.Clear();

        foreach (var format in _textFormatCache.Values)
        {
            format.Dispose();
        }
        _textFormatCache.Clear();

        foreach (var entry in _bitmapCache.Values)
        {
            entry.Bitmap.Dispose();
        }
        _bitmapCache.Clear();
        _bitmapCacheBytes = 0;
    }

    /// <summary>
    /// Clears only cached bitmaps. Useful during window teardown to quickly release
    /// large image resources while avoiding text/brush teardown order issues.
    /// </summary>
    public void ClearBitmapCache()
    {
        foreach (var entry in _bitmapCache.Values)
        {
            entry.Bitmap.Dispose();
        }

        _bitmapCache.Clear();
        _bitmapCacheBytes = 0;
    }

    /// <summary>
    /// Trims caches if they exceed their maximum size.
    /// Call this after each frame to prevent memory from growing unbounded.
    /// </summary>
    public void TrimCacheIfNeeded()
    {
        if (_brushCache.Count > MaxBrushCacheSize)
        {
            // Clear half of the cache when limit is exceeded
            var toRemove = _brushCache.Take(_brushCache.Count / 2).ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _brushCache.Remove(kvp.Key);
            }
        }

        if (_textFormatCache.Count > MaxTextFormatCacheSize)
        {
            var toRemove = _textFormatCache.Take(_textFormatCache.Count / 2).ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _textFormatCache.Remove(kvp.Key);
            }
        }

        TrimBitmapCacheIfNeeded();
    }

    private NativeBrush? GetNativeBrush(Brush brush)
        => GetNativeBrush(brush, 0, 0, 0, 0);

    private NativeBrush? GetNativeBrush(Brush brush, float bx, float by, float bw, float bh)
    {
        if (brush == null) return null;

        if (brush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            // Cache based on (brush reference, current color) to invalidate
            // when the same brush object has its Color property changed
            if (_brushCache.TryGetValue(brush, out var cached))
            {
                if (cached.CachedColor == color)
                    return cached;
                // Color changed — dispose old native brush and recreate
                cached.Dispose();
                _brushCache.Remove(brush);
            }

            var nb = _context.CreateSolidBrush(color.ScR, color.ScG, color.ScB, color.ScA);
            nb.CachedColor = color;
            _brushCache[brush] = nb;
            return nb;
        }

        if (brush is LinearGradientBrush linear)
        {
            return CreateNativeLinearGradient(linear, bx, by, bw, bh);
        }

        if (brush is RadialGradientBrush radial)
        {
            return CreateNativeRadialGradient(radial, bx, by, bw, bh);
        }

        return null;
    }

    private static float[] MarshalGradientStops(List<GradientStop> stops)
    {
        var arr = new float[stops.Count * 5];
        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            int off = i * 5;
            arr[off] = (float)s.Offset;
            arr[off + 1] = s.Color.ScR;
            arr[off + 2] = s.Color.ScG;
            arr[off + 3] = s.Color.ScB;
            arr[off + 4] = s.Color.ScA;
        }
        return arr;
    }

    private NativeBrush CreateNativeLinearGradient(LinearGradientBrush brush,
        float bx, float by, float bw, float bh)
    {
        float sx, sy, ex, ey;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            sx = bx + (float)brush.StartPoint.X * bw;
            sy = by + (float)brush.StartPoint.Y * bh;
            ex = bx + (float)brush.EndPoint.X * bw;
            ey = by + (float)brush.EndPoint.Y * bh;
        }
        else
        {
            sx = (float)brush.StartPoint.X;
            sy = (float)brush.StartPoint.Y;
            ex = (float)brush.EndPoint.X;
            ey = (float)brush.EndPoint.Y;
        }

        var stops = MarshalGradientStops(brush.GradientStops);
        var nb = _context.CreateLinearGradientBrush(sx, sy, ex, ey, stops, (uint)brush.GradientStops.Count);

        // Replace previous cached entry if any
        if (_brushCache.TryGetValue(brush, out var old))
            old.Dispose();
        _brushCache[brush] = nb;
        return nb;
    }

    private NativeBrush CreateNativeRadialGradient(RadialGradientBrush brush,
        float bx, float by, float bw, float bh)
    {
        float cx, cy, rx, ry, ox, oy;
        if (brush.MappingMode == BrushMappingMode.RelativeToBoundingBox)
        {
            cx = bx + (float)brush.Center.X * bw;
            cy = by + (float)brush.Center.Y * bh;
            rx = (float)brush.RadiusX * bw;
            ry = (float)brush.RadiusY * bh;
            ox = bx + (float)brush.GradientOrigin.X * bw;
            oy = by + (float)brush.GradientOrigin.Y * bh;
        }
        else
        {
            cx = (float)brush.Center.X;
            cy = (float)brush.Center.Y;
            rx = (float)brush.RadiusX;
            ry = (float)brush.RadiusY;
            ox = (float)brush.GradientOrigin.X;
            oy = (float)brush.GradientOrigin.Y;
        }

        var stops = MarshalGradientStops(brush.GradientStops);
        var nb = _context.CreateRadialGradientBrush(cx, cy, rx, ry, ox, oy, stops, (uint)brush.GradientStops.Count);

        // Replace previous cached entry if any
        if (_brushCache.TryGetValue(brush, out var old))
            old.Dispose();
        _brushCache[brush] = nb;
        return nb;
    }

    private NativeTextFormat? GetTextFormat(string fontFamily, double fontSize, int fontWeight, int fontStyle)
    {
        if (string.IsNullOrWhiteSpace(fontFamily))
        {
            fontFamily = "Segoe UI";
        }

        if (double.IsNaN(fontSize) || double.IsInfinity(fontSize) || fontSize <= 0)
        {
            fontSize = 12;
        }

        var key = $"{fontFamily}_{fontSize}_{fontWeight}_{fontStyle}";

        if (_textFormatCache.TryGetValue(key, out var cached) && cached.IsValid)
        {
            return cached;
        }

        var format = _context.CreateTextFormat(fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format != null)
        {
            _textFormatCache[key] = format;
        }

        return format;
    }

    private NativeBitmap? GetNativeBitmap(ImageSource imageSource)
    {
        if (imageSource == null) return null;

        if (_bitmapCache.TryGetValue(imageSource, out var cached))
        {
            if (cached.Bitmap.IsValid)
            {
                cached.LastAccessSequence = ++_bitmapCacheSequence;
                return cached.Bitmap;
            }

            RemoveBitmapCacheEntry(imageSource, cached);
        }

        NativeBitmap? nativeBitmap = null;

        if (imageSource is BitmapImage bitmapImage)
        {
            try
            {
                if (bitmapImage.RawPixelData != null &&
                    bitmapImage.PixelWidth > 0 &&
                    bitmapImage.PixelHeight > 0)
                {
                    nativeBitmap = _context.CreateBitmapFromPixels(
                        bitmapImage.RawPixelData,
                        bitmapImage.PixelWidth,
                        bitmapImage.PixelHeight,
                        bitmapImage.PixelStride);
                }
                else if (bitmapImage.ImageData != null)
                {
                    nativeBitmap = _context.CreateBitmap(bitmapImage.ImageData);
                }
            }
            catch
            {
                // Failed to create bitmap, return null
            }
        }

        if (nativeBitmap != null)
        {
            var estimatedBytes = EstimateBitmapBytes(nativeBitmap);
            _bitmapCache[imageSource] = new BitmapCacheEntry(
                nativeBitmap,
                estimatedBytes,
                ++_bitmapCacheSequence);
            _bitmapCacheBytes += estimatedBytes;
        }

        return nativeBitmap;
    }

    private void TrimBitmapCacheIfNeeded()
    {
        if (_bitmapCache.Count == 0)
        {
            return;
        }

        var bitmapCacheByteBudget = GetBitmapCacheByteBudget();
        while (_bitmapCache.Count > MaxBitmapCacheSize || _bitmapCacheBytes > bitmapCacheByteBudget)
        {
            KeyValuePair<ImageSource, BitmapCacheEntry>? oldest = null;
            foreach (var kvp in _bitmapCache)
            {
                if (oldest == null || kvp.Value.LastAccessSequence < oldest.Value.Value.LastAccessSequence)
                {
                    oldest = kvp;
                }
            }

            if (oldest == null)
            {
                break;
            }

            RemoveBitmapCacheEntry(oldest.Value.Key, oldest.Value.Value);
        }
    }

    private static long GetBitmapCacheByteBudget()
    {
        var workingSetBytes = Environment.WorkingSet;
        if (workingSetBytes >= HighMemoryPressureWorkingSetBytes)
        {
            return HighPressureBitmapCacheBytes;
        }

        if (workingSetBytes >= MediumMemoryPressureWorkingSetBytes)
        {
            return MediumPressureBitmapCacheBytes;
        }

        return MaxBitmapCacheBytes;
    }

    private void RemoveBitmapCacheEntry(ImageSource key, BitmapCacheEntry entry)
    {
        if (_bitmapCache.Remove(key))
        {
            _bitmapCacheBytes = Math.Max(0, _bitmapCacheBytes - entry.EstimatedBytes);
            entry.Bitmap.Dispose();
        }
    }

    private static long EstimateBitmapBytes(NativeBitmap bitmap)
    {
        // Native bitmaps are stored as RGBA8 textures (4 bytes per pixel).
        return (long)bitmap.Width * bitmap.Height * 4;
    }

    private enum DrawingStateType
    {
        Transform,
        NativeTransform,
        Clip,
        Opacity,
        ViewportOnly
    }

    private readonly struct DrawingState
    {
        public DrawingStateType Type { get; }
        public Point SavedOffset { get; }

        public DrawingState(DrawingStateType type, Point savedOffset)
        {
            Type = type;
            SavedOffset = savedOffset;
        }
    }
}
