using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;
using Jalium.UI.Markup;

namespace Jalium.UI.Media;

/// <summary>
/// Represents a bitmap image source. PNG / JPEG / WebP / GIF / BMP / HEIF input is
/// decoded to BGRA8 pixels by the platform-native <see cref="INativeImageDecoder"/>
/// (WIC on Windows, NDK <c>AImageDecoder</c> / <c>BitmapFactory</c> on Android).
/// </summary>
public class BitmapImage : Imaging.BitmapSource, IDisposable, IReclaimableResource, IUriContext
{
    private static INativeImageDecoder? s_decoder;
    private static readonly object s_decoderLock = new();

    private nint _nativeHandle;
    private double _width;
    private double _height;
    private Uri? _uriSource;
    private Uri? _baseUri;
    private byte[]? _imageData;
    private byte[]? _rawPixelData;
    private int _pixelStride;
    private CancellationTokenSource? _httpCts;
    private bool _isDownloading;

    /// <summary>
    /// Occurs when the image has been loaded from a remote source.
    /// </summary>
    public event EventHandler? OnImageLoaded;

    /// <inheritdoc />
    public override double Width => _width;

    /// <inheritdoc />
    public override double Height => _height;

    /// <inheritdoc />
    public override nint NativeHandle => _nativeHandle;

    /// <summary>
    /// Gets the raw image data bytes (encoded PNG/JPEG/etc.).
    /// </summary>
    public byte[]? ImageData => _imageData;

    /// <summary>
    /// Gets the raw BGRA8 pixel buffer (always populated after decode).
    /// </summary>
    public byte[]? RawPixelData => _rawPixelData;

    /// <summary>
    /// Gets the pixel width.
    /// </summary>
    public override int PixelWidth => (int)Math.Round(_width);

    /// <summary>
    /// Gets the pixel height.
    /// </summary>
    public override int PixelHeight => (int)Math.Round(_height);

    /// <inheritdoc />
    public override bool IsDownloading => _isDownloading;

    /// <summary>
    /// Gets the number of bytes between two adjacent rows in the raw pixel buffer.
    /// </summary>
    public int PixelStride => _pixelStride;

    /// <summary>
    /// Gets or sets the URI source of the bitmap image.
    /// </summary>
    public Uri? UriSource
    {
        get => _uriSource;
        set
        {
            _httpCts?.Cancel();
            _httpCts?.Dispose();
            _httpCts = null;

            _uriSource = value;
            ClearLoadFailure();
            if (value != null)
            {
                LoadFromUri(ResolveUri(value));
            }
        }
    }

    Uri? IUriContext.BaseUri
    {
        get => BaseUriCore;
        set => BaseUriCore = value;
    }

