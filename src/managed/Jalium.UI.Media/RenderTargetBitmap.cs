using Jalium.UI.Media;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Converts a visual object into a bitmap using Jalium's software rendering path.
/// </summary>
/// <remarks>
/// The visual is rasterized at the bitmap's origin: its own arrange offset is NOT applied, so
/// rendering a deeply nested element does not push it off the edge of a small target. Offsets
/// INSIDE the subtree are applied normally. See <see cref="SoftwareDrawingContext"/> for the
/// primitives this path supports and the ones it deliberately skips (text, images, backdrops).
/// </remarks>
public sealed partial class RenderTargetBitmap : BitmapSource
{
    private readonly byte[] _pixelBuffer;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly int _stride;
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
        _dpiX = double.IsFinite(dpiX) && dpiX > 0 ? dpiX : 96.0;
        _dpiY = double.IsFinite(dpiY) && dpiY > 0 ? dpiY : 96.0;
        _stride = PixelBufferLayout.GetMinimumStride(pixelWidth);
        _pixelBuffer = new byte[
            PixelBufferLayout.GetRequiredByteCount(pixelWidth, pixelHeight, _stride)]; // BGRA32
    }

    /// <summary>
    /// Renders a visual to this bitmap.
    /// </summary>
    /// <param name="visual">The visual to render.</param>
    public void Render(Visual visual)
    {
        if (visual == null) throw new ArgumentNullException(nameof(visual));

        var drawingContext = new SoftwareDrawingContext(this);

        // Visual.Render is the public render entry point: it invokes the element's OnRender and
        // then recurses into the subtree. Children's arrange offsets, opacity and render
        // transforms are NOT baked into the recursion — Visual.RenderChildVisualInline applies
        // them by driving the drawing context's IOffsetDrawingContext / IOpacityDrawingContext /
        // ITransformDrawingContext facets, and layout clips travel through IClipDrawingContext.
        // SoftwareDrawingContext implements all four; a context that does not would receive an
        // untransformed, unclipped flattening of the subtree.
        visual.Render(drawingContext);

        drawingContext.Close();
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
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var startX = sourceRect.X;
        var startY = sourceRect.Y;
        var width = sourceRect.Width == 0 ? _pixelWidth : sourceRect.Width;
        var height = sourceRect.Height == 0 ? _pixelHeight : sourceRect.Height;

        if (startX < 0 || startY < 0 || width < 0 || height < 0 ||
            startX > _pixelWidth - width || startY > _pixelHeight - height)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect), "Source rectangle exceeds bitmap dimensions.");
        }

        var rowBytes = checked(width * PixelBufferLayout.BytesPerPixel);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                "Destination stride is smaller than the requested row width.");
        }

        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var requiredBytes = height == 0
            ? (long)offset
            : (long)offset + ((long)(height - 1) * stride) + rowBytes;
        if (requiredBytes > pixels.Length)
            throw new ArgumentException("Destination pixel buffer is too small.", nameof(pixels));

        for (var y = 0; y < height; y++)
        {
            var srcOffset = checked(((startY + y) * _stride) +
                                    (startX * PixelBufferLayout.BytesPerPixel));
            var dstOffset = checked(offset + (y * stride));
            Array.Copy(_pixelBuffer, srcOffset, pixels, dstOffset, rowBytes);
        }
    }

    /// <summary>
    /// Gets the raw pixel buffer.
    /// </summary>
    internal byte[] GetPixelBuffer() => _pixelBuffer;

    /// <summary>
    /// Gets the row pitch of <see cref="GetPixelBuffer"/> in bytes.
    /// </summary>
    internal int Stride => _stride;
}
