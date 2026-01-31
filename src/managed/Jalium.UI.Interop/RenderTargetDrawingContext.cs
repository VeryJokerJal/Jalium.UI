using System.Linq;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// A DrawingContext implementation that renders to a RenderTarget.
/// </summary>
public sealed class RenderTargetDrawingContext : DrawingContext, IOffsetDrawingContext
{
    private const int MaxBrushCacheSize = 256;
    private const int MaxTextFormatCacheSize = 64;
    private const int MaxBitmapCacheSize = 128;

    private readonly RenderTarget _renderTarget;
    private readonly RenderContext _context;
    private readonly Dictionary<Brush, NativeBrush> _brushCache = new();
    private readonly Dictionary<string, NativeTextFormat> _textFormatCache = new();
    private readonly Dictionary<ImageSource, NativeBitmap> _bitmapCache = new();
    private readonly Stack<DrawingState> _stateStack = new();
    private bool _closed;

    /// <summary>
    /// Gets the underlying render target.
    /// </summary>
    public RenderTarget RenderTarget => _renderTarget;

    /// <summary>
    /// Gets or sets the current transform offset for child rendering.
    /// </summary>
    public Point Offset { get; set; }

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

        // Round to pixel boundaries to prevent sub-pixel jittering
        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush);
            if (nativeBrush != null)
            {
                _renderTarget.FillRectangle(x, y, width, height, nativeBrush);
            }
        }

        // Stroke
        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRectangle(x, y, width, height, strokeBrush, (float)pen.Thickness);
            }
        }
    }

    /// <inheritdoc />
    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
    {
        if (_closed) return;

        // Round to pixel boundaries to prevent sub-pixel jittering
        var x = (float)Math.Round(rectangle.X + Offset.X);
        var y = (float)Math.Round(rectangle.Y + Offset.Y);
        var width = (float)rectangle.Width;
        var height = (float)rectangle.Height;
        var rx = (float)radiusX;
        var ry = (float)radiusY;

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush);
            if (nativeBrush != null)
            {
                _renderTarget.FillRoundedRectangle(x, y, width, height, rx, ry, nativeBrush);
            }
        }

        // Stroke
        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush);
            if (strokeBrush != null)
            {
                _renderTarget.DrawRoundedRectangle(x, y, width, height, rx, ry, strokeBrush, (float)pen.Thickness);
            }
        }
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

        // Fill
        if (brush != null)
        {
            var nativeBrush = GetNativeBrush(brush);
            if (nativeBrush != null)
            {
                _renderTarget.FillEllipse(cx, cy, rx, ry, nativeBrush);
            }
        }

        // Stroke
        if (pen?.Brush != null)
        {
            var strokeBrush = GetNativeBrush(pen.Brush);
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

        var format = GetTextFormat(formattedText.FontFamily, formattedText.FontSize);
        if (format == null) return;

        // Apply trimming if set
        var trimming = formattedText.Trimming switch
        {
            TextTrimming.CharacterEllipsis => TextTrimmingMode.CharacterEllipsis,
            TextTrimming.WordEllipsis => TextTrimmingMode.WordEllipsis,
            _ => TextTrimmingMode.None
        };
        format.SetTrimming(trimming);

        // Round text coordinates to pixel boundaries to prevent sub-pixel jittering
        // DirectWrite renders text using sub-pixel positioning which can cause visual instability
        var x = (float)Math.Round(origin.X + Offset.X);
        var y = (float)Math.Round(origin.Y + Offset.Y);
        var width = (float)formattedText.MaxTextWidth;
        var height = (float)formattedText.MaxTextHeight;

        if (double.IsInfinity(width) || double.IsNaN(width)) width = 10000;
        if (double.IsInfinity(height) || double.IsNaN(height)) height = 10000;

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
        else if (geometry is PathGeometry pathGeom)
        {
            DrawPathGeometry(brush, pen, pathGeom);
        }
    }

    private void DrawPathGeometry(Brush? brush, Pen? pen, PathGeometry pathGeom)
    {
        // Draw path figures by rendering each segment
        // Note: This is a simplified implementation; full path rendering would require native path APIs
        foreach (var figure in pathGeom.Figures)
        {
            var currentPoint = figure.StartPoint;

            foreach (var segment in figure.Segments)
            {
                if (!segment.IsStroked && pen != null) continue;

                if (segment is LineSegment lineSeg)
                {
                    if (pen != null)
                    {
                        DrawLine(pen, currentPoint, lineSeg.Point);
                    }
                    currentPoint = lineSeg.Point;
                }
                else if (segment is PolyLineSegment polyLine)
                {
                    foreach (var point in polyLine.Points)
                    {
                        if (pen != null)
                        {
                            DrawLine(pen, currentPoint, point);
                        }
                        currentPoint = point;
                    }
                }
                else if (segment is BezierSegment bezier)
                {
                    // Approximate bezier with line segments
                    if (pen != null)
                    {
                        DrawBezierApprox(pen, currentPoint, bezier.Point1, bezier.Point2, bezier.Point3);
                    }
                    currentPoint = bezier.Point3;
                }
                else if (segment is QuadraticBezierSegment quadBezier)
                {
                    // Approximate quadratic bezier with line segments
                    if (pen != null)
                    {
                        DrawQuadBezierApprox(pen, currentPoint, quadBezier.Point1, quadBezier.Point2);
                    }
                    currentPoint = quadBezier.Point2;
                }
                else if (segment is ArcSegment arc)
                {
                    // Approximate arc with line segments
                    if (pen != null)
                    {
                        DrawArcApprox(pen, currentPoint, arc);
                    }
                    currentPoint = arc.Point;
                }
            }

            // Close the figure if needed
            if (figure.IsClosed && pen != null)
            {
                DrawLine(pen, currentPoint, figure.StartPoint);
            }
        }
    }

    private void DrawBezierApprox(Pen pen, Point p0, Point p1, Point p2, Point p3)
    {
        const int segments = 16;
        var prevPoint = p0;

        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1 - t;

            // Cubic bezier formula
            double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;

            var point = new Point(x, y);
            DrawLine(pen, prevPoint, point);
            prevPoint = point;
        }
    }

    private void DrawQuadBezierApprox(Pen pen, Point p0, Point p1, Point p2)
    {
        const int segments = 12;
        var prevPoint = p0;

        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            double u = 1 - t;

            // Quadratic bezier formula
            double x = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
            double y = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;

            var point = new Point(x, y);
            DrawLine(pen, prevPoint, point);
            prevPoint = point;
        }
    }

    private void DrawArcApprox(Pen pen, Point start, ArcSegment arc)
    {
        // Simplified arc approximation using line segments
        const int segments = 16;
        var prevPoint = start;

        var dx = arc.Point.X - start.X;
        var dy = arc.Point.Y - start.Y;

        for (int i = 1; i <= segments; i++)
        {
            double t = i / (double)segments;
            var point = new Point(start.X + dx * t, start.Y + dy * t);
            DrawLine(pen, prevPoint, point);
            prevPoint = point;
        }
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
            (float)cornerRadius.TopLeft,
            (float)cornerRadius.TopRight,
            (float)cornerRadius.BottomRight,
            (float)cornerRadius.BottomLeft);
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

        // Store current offset
        _stateStack.Push(new DrawingState(DrawingStateType.Transform, Offset));

        // Apply transform offset (simplified - only translation for now)
        if (transform is TranslateTransform translate)
        {
            Offset = new Point(Offset.X + translate.X, Offset.Y + translate.Y);
        }
    }

    /// <inheritdoc />
    public override void PushClip(Geometry clipGeometry)
    {
        if (_closed || clipGeometry == null) return;

        var bounds = clipGeometry.Bounds;
        // Round to pixel boundaries to prevent sub-pixel jittering
        _renderTarget.PushClip(
            (float)Math.Round(bounds.X + Offset.X),
            (float)Math.Round(bounds.Y + Offset.Y),
            (float)bounds.Width,
            (float)bounds.Height);

        _stateStack.Push(new DrawingState(DrawingStateType.Clip, Point.Zero));
    }

    /// <inheritdoc />
    public override void PushOpacity(double opacity)
    {
        if (_closed) return;

        _renderTarget.PushOpacity((float)opacity);
        _stateStack.Push(new DrawingState(DrawingStateType.Opacity, Point.Zero));
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
            case DrawingStateType.Clip:
                _renderTarget.PopClip();
                break;
            case DrawingStateType.Opacity:
                _renderTarget.PopOpacity();
                break;
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

        foreach (var bitmap in _bitmapCache.Values)
        {
            bitmap.Dispose();
        }
        _bitmapCache.Clear();
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

        if (_bitmapCache.Count > MaxBitmapCacheSize)
        {
            var toRemove = _bitmapCache.Take(_bitmapCache.Count / 2).ToList();
            foreach (var kvp in toRemove)
            {
                kvp.Value.Dispose();
                _bitmapCache.Remove(kvp.Key);
            }
        }
    }

    private NativeBrush? GetNativeBrush(Brush brush)
    {
        if (brush == null) return null;

        if (_brushCache.TryGetValue(brush, out var cached))
        {
            return cached;
        }

        NativeBrush? nativeBrush = null;

        if (brush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            nativeBrush = _context.CreateSolidBrush(color.ScR, color.ScG, color.ScB, color.ScA);
        }

        if (nativeBrush != null)
        {
            _brushCache[brush] = nativeBrush;
        }

        return nativeBrush;
    }

    private NativeTextFormat? GetTextFormat(string fontFamily, double fontSize)
    {
        var key = $"{fontFamily}_{fontSize}";

        if (_textFormatCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var format = _context.CreateTextFormat(fontFamily, (float)fontSize);
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
            return cached;
        }

        NativeBitmap? nativeBitmap = null;

        if (imageSource is BitmapImage bitmapImage && bitmapImage.ImageData != null)
        {
            try
            {
                nativeBitmap = _context.CreateBitmap(bitmapImage.ImageData);
            }
            catch
            {
                // Failed to create bitmap, return null
            }
        }

        if (nativeBitmap != null)
        {
            _bitmapCache[imageSource] = nativeBitmap;
        }

        return nativeBitmap;
    }

    private enum DrawingStateType
    {
        Transform,
        Clip,
        Opacity
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
