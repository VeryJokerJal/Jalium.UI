namespace Jalium.UI.Media;

/// <summary>
/// Converts a Visual object into a bitmap.
/// </summary>
public sealed class RenderTargetBitmap : BitmapSource
{
    private byte[] _pixelBuffer;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly double _dpiX;
    private readonly double _dpiY;

    /// <summary>
    /// Gets the width of the bitmap in pixels.
    /// </summary>
    public override double Width => _pixelWidth;

    /// <summary>
    /// Gets the height of the bitmap in pixels.
    /// </summary>
    public override double Height => _pixelHeight;

    /// <summary>
    /// Gets the pixel width of the bitmap.
    /// </summary>
    public override int PixelWidth => _pixelWidth;

    /// <summary>
    /// Gets the pixel height of the bitmap.
    /// </summary>
    public override int PixelHeight => _pixelHeight;

    /// <summary>
    /// Gets the horizontal DPI of the bitmap.
    /// </summary>
    public override double DpiX => _dpiX;

    /// <summary>
    /// Gets the vertical DPI of the bitmap.
    /// </summary>
    public override double DpiY => _dpiY;

    /// <summary>
    /// Gets the pixel format (always BGRA32).
    /// </summary>
    public override PixelFormat Format => PixelFormat.Bgra32;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public override nint NativeHandle { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderTargetBitmap"/> class.
    /// </summary>
    /// <param name="pixelWidth">The width in pixels.</param>
    /// <param name="pixelHeight">The height in pixels.</param>
    /// <param name="dpiX">The horizontal DPI.</param>
    /// <param name="dpiY">The vertical DPI.</param>
    /// <param name="pixelFormat">The pixel format (ignored, always BGRA32).</param>
    public RenderTargetBitmap(int pixelWidth, int pixelHeight, double dpiX, double dpiY, PixelFormat pixelFormat)
    {
        if (pixelWidth <= 0) throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        if (pixelHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelHeight));

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _dpiX = dpiX > 0 ? dpiX : 96.0;
        _dpiY = dpiY > 0 ? dpiY : 96.0;
        _pixelBuffer = new byte[pixelWidth * pixelHeight * 4]; // BGRA32
    }

    /// <summary>
    /// Renders a visual to this bitmap.
    /// </summary>
    /// <param name="visual">The visual to render.</param>
    public void Render(Visual visual)
    {
        if (visual == null) throw new ArgumentNullException(nameof(visual));

        // Create an off-screen drawing context
        var drawingContext = new RenderTargetDrawingContext(this);

        // Render the visual hierarchy
        RenderVisual(visual, drawingContext);

        drawingContext.Close();
    }

    private void RenderVisual(Visual visual, DrawingContext drawingContext)
    {
        // Note: OnRender is protected, so we can't call it directly from here.
        // This is a simplified implementation that just traverses the visual tree.
        // For full rendering support, the Visual would need to expose an internal
        // method or this class would need to be in the same assembly.

        // Apply transforms if available (cast object to Transform)
        Transform? transform = null;
        if (visual is UIElement uiElement && uiElement.RenderTransform is Transform t)
        {
            transform = t;
            drawingContext.PushTransform(transform);
        }

        // Render children
        for (var i = 0; i < visual.VisualChildrenCount; i++)
        {
            var child = visual.GetVisualChild(i);
            if (child != null)
            {
                RenderVisual(child, drawingContext);
            }
        }

        // Pop transform
        if (transform != null)
        {
            drawingContext.Pop();
        }
    }

    /// <summary>
    /// Clears the render target to a specified color.
    /// </summary>
    /// <param name="color">The color to clear to.</param>
    public void Clear(Color color)
    {
        for (var i = 0; i < _pixelBuffer.Length; i += 4)
        {
            _pixelBuffer[i] = color.B;
            _pixelBuffer[i + 1] = color.G;
            _pixelBuffer[i + 2] = color.R;
            _pixelBuffer[i + 3] = color.A;
        }
    }

    /// <summary>
    /// Copies the pixel data to an array.
    /// </summary>
    public new void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        if (pixels == null) throw new ArgumentNullException(nameof(pixels));

        var srcStride = _pixelWidth * 4;
        var startX = sourceRect.X;
        var startY = sourceRect.Y;
        var width = sourceRect.Width == 0 ? _pixelWidth : sourceRect.Width;
        var height = sourceRect.Height == 0 ? _pixelHeight : sourceRect.Height;

        for (var y = 0; y < height; y++)
        {
            var srcOffset = ((startY + y) * srcStride) + (startX * 4);
            var dstOffset = offset + (y * stride);
            Array.Copy(_pixelBuffer, srcOffset, pixels, dstOffset, Math.Min(width * 4, stride));
        }
    }

    /// <summary>
    /// Gets the raw pixel buffer.
    /// </summary>
    internal byte[] GetPixelBuffer() => _pixelBuffer;

    /// <summary>
    /// Sets a pixel at the specified coordinates.
    /// </summary>
    internal void SetPixel(int x, int y, Color color)
    {
        if (x < 0 || x >= _pixelWidth || y < 0 || y >= _pixelHeight) return;

        var offset = (y * _pixelWidth + x) * 4;
        _pixelBuffer[offset] = color.B;
        _pixelBuffer[offset + 1] = color.G;
        _pixelBuffer[offset + 2] = color.R;
        _pixelBuffer[offset + 3] = color.A;
    }
}

