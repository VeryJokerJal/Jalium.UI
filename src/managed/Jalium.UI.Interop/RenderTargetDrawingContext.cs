using System.Buffers;
using System.Linq;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// A DrawingContext implementation that renders to a RenderTarget.
/// </summary>
public sealed class RenderTargetDrawingContext : DrawingContext, IOffsetDrawingContext, IClipBoundsDrawingContext, IOpacityDrawingContext, IEffectDrawingContext, ITransformDrawingContext
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
    private long _brushCacheSequence;
    private long _textFormatCacheSequence;
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

    // Ellipse batch buffer for particle brush optimization
    private float[]? _ellipseBatchBuffer;
    private int _ellipseBatchCount;
    private bool _isEllipseBatching;

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
    /// When true, temporarily replaces GPU-expensive effects with cheap overlays.
    /// Intended for interactive resize / recovery scenarios where stability matters
    /// more than perfect visual fidelity.
    /// </summary>
    internal bool SimplifyGpuEffects { get; set; }

    /// <summary>
    /// Begins batching ellipse draw calls. While batching is active, DrawEllipse calls
    /// with solid color brushes are accumulated and flushed as a single native call.
    /// </summary>
    public void BeginEllipseBatch(int estimatedCount = 256)
    {
        if (_isEllipseBatching) return;
        _isEllipseBatching = true;
        _ellipseBatchCount = 0;
        var bufferSize = estimatedCount * 5;
        if (_ellipseBatchBuffer == null || _ellipseBatchBuffer.Length < bufferSize)
            _ellipseBatchBuffer = new float[bufferSize];
    }

    /// <summary>
    /// Flushes all accumulated ellipses as a single native batch call.
    /// </summary>
    public void EndEllipseBatch()
    {
        if (!_isEllipseBatching) return;
        _isEllipseBatching = false;

        if (_ellipseBatchCount > 0 && _ellipseBatchBuffer != null)
        {
            _renderTarget.FillEllipseBatch(_ellipseBatchBuffer, (uint)_ellipseBatchCount);
            _ellipseBatchCount = 0;
        }
    }

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

    private const double SnapEpsilon = 0.0001;

    private void FillTransientOverlay(float x, float y, float width, float height, float radiusX, float radiusY,
        float r, float g, float b, float a)
    {
        if (width <= 0 || height <= 0 || a <= 0) return;

        using var brush = _context.CreateSolidBrush(r, g, b, a);
        if (radiusX > 0 || radiusY > 0)
        {
            _renderTarget.FillRoundedRectangle(x, y, width, height, radiusX, radiusY, brush);
        }
        else
        {
            _renderTarget.FillRectangle(x, y, width, height, brush);
        }
    }

    private void DrawSimplifiedBackdropEffect(float x, float y, float width, float height,
        CornerRadius cornerRadius, IBackdropEffect effect)
    {
        var normalizedCornerRadius = cornerRadius.Normalize(width, height);
        float radiusX = (float)Math.Max(normalizedCornerRadius.TopLeft, normalizedCornerRadius.TopRight);
        float radiusY = (float)Math.Max(normalizedCornerRadius.TopLeft, normalizedCornerRadius.BottomLeft);

        uint tintColorArgb = effect.TintColorArgb;
        float overlayAlpha = Math.Clamp(effect.TintOpacity > 0 ? effect.TintOpacity : 0.14f, 0.08f, 0.45f);
        float r = 0.12f;
        float g = 0.12f;
        float b = 0.12f;

        if (tintColorArgb != 0)
        {
            r = ((tintColorArgb >> 16) & 0xFF) / 255f;
            g = ((tintColorArgb >> 8) & 0xFF) / 255f;
            b = (tintColorArgb & 0xFF) / 255f;
        }

        FillTransientOverlay(x, y, width, height, radiusX, radiusY, r, g, b, overlayAlpha);
    }

    private static float SnapCoordinate(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0f;
        }

        var rounded = Math.Round(value);
        if (Math.Abs(value - rounded) < SnapEpsilon)
        {
            return (float)rounded;
        }

        var halfPixel = Math.Floor(value) + 0.5;
        if (Math.Abs(value - halfPixel) < SnapEpsilon)
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

        var x0 = SnapCoordinate(point0.X + Offset.X);
        var y0 = SnapCoordinate(point0.Y + Offset.Y);
        var x1 = SnapCoordinate(point1.X + Offset.X);
        var y1 = SnapCoordinate(point1.Y + Offset.Y);
        var thickness = (float)pen.Thickness;

        // Dashed line: split into segments
        if (pen.DashStyle is { Dashes.Count: > 0 })
        {
            DrawDashedLine(x0, y0, x1, y1, brush, thickness, pen.DashStyle, pen.Thickness);
            return;
        }

        _renderTarget.DrawLine(x0, y0, x1, y1, brush, thickness);
    }

    private void DrawDashedLine(float x0, float y0, float x1, float y1,
        NativeBrush nativeBrush, float thickness, DashStyle dashStyle, double penThickness)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var lineLength = Math.Sqrt(dx * dx + dy * dy);
        if (lineLength < 0.5) return;

        var dashes = dashStyle.Dashes;
        var offset = dashStyle.Offset * penThickness;
        var ux = (float)(dx / lineLength);
        var uy = (float)(dy / lineLength);

        double pos = -offset;
        int dashIndex = 0;
        while (pos < lineLength)
        {
            var dashLen = dashes[dashIndex % dashes.Count] * penThickness;
            var gapLen = dashes[(dashIndex + 1) % dashes.Count] * penThickness;

            var start = Math.Max(0, pos);
            var end = Math.Min(lineLength, pos + dashLen);

            if (end > start)
            {
                _renderTarget.DrawLine(
                    x0 + ux * (float)start, y0 + uy * (float)start,
                    x0 + ux * (float)end, y0 + uy * (float)end,
                    nativeBrush, thickness);
            }

            pos += dashLen + gapLen;
            dashIndex += 2;
        }
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

        // Fast path: batch filled ellipses with solid color brushes (particle brushes)
        if (_isEllipseBatching && brush is SolidColorBrush solidBrush && pen == null)
        {
            EnsureBatchCapacity();
            var offset = _ellipseBatchCount * 5;
            _ellipseBatchBuffer![offset] = cx;
            _ellipseBatchBuffer[offset + 1] = cy;
            _ellipseBatchBuffer[offset + 2] = rx;
            _ellipseBatchBuffer[offset + 3] = ry;
            // Pack color as RGBA uint32 stored in float bits
            var c = solidBrush.Color;
            uint packed = (uint)c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24);
            _ellipseBatchBuffer[offset + 4] = BitConverter.Int32BitsToSingle((int)packed);
            _ellipseBatchCount++;
            return;
        }

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

    private void EnsureBatchCapacity()
    {
        var needed = (_ellipseBatchCount + 1) * 5;
        if (_ellipseBatchBuffer == null || _ellipseBatchBuffer.Length < needed)
        {
            var newSize = Math.Max(needed, (_ellipseBatchBuffer?.Length ?? 256) * 2);
            var newBuffer = new float[newSize];
            if (_ellipseBatchBuffer != null)
                Array.Copy(_ellipseBatchBuffer, newBuffer, _ellipseBatchCount * 5);
            _ellipseBatchBuffer = newBuffer;
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

        // Apply Geometry.Transform if set
        var geometryTransform = geometry.Transform;
        bool pushedTransform = false;
        if (geometryTransform != null && !geometryTransform.Value.IsIdentity)
        {
            PushTransform(geometryTransform);
            pushedTransform = true;
        }

        try
        {
            DrawGeometryCore(brush, pen, geometry);
        }
        finally
        {
            if (pushedTransform)
                Pop();
        }
    }

    private void DrawGeometryCore(Brush? brush, Pen? pen, Geometry geometry)
    {
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
            switch (combined.GeometryCombineMode)
            {
                case GeometryCombineMode.Exclude:
                    // Draw Geometry1 only; Geometry2 defines the excluded region.
                    // A proper implementation requires native CSG; for now draw Geometry1
                    // which is visually closer than drawing both.
                    if (combined.Geometry1 != null) DrawGeometry(brush, pen, combined.Geometry1);
                    break;
                case GeometryCombineMode.Intersect:
                    // Intersection is hard to approximate without native support.
                    // Draw the smaller geometry as a rough approximation.
                    if (combined.Geometry1 != null && combined.Geometry2 != null)
                    {
                        var b1 = combined.Geometry1.Bounds;
                        var b2 = combined.Geometry2.Bounds;
                        DrawGeometry(brush, pen,
                            (b1.Width * b1.Height <= b2.Width * b2.Height) ? combined.Geometry1 : combined.Geometry2);
                    }
                    else
                    {
                        if (combined.Geometry1 != null) DrawGeometry(brush, pen, combined.Geometry1);
                    }
                    break;
                case GeometryCombineMode.Xor:
                case GeometryCombineMode.Union:
                default:
                    // Union / Xor: draw both geometries
                    if (combined.Geometry1 != null) DrawGeometry(brush, pen, combined.Geometry1);
                    if (combined.Geometry2 != null) DrawGeometry(brush, pen, combined.Geometry2);
                    break;
            }
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

    private static bool FigureHasCurves(PathFigure figure)
    {
        foreach (var segment in figure.Segments)
        {
            if (segment is BezierSegment or QuadraticBezierSegment
                or PolyBezierSegment or PolyQuadraticBezierSegment
                or ArcSegment)
            {
                return true;
            }
        }
        return false;
    }

    private void DrawPathGeometry(Brush? brush, Pen? pen, PathGeometry pathGeom)
    {
        // Check if we need managed dashed stroke rendering
        bool hasDash = pen?.DashStyle?.Dashes is { Count: > 0 };
        // Use managed widening only for non-Flat line caps (which the native
        // DrawPolygon cannot render).  LineJoin differences (Miter vs Bevel)
        // are handled natively — the managed widening + FillPolygon path
        // cannot correctly render closed stroke outlines on D3D12 (triangle
        // fan doesn't support concave/ring polygons).
        bool hasNonFlatCaps = pen != null && (
            pen.StartLineCap != PenLineCap.Flat ||
            pen.EndLineCap != PenLineCap.Flat);

        // For fill: use compound path rendering when there are multiple figures
        // (enables proper hole/fill rule handling in the native triangulator)
        var fillFigures = brush != null
            ? pathGeom.Figures.Where(f => f.IsFilled).ToList()
            : null;

        if (fillFigures != null && fillFigures.Count > 1)
        {
            // Send all figures as a single compound path with MoveTo separators
            DrawCompoundPathFill(brush!, fillFigures, pathGeom.FillRule);
        }
        else if (fillFigures != null && fillFigures.Count == 1)
        {
            var figure = fillFigures[0];
            if (FigureHasCurves(figure))
                DrawPathFigureNative(brush, null, figure, pathGeom.FillRule);
            else
                DrawPathFigurePolygon(brush, null, figure, pathGeom.FillRule);
        }

        // Stroke rendering: each figure stroked individually
        if (pen?.Brush != null)
        {
            foreach (var figure in pathGeom.Figures)
            {
                bool needsWidening = hasNonFlatCaps && !figure.IsClosed;

                if (hasDash)
                {
                    DrawDashedPathFigure(pen, figure);
                }
                else if (needsWidening)
                {
                    // Use GetWidenedPathGeometry for accurate line caps/joins on open paths
                    DrawWidenedStroke(pen, figure, pathGeom.FillRule);
                }
                else if (FigureHasCurves(figure))
                {
                    DrawPathFigureNative(null, pen, figure, pathGeom.FillRule);
                }
                else
                {
                    DrawPathFigurePolygon(null, pen, figure, pathGeom.FillRule);
                }
            }
        }
    }

    /// <summary>
    /// Sends multiple path figures as a single compound path to native FillPath,
    /// using tag 2 (MoveTo) to separate contours.  This enables the native
    /// triangulator to handle holes and fill rules correctly.
    /// </summary>
    private void DrawCompoundPathFill(Brush brush, List<PathFigure> figures, FillRule fillRule)
    {
        _pathCommandBuffer ??= new List<float>(256);
        _pathCommandBuffer.Clear();
        var cmds = _pathCommandBuffer;
        var ox = Offset.X;
        var oy = Offset.Y;

        // First figure: use the normal startX/startY
        var firstFigure = figures[0];
        float startX = (float)(firstFigure.StartPoint.X + ox);
        float startY = (float)(firstFigure.StartPoint.Y + oy);

        AppendFigureSegments(cmds, firstFigure, firstFigure.StartPoint, ox, oy);
        if (firstFigure.IsClosed) cmds.Add(5f); // ClosePath tag

        // Subsequent figures: use MoveTo (tag 2) to start new contours
        for (int f = 1; f < figures.Count; f++)
        {
            var figure = figures[f];
            cmds.Add(2f); // MoveTo tag
            cmds.Add((float)(figure.StartPoint.X + ox));
            cmds.Add((float)(figure.StartPoint.Y + oy));

            AppendFigureSegments(cmds, figure, figure.StartPoint, ox, oy);
            if (figure.IsClosed) cmds.Add(5f); // ClosePath tag
        }

        if (cmds.Count == 0) return;

        var nativeBrush = GetNativeBrush(brush);
        if (nativeBrush != null)
        {
            int rule = fillRule == FillRule.Nonzero ? 1 : 0;
            _renderTarget.FillPath(startX, startY, cmds.ToArray(), nativeBrush, rule);
        }
    }

    private void DrawWidenedStroke(Pen pen, PathFigure figure, FillRule fillRule)
    {
        // Build a single-figure PathGeometry, widen it, then fill the result
        var singleGeom = new PathGeometry { FillRule = fillRule };
        singleGeom.Figures.Add(figure);

        var widened = singleGeom.GetWidenedPathGeometry(pen);
        if (widened.Figures.Count == 0) return;

        var strokeBrush = pen.Brush;
        foreach (var wFigure in widened.Figures)
        {
            DrawPathFigurePolygon(strokeBrush, null, wFigure, FillRule.Nonzero);
        }
    }

    private void DrawDashedPathFigure(Pen pen, PathFigure figure)
    {
        // Flatten the figure to get all points
        var points = new List<Point> { figure.StartPoint };
        var currentPoint = figure.StartPoint;
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment ls:
                    points.Add(ls.Point);
                    currentPoint = ls.Point;
                    break;
                case PolyLineSegment pls:
                    points.AddRange(pls.Points);
                    if (pls.Points.Count > 0) currentPoint = pls.Points[^1];
                    break;
                case BezierSegment bez:
                    points.AddRange(GetBezierPoints(currentPoint, bez.Point1, bez.Point2, bez.Point3));
                    currentPoint = bez.Point3;
                    break;
                case PolyBezierSegment pbez:
                    var bpts = pbez.Points;
                    for (int i = 0; i + 2 < bpts.Count; i += 3)
                    {
                        points.AddRange(GetBezierPoints(currentPoint, bpts[i], bpts[i + 1], bpts[i + 2]));
                        currentPoint = bpts[i + 2];
                    }
                    break;
                case QuadraticBezierSegment q:
                    points.AddRange(GetQuadBezierPoints(currentPoint, q.Point1, q.Point2));
                    currentPoint = q.Point2;
                    break;
                case PolyQuadraticBezierSegment pq:
                    var qpts = pq.Points;
                    for (int i = 0; i + 1 < qpts.Count; i += 2)
                    {
                        points.AddRange(GetQuadBezierPoints(currentPoint, qpts[i], qpts[i + 1]));
                        currentPoint = qpts[i + 1];
                    }
                    break;
                case ArcSegment arc:
                    points.AddRange(GetArcPoints(currentPoint, arc));
                    currentPoint = arc.Point;
                    break;
            }
        }

        if (figure.IsClosed && points.Count > 1)
        {
            var first = points[0];
            var last = points[^1];
            if (Math.Abs(first.X - last.X) > 1e-10 || Math.Abs(first.Y - last.Y) > 1e-10)
                points.Add(first);
        }

        if (points.Count < 2) return;

        // Compute cumulative distances
        var dashes = pen.DashStyle!.Dashes;
        var dashOffset = pen.DashStyle.Offset * pen.Thickness;
        if (dashes.Count == 0) return;

        // Build the dash pattern in absolute units
        var pattern = new double[dashes.Count];
        double patternLength = 0;
        for (int i = 0; i < dashes.Count; i++)
        {
            pattern[i] = dashes[i] * pen.Thickness;
            patternLength += pattern[i];
        }
        if (patternLength <= 0) return;

        // Walk along the polyline, emitting dashed sub-segments
        if (pen.Brush == null) return;
        var strokeBrush = GetNativeBrush(pen.Brush);
        if (strokeBrush == null) return;

        int dashIndex = 0;
        bool drawing = true; // true = dash (visible), false = gap
        double remaining = pattern[0];

        // Apply dash offset
        double offset = dashOffset % patternLength;
        if (offset < 0) offset += patternLength;
        while (offset > 0)
        {
            if (offset >= remaining)
            {
                offset -= remaining;
                dashIndex = (dashIndex + 1) % pattern.Length;
                drawing = !drawing;
                remaining = pattern[dashIndex];
            }
            else
            {
                remaining -= offset;
                offset = 0;
            }
        }

        var dashStart = points[0];
        int ptIndex = 0;

        while (ptIndex < points.Count - 1)
        {
            var segStart = points[ptIndex];
            var segEnd = points[ptIndex + 1];
            var segDx = segEnd.X - segStart.X;
            var segDy = segEnd.Y - segStart.Y;
            var segLen = Math.Sqrt(segDx * segDx + segDy * segDy);

            if (segLen < 1e-10)
            {
                ptIndex++;
                continue;
            }

            double consumed = 0;
            while (consumed < segLen - 1e-10)
            {
                var available = segLen - consumed;
                if (remaining <= available)
                {
                    // Finish this dash/gap segment
                    var t = (consumed + remaining) / segLen;
                    var endPt = new Point(
                        segStart.X + segDx * t,
                        segStart.Y + segDy * t);

                    if (drawing)
                    {
                        // Emit stroke from dashStart to endPt
                        EmitStrokeLine(dashStart, endPt, strokeBrush, (float)pen.Thickness);
                    }

                    consumed += remaining;
                    dashStart = endPt;
                    dashIndex = (dashIndex + 1) % pattern.Length;
                    drawing = !drawing;
                    remaining = pattern[dashIndex];
                }
                else
                {
                    // This segment ends before the current dash/gap completes
                    remaining -= available;
                    if (drawing)
                    {
                        // dashStart to segEnd is part of a visible dash; don't emit yet
                    }
                    consumed = segLen;
                }
            }

            ptIndex++;
            if (ptIndex < points.Count && !drawing)
            {
                // In a gap, update dashStart to next point
            }
            else if (ptIndex < points.Count && drawing)
            {
                // Continuing a dash into the next segment, dashStart stays
            }
        }

        // Emit final dash segment if we're still drawing
        if (drawing && ptIndex > 0)
        {
            var lastPt = points[^1];
            if (Math.Abs(dashStart.X - lastPt.X) > 1e-10 || Math.Abs(dashStart.Y - lastPt.Y) > 1e-10)
            {
                EmitStrokeLine(dashStart, lastPt, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    private void EmitStrokeLine(Point from, Point to, NativeBrush brush, float strokeWidth)
    {
        var ox = Offset.X;
        var oy = Offset.Y;
        _renderTarget.DrawLine(
            (float)(from.X + ox), (float)(from.Y + oy),
            (float)(to.X + ox), (float)(to.Y + oy),
            brush, strokeWidth);
    }

    /// <summary>
    /// Promotes a quadratic bezier to cubic and appends the cubic command.
    /// cp1 = start + 2/3*(ctrl - start), cp2 = end + 2/3*(ctrl - end)
    /// </summary>
    private static void AppendQuadAsCubic(List<float> cmds, Point start, Point ctrl, Point end, double ox, double oy)
    {
        var cp1X = start.X + 2.0 / 3.0 * (ctrl.X - start.X);
        var cp1Y = start.Y + 2.0 / 3.0 * (ctrl.Y - start.Y);
        var cp2X = end.X + 2.0 / 3.0 * (ctrl.X - end.X);
        var cp2Y = end.Y + 2.0 / 3.0 * (ctrl.Y - end.Y);

        cmds.Add(1f);
        cmds.Add((float)(cp1X + ox));
        cmds.Add((float)(cp1Y + oy));
        cmds.Add((float)(cp2X + ox));
        cmds.Add((float)(cp2Y + oy));
        cmds.Add((float)(end.X + ox));
        cmds.Add((float)(end.Y + oy));
    }

    /// <summary>
    /// Converts an SVG-style arc to cubic bezier curves appended to the command buffer.
    /// Uses the standard endpoint-to-center parameterization, then approximates each
    /// arc segment (≤ π/2) with a single cubic bezier.
    /// </summary>
    private static void AppendArcAsCubicBeziers(List<float> cmds, Point start, ArcSegment arc, double ox, double oy)
    {
        var end = arc.Point;
        var rx = arc.Size.Width;
        var ry = arc.Size.Height;

        // Handle degenerate cases
        if (rx == 0 || ry == 0 || (start.X == end.X && start.Y == end.Y))
        {
            cmds.Add(0f);
            cmds.Add((float)(end.X + ox));
            cmds.Add((float)(end.Y + oy));
            return;
        }

        // Convert endpoint parameterization to center parameterization (SVG spec F.6.5-F.6.6)
        var rotAngle = arc.RotationAngle * Math.PI / 180.0;
        var cosA = Math.Cos(rotAngle);
        var sinA = Math.Sin(rotAngle);

        var dx2 = (start.X - end.X) / 2.0;
        var dy2 = (start.Y - end.Y) / 2.0;
        var x1p = cosA * dx2 + sinA * dy2;
        var y1p = -sinA * dx2 + cosA * dy2;

        // Ensure radii are large enough
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;
        var rxSq = rx * rx;
        var rySq = ry * ry;
        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1)
        {
            var sqrtLam = Math.Sqrt(lambda);
            rx *= sqrtLam;
            ry *= sqrtLam;
            rxSq = rx * rx;
            rySq = ry * ry;
        }

        // Calculate center point
        var sign = (arc.IsLargeArc != (arc.SweepDirection == SweepDirection.Clockwise)) ? 1.0 : -1.0;
        var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
        var coef = sign * Math.Sqrt(sq);
        var cxp = coef * rx * y1p / ry;
        var cyp = -coef * ry * x1p / rx;
        var cx = cosA * cxp - sinA * cyp + (start.X + end.X) / 2.0;
        var cy = sinA * cxp + cosA * cyp + (start.Y + end.Y) / 2.0;

        // Calculate start and sweep angles
        var startAngle = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var endAngle = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var deltaAngle = endAngle - startAngle;

        if (arc.SweepDirection == SweepDirection.Clockwise && deltaAngle < 0)
            deltaAngle += 2 * Math.PI;
        else if (arc.SweepDirection == SweepDirection.Counterclockwise && deltaAngle > 0)
            deltaAngle -= 2 * Math.PI;

        // Split into segments of at most π/2 and approximate each with a cubic bezier
        int segCount = (int)Math.Ceiling(Math.Abs(deltaAngle) / (Math.PI / 2.0));
        segCount = Math.Max(1, segCount);
        var segAngle = deltaAngle / segCount;

        for (int i = 0; i < segCount; i++)
        {
            var a1 = startAngle + segAngle * i;
            var a2 = a1 + segAngle;

            // Cubic bezier approximation of a unit circle arc from a1 to a2:
            // alpha = sin(da) * (sqrt(4 + 3*tan(da/2)^2) - 1) / 3
            var da = a2 - a1;
            var halfTan = Math.Tan(da / 2.0);
            var alpha = Math.Sin(da) * (Math.Sqrt(4 + 3 * halfTan * halfTan) - 1) / 3.0;

            var cos1 = Math.Cos(a1);
            var sin1 = Math.Sin(a1);
            var cos2 = Math.Cos(a2);
            var sin2 = Math.Sin(a2);

            // Points on the unit ellipse (before rotation/translation)
            var ep1x = rx * cos1;
            var ep1y = ry * sin1;
            var ep2x = rx * cos2;
            var ep2y = ry * sin2;

            // Control point tangent directions
            var d1x = -rx * sin1;
            var d1y = ry * cos1;
            var d2x = -rx * sin2;
            var d2y = ry * cos2;

            var cp1x = ep1x + alpha * d1x;
            var cp1y = ep1y + alpha * d1y;
            var cp2x = ep2x - alpha * d2x;
            var cp2y = ep2y - alpha * d2y;

            // Apply rotation and translation
            var fcp1x = cosA * cp1x - sinA * cp1y + cx;
            var fcp1y = sinA * cp1x + cosA * cp1y + cy;
            var fcp2x = cosA * cp2x - sinA * cp2y + cx;
            var fcp2y = sinA * cp2x + cosA * cp2y + cy;
            var fep2x = cosA * ep2x - sinA * ep2y + cx;
            var fep2y = sinA * ep2x + cosA * ep2y + cy;

            cmds.Add(1f); // BezierTo
            cmds.Add((float)(fcp1x + ox));
            cmds.Add((float)(fcp1y + oy));
            cmds.Add((float)(fcp2x + ox));
            cmds.Add((float)(fcp2y + oy));
            cmds.Add((float)(fep2x + ox));
            cmds.Add((float)(fep2y + oy));
        }
    }

    /// <summary>
    /// Appends all segments of a PathFigure to the command buffer.
    /// Used by both single-figure and compound-path rendering.
    /// </summary>
    private static Point AppendFigureSegments(List<float> cmds, PathFigure figure, Point currentPoint, double ox, double oy)
    {
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
            else if (segment is PolyBezierSegment polyBezier)
            {
                var pts = polyBezier.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                {
                    cmds.Add(1f);
                    cmds.Add((float)(pts[i].X + ox));
                    cmds.Add((float)(pts[i].Y + oy));
                    cmds.Add((float)(pts[i + 1].X + ox));
                    cmds.Add((float)(pts[i + 1].Y + oy));
                    cmds.Add((float)(pts[i + 2].X + ox));
                    cmds.Add((float)(pts[i + 2].Y + oy));
                    currentPoint = pts[i + 2];
                }
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                // Native QuadTo tag 3: [3, cpx, cpy, ex, ey]
                cmds.Add(3f);
                cmds.Add((float)(quad.Point1.X + ox));
                cmds.Add((float)(quad.Point1.Y + oy));
                cmds.Add((float)(quad.Point2.X + ox));
                cmds.Add((float)(quad.Point2.Y + oy));
                currentPoint = quad.Point2;
            }
            else if (segment is PolyQuadraticBezierSegment polyQuad)
            {
                var pts = polyQuad.Points;
                for (int i = 0; i + 1 < pts.Count; i += 2)
                {
                    // Native QuadTo tag 3: [3, cpx, cpy, ex, ey]
                    cmds.Add(3f);
                    cmds.Add((float)(pts[i].X + ox));
                    cmds.Add((float)(pts[i].Y + oy));
                    cmds.Add((float)(pts[i + 1].X + ox));
                    cmds.Add((float)(pts[i + 1].Y + oy));
                    currentPoint = pts[i + 1];
                }
            }
            else if (segment is ArcSegment arc)
            {
                // Convert arc to cubic bezier curves that native can render (tag 1).
                // Native backends don't support raw arc commands.
                AppendArcAsCubicBeziers(cmds, currentPoint, arc, ox, oy);
                currentPoint = arc.Point;
            }
        }
        return currentPoint;
    }

    /// <summary>
    /// Renders a path figure using the native FillPath/StrokePath API with real bezier curves.
    /// </summary>
    // Reusable command buffer for path rendering to reduce GC pressure.
    private List<float>? _pathCommandBuffer;

    private void DrawPathFigureNative(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule)
    {
        // Build command buffer: tag 0 = LineTo [0,x,y], tag 1 = BezierTo [1,cp1x,cp1y,cp2x,cp2y,ex,ey]
        _pathCommandBuffer ??= new List<float>(128);
        _pathCommandBuffer.Clear();
        var cmds = _pathCommandBuffer;
        var ox = Offset.X;
        var oy = Offset.Y;

        AppendFigureSegments(cmds, figure, figure.StartPoint, ox, oy);

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
                int nativeLineCap = pen.StartLineCap switch
                {
                    PenLineCap.Round => 2,    // kLineCapRound
                    PenLineCap.Square => 1,   // kLineCapSquare
                    _ => 0                    // kLineCapButt (Flat, Triangle)
                };
                _renderTarget.StrokePath(startX, startY, cmdArray, strokeBrush, (float)pen.Thickness, figure.IsClosed, (int)pen.LineJoin, (float)pen.MiterLimit, nativeLineCap);
            }
        }
    }

    /// <summary>
    /// Renders a path figure as a polygon (all segments flattened to line points).
    /// </summary>
    // Reusable point buffer for polygon flattening to reduce GC pressure.
    private List<Point>? _polygonPointBuffer;

    private void DrawPathFigurePolygon(Brush? brush, Pen? pen, PathFigure figure, FillRule fillRule)
    {
        _polygonPointBuffer ??= new List<Point>(64);
        _polygonPointBuffer.Clear();
        var points = _polygonPointBuffer;
        points.Add(figure.StartPoint);
        var currentPoint = figure.StartPoint;
        bool hasCurvedSegments = false;

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
                hasCurvedSegments = true;
                var arcPoints = GetArcPoints(currentPoint, arc);
                points.AddRange(arcPoints);
                currentPoint = arc.Point;
            }
            else if (segment is BezierSegment bezier)
            {
                hasCurvedSegments = true;
                var bezierPoints = GetBezierPoints(currentPoint, bezier.Point1, bezier.Point2, bezier.Point3);
                points.AddRange(bezierPoints);
                currentPoint = bezier.Point3;
            }
            else if (segment is PolyBezierSegment polyBezier)
            {
                hasCurvedSegments = true;
                var pts = polyBezier.Points;
                for (int i = 0; i + 2 < pts.Count; i += 3)
                {
                    var bezierPoints = GetBezierPoints(currentPoint, pts[i], pts[i + 1], pts[i + 2]);
                    points.AddRange(bezierPoints);
                    currentPoint = pts[i + 2];
                }
            }
            else if (segment is QuadraticBezierSegment quad)
            {
                hasCurvedSegments = true;
                var quadPoints = GetQuadBezierPoints(currentPoint, quad.Point1, quad.Point2);
                points.AddRange(quadPoints);
                currentPoint = quad.Point2;
            }
            else if (segment is PolyQuadraticBezierSegment polyQuad)
            {
                hasCurvedSegments = true;
                var pts = polyQuad.Points;
                for (int i = 0; i + 1 < pts.Count; i += 2)
                {
                    var quadPoints = GetQuadBezierPoints(currentPoint, pts[i], pts[i + 1]);
                    points.AddRange(quadPoints);
                    currentPoint = pts[i + 1];
                }
            }
        }

        // The native DrawPolygon already adds a 0.5 offset for odd-pixel strokes
        // to align to pixel centers.  The managed side must therefore snap to the
        // nearest *integer* so the combined result lands on half-pixel → crisp 1px.
        // Using SnapCoordinate (which preserves half-pixel values) would cause a
        // double offset: 0.5 (snap) + 0.5 (native) = 1.0 → integer position →
        // the stroke spans two pixel rows and appears ~2px thick.
        //
        // For paths that contain diagonal segments we skip snapping entirely so
        // that the native 0.5 shift is a uniform translation (no visual impact on
        // thickness) and anti-aliased diagonals render at their natural weight.
        bool isAxisAligned = !hasCurvedSegments && IsAxisAlignedPath(points);

        var pointArray = new float[points.Count * 2];
        if (isAxisAligned && points.Count > 0)
        {
            // Snap the first point to the nearest integer, then apply the
            // same fractional offset to all subsequent points.  This preserves
            // relative distances (lengths) between points while still aligning
            // the path to the pixel grid for crisp rendering.
            var baseX = points[0].X + Offset.X;
            var baseY = points[0].Y + Offset.Y;
            var snapDx = Math.Round(baseX) - baseX;
            var snapDy = Math.Round(baseY) - baseY;

            for (int i = 0; i < points.Count; i++)
            {
                pointArray[i * 2] = (float)(points[i].X + Offset.X + snapDx);
                pointArray[i * 2 + 1] = (float)(points[i].Y + Offset.Y + snapDy);
            }
        }
        else
        {
            for (int i = 0; i < points.Count; i++)
            {
                pointArray[i * 2] = (float)(points[i].X + Offset.X);
                pointArray[i * 2 + 1] = (float)(points[i].Y + Offset.Y);
            }
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
                _renderTarget.DrawPolygon(pointArray, strokeBrush, (float)pen.Thickness, figure.IsClosed, (int)pen.LineJoin, (float)pen.MiterLimit);
            }
        }
    }

    /// <summary>
    /// Checks whether all consecutive point pairs form axis-aligned (horizontal or vertical) segments.
    /// </summary>
    private static bool IsAxisAlignedPath(List<Point> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            // Segment is axis-aligned if either X or Y is the same
            if (Math.Abs(p1.X - p2.X) > 0.001 && Math.Abs(p1.Y - p2.Y) > 0.001)
                return false;
        }
        return true;
    }

    private const double FlatteningTolerance = 0.25;

    private List<Point> GetBezierPoints(Point p0, Point p1, Point p2, Point p3)
    {
        var points = new List<Point>();
        FlattenCubicBezier(points, p0.X, p0.Y, p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, 0);
        return points;
    }

    private static void FlattenCubicBezier(List<Point> points,
        double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, int depth)
    {
        if (depth > 10)
        {
            points.Add(new Point(x3, y3));
            return;
        }

        // Flatness test: distance of control points from the chord
        double dx = x3 - x0, dy = y3 - y0;
        double len2 = dx * dx + dy * dy;
        double d1, d2;
        if (len2 < 1e-10)
        {
            d1 = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            d2 = Math.Sqrt((x2 - x0) * (x2 - x0) + (y2 - y0) * (y2 - y0));
        }
        else
        {
            double invLen = 1.0 / Math.Sqrt(len2);
            double nx = -dy * invLen, ny = dx * invLen;
            d1 = Math.Abs(nx * (x1 - x0) + ny * (y1 - y0));
            d2 = Math.Abs(nx * (x2 - x0) + ny * (y2 - y0));
        }

        if (d1 + d2 <= FlatteningTolerance)
        {
            points.Add(new Point(x3, y3));
            return;
        }

        // De Casteljau subdivision at t=0.5
        double m01x = (x0 + x1) * 0.5, m01y = (y0 + y1) * 0.5;
        double m12x = (x1 + x2) * 0.5, m12y = (y1 + y2) * 0.5;
        double m23x = (x2 + x3) * 0.5, m23y = (y2 + y3) * 0.5;
        double m012x = (m01x + m12x) * 0.5, m012y = (m01y + m12y) * 0.5;
        double m123x = (m12x + m23x) * 0.5, m123y = (m12y + m23y) * 0.5;
        double mx = (m012x + m123x) * 0.5, my = (m012y + m123y) * 0.5;

        FlattenCubicBezier(points, x0, y0, m01x, m01y, m012x, m012y, mx, my, depth + 1);
        FlattenCubicBezier(points, mx, my, m123x, m123y, m23x, m23y, x3, y3, depth + 1);
    }

    private List<Point> GetQuadBezierPoints(Point p0, Point p1, Point p2)
    {
        // Promote to cubic: cp1 = p0 + 2/3*(p1-p0), cp2 = p2 + 2/3*(p1-p2)
        var cp1x = p0.X + 2.0 / 3.0 * (p1.X - p0.X);
        var cp1y = p0.Y + 2.0 / 3.0 * (p1.Y - p0.Y);
        var cp2x = p2.X + 2.0 / 3.0 * (p1.X - p2.X);
        var cp2y = p2.Y + 2.0 / 3.0 * (p1.Y - p2.Y);
        return GetBezierPoints(p0, new Point(cp1x, cp1y), new Point(cp2x, cp2y), p2);
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

        // Adaptive segment count based on arc size and sweep angle
        var circumference = Math.Abs(deltaAngle) * Math.Max(rx, ry);
        var segments = Math.Clamp((int)(circumference / FlatteningTolerance), 4, 256);
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

        if (SimplifyGpuEffects)
        {
            DrawSimplifiedBackdropEffect(x, y, width, height, cornerRadius, effect);
            return;
        }

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

        if (SimplifyGpuEffects)
        {
            float overlayAlpha = Math.Clamp(tintOpacity > 0 ? tintOpacity : 0.22f, 0.14f, 0.42f);
            FillTransientOverlay(x, y, width, height, cornerRadius, cornerRadius, tintR, tintG, tintB, overlayAlpha);
            return;
        }

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

    /// <summary>
    /// Explicit implementation of ITransformDrawingContext.PushTransform.
    /// Accepts an object and delegates to the typed PushTransform, composing with origin offset.
    /// </summary>
    void ITransformDrawingContext.PushTransform(object transform, double originX, double originY)
    {
        if (transform is Transform t)
        {
            if (originX != 0 || originY != 0)
            {
                // Compose: T(-origin) * transform * T(+origin)
                var m = t.Value;
                var pre = new Matrix(1, 0, 0, 1, -originX, -originY);
                var post = new Matrix(1, 0, 0, 1, originX, originY);
                var combined = Matrix.Multiply(Matrix.Multiply(pre, m), post);
                PushTransform(new MatrixTransform(combined));
            }
            else
            {
                PushTransform(t);
            }
        }
    }

    /// <summary>
    /// Explicit implementation of ITransformDrawingContext.PopTransform.
    /// </summary>
    void ITransformDrawingContext.PopTransform()
    {
        Pop();
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

    /// <summary>
    /// Pushes a rounded-rect clip using element bounds and corner radius.
    /// </summary>
    public void PushRoundedRectClip(Rect bounds, CornerRadius cornerRadius)
    {
        if (_closed) return;

        var x = (float)(bounds.X + Offset.X);
        var y = (float)(bounds.Y + Offset.Y);
        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        var r = (float)Math.Max(Math.Max(cornerRadius.TopLeft, cornerRadius.TopRight),
                                Math.Max(cornerRadius.BottomRight, cornerRadius.BottomLeft));

        var clipRect = new Rect(x, y, w, h);
        Rect? effectiveClip = clipRect;
        if (_clipBoundsStack.Count > 0)
        {
            var parentClip = _clipBoundsStack.Peek();
            effectiveClip = parentClip.HasValue ? parentClip.Value.Intersect(clipRect) : clipRect;
        }
        _clipBoundsStack.Push(effectiveClip);

        _renderTarget.PushRoundedRectClip(x, y, w, h, r, r);
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
    /// Sets the current shape type for subsequent SDF rect draw calls.
    /// Call with (0, 0) to reset to default rounded rectangle mode.
    /// </summary>
    /// <param name="type">0 = RoundedRect, 1 = SuperEllipse.</param>
    /// <param name="n">SuperEllipse exponent (e.g. 4.0 for squircle).</param>
    public void SetShapeType(int type, float n)
    {
        if (_closed) return;
        _renderTarget.SetShapeType(type, n);
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
    public void ApplyElementEffect(IEffect effect, float x, float y, float w, float h,
        float captureOriginX = 0, float captureOriginY = 0,
        float cornerTL = 0, float cornerTR = 0, float cornerBR = 0, float cornerBL = 0)
    {
        if (_closed || effect == null) return;

        // UV offset: difference between element position and capture origin.
        // The offscreen texture starts at captureOrigin; the element content sits
        // at (x - captureOriginX, y - captureOriginY) inside the texture.
        float uvOffX = x - captureOriginX;
        float uvOffY = y - captureOriginY;

        if (SimplifyGpuEffects)
        {
            _renderTarget.DrawCapturedTransition(0, x, y, w, h, 1.0f);
            return;
        }

        if (effect is Media.Effects.BlurEffect blur)
        {
            if (blur.Radius > 0.5)
            {
                // Blur content should be clipped to element's rounded corners.
                // x,y already contain the element's screen position (= Offset).
                bool hasCorners = cornerTL > 0 || cornerTR > 0 || cornerBR > 0 || cornerBL > 0;
                if (hasCorners)
                {
                    float maxR = Math.Max(Math.Max(cornerTL, cornerTR), Math.Max(cornerBR, cornerBL));
                    _renderTarget.PushRoundedRectClip(x, y, w, h, maxR, maxR);
                }
                _renderTarget.DrawBlurEffect(x, y, w, h, (float)blur.Radius, uvOffX, uvOffY);
                if (hasCorners)
                {
                    _renderTarget.PopClip();
                }
            }
        }
        else if (effect is Media.Effects.ElementBlurEffect elementBlur)
        {
            if (elementBlur.Radius > 0.5)
                _renderTarget.DrawBlurEffect(x, y, w, h, (float)elementBlur.Radius, uvOffX, uvOffY);
        }
        else if (effect is Media.Effects.DropShadowEffect shadow)
        {
            var color = shadow.Color;
            var effectiveAlpha = (color.A / 255f) * (float)shadow.Opacity;
            _renderTarget.DrawDropShadowEffect(x, y, w, h,
                (float)shadow.BlurRadius,
                (float)shadow.OffsetX,
                (float)shadow.OffsetY,
                color.R / 255f, color.G / 255f, color.B / 255f,
                effectiveAlpha,
                uvOffX, uvOffY,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.OuterGlowEffect glow)
        {
            var color = glow.GlowColor;
            _renderTarget.DrawOuterGlowEffect(x, y, w, h,
                (float)glow.EffectiveBlurRadius,
                color.R / 255f, color.G / 255f, color.B / 255f,
                (float)glow.Opacity, (float)glow.Intensity,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.InnerShadowEffect innerShadow)
        {
            var color = innerShadow.Color;
            _renderTarget.DrawInnerShadowEffect(x, y, w, h,
                (float)innerShadow.BlurRadius,
                (float)innerShadow.OffsetX,
                (float)innerShadow.OffsetY,
                color.R / 255f, color.G / 255f, color.B / 255f,
                (float)innerShadow.Opacity,
                cornerTL, cornerTR, cornerBR, cornerBL);
        }
        else if (effect is Media.Effects.EmbossEffect emboss)
        {
            _renderTarget.DrawEmbossEffect(x, y, w, h,
                (float)emboss.Amount,
                (float)emboss.LightDirectionX,
                (float)emboss.LightDirectionY,
                (float)emboss.Relief);
        }
        else if (effect is Media.Effects.ColorMatrixEffect colorMatrix)
        {
            var m = colorMatrix.Matrix;
            Span<float> matrixData = stackalloc float[20];
            matrixData[0] = m.M11; matrixData[1] = m.M12; matrixData[2] = m.M13; matrixData[3] = m.M14; matrixData[4] = m.M15;
            matrixData[5] = m.M21; matrixData[6] = m.M22; matrixData[7] = m.M23; matrixData[8] = m.M24; matrixData[9] = m.M25;
            matrixData[10] = m.M31; matrixData[11] = m.M32; matrixData[12] = m.M33; matrixData[13] = m.M34; matrixData[14] = m.M35;
            matrixData[15] = m.M41; matrixData[16] = m.M42; matrixData[17] = m.M43; matrixData[18] = m.M44; matrixData[19] = m.M45;
            _renderTarget.DrawColorMatrixEffect(x, y, w, h, matrixData);
        }
        else if (effect is Media.Effects.ShaderEffect shaderEffect)
        {
            var shaderBytecode = shaderEffect.PixelShader?.ShaderBytecode;
            if (shaderBytecode is { Length: > 0 })
            {
                _renderTarget.DrawShaderEffect(x, y, w, h,
                    shaderBytecode,
                    shaderEffect.BuildConstantBuffer());
            }
            else
            {
                _renderTarget.DrawBlurEffect(x, y, w, h, 0);
            }
        }
        else if (effect is Media.Effects.EffectGroup group)
        {
            // Apply the first active child effect
            var activeEffects = group.ActiveEffects;
            if (activeEffects.Count > 0)
            {
                ApplyElementEffect(activeEffects[0], x, y, w, h);
            }
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
            // LRU eviction: remove the least recently used half
            var toRemove = _brushCache
                .OrderBy(static kvp => kvp.Value.LastAccessSequence)
                .Take(_brushCache.Count / 2)
                .ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _brushCache.Remove(kvp.Key);
            }
        }

        if (_textFormatCache.Count > MaxTextFormatCacheSize)
        {
            // LRU eviction: remove least recently used half
            var toRemove = _textFormatCache
                .OrderBy(static kvp => kvp.Value.LastAccessSequence)
                .Take(_textFormatCache.Count / 2)
                .ToList();
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
                {
                    cached.LastAccessSequence = ++_brushCacheSequence;
                    return cached;
                }
                // Color changed — dispose old native brush and recreate
                cached.Dispose();
                _brushCache.Remove(brush);
            }

            // Pass sRGB values to native: D2D expects sRGB, and the direct D3D12
            // path converts to linear internally (SRGB RTV handles gamma).
            var nb = _context.CreateSolidBrush(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            nb.CachedColor = color;
            nb.LastAccessSequence = ++_brushCacheSequence;
            _brushCache[brush] = nb;
            return nb;
        }

        if (brush is LinearGradientBrush linear)
        {
            // Cache gradient brushes when bounding box is (0,0,0,0) — i.e. Absolute mapping
            // or when called without bounds. For RelativeToBoundingBox, bounds change per call.
            if (linear.MappingMode != BrushMappingMode.RelativeToBoundingBox &&
                _brushCache.TryGetValue(brush, out var cachedLinear))
            {
                cachedLinear.LastAccessSequence = ++_brushCacheSequence;
                return cachedLinear;
            }
            return CreateNativeLinearGradient(linear, bx, by, bw, bh);
        }

        if (brush is RadialGradientBrush radial)
        {
            if (radial.MappingMode != BrushMappingMode.RelativeToBoundingBox &&
                _brushCache.TryGetValue(brush, out var cachedRadial))
            {
                cachedRadial.LastAccessSequence = ++_brushCacheSequence;
                return cachedRadial;
            }
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
            // Pass sRGB values: D2D D2D1_GAMMA_2_2 expects sRGB, and the
            // software backend's InterpolateGradientStops handles sRGB↔linear.
            arr[off + 1] = s.Color.R / 255f;
            arr[off + 2] = s.Color.G / 255f;
            arr[off + 3] = s.Color.B / 255f;
            arr[off + 4] = s.Color.A / 255f;
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
        nb.LastAccessSequence = ++_brushCacheSequence;

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
        nb.LastAccessSequence = ++_brushCacheSequence;

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
            cached.LastAccessSequence = ++_textFormatCacheSequence;
            return cached;
        }

        var format = _context.CreateTextFormat(fontFamily, (float)fontSize, fontWeight, fontStyle);
        if (format != null)
        {
            format.LastAccessSequence = ++_textFormatCacheSequence;
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RenderTargetDrawingContext] Failed to create bitmap: {ex.Message}");
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
