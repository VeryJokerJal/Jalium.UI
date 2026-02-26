namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents a container for bitmap frames. Each bitmap decoder can contain one or more BitmapFrame objects.
/// </summary>
public abstract class BitmapDecoder
{
    /// <summary>
    /// Gets the content of an individual frame within a bitmap.
    /// </summary>
    public abstract IReadOnlyList<BitmapFrame> Frames { get; }

    /// <summary>
    /// Gets the codec info for this decoder.
    /// </summary>
    public virtual BitmapCodecInfo? CodecInfo => null;

    /// <summary>
    /// Gets the color contexts associated with this decoder.
    /// </summary>
    public virtual IReadOnlyList<ColorContext>? ColorContexts => null;

    /// <summary>
    /// Gets the bitmap palette.
    /// </summary>
    public virtual BitmapPalette? Palette => null;

    /// <summary>
    /// Gets the preview thumbnail.
    /// </summary>
    public virtual BitmapSource? Preview => null;

    /// <summary>
    /// Gets the thumbnail.
    /// </summary>
    public virtual BitmapSource? Thumbnail => null;

    /// <summary>
    /// Gets a value indicating whether the decoder is downloading content.
    /// </summary>
    public virtual bool IsDownloading => false;

    /// <summary>
    /// Creates a BitmapDecoder from a stream.
    /// </summary>
    public static BitmapDecoder Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        return new GenericBitmapDecoder(bitmapStream, createOptions, cacheOption);
    }

    /// <summary>
    /// Creates a BitmapDecoder from a URI.
    /// </summary>
    public static BitmapDecoder Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        return new GenericBitmapDecoder(bitmapUri, createOptions, cacheOption);
    }
}

/// <summary>
/// Defines a decoder for PNG encoded images.
/// </summary>
public sealed class PngBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    public PngBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }

    public PngBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

/// <summary>
/// Defines a decoder for JPEG encoded images.
/// </summary>
public sealed class JpegBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    public JpegBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }

    public JpegBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

/// <summary>
/// Defines a decoder for BMP encoded images.
/// </summary>
public sealed class BmpBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    public BmpBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

/// <summary>
/// Defines a decoder for GIF encoded images.
/// </summary>
public sealed class GifBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    public GifBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

/// <summary>
/// Defines a decoder for TIFF encoded images.
/// </summary>
public sealed class TiffBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    public TiffBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

/// <summary>
/// Generic decoder implementation used by the factory method.
/// </summary>
internal sealed class GenericBitmapDecoder : BitmapDecoder
{
    private readonly List<BitmapFrame> _frames = new();

    /// <inheritdoc />
    public override IReadOnlyList<BitmapFrame> Frames => _frames;

    internal GenericBitmapDecoder(Stream stream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }

    internal GenericBitmapDecoder(Uri uri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
    }
}

#region Enums

/// <summary>
/// Specifies initialization options for bitmap images.
/// </summary>
[Flags]
public enum BitmapCreateOptions
{
    None = 0,
    PreservePixelFormat = 1,
    DelayCreation = 2,
    IgnoreColorProfile = 4,
    IgnoreImageCache = 8
}

/// <summary>
/// Specifies how a bitmap image takes advantage of memory caching.
/// </summary>
public enum BitmapCacheOption
{
    Default,
    OnDemand,
    OnLoad,
    None
}

#endregion
