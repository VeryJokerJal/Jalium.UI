using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Native;

namespace Jalium.UI.Media;

/// <summary>
/// Represents a bitmap image source. PNG / JPEG / WebP / GIF / BMP / HEIF input is
/// decoded to BGRA8 pixels by the platform-native <see cref="INativeImageDecoder"/>
/// (WIC on Windows, NDK <c>AImageDecoder</c> / <c>BitmapFactory</c> on Android).
/// </summary>
public sealed class BitmapImage : ImageSource, IDisposable, IReclaimableResource
{
    private static INativeImageDecoder? s_decoder;
    private static readonly object s_decoderLock = new();

    private nint _nativeHandle;
    private double _width;
    private double _height;
    private Uri? _uriSource;
    private byte[]? _imageData;
    private byte[]? _rawPixelData;
    private int _pixelStride;
    private CancellationTokenSource? _httpCts;

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
    public int PixelWidth => (int)Math.Round(_width);

    /// <summary>
    /// Gets the pixel height.
    /// </summary>
    public int PixelHeight => (int)Math.Round(_height);

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
            if (value != null)
            {
                LoadFromUri(value);
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
                }
            }
        }
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
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var httpClient = new System.Net.Http.HttpClient();
            var bytes = await httpClient.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (dispatcher != null)
            {
                // Fire-and-forget marshal back to the UI thread; BeginInvoke now returns a
                // DispatcherOperation (awaitable), so discard it to signal intentional no-await.
                _ = dispatcher.BeginInvoke(() => LoadFromBytes(bytes));
            }
            else
            {
                LoadFromBytes(bytes);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
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

        _imageData = data;
        var decoder = GetDecoderOrThrow();
        var decoded = decoder.Decode(data);
        AdoptDecoded(decoded);
    }

    private void LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var bytes = System.IO.File.ReadAllBytes(filePath);
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
