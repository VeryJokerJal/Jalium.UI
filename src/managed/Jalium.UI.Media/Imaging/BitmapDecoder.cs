using System.Collections.ObjectModel;
using System.Net.Cache;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents a container for bitmap frames. Each bitmap decoder can contain one or more
/// <see cref="BitmapFrame"/> objects.
/// </summary>
public abstract class BitmapDecoder : DispatcherObject
{
    private static readonly BitmapCreateOptions s_knownCreateOptions =
        BitmapCreateOptions.PreservePixelFormat |
        BitmapCreateOptions.DelayCreation |
        BitmapCreateOptions.IgnoreColorProfile |
        BitmapCreateOptions.IgnoreImageCache;

    private readonly object _initializationGate = new();
    private Func<byte[]>? _sourceLoader;
    private byte[]? _encodedData;
    private ReadOnlyCollection<BitmapFrame>? _frames;
    private BitmapContainerFormat _expectedFormat;
    private BitmapCacheOption _cacheOption;
    private string _sourceDescription = "bitmap source";
    private EventHandler? _downloadCompleted;
    private EventHandler<ExceptionEventArgs>? _downloadFailed;
    private EventHandler<DownloadProgressEventArgs>? _downloadProgress;

    /// <summary>
    /// Initializes a decoder without a built-in source. This remains available to custom
    /// decoders which override <see cref="Frames"/>.
    /// </summary>
    protected BitmapDecoder()
    {
        _frames = Array.AsReadOnly(Array.Empty<BitmapFrame>());
    }

    private protected BitmapDecoder(
        Stream bitmapStream,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        BitmapContainerFormat expectedFormat)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        if (!bitmapStream.CanRead)
        {
            throw new ArgumentException("The bitmap stream must be readable.", nameof(bitmapStream));
        }

