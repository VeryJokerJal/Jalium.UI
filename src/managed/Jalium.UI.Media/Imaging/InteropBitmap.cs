namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides interop support for unmanaged bitmap surfaces. This class wraps a shared
/// memory section (HBITMAP or similar) and allows WPF-style rendering of native bitmaps.
/// </summary>
public sealed class InteropBitmap : BitmapSource
{
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiX = 96.0;
    private double _dpiY = 96.0;

    /// <summary>
    /// Initializes a new instance of the <see cref="InteropBitmap"/> class.
    /// This constructor is internal; instances are created through
    /// <see cref="System.Windows.Interop.Imaging"/>-style factory methods.
    /// </summary>
    internal InteropBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InteropBitmap"/> class
    /// with specified dimensions.
    /// </summary>
    internal InteropBitmap(int pixelWidth, int pixelHeight, double dpiX = 96.0, double dpiY = 96.0)
    {
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _dpiX = dpiX;
        _dpiY = dpiY;
    }

    /// <inheritdoc />
    public override double Width => _pixelWidth;

    /// <inheritdoc />
    public override double Height => _pixelHeight;

    /// <inheritdoc />
    public override int PixelWidth => _pixelWidth;

    /// <inheritdoc />
    public override int PixelHeight => _pixelHeight;

    /// <inheritdoc />
    public override double DpiX => _dpiX;

    /// <inheritdoc />
    public override double DpiY => _dpiY;

    /// <inheritdoc />
    public override PixelFormat Format => PixelFormat.Bgra32;

    /// <inheritdoc />
    public override nint NativeHandle => nint.Zero;

    /// <summary>
    /// Requests a re-render of the entire bitmap surface. Call this method after the
    /// unmanaged bitmap content has changed and the UI needs to be updated.
    /// </summary>
    public void Invalidate()
    {
        // Trigger a full re-render of the bitmap surface.
        // In a full implementation, this would notify the composition engine
        // that the shared surface has changed.
    }

    /// <summary>
    /// Requests a re-render of a specific rectangular region of the bitmap surface.
    /// </summary>
    /// <param name="sourceRect">The dirty rectangle to invalidate. Only this region
    /// will be re-composited, which can improve performance for partial updates.</param>
    public void Invalidate(Int32Rect sourceRect)
    {
        // Trigger a partial re-render limited to the specified rectangle.
        // In a full implementation, this would mark only the dirty region
        // for re-composition.
    }
}