/// <summary>
/// Base class for bitmap sources.
/// </summary>
public abstract class BitmapSource : ImageSource
{
    /// <summary>
    /// Gets the width of the bitmap in pixels.
    /// </summary>
    public virtual int PixelWidth => (int)Width;

    /// <summary>
    /// Gets the height of the bitmap in pixels.
    /// </summary>
    public virtual int PixelHeight => (int)Height;

    /// <summary>
    /// Gets the horizontal DPI of the bitmap.
    /// </summary>
    public virtual double DpiX => 96.0;

    /// <summary>
    /// Gets the vertical DPI of the bitmap.
    /// </summary>
    public virtual double DpiY => 96.0;

    /// <summary>
    /// Gets the pixel format of the bitmap.
    /// </summary>
    public virtual PixelFormat Format => PixelFormat.Bgra32;

    /// <summary>
    /// Gets the bitmap palette, or null if no palette is defined.
    /// </summary>
    public virtual Imaging.BitmapPalette? Palette => null;

    /// <summary>
    /// Copies pixels from the bitmap source to an array.
    /// </summary>
    public virtual void CopyPixels(byte[] pixels, int stride, int offset)
    {
        CopyPixels(new Int32Rect(0, 0, (int)Width, (int)Height), pixels, stride, offset);
    }

    /// <summary>
    /// Copies pixels from a specific rectangle to an array.
    /// </summary>
    public virtual void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        // Default implementation - override in derived classes
    }
}