        ValidateOptions(createOptions, cacheOption);
        _expectedFormat = expectedFormat;
        _cacheOption = cacheOption;
        _sourceDescription = "bitmap stream";
        _sourceLoader = () => ReadStream(bitmapStream);
        Initialize(createOptions);
    }

    private protected BitmapDecoder(
        Uri bitmapUri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        BitmapContainerFormat expectedFormat,
        RequestCachePolicy? uriCachePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        ValidateOptions(createOptions, cacheOption);
        _expectedFormat = expectedFormat;
        _cacheOption = cacheOption;
        _sourceDescription = bitmapUri.ToString();
        _sourceLoader = () => ReadUri(bitmapUri, uriCachePolicy);
        Initialize(createOptions);
    }

    /// <summary>
    /// Gets the decoded frames. With <see cref="BitmapCreateOptions.DelayCreation"/>, source
    /// I/O is deferred until this property is read. With an on-demand cache, pixel decoding
    /// is likewise deferred until this property is read.
    /// </summary>
    public virtual ReadOnlyCollection<BitmapFrame> Frames
    {
        get
        {
            EnsureFrames();
            return _frames!;
        }
    }

    /// <summary>
    /// Gets the codec info for this decoder.
    /// </summary>
    public virtual BitmapCodecInfo? CodecInfo => null;

    /// <summary>
    /// Gets the color contexts associated with this decoder.
    /// </summary>
    public virtual ReadOnlyCollection<ColorContext>? ColorContexts => null;

    /// <summary>Gets the metadata associated with the first decoded frame.</summary>
    public virtual BitmapMetadata? Metadata => Frames.Count == 0 ? null : Frames[0].Metadata;

    /// <summary>
    /// Gets the bitmap palette when the source codec exposes one.
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
    /// Concrete decoders perform URI acquisition synchronously and therefore return false.
    /// </summary>
    public virtual bool IsDownloading => false;

    /// <summary>Occurs when an asynchronous download completes.</summary>
    public virtual event EventHandler? DownloadCompleted
    {
        add => _downloadCompleted += value;
        remove => _downloadCompleted -= value;
    }

    /// <summary>Occurs when an asynchronous download fails.</summary>
    public virtual event EventHandler<ExceptionEventArgs>? DownloadFailed
    {
        add => _downloadFailed += value;
        remove => _downloadFailed -= value;
    }

    /// <summary>Occurs when asynchronous download progress changes.</summary>
    public virtual event EventHandler<DownloadProgressEventArgs>? DownloadProgress
    {
        add => _downloadProgress += value;
        remove => _downloadProgress -= value;
    }

    /// <summary>
    /// Creates a decoder from a stream. The concrete codec is selected by the native image
    /// pipeline when the frames are requested.
    /// </summary>
    public static BitmapDecoder Create(
        Stream bitmapStream,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapStream);
        return new LateBoundBitmapDecoder(bitmapStream, createOptions, cacheOption);
    }

    /// <summary>
    /// Creates a decoder from a URI. The concrete codec is selected by the native image
    /// pipeline when the frames are requested.
    /// </summary>
    public static BitmapDecoder Create(
        Uri bitmapUri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        return new LateBoundBitmapDecoder(bitmapUri, createOptions, cacheOption);
    }

    /// <summary>Creates a decoder from a URI using the specified request cache policy.</summary>
    public static BitmapDecoder Create(
        Uri bitmapUri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        RequestCachePolicy? uriCachePolicy)
    {
        ArgumentNullException.ThrowIfNull(bitmapUri);
        return new LateBoundBitmapDecoder(bitmapUri, createOptions, cacheOption, uriCachePolicy);
    }

    /// <summary>Creates a writable metadata view for the decoded image.</summary>
    public virtual InPlaceBitmapMetadataWriter CreateInPlaceBitmapMetadataWriter()
    {
        var writer = new InPlaceBitmapMetadataWriter();
        if (Metadata is { } metadata)
        {
            foreach ((string query, object? value) in metadata.Queries)
            {
                writer.SetQuery(query, value);
            }
        }

        return writer;
    }

    /// <inheritdoc />
    public override string ToString() => _sourceDescription;

    private void Initialize(BitmapCreateOptions createOptions)
    {
        if ((createOptions & BitmapCreateOptions.DelayCreation) == 0)
        {
            _encodedData = LoadAndValidateSource();
        }

        if ((createOptions & BitmapCreateOptions.DelayCreation) == 0 &&
            _cacheOption == BitmapCacheOption.OnLoad)
        {
            EnsureFrames();
        }
    }

    private void EnsureFrames()
    {
        if (_frames is not null)
        {
            return;
        }

        lock (_initializationGate)
        {
            if (_frames is not null)
            {
                return;
            }

            var data = _encodedData ?? LoadAndValidateSource();
            _encodedData = data;
            var nativeDecoder = BitmapImage.ResolveDecoder();
            var frameCount = nativeDecoder.ReadFrameCount(data);
            if (frameCount <= 0)
            {
                throw new InvalidDataException($"'{_sourceDescription}' does not contain a bitmap frame.");
            }

            // A corrupt source must not be able to force an unbounded managed allocation.
            if (frameCount > 16_384)
            {
                throw new InvalidDataException(
                    $"'{_sourceDescription}' reports an unsupported frame count of {frameCount}.");
            }

            var decodedFrames = new BitmapFrame[frameCount];
            for (var index = 0; index < decodedFrames.Length; index++)
            {
                var decoded = nativeDecoder.DecodeFrame(data, index).Image;
                decodedFrames[index] = BitmapFrame.Create(decoded, this);
            }

            _frames = Array.AsReadOnly(decodedFrames);
            if (_cacheOption is BitmapCacheOption.OnLoad or BitmapCacheOption.None)
            {
                _encodedData = null;
            }
        }
    }

    private byte[] LoadAndValidateSource()
    {
        var loader = _sourceLoader ?? throw new InvalidOperationException("The bitmap source is unavailable.");
        var data = loader();
        if (data.Length == 0)
        {
            throw new InvalidDataException($"'{_sourceDescription}' is empty.");
        }

        if (_expectedFormat != BitmapContainerFormat.Any && !MatchesContainer(data, _expectedFormat))
        {
            throw new InvalidDataException(
                $"'{_sourceDescription}' is not encoded as {_expectedFormat.ToString().ToUpperInvariant()}.");
        }

        // Once bytes have been acquired the original stream/URI no longer needs to be retained.
        _sourceLoader = null;
        return data;
    }

    private static void ValidateOptions(
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption)
    {
        if ((createOptions & ~s_knownCreateOptions) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(createOptions), createOptions, "Unknown bitmap creation option.");
        }

        if (!Enum.IsDefined(cacheOption))
        {
            throw new ArgumentOutOfRangeException(nameof(cacheOption), cacheOption, "Unknown bitmap cache option.");
        }
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] ReadUri(Uri uri, RequestCachePolicy? uriCachePolicy)
    {
        if (!uri.IsAbsoluteUri)
        {
            return File.ReadAllBytes(uri.OriginalString);
        }

        if (uri.IsFile)
        {
            return File.ReadAllBytes(uri.LocalPath);
        }

        if (uri.Scheme is "http" or "https")
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uriCachePolicy?.Level == RequestCacheLevel.BypassCache)
            {
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                };
            }
            using HttpResponseMessage response = client.Send(request);
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }

        throw new NotSupportedException($"Bitmap URI scheme '{uri.Scheme}' is not supported.");
    }

    /// <summary>Raises the decoder download-completed event for derived asynchronous decoders.</summary>
    protected void OnDownloadCompleted() => _downloadCompleted?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises the decoder download-failed event for derived asynchronous decoders.</summary>
    protected void OnDownloadFailed(Exception exception) =>
        _downloadFailed?.Invoke(this, new ExceptionEventArgs(exception));

    /// <summary>Raises the decoder download-progress event for derived asynchronous decoders.</summary>
    protected void OnDownloadProgress(int progress) =>
        _downloadProgress?.Invoke(this, new DownloadProgressEventArgs(progress));

    private static bool MatchesContainer(ReadOnlySpan<byte> data, BitmapContainerFormat format)
        => format switch
        {
            BitmapContainerFormat.Bmp =>
                data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M',
            BitmapContainerFormat.Gif =>
                data.Length >= 6 &&
                data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
                data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') &&
                data[5] == (byte)'a',
            BitmapContainerFormat.Tiff =>
                data.Length >= 4 &&
                ((data[0] == (byte)'I' && data[1] == (byte)'I' &&
                  (data[2] == 42 || data[2] == 43) && data[3] == 0) ||
                 (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 &&
                  (data[3] == 42 || data[3] == 43))),
            BitmapContainerFormat.Png =>
                data.Length >= 8 &&
                data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G' &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A,
            BitmapContainerFormat.Jpeg =>
                data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF,
            BitmapContainerFormat.Icon =>
                data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0,
            BitmapContainerFormat.Wmp =>
                data.Length >= 4 && data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 0xBC && data[3] == 0x01,
            BitmapContainerFormat.Any => true,
            _ => false,
        };
}