    /// <summary>Gets or sets the base URI used by derived WPF-compatible facades.</summary>
    protected Uri? BaseUriCore
    {
        get => _baseUri;
        set
        {
            if (Equals(_baseUri, value))
            {
                return;
            }

            _baseUri = value;
            if (_uriSource is not { IsAbsoluteUri: false } relativeSource)
            {
                return;
            }

            ClearLoadFailure();
            try
            {
                LoadFromUri(ResolveUri(relativeSource));
            }
            catch
            {
                // The concrete load path already reported the exception. Base URI propagation
                // occurs inside a Source property callback, so keep that callback event-driven.
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapImage"/> class.
    /// </summary>
    public BitmapImage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapImage"/> class with the specified URI.
    /// </summary>
    public BitmapImage(Uri uriSource)
    {
        UriSource = uriSource;
    }

    /// <summary>
    /// 注入自定义 <see cref="INativeImageDecoder"/>。当 <see cref="MediaAppBuilderExtensions"/>
    /// 注册原生媒体管道时会自动调用；测试可手动设置 mock 实现。
    /// </summary>
    public static void SetDecoder(INativeImageDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        lock (s_decoderLock)
        {
            s_decoder = decoder;
        }
    }

    /// <summary>
    /// 创建 BitmapImage 从文件路径。
    /// </summary>
    public static BitmapImage FromFile(string filePath)
    {
        var image = new BitmapImage();
        image.LoadFromFile(filePath);
        return image;
    }

    /// <summary>
    /// 创建 BitmapImage 从 BGRA8 原始像素。
    /// </summary>
    /// <param name="pixels">BGRA8 像素数据。</param>
    /// <param name="width">像素宽度。</param>
    /// <param name="height">像素高度。</param>
    /// <param name="stride">行跨度（字节）。0 表示 <c>width * 4</c>。</param>
    public static BitmapImage FromPixels(byte[] pixels, int width, int height, int stride = 0)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (stride <= 0)
        {
            stride = checked(width * 4);
        }

        var minimumBytes = checked(stride * height);
        if (pixels.Length < minimumBytes)
        {
            throw new ArgumentException("Pixel buffer is smaller than the specified dimensions and stride.", nameof(pixels));
        }

        var pixelCopy = new byte[minimumBytes];
        Buffer.BlockCopy(pixels, 0, pixelCopy, 0, minimumBytes);

        var image = new BitmapImage
        {
            _width = width,
            _height = height,
            _rawPixelData = pixelCopy,
            _pixelStride = stride
        };
        return image;
    }

    /// <summary>
    /// 创建 BitmapImage 从已解码的 <see cref="DecodedImage"/>。
    /// </summary>
    public static BitmapImage FromDecodedImage(DecodedImage decoded)
    {
        var image = new BitmapImage();
        image.AdoptDecoded(decoded);
        return image;
    }

    /// <summary>
    /// 创建 BitmapImage 从池化的 <see cref="MediaFrame"/>。这条路径专供 VideoDrawing /
    /// CameraView 热路径使用 — 数据被复制出来，调用方可立即 Dispose 帧以归还池。
    /// </summary>
    public static BitmapImage FromMediaFrame(MediaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var pixels = frame.Pixels.Span;
        var copy = new byte[pixels.Length];
        pixels.CopyTo(copy);

        var image = new BitmapImage
        {
            _width = frame.Width,
            _height = frame.Height,
            _rawPixelData = copy,
            _pixelStride = frame.Stride
        };
        return image;
    }

    private void LoadFromUri(Uri uri)
    {
        if (uri.IsAbsoluteUri && (uri.IsFile || uri.Scheme == "file"))
        {
            LoadFromFile(uri.LocalPath);
            return;
        }

        if (uri.IsAbsoluteUri && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            var cts = new CancellationTokenSource();
            _httpCts = cts;
            _isDownloading = true;
            OnDownloadProgress(0);
            _ = LoadFromHttpAsync(uri, cts.Token);
            return;
        }

        if (!uri.IsAbsoluteUri)
        {
            // Relative URI: resolve against assembly manifest resources first
            // (covers <Resource Include="..."> items embedded by Jalium.UI.Build's
            // EmbedJaliumResourceItems target), then fall back to a disk-relative
            // lookup against AppContext.BaseDirectory for projects that ship the
            // file as <Content CopyToOutputDirectory="...">.
            if (TryLoadFromAssemblyResource(uri.OriginalString))
            {
                return;
            }

            var basePath = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(basePath))
            {
                var diskCandidate = System.IO.Path.Combine(basePath, uri.OriginalString);
                if (System.IO.File.Exists(diskCandidate))
                {
                    LoadFromFile(diskCandidate);
                    return;
                }
            }

            ReportLoadFailure(new FileNotFoundException(
                $"The image resource '{uri.OriginalString}' could not be found.",
                uri.OriginalString));
        }
    }

    private Uri ResolveUri(Uri uri)
    {
        if (uri.IsAbsoluteUri || _baseUri is not { IsAbsoluteUri: true } baseUri)
        {
            return uri;
        }

        return new Uri(baseUri, uri);
    }

    /// <summary>
    /// Walks the AppDomain's loaded assemblies looking for a manifest resource
    /// that matches <paramref name="relativePath"/>. Mirrors the candidate-name
    /// strategy used by ThemeLoader for <c>ResourceDictionary Source="..."</c>
    /// so consumer XAML and code-behind can share the same authoring shape.
    /// Returns <c>true</c> when the bytes were decoded into this BitmapImage.
    /// </summary>
    private bool TryLoadFromAssemblyResource(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var separators = new[] { '/', '\\' };
        var dotted = relativePath.Replace('/', '.').Replace('\\', '.').TrimStart('.');
        var lastSep = relativePath.LastIndexOfAny(separators);
        var fileName = lastSep >= 0 ? relativePath.Substring(lastSep + 1) : relativePath;

        var frameworkAssembly = typeof(BitmapImage).Assembly;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly == frameworkAssembly)
            {
                continue;
            }

            string[] manifestNames;
            try
            {
                manifestNames = assembly.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }
            if (manifestNames.Length == 0)
            {
                continue;
            }

            var assemblyName = assembly.GetName().Name ?? string.Empty;
            string?[] candidates =
            [
                dotted,
                string.IsNullOrEmpty(assemblyName) ? null : assemblyName + "." + dotted,
                fileName,
                string.IsNullOrEmpty(assemblyName) ? null : assemblyName + "." + fileName,
            ];

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                var actual = Array.Find(manifestNames,
                    n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase));
                if (actual == null)
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(actual);
                if (stream == null)
                {
                    continue;
                }

                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }
                    read += n;
                }
                LoadFromBytes(bytes);
                return true;
            }
        }

        return false;
    }

    private async Task LoadFromHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            var bytes = await httpClient.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (dispatcher != null)
            {
                // Fire-and-forget marshal back to the UI thread; BeginInvoke now returns a
                // DispatcherOperation (awaitable), so discard it to signal intentional no-await.
                _ = dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        LoadFromBytes(bytes);
                        _isDownloading = false;
                        OnDownloadProgress(100);
                        OnDownloadCompleted();
                    }
                    catch (Exception ex)
                    {
                        // LoadFromBytes reports decode failures before rethrowing.
                        _isDownloading = false;
                        OnDownloadFailed(ex);
                    }
                });
            }
            else
            {
                try
                {
                    LoadFromBytes(bytes);
                    _isDownloading = false;
                    OnDownloadProgress(100);
                    OnDownloadCompleted();
                }
                catch (Exception ex)
                {
                    // LoadFromBytes reports decode failures before rethrowing.
                    _isDownloading = false;
                    OnDownloadFailed(ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _isDownloading = false;
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            if (dispatcher != null)
            {
                _ = dispatcher.BeginInvoke(() =>
                {
                    ReportLoadFailure(ex);
                    OnDownloadFailed(ex);
                });
            }
            else
            {
                ReportLoadFailure(ex);
                OnDownloadFailed(ex);
            }
            // HTTP 请求失败、网络错误等：保持空状态。
        }
    }

    /// <summary>
    /// Creates a BitmapImage from a byte array.
    /// </summary>
    public static BitmapImage FromBytes(byte[] data)
    {
        var image = new BitmapImage();
        image.LoadFromBytes(data);
        return image;
    }

    private void LoadFromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Image data is empty.", nameof(data));

        try
        {
            _imageData = data;
            var decoder = GetDecoderOrThrow();
            var decoded = decoder.Decode(data);
            AdoptDecoded(decoded);
        }
        catch (Exception ex)
        {
            ReportLoadFailure(ex);
            OnDecodeFailed(ex);
            throw;
        }
    }

    /// <summary>Loads encoded image data from a caller-owned stream.</summary>
    protected void LoadFromStreamSource(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The bitmap stream must be readable.", nameof(stream));
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        LoadFromBytes(copy.ToArray());
    }

    /// <summary>Copies the decoded and source state into a clone.</summary>
    protected void CopyBitmapStateFrom(BitmapImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _nativeHandle = source._nativeHandle;
        _width = source._width;
        _height = source._height;
        _uriSource = source._uriSource;
        _baseUri = source._baseUri;
        _imageData = source._imageData is null ? null : (byte[])source._imageData.Clone();
        _rawPixelData = source._rawPixelData is null ? null : (byte[])source._rawPixelData.Clone();
        _pixelStride = source._pixelStride;
    }

    /// <summary>Applies WPF BitmapImage crop, rotation, and decode-size options.</summary>
    protected void ApplyDecodeOptions(Int32Rect sourceRect, int decodePixelWidth, int decodePixelHeight, Rotation rotation)
    {
        if (_rawPixelData is null || PixelWidth <= 0 || PixelHeight <= 0)
        {
            return;
        }

        int width = PixelWidth;
        int height = PixelHeight;
        byte[] pixels = _rawPixelData;
        int stride = _pixelStride;

        if (!sourceRect.IsEmpty)
        {
            if (sourceRect.X < 0 || sourceRect.Y < 0 || sourceRect.Width <= 0 || sourceRect.Height <= 0 ||
                sourceRect.X + sourceRect.Width > width || sourceRect.Y + sourceRect.Height > height)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRect));
            }

            int croppedStride = checked(sourceRect.Width * 4);
            var cropped = new byte[checked(croppedStride * sourceRect.Height)];
            for (int row = 0; row < sourceRect.Height; row++)
            {
                Buffer.BlockCopy(
                    pixels,
                    ((sourceRect.Y + row) * stride) + (sourceRect.X * 4),
                    cropped,
                    row * croppedStride,
                    croppedStride);
            }

            pixels = cropped;
            width = sourceRect.Width;
            height = sourceRect.Height;
            stride = croppedStride;
        }

        if (rotation != Rotation.Rotate0)
        {
            int rotatedWidth = rotation is Rotation.Rotate90 or Rotation.Rotate270 ? height : width;
            int rotatedHeight = rotation is Rotation.Rotate90 or Rotation.Rotate270 ? width : height;
            int rotatedStride = checked(rotatedWidth * 4);
            var rotated = new byte[checked(rotatedStride * rotatedHeight)];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    (int destinationX, int destinationY) = rotation switch
                    {
                        Rotation.Rotate90 => (height - 1 - y, x),
                        Rotation.Rotate180 => (width - 1 - x, height - 1 - y),
                        Rotation.Rotate270 => (y, width - 1 - x),
                        _ => (x, y),
                    };
                    Buffer.BlockCopy(pixels, (y * stride) + (x * 4), rotated,
                        (destinationY * rotatedStride) + (destinationX * 4), 4);
                }
            }

            pixels = rotated;
            width = rotatedWidth;
            height = rotatedHeight;
            stride = rotatedStride;
        }

        if (decodePixelWidth > 0 || decodePixelHeight > 0)
        {
            int targetWidth = decodePixelWidth;
            int targetHeight = decodePixelHeight;
            if (targetWidth <= 0)
            {
                targetWidth = Math.Max(1, (int)Math.Round(width * (targetHeight / (double)height)));
            }
            else if (targetHeight <= 0)
            {
                targetHeight = Math.Max(1, (int)Math.Round(height * (targetWidth / (double)width)));
            }

            if (targetWidth != width || targetHeight != height)
            {
                int targetStride = checked(targetWidth * 4);
                var resized = new byte[checked(targetStride * targetHeight)];
                for (int y = 0; y < targetHeight; y++)
                {
                    int sourceY = Math.Min(height - 1, (int)((long)y * height / targetHeight));
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int sourceX = Math.Min(width - 1, (int)((long)x * width / targetWidth));
                        Buffer.BlockCopy(pixels, (sourceY * stride) + (sourceX * 4), resized,
                            (y * targetStride) + (x * 4), 4);
                    }
                }

                pixels = resized;
                width = targetWidth;
                height = targetHeight;
                stride = targetStride;
            }
        }

        _rawPixelData = pixels;
        _width = width;
        _height = height;
        _pixelStride = stride;
    }

    /// <inheritdoc />
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (_rawPixelData is null)
        {
            throw new InvalidOperationException("The bitmap has not been decoded.");
        }

        int width = sourceRect.Width == 0 ? PixelWidth : sourceRect.Width;
        int height = sourceRect.Height == 0 ? PixelHeight : sourceRect.Height;
        if (sourceRect.X < 0 || sourceRect.Y < 0 || width < 0 || height < 0 ||
            sourceRect.X + width > PixelWidth || sourceRect.Y + height > PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        }

        int rowBytes = checked(width * 4);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        int required = height == 0 ? offset : checked(offset + ((height - 1) * stride) + rowBytes);
        if (offset < 0 || required > pixels.Length)
        {
            throw new ArgumentException("The destination buffer is too small.", nameof(pixels));
        }

        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(_rawPixelData,
                ((sourceRect.Y + row) * _pixelStride) + (sourceRect.X * 4),
                pixels, offset + (row * stride), rowBytes);
        }
    }

    private void LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            ReportLoadFailure(ex);
            throw;
        }

        LoadFromBytes(bytes);
    }

    private void AdoptDecoded(DecodedImage decoded)
    {
        var span = decoded.Pixels.Span;
        var copy = new byte[span.Length];
        span.CopyTo(copy);

        _width = decoded.Width;
        _height = decoded.Height;
        _rawPixelData = copy;
        _pixelStride = decoded.Stride;

        ClearLoadFailure();
        OnImageLoaded?.Invoke(this, EventArgs.Empty);
    }

    private static INativeImageDecoder GetDecoderOrThrow()
    {
        var decoder = Volatile.Read(ref s_decoder);
        if (decoder is not null) return decoder;

        lock (s_decoderLock)
        {
            if (s_decoder is null)
            {
                s_decoder = new NativeImageDecoder();
            }
            return s_decoder;
        }
    }

    /// <summary>
    /// Returns the process-wide image decoder (the one injected via
    /// <see cref="SetDecoder"/>, otherwise the lazily-created native default).
    /// Shared with <see cref="AnimatedBitmap"/> and <see cref="ImageSourceLoader"/>
    /// so frame-count probing and frame decoding always agree.
    /// </summary>
    internal static INativeImageDecoder ResolveDecoder() => GetDecoderOrThrow();

    /// <summary>
    /// Reports how many frames the encoded <paramref name="data"/> contains
    /// without decoding any pixels. Used by <see cref="ImageSourceLoader"/> to
    /// pick between a static <see cref="BitmapImage"/> and an animated
    /// <see cref="AnimatedBitmap"/>; honors a decoder injected via
    /// <see cref="SetDecoder"/> so tests and custom pipelines stay consistent.
    /// </summary>
    internal static int ProbeFrameCount(ReadOnlySpan<byte> data)
        => ResolveDecoder().ReadFrameCount(data);

    /// <summary>
    /// Cancels any pending HTTP load and releases resources.
    /// </summary>
    public void Dispose()
    {
        _httpCts?.Cancel();
        _httpCts?.Dispose();
        _httpCts = null;
    }

    /// <summary>
    /// Drops the decoded BGRA8 pixel buffer and asks every active GPU bitmap
    /// cache to release its upload of this image. Idempotent. Encoded
    /// <see cref="ImageData"/> is preserved so the next render that needs the
    /// bitmap can re-decode and re-upload; if no encoded source is available
    /// (the bitmap was loaded directly from raw pixels and the encoded bytes
    /// were never captured), the pixel buffer is kept so the image is not
    /// lost permanently.
    /// </summary>
    /// <remarks>
    /// Called by the framework's idle-resource reclaimer when an
    /// <see cref="IReclaimableResource"/> element that owns this source has
    /// stayed off-screen past the configured idle window — see
    /// <c>JaliumAppExtensions.UseIdleResourceReclamation</c>. Safe to call
    /// directly to free memory under pressure.
    /// </remarks>
    public void ReclaimIdleResources()
    {
        // Always evict GPU uploads — they can be rebuilt from either
        // _rawPixelData (if still around) or _imageData (re-decode).
        RaiseGpuCacheEviction(this);

        // Drop CPU pixels only when we still have an encoded source we can
        // re-decode from; otherwise the image would be unrecoverable.
        if (_imageData != null)
        {
            _rawPixelData = null;
        }
    }

    /// <summary>
    /// Sets the native handle and dimensions (called by the rendering backend).
    /// </summary>
    internal void SetNativeImage(nint handle, double width, double height)
    {
        _nativeHandle = handle;
        _width = width;
        _height = height;
    }
}