/// <summary>
/// Represents a rectangle with integer coordinates.
/// </summary>
public readonly struct Int32Rect : IEquatable<Int32Rect>
{
    /// <summary>
    /// Gets an empty rectangle.
    /// </summary>
    public static Int32Rect Empty { get; } = new(0, 0, 0, 0);

    /// <summary>
    /// Gets the X coordinate.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Gets the Y coordinate.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Gets the width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets whether the rectangle is empty.
    /// </summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// Initializes a new Int32Rect.
    /// </summary>
    public Int32Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Equals(Int32Rect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is Int32Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(Int32Rect left, Int32Rect right) => left.Equals(right);
    public static bool operator !=(Int32Rect left, Int32Rect right) => !left.Equals(right);
}

/// <summary>
/// Specifies the pixel format of a bitmap.
/// </summary>
public enum PixelFormat
{
    /// <summary>
    /// 32-bit BGRA format.
    /// </summary>
    Bgra32,

    /// <summary>
    /// 32-bit RGBA format.
    /// </summary>
    Rgba32,

    /// <summary>
    /// 32-bit RGB format (with alpha ignored).
    /// </summary>
    Rgb32,

    /// <summary>
    /// 24-bit BGR format.
    /// </summary>
    Bgr24,

    /// <summary>
    /// 24-bit RGB format.
    /// </summary>
    Rgb24,

    /// <summary>
    /// 8-bit grayscale.
    /// </summary>
    Gray8,

    /// <summary>
    /// 16-bit grayscale.
    /// </summary>
    Gray16,

    /// <summary>
    /// Pre-multiplied 32-bit BGRA format.
    /// </summary>
    Pbgra32
}

/// <summary>
/// Drawing context for RenderTargetBitmap.
/// </summary>
internal sealed class RenderTargetDrawingContext : DrawingContext
{
    private readonly RenderTargetBitmap _target;
    private readonly Stack<Matrix> _transformStack = new();
    private Matrix _currentTransform = Matrix.Identity;

    public RenderTargetDrawingContext(RenderTargetBitmap target)
    {
        _target = target;
    }

    public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect)
    {
        // Apply current transform
        var transformedRect = TransformRect(rect);

        // Fill rectangle
        if (brush is SolidColorBrush solidBrush)
        {
            FillRect(transformedRect, solidBrush.Color);
        }

        // Draw border
        if (pen?.Brush is SolidColorBrush strokeBrush)
        {
            DrawRectOutline(transformedRect, strokeBrush.Color, pen.Thickness);
        }
    }

    public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double radiusX, double radiusY)
    {
        // Simplified: draw as regular rectangle
        DrawRectangle(brush, pen, rect);
    }

    public override void DrawLine(Pen pen, Point point0, Point point1)
    {
        if (pen?.Brush is not SolidColorBrush brush) return;

        var p0 = TransformPoint(point0);
        var p1 = TransformPoint(point1);

        DrawLineBresenham((int)p0.X, (int)p0.Y, (int)p1.X, (int)p1.Y, brush.Color);
    }

    public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
    {
        var transformedCenter = TransformPoint(center);

        if (brush is SolidColorBrush fillBrush)
        {
            FillEllipse(transformedCenter, radiusX, radiusY, fillBrush.Color);
        }

        if (pen?.Brush is SolidColorBrush strokeBrush)
        {
            DrawEllipseOutline(transformedCenter, radiusX, radiusY, strokeBrush.Color);
        }
    }

    public override void DrawText(FormattedText formattedText, Point origin)
    {
        // Text rendering would require font rasterization
        // This is a placeholder - in production, use a font library
    }

    public override void DrawImage(ImageSource imageSource, Rect rect)
    {
        // Image compositing would require proper blending
        // This is a placeholder
    }

    public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
    {
        // Geometry rendering - draw the bounding rect as approximation
        var bounds = geometry.Bounds;
        DrawRectangle(brush, pen, bounds);
    }

    public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
    {
        // Backdrop effects are not supported in software rendering
        // This is a placeholder
    }

    public override void PushClip(Geometry clipGeometry)
    {
        // Clipping would require proper clip region management
        // This is a placeholder - store clip geometry for later use
    }

    public override void PushOpacity(double opacity)
    {
        // Opacity would require proper alpha blending
        // This is a placeholder
    }

    public override void PushTransform(Transform transform)
    {
        _transformStack.Push(_currentTransform);
        _currentTransform = Matrix.Multiply(_currentTransform, transform.Value);
    }

    public override void Pop()
    {
        if (_transformStack.Count > 0)
        {
            _currentTransform = _transformStack.Pop();
        }
    }

    public override void Close()
    {
        // Finalize rendering
    }

    private Rect TransformRect(Rect rect)
    {
        var topLeft = TransformPoint(new Point(rect.X, rect.Y));
        var bottomRight = TransformPoint(new Point(rect.Right, rect.Bottom));
        return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }

    private Point TransformPoint(Point point)
    {
        return _currentTransform.Transform(point);
    }

    private void FillRect(Rect rect, Color color)
    {
        var x1 = Math.Max(0, (int)rect.X);
        var y1 = Math.Max(0, (int)rect.Y);
        var x2 = Math.Min(_target.PixelWidth, (int)rect.Right);
        var y2 = Math.Min(_target.PixelHeight, (int)rect.Bottom);

        for (var y = y1; y < y2; y++)
        {
            for (var x = x1; x < x2; x++)
            {
                _target.SetPixel(x, y, color);
            }
        }
    }

    private void DrawRectOutline(Rect rect, Color color, double thickness)
    {
        var t = (int)Math.Max(1, thickness);

        // Top
        FillRect(new Rect(rect.X, rect.Y, rect.Width, t), color);
        // Bottom
        FillRect(new Rect(rect.X, rect.Bottom - t, rect.Width, t), color);
        // Left
        FillRect(new Rect(rect.X, rect.Y, t, rect.Height), color);
        // Right
        FillRect(new Rect(rect.Right - t, rect.Y, t, rect.Height), color);
    }

    private void DrawLineBresenham(int x0, int y0, int x1, int y1, Color color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            _target.SetPixel(x0, y0, color);

            if (x0 == x1 && y0 == y1) break;

            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private void FillEllipse(Point center, double rx, double ry, Color color)
    {
        var cx = (int)center.X;
        var cy = (int)center.Y;
        var irx = (int)rx;
        var iry = (int)ry;

        for (var y = -iry; y <= iry; y++)
        {
            for (var x = -irx; x <= irx; x++)
            {
                if ((x * x * iry * iry + y * y * irx * irx) <= irx * irx * iry * iry)
                {
                    _target.SetPixel(cx + x, cy + y, color);
                }
            }
        }
    }

    private void DrawEllipseOutline(Point center, double rx, double ry, Color color)
    {
        // Midpoint ellipse algorithm
        var cx = (int)center.X;
        var cy = (int)center.Y;
        var a = (int)rx;
        var b = (int)ry;

        var a2 = a * a;
        var b2 = b * b;
        var fa2 = 4 * a2;
        var fb2 = 4 * b2;

        // First region
        var x = 0;
        var y = b;
        var sigma = 2 * b2 + a2 * (1 - 2 * b);

        while (b2 * x <= a2 * y)
        {
            SetEllipsePoints(cx, cy, x, y, color);

            if (sigma >= 0)
            {
                sigma += fa2 * (1 - y);
                y--;
            }
            sigma += b2 * (4 * x + 6);
            x++;
        }

        // Second region
        x = a;
        y = 0;
        sigma = 2 * a2 + b2 * (1 - 2 * a);

        while (a2 * y <= b2 * x)
        {
            SetEllipsePoints(cx, cy, x, y, color);

            if (sigma >= 0)
            {
                sigma += fb2 * (1 - x);
                x--;
            }
            sigma += a2 * (4 * y + 6);
            y++;
        }
    }

    private void SetEllipsePoints(int cx, int cy, int x, int y, Color color)
    {
        _target.SetPixel(cx + x, cy + y, color);
        _target.SetPixel(cx - x, cy + y, color);
        _target.SetPixel(cx + x, cy - y, color);
        _target.SetPixel(cx - x, cy - y, color);
    }
}