/// <summary>Defines a decoder for PNG encoded images.</summary>
public sealed class PngBitmapDecoder : BitmapDecoder
{
    public PngBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Png)
    {
    }

    public PngBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Png)
    {
    }
}

/// <summary>Defines a decoder for JPEG encoded images.</summary>
public sealed class JpegBitmapDecoder : BitmapDecoder
{
    public JpegBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Jpeg)
    {
    }

    public JpegBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Jpeg)
    {
    }
}

/// <summary>Defines a decoder for BMP encoded images.</summary>
public sealed class BmpBitmapDecoder : BitmapDecoder
{
    public BmpBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Bmp)
    {
    }

    public BmpBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Bmp)
    {
    }
}

/// <summary>Defines a decoder for GIF encoded images.</summary>
public sealed class GifBitmapDecoder : BitmapDecoder
{
    public GifBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Gif)
    {
    }

    public GifBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Gif)
    {
    }
}

/// <summary>Defines a decoder for TIFF encoded images.</summary>
public sealed class TiffBitmapDecoder : BitmapDecoder
{
    public TiffBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Tiff)
    {
    }

    public TiffBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Tiff)
    {
    }
}

/// <summary>Generic decoder implementation used by the factory methods.</summary>
internal sealed class GenericBitmapDecoder : BitmapDecoder
{
    internal GenericBitmapDecoder(Stream stream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(stream, createOptions, cacheOption, BitmapContainerFormat.Any)
    {
    }

    internal GenericBitmapDecoder(Uri uri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(uri, createOptions, cacheOption, BitmapContainerFormat.Any)
    {
    }

    internal GenericBitmapDecoder(
        Uri uri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        RequestCachePolicy? uriCachePolicy)
        : base(uri, createOptions, cacheOption, BitmapContainerFormat.Any, uriCachePolicy)
    {
    }
}

/// <summary>Represents a decoder whose concrete codec is selected after the source is inspected.</summary>
public sealed class LateBoundBitmapDecoder : BitmapDecoder
{
    internal LateBoundBitmapDecoder(Stream stream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(stream, createOptions, cacheOption, BitmapContainerFormat.Any)
    {
    }

    internal LateBoundBitmapDecoder(Uri uri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(uri, createOptions, cacheOption, BitmapContainerFormat.Any)
    {
    }

    internal LateBoundBitmapDecoder(
        Uri uri,
        BitmapCreateOptions createOptions,
        BitmapCacheOption cacheOption,
        RequestCachePolicy? uriCachePolicy)
        : base(uri, createOptions, cacheOption, BitmapContainerFormat.Any, uriCachePolicy)
    {
    }
}

/// <summary>Defines a decoder for Windows Media Photo / JPEG XR images.</summary>
public sealed class WmpBitmapDecoder : BitmapDecoder
{
    public WmpBitmapDecoder(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapStream, createOptions, cacheOption, BitmapContainerFormat.Wmp)
    {
    }

    public WmpBitmapDecoder(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption)
        : base(bitmapUri, createOptions, cacheOption, BitmapContainerFormat.Wmp)
    {
    }
}

internal enum BitmapContainerFormat
{
    Any,
    Bmp,
    Gif,
    Tiff,
    Png,
    Jpeg,
    Icon,
    Wmp,
}

/// <summary>Specifies initialization options for bitmap images.</summary>
[Flags]
public enum BitmapCreateOptions
{
    None = 0,
    PreservePixelFormat = 1,
    DelayCreation = 2,
    IgnoreColorProfile = 4,
    IgnoreImageCache = 8,
}

/// <summary>Specifies how a bitmap image takes advantage of memory caching.</summary>
public enum BitmapCacheOption
{
    Default = 0,
    OnDemand = 0,
    OnLoad = 1,
    None = 2,
}
