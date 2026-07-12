using System.Collections.ObjectModel;
using System.Net.Cache;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents image data returned by a decoder and accepted by encoders.
/// </summary>
public abstract class BitmapFrame : BitmapSource
{
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiX = 96.0;
    private double _dpiY = 96.0;
    private PixelFormat _format = PixelFormat.Bgra32;
    private byte[]? _pixels;
    private int _stride;
    private BitmapPalette? _palette;
    private BitmapSource? _thumbnail;
    private ReadOnlyCollection<ColorContext>? _colorContexts;
    private BitmapDecoder? _decoder;
    private BitmapMetadata? _metadata;

    protected BitmapFrame()
    {
    }

    private protected BitmapFrame(DecodedImage decoded, BitmapDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);

        _pixelWidth = decoded.Width;
        _pixelHeight = decoded.Height;
        _stride = decoded.Stride;
        _format = decoded.Format == NativePixelFormat.Rgba8
            ? PixelFormat.Rgba32
            : PixelFormat.Bgra32;
        var requiredLength = checked(decoded.Stride * decoded.Height);
        if (decoded.Pixels.Length < requiredLength)
        {
            throw new ArgumentException("Decoded pixel data is smaller than its dimensions and stride.", nameof(decoded));
        }

        _pixels = decoded.Pixels.ToArray();
        _decoder = decoder;
    }

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

    /// <inheritdoc />
    public override BitmapPalette? Palette => _palette;

    /// <summary>
    /// Gets the thumbnail image associated with this BitmapFrame.
    /// </summary>
    public virtual BitmapSource? Thumbnail => _thumbnail;

    /// <summary>
    /// Gets the color contexts associated with this frame.
    /// </summary>
    public virtual ReadOnlyCollection<ColorContext>? ColorContexts => _colorContexts;

    public abstract Uri? BaseUri { get; set; }

    public override BitmapMetadata? Metadata => _metadata;

    /// <summary>
    /// Gets the decoder associated with this frame.
    /// </summary>
    public virtual BitmapDecoder? Decoder => _decoder;

    /// <summary>
    /// Creates a new BitmapFrame from a BitmapSource.
    /// </summary>
    public static BitmapFrame Create(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var frame = new BitmapFrameImpl
        {
            _pixelWidth = source.PixelWidth,
            _pixelHeight = source.PixelHeight,
            _dpiX = source.DpiX,
            _dpiY = source.DpiY,
            _format = source.Format,
            _palette = source.Palette,
        };

        if (frame._pixelWidth > 0 && frame._pixelHeight > 0)
        {
            var bytesPerPixel = GetBytesPerPixel(frame._format);
            frame._stride = checked(frame._pixelWidth * bytesPerPixel);
            frame._pixels = new byte[checked(frame._stride * frame._pixelHeight)];
            source.CopyPixels(
                new Int32Rect(0, 0, frame._pixelWidth, frame._pixelHeight),
                frame._pixels,
                frame._stride,
                0);
        }

        if (source is BitmapFrame sourceFrame)
        {
            frame._thumbnail = sourceFrame.Thumbnail;
            frame._colorContexts = sourceFrame.ColorContexts;
            frame._decoder = sourceFrame.Decoder;
        }

        return frame;
    }

    /// <summary>
    /// Creates a new BitmapFrame from a BitmapSource with the specified thumbnail.
    /// </summary>
    public static BitmapFrame Create(BitmapSource source, BitmapSource? thumbnail)
    {
        var frame = Create(source);
        frame._thumbnail = thumbnail;
        return frame;
    }

    internal static BitmapFrame Create(DecodedImage decoded, BitmapDecoder decoder) =>
        new BitmapFrameImpl(decoded, decoder);

    public static BitmapFrame Create(
        BitmapSource source,
        BitmapSource? thumbnail,
        BitmapMetadata? metadata,
        ReadOnlyCollection<ColorContext>? colorContexts)
    {
        var frame = Create(source, thumbnail);
        frame._metadata = metadata;
        frame._colorContexts = colorContexts;
        return frame;
    }

    /// <summary>
    /// Creates a new BitmapFrame from a URI.
    /// </summary>
    public static BitmapFrame Create(Uri bitmapUri)
    {
        return Create(bitmapUri, BitmapCreateOptions.None, BitmapCacheOption.Default);
    }

    public static BitmapFrame Create(Uri bitmapUri, RequestCachePolicy? uriCachePolicy)
        => Create(bitmapUri, BitmapCreateOptions.None, BitmapCacheOption.Default, uriCachePolicy);

    /// <summary>
    /// Creates a new BitmapFrame from a URI with the specified create options and cache option.
    /// </summary>
    public static BitmapFrame Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        var decoder = BitmapDecoder.Create(bitmapUri, createOptions, cacheOption);
        return GetFirstFrame(decoder);
    }

    /// <summary>
    /// Creates a new BitmapFrame from a stream.
    /// </summary>
    public static BitmapFrame Create(Stream bitmapStream)
    {
        return Create(bitmapStream, BitmapCreateOptions.None, BitmapCacheOption.Default);
    }

    /// <summary>
    /// Creates a new BitmapFrame from a stream with the specified create options and cache option.
    /// </summary>
    public static BitmapFrame Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        var decoder = BitmapDecoder.Create(bitmapStream, createOptions, cacheOption);
        return GetFirstFrame(decoder);
    }

    public static BitmapFrame Create(
        Uri bitmapUri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        RequestCachePolicy? uriCachePolicy)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        BitmapFrame frame = GetFirstFrame(BitmapDecoder.Create(bitmapUri, createOptions, cacheOption, uriCachePolicy));
        frame.BaseUri = bitmapUri;
        return frame;
    }

    public abstract InPlaceBitmapMetadataWriter CreateInPlaceBitmapMetadataWriter();

    /// <summary>Creates a metadata writer initialized from this frame.</summary>
    protected InPlaceBitmapMetadataWriter CreateMetadataWriterCore()
    {
        var writer = new InPlaceBitmapMetadataWriter();
        if (_metadata is not null)
        {
            foreach ((string query, object? value) in _metadata.Queries)
            {
                writer.SetQuery(query, value);
            }
        }

        return writer;
    }

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyFrameState((BitmapFrame)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyFrameState((BitmapFrame)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyFrameState((BitmapFrame)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyFrameState((BitmapFrame)sourceFreezable);
    }

    /// <inheritdoc />
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (_pixels is null)
        {
            throw new InvalidOperationException("The bitmap frame does not contain decoded pixels.");
        }

        if (offset < 0 || offset > pixels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var width = sourceRect.Width == 0 ? _pixelWidth : sourceRect.Width;
        var height = sourceRect.Height == 0 ? _pixelHeight : sourceRect.Height;
        var x = sourceRect.X;
        var y = sourceRect.Y;
        if (x < 0 || y < 0 || width < 0 || height < 0 ||
            width > _pixelWidth - x || height > _pixelHeight - y)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        }

        var rowBytes = checked(width * GetBytesPerPixel(_format));
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than the requested row width.");
        }

        var requiredLength = height == 0
            ? offset
            : checked(offset + ((height - 1) * stride) + rowBytes);
        if (requiredLength > pixels.Length)
        {
            throw new ArgumentException("The destination buffer is too small for the requested pixels.", nameof(pixels));
        }

        var bytesPerPixel = GetBytesPerPixel(_format);
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = ((y + row) * _stride) + (x * bytesPerPixel);
            Buffer.BlockCopy(_pixels, sourceOffset, pixels, offset + (row * stride), rowBytes);
        }
    }

    private static BitmapFrame GetFirstFrame(BitmapDecoder decoder)
    {
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidDataException("The bitmap decoder did not expose any frames.");
        }

        return decoder.Frames[0];
    }

    private static int GetBytesPerPixel(PixelFormat format)
    {
        if (format == PixelFormat.Bgra32 || format == PixelFormat.Rgba32 ||
            format == PixelFormat.Rgb32 || format == PixelFormat.Pbgra32)
        {
            return 4;
        }

        if (format == PixelFormat.Bgr24 || format == PixelFormat.Rgb24)
        {
            return 3;
        }

        if (format == PixelFormat.Gray16)
        {
            return 2;
        }

        if (format == PixelFormat.Gray8)
        {
            return 1;
        }

        throw new NotSupportedException($"Pixel format '{format}' is not supported by BitmapFrame.");
    }

    private void CopyFrameState(BitmapFrame source)
    {
        _pixelWidth = source._pixelWidth;
        _pixelHeight = source._pixelHeight;
        _dpiX = source._dpiX;
        _dpiY = source._dpiY;
        _format = source._format;
        _pixels = source._pixels is null ? null : (byte[])source._pixels.Clone();
        _stride = source._stride;
        _palette = source._palette;
        _thumbnail = source._thumbnail;
        _colorContexts = source._colorContexts;
        _decoder = source._decoder;
        _metadata = source._metadata?.Clone();
        BaseUri = source.BaseUri;
    }

    private sealed class BitmapFrameImpl : BitmapFrame
    {
        public BitmapFrameImpl()
        {
        }

        public BitmapFrameImpl(DecodedImage decoded, BitmapDecoder decoder)
            : base(decoded, decoder)
        {
        }

        public override Uri? BaseUri { get; set; }

        public override InPlaceBitmapMetadataWriter CreateInPlaceBitmapMetadataWriter() =>
            CreateMetadataWriterCore();

        protected override Freezable CreateInstanceCore() => new BitmapFrameImpl();
    }
}
