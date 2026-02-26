namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Crops a BitmapSource to a specified rectangle.
/// </summary>
public sealed class CroppedBitmap : BitmapSource
{
    private BitmapSource? _source;
    private Int32Rect _sourceRect;

    /// <summary>
    /// Initializes a new instance of the <see cref="CroppedBitmap"/> class.
    /// </summary>
    public CroppedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CroppedBitmap"/> class with the specified source and rectangle.
    /// </summary>
    public CroppedBitmap(BitmapSource source, Int32Rect sourceRect)
    {
        _source = source;
        _sourceRect = sourceRect;
    }

    /// <summary>
    /// Gets or sets the source of the cropped bitmap.
    /// </summary>
    public BitmapSource? Source
    {
        get => _source;
        set => _source = value;
    }

    /// <summary>
    /// Gets or sets the rectangular area that the bitmap is cropped to.
    /// </summary>
    public Int32Rect SourceRect
    {
        get => _sourceRect;
        set => _sourceRect = value;
    }

    /// <inheritdoc />
    public override double Width => _sourceRect.Width > 0 ? _sourceRect.Width : (_source?.Width ?? 0);

    /// <inheritdoc />
    public override double Height => _sourceRect.Height > 0 ? _sourceRect.Height : (_source?.Height ?? 0);

    /// <inheritdoc />
    public override nint NativeHandle => _source?.NativeHandle ?? nint.Zero;

    /// <inheritdoc />
    public override int PixelWidth => _sourceRect.Width > 0 ? _sourceRect.Width : (_source?.PixelWidth ?? 0);

    /// <inheritdoc />
    public override int PixelHeight => _sourceRect.Height > 0 ? _sourceRect.Height : (_source?.PixelHeight ?? 0);

    /// <inheritdoc />
    public override double DpiX => _source?.DpiX ?? 96.0;

    /// <inheritdoc />
    public override double DpiY => _source?.DpiY ?? 96.0;

    /// <inheritdoc />
    public override PixelFormat Format => _source?.Format ?? PixelFormat.Bgra32;
}

/// <summary>
/// Provides pixel format conversion for a BitmapSource.
/// </summary>
public sealed class FormatConvertedBitmap : BitmapSource
{
    private BitmapSource? _source;
    private PixelFormat _destinationFormat;
    private double _alphaThreshold;
    private BitmapPalette? _destinationPalette;

    /// <summary>
    /// Initializes a new instance of the <see cref="FormatConvertedBitmap"/> class.
    /// </summary>
    public FormatConvertedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance with source, destination format, palette, and alpha threshold.
    /// </summary>
    public FormatConvertedBitmap(BitmapSource source, PixelFormat destinationFormat, BitmapPalette? destinationPalette, double alphaThreshold)
    {
        _source = source;
        _destinationFormat = destinationFormat;
        _destinationPalette = destinationPalette;
        _alphaThreshold = alphaThreshold;
    }

    /// <summary>
    /// Gets or sets the source bitmap.
    /// </summary>
    public BitmapSource? Source
    {
        get => _source;
        set => _source = value;
    }

    /// <summary>
    /// Gets or sets the destination pixel format.
    /// </summary>
    public PixelFormat DestinationFormat
    {
        get => _destinationFormat;
        set => _destinationFormat = value;
    }

    /// <summary>
    /// Gets or sets the destination palette.
    /// </summary>
    public BitmapPalette? DestinationPalette
    {
        get => _destinationPalette;
        set => _destinationPalette = value;
    }

    /// <summary>
    /// Gets or sets the alpha threshold.
    /// </summary>
    public double AlphaThreshold
    {
        get => _alphaThreshold;
        set => _alphaThreshold = value;
    }

    /// <inheritdoc />
    public override double Width => _source?.Width ?? 0;

    /// <inheritdoc />
    public override double Height => _source?.Height ?? 0;

    /// <inheritdoc />
    public override nint NativeHandle => _source?.NativeHandle ?? nint.Zero;

    /// <inheritdoc />
    public override int PixelWidth => _source?.PixelWidth ?? 0;

    /// <inheritdoc />
    public override int PixelHeight => _source?.PixelHeight ?? 0;

    /// <inheritdoc />
    public override double DpiX => _source?.DpiX ?? 96.0;

    /// <inheritdoc />
    public override double DpiY => _source?.DpiY ?? 96.0;

    /// <inheritdoc />
    public override PixelFormat Format => _destinationFormat;
}

/// <summary>
/// Scales and rotates a BitmapSource.
/// </summary>
public sealed class TransformedBitmap : BitmapSource
{
    private BitmapSource? _source;
    private Transform _transform = Transform.Identity;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransformedBitmap"/> class.
    /// </summary>
    public TransformedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified source and transform.
    /// </summary>
    public TransformedBitmap(BitmapSource source, Transform transform)
    {
        _source = source;
        _transform = transform;
    }

    /// <summary>
    /// Gets or sets the source bitmap.
    /// </summary>
    public BitmapSource? Source
    {
        get => _source;
        set => _source = value;
    }

    /// <summary>
    /// Gets or sets the transform to apply to the bitmap.
    /// </summary>
    public Transform Transform
    {
        get => _transform;
        set => _transform = value;
    }

    /// <inheritdoc />
    public override double Width => _source?.Width ?? 0;

    /// <inheritdoc />
    public override double Height => _source?.Height ?? 0;

    /// <inheritdoc />
    public override nint NativeHandle => _source?.NativeHandle ?? nint.Zero;

    /// <inheritdoc />
    public override int PixelWidth => _source?.PixelWidth ?? 0;

    /// <inheritdoc />
    public override int PixelHeight => _source?.PixelHeight ?? 0;

    /// <inheritdoc />
    public override double DpiX => _source?.DpiX ?? 96.0;

    /// <inheritdoc />
    public override double DpiY => _source?.DpiY ?? 96.0;

    /// <inheritdoc />
    public override PixelFormat Format => _source?.Format ?? PixelFormat.Bgra32;
}
