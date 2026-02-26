namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Provides cached access to a BitmapSource for performance optimization.
/// </summary>
public sealed class CachedBitmap : BitmapSource
{
    private readonly BitmapSource _source;

    /// <summary>
    /// Initializes a new instance of the CachedBitmap class.
    /// </summary>
    public CachedBitmap(BitmapSource source, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public override int PixelWidth => _source.PixelWidth;
    public override int PixelHeight => _source.PixelHeight;
    public override double DpiX => _source.DpiX;
    public override double DpiY => _source.DpiY;
    public override PixelFormat Format => _source.Format;
    public override double Width => _source.Width;
    public override double Height => _source.Height;
    public override nint NativeHandle => _source.NativeHandle;
}

/// <summary>
/// Converts the color space of a BitmapSource.
/// </summary>
public sealed class ColorConvertedBitmap : BitmapSource
{
    private readonly BitmapSource? _source;

    /// <summary>
    /// Initializes a new instance of the ColorConvertedBitmap class.
    /// </summary>
    public ColorConvertedBitmap()
    {
    }

    /// <summary>
    /// Initializes a new instance with source, source and destination color contexts and pixel format.
    /// </summary>
    public ColorConvertedBitmap(BitmapSource source, ColorContext sourceColorContext,
        ColorContext destinationColorContext, PixelFormat format)
    {
        _source = source;
        SourceColorContext = sourceColorContext;
        DestinationColorContext = destinationColorContext;
        DestinationFormat = format;
    }

    /// <summary>
    /// Gets or sets the source color context.
    /// </summary>
    public ColorContext? SourceColorContext { get; set; }

    /// <summary>
    /// Gets or sets the destination color context.
    /// </summary>
    public ColorContext? DestinationColorContext { get; set; }

    /// <summary>
    /// Gets or sets the destination pixel format.
    /// </summary>
    public PixelFormat DestinationFormat { get; set; }

    public override int PixelWidth => _source?.PixelWidth ?? 0;
    public override int PixelHeight => _source?.PixelHeight ?? 0;
    public override double Width => _source?.Width ?? 0;
    public override double Height => _source?.Height ?? 0;
    public override nint NativeHandle => _source?.NativeHandle ?? 0;
}

/// <summary>
/// Specifies the size of a bitmap image.
/// </summary>
public sealed class BitmapSizeOptions
{
    private BitmapSizeOptions() { }

    /// <summary>
    /// Gets the width of the bitmap.
    /// </summary>
    public int PixelWidth { get; private set; }

    /// <summary>
    /// Gets the height of the bitmap.
    /// </summary>
    public int PixelHeight { get; private set; }

    /// <summary>
    /// Gets a value that indicates whether the aspect ratio is preserved.
    /// </summary>
    public bool PreservesAspectRatio { get; private set; }

    /// <summary>
    /// Gets the rotation to apply.
    /// </summary>
    public Rotation Rotation { get; private set; }

    /// <summary>
    /// Creates BitmapSizeOptions that preserves the aspect ratio.
    /// </summary>
    public static BitmapSizeOptions FromHeight(int pixelHeight)
    {
        return new BitmapSizeOptions { PixelHeight = pixelHeight, PreservesAspectRatio = true };
    }

    /// <summary>
    /// Creates BitmapSizeOptions that preserves the aspect ratio.
    /// </summary>
    public static BitmapSizeOptions FromWidth(int pixelWidth)
    {
        return new BitmapSizeOptions { PixelWidth = pixelWidth, PreservesAspectRatio = true };
    }

    /// <summary>
    /// Creates BitmapSizeOptions with the exact size specified.
    /// </summary>
    public static BitmapSizeOptions FromWidthAndHeight(int pixelWidth, int pixelHeight)
    {
        return new BitmapSizeOptions { PixelWidth = pixelWidth, PixelHeight = pixelHeight };
    }

    /// <summary>
    /// Creates BitmapSizeOptions that applies a rotation.
    /// </summary>
    public static BitmapSizeOptions FromRotation(Rotation rotation)
    {
        return new BitmapSizeOptions { Rotation = rotation, PreservesAspectRatio = true };
    }
}

// Rotation and BitmapCreateOptions are defined in BitmapEncoder.cs and BitmapDecoder.cs respectively.
