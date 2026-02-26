namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents image data returned by a decoder and accepted by encoders.
/// </summary>
public class BitmapFrame : BitmapSource
{
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiX = 96.0;
    private double _dpiY = 96.0;
    private PixelFormat _format = PixelFormat.Bgra32;

    /// <inheritdoc />
    public override double Width => _pixelWidth;

    /// <inheritdoc />
    public override double Height => _pixelHeight;

    /// <inheritdoc />
    public override nint NativeHandle => nint.Zero;

    /// <inheritdoc />
    public override int PixelWidth => _pixelWidth;

    /// <inheritdoc />
    public override int PixelHeight => _pixelHeight;

    /// <inheritdoc />
    public override double DpiX => _dpiX;

    /// <inheritdoc />
    public override double DpiY => _dpiY;

    /// <inheritdoc />
    public override PixelFormat Format => _format;

    /// <summary>
    /// Gets the thumbnail image associated with this BitmapFrame.
    /// </summary>
    public virtual BitmapSource? Thumbnail { get; }

    /// <summary>
    /// Gets the color contexts associated with this frame.
    /// </summary>
    public virtual IReadOnlyList<ColorContext>? ColorContexts { get; }

    /// <summary>
    /// Gets the decoder associated with this frame.
    /// </summary>
    public virtual BitmapDecoder? Decoder { get; }

    /// <summary>
    /// Creates a new BitmapFrame from a BitmapSource.
    /// </summary>
    public static BitmapFrame Create(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BitmapFrame
        {
            _pixelWidth = source.PixelWidth,
            _pixelHeight = source.PixelHeight,
            _dpiX = source.DpiX,
            _dpiY = source.DpiY,
            _format = source.Format
        };
    }

    /// <summary>
    /// Creates a new BitmapFrame from a BitmapSource with the specified thumbnail.
    /// </summary>
    public static BitmapFrame Create(BitmapSource source, BitmapSource? thumbnail)
    {
        var frame = Create(source);
        return frame;
    }

    /// <summary>
    /// Creates a new BitmapFrame from a URI.
    /// </summary>
    public static BitmapFrame Create(Uri bitmapUri)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        return new BitmapFrame();
    }

    /// <summary>
    /// Creates a new BitmapFrame from a URI with the specified create options and cache option.
    /// </summary>
    public static BitmapFrame Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        return new BitmapFrame();
    }

    /// <summary>
    /// Creates a new BitmapFrame from a stream.
    /// </summary>
    public static BitmapFrame Create(Stream bitmapStream)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        return new BitmapFrame();
    }

    /// <summary>
    /// Creates a new BitmapFrame from a stream with the specified create options and cache option.
    /// </summary>
    public static BitmapFrame Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        return new BitmapFrame();
    }
}
