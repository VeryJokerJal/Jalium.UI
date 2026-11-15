using Jalium.UI.Diagnostics;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media;
using Jalium.UI.Media.Native;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Represents a bitmap image source. PNG / JPEG / WebP / GIF / BMP / HEIF input is
/// decoded to BGRA8 pixels by the platform-native <see cref="INativeImageDecoder"/>
/// (WIC on Windows, NDK <c>AImageDecoder</c> / <c>BitmapFactory</c> on Android).
/// </summary>
/// <summary>
/// WPF-compatible bitmap image with Jalium native-decoder and media-frame extensions.
/// </summary>
public sealed partial class BitmapImage : BitmapSource, IDisposable, IReclaimableResource
{
    private static INativeImageDecoder? s_decoder;
    private static readonly object s_decoderLock = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        System.Reflection.Assembly,
        string[]> s_manifestNameCache = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        WeakReference<System.Reflection.Assembly>> s_resourceAssemblyHints =
        new(StringComparer.OrdinalIgnoreCase);

    private nint _nativeHandle;

    /// <summary>
    /// The CANONICAL pixel geometry: the natural decoded size with the author's
    /// <c>SourceRect</c> / <c>DecodePixelWidth</c> / <c>Rotation</c> applied. Backs
    /// <see cref="Width"/>, <see cref="Height"/>, <see cref="PixelWidth"/> and
    /// <see cref="PixelHeight"/>.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately NOT the display bucket the renderer currently holds. The bucket is a
    /// function of the DPI and of the layout slot this source happens to be drawn into, so
    /// mirroring it here made the intrinsic size layout-derived — and the intrinsic size is an
    /// INPUT to layout. With <c>Stretch.None</c> the draw rect IS the intrinsic size and the draw
    /// rect is what the renderer turns into the next decode hint, so each publish asked for a
    /// bigger bucket than the last and an ordinary fixed-size grid cell paid three or more full
    /// native decodes and re-laid-out its subtree after every one of them. It also made
    /// <c>Image.OnSourceAsyncLoaded</c>'s measure-skip unreachable, because the reported size
    /// changed on every publish.</para>
    /// <para>The resident raster's own geometry lives in <see cref="_rasterPixelWidth"/> /
    /// <see cref="_rasterPixelHeight"/> / <see cref="_pixelStride"/>, and the two must never be
    /// crossed: handing a canonical dimension triple to a bucket-sized buffer is an out-of-range
    /// read whose only symptom is a black image.</para>
    /// </remarks>
    private double _width;
    private double _height;

    private Uri? _uriSource;
    private Uri? _baseUri;
    private byte[]? _imageData;
    private byte[]? _rawPixelData;
    private int _pixelStride;

    /// <summary>
    /// Geometry of <see cref="_rawPixelData"/> — the raster actually resident, which for a
    /// deferred source is a display bucket no larger than the canonical size.
    /// </summary>
    /// <remarks>
    /// Written by every site that writes <see cref="_rawPixelData"/>, under
    /// <c>_deferredDecodeLock</c>, so the buffer and the numbers that describe it are always one
    /// writer's complete tuple. This is what <c>TryGetPixelSnapshot</c>'s legacy fallback reads;
    /// it used to read <see cref="_width"/>/<see cref="_height"/>, which only worked while those
    /// mirrored the bucket.
    /// </remarks>
    private int _rasterPixelWidth;
    private int _rasterPixelHeight;
    // Publication id for the legacy _rawPixelData mirror. Bumped by every site that replaces the
    // buffer outside the deferred decoder so a cache keyed on BitmapPixelSnapshot.Generation
    // still notices that a raster was swapped out from under it.
    private long _legacyPixelGeneration;
    private CancellationTokenSource? _httpCts;
    private bool _isDownloading;
    // Serializes TryRestorePixelData so concurrent renderers don't each pay a
    // decode for the same reclaimed image.
    private readonly object _pixelRestoreLock = new();
    // Guarded by _deferredDecodeLock; callers share an eager cache restore
    // without ever waiting for the synchronous decoder on the UI thread.
    private Task? _eagerPixelPreloadTask;
    private long _eagerPixelPreloadVersion;
    private static readonly SemaphoreSlim s_eagerPixelRestoreGate = new(BitmapDecodeScheduler.MaxWorkers);

    /// <summary>
    /// Occurs when the image has been loaded from a remote source.
    /// </summary>
    public event EventHandler? OnImageLoaded;

    /// <inheritdoc />
    /// <remarks>
    /// <para>The intrinsic width every layout pass measures from: the canonical decoded size,
    /// invariant across the display buckets the renderer climbs through. See
    /// <see cref="_width"/>.</para>
    /// <para><b>Asynchronous for a URI source.</b> WPF decodes inside <see cref="EndInit"/>, so it
    /// can answer immediately; this pipeline decodes off the UI thread at the size layout turns
    /// out to need, so a <see cref="UriSource"/>-backed bitmap reports 0 until its first decode
    /// publishes. <c>Image</c> covers the layout half of that window itself with a header probe, so
    /// an element does not measure at zero waiting for pixels. An application that needs the size
    /// on the calling thread has the eager paths — <see cref="FromFile"/>,
    /// <see cref="FromBytes"/>, <see cref="StreamSource"/> — or can wait for
    /// <see cref="OnImageLoaded"/>.</para>
    /// </remarks>
    public override double Width => _width;

    /// <inheritdoc />
    /// <remarks>The intrinsic height. See <see cref="Width"/>.</remarks>
    public override double Height => _height;

    /// <inheritdoc />
    public override nint NativeHandle => _nativeHandle;

    /// <summary>
    /// Gets the raw image data bytes (encoded PNG/JPEG/etc.).
    /// </summary>
    public byte[]? ImageData => _imageData;

    /// <summary>
    /// Gets the raw BGRA8 buffer of the raster currently RESIDENT, which for a URI-backed source
    /// is a display bucket that may be smaller than <see cref="PixelWidth"/>x<see cref="PixelHeight"/>.
    /// </summary>
    /// <remarks>
    /// <para>Pair it with <see cref="RasterPixelWidth"/>, <see cref="RasterPixelHeight"/> and
    /// <see cref="PixelStride"/> — never with <see cref="PixelWidth"/>/<see cref="PixelHeight"/>,
    /// which report the canonical size and would describe more pixels than this buffer holds.
    /// Callers that want the full-resolution image should use
    /// <see cref="CopyPixels(Int32Rect, byte[], int, int)"/>, which resolves it for them.</para>
    /// <para>Null once the idle reclaimer has dropped the pixels; the next draw restores them.</para>
    /// </remarks>
    public byte[]? RawPixelData => _rawPixelData;

    /// <summary>
    /// Gets the canonical pixel width — the natural decoded width with the author's decode options
    /// applied, matching WPF. Independent of the display bucket currently resident.
    /// </summary>
    /// <remarks>
    /// An explicit <see cref="DecodePixelWidth"/> is honoured exactly, as in WPF: it names the
    /// decode resolution rather than an upper bound the display-bucket ladder may shrink further.
    /// Zero until the first decode publishes for a <see cref="UriSource"/>-backed bitmap; see
    /// <see cref="Width"/>.
    /// </remarks>
    public override int PixelWidth => (int)Math.Round(_width);

    /// <summary>
    /// Gets the canonical pixel height. See <see cref="PixelWidth"/>.
    /// </summary>
    public override int PixelHeight => (int)Math.Round(_height);

    /// <summary>
    /// Gets the pixel width of <see cref="RawPixelData"/> — the resident display bucket, which is
    /// less than or equal to <see cref="PixelWidth"/>. Zero when no raster is resident.
    /// </summary>
    /// <remarks>
    /// Exists so a consumer reading <see cref="RawPixelData"/> directly has a dimension pair that
    /// actually describes that buffer. Every consumer inside the framework goes through the
    /// internal snapshot instead, which carries the same numbers as one immutable tuple.
    /// </remarks>
    public int RasterPixelWidth => _rasterPixelWidth;

    /// <summary>
    /// Gets the pixel height of <see cref="RawPixelData"/>. See <see cref="RasterPixelWidth"/>.
    /// </summary>
    public int RasterPixelHeight => _rasterPixelHeight;

    /// <inheritdoc />
    public override bool IsDownloading => _isDownloading;

    /// <summary>
    /// Gets the number of bytes between two adjacent rows of <see cref="RawPixelData"/>.
    /// </summary>
    public int PixelStride => _pixelStride;

    /// <summary>
    /// Gets or sets the URI source of the bitmap image.
    /// </summary>
    private Uri? UriSourceCore
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
    private Uri? BaseUriCore
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

        int minimumStride = PixelBufferLayout.GetMinimumStride(width);
        if (stride <= 0)
        {
            stride = minimumStride;
        }

        var minimumBytes = PixelBufferLayout.GetRequiredByteCount(width, height, stride);
        if (pixels.Length < minimumBytes)
        {
            throw new ArgumentException("Pixel buffer is smaller than the specified dimensions and stride.", nameof(pixels));
        }

        var pixelCopy = new byte[minimumBytes];
        Buffer.BlockCopy(pixels, 0, pixelCopy, 0, minimumBytes);

        var image = new BitmapImage();
        image.PublishLegacyPixels(pixelCopy, width, height, stride, NativePixelFormat.Bgra8);
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

        var image = new BitmapImage();
        image.PublishLegacyPixels(copy, frame.Width, frame.Height, frame.Stride, frame.Format);
        return image;
    }

    private void LoadFromUri(Uri uri)
    {
        if (uri.IsAbsoluteUri && (uri.IsFile || uri.Scheme == "file"))
        {
            ConfigureDeferredFileSource(uri.LocalPath);
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
            if (TryConfigureFromAssemblyResource(uri.OriginalString))
            {
                return;
            }

            var basePath = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(basePath))
            {
                var diskCandidate = System.IO.Path.Combine(basePath, uri.OriginalString);
                if (System.IO.File.Exists(diskCandidate))
                {
                    ConfigureDeferredFileSource(diskCandidate);
                    return;
                }
            }

            ReportLoadFailure(new FileNotFoundException(
                $"The image resource '{uri.OriginalString}' could not be found.",
                uri.OriginalString));
            return;
        }

        // An absolute URI in a scheme none of the branches above handles — pack://, ms-appx://,
        // res://, a custom scheme — used to fall out of this method in complete silence: no
        // deferred source was configured, no failure was latched, nothing was enqueued, so the
        // element drew nothing and reported nothing for the life of the process. That is
        // indistinguishable from a working image with transparent content, and it is the third of
        // the three "no trace at all" cases a support capture cannot currently explain.
        ReportLoadFailure(new NotSupportedException(
            $"The image URI scheme '{uri.Scheme}' is not supported. Use a file path, a file:// " +
            "URI, http(s), or a relative URI resolved against the application's resources."));
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
    /// Walks every loaded assembly looking for a manifest resource that matches
    /// <paramref name="relativePath"/>. Mirrors the candidate-name strategy used by
    /// ThemeLoader for <c>ResourceDictionary Source="..."</c> so consumer XAML and
    /// code-behind can share the same authoring shape.
    /// Returns <c>true</c> when a deferred resource descriptor was configured.
    /// </summary>
    private bool TryConfigureFromAssemblyResource(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        var separators = new[] { '/', '\\' };
        var dotted = relativePath.Replace('/', '.').Replace('\\', '.').TrimStart('.');
        var lastSep = relativePath.LastIndexOfAny(separators);
        var fileName = lastSep >= 0 ? relativePath.Substring(lastSep + 1) : relativePath;
        var directoryHint = lastSep >= 0
            ? relativePath[..lastSep].Replace('\\', '/').Trim('/')
            : string.Empty;

        var frameworkAssembly = typeof(BitmapImage).Assembly;
        System.Reflection.Assembly? hintedAssembly = null;
        if (s_resourceAssemblyHints.TryGetValue(directoryHint, out var hint) &&
            hint.TryGetTarget(out hintedAssembly) &&
            TryConfigureFromAssembly(hintedAssembly, dotted, fileName, directoryHint))
        {
            return true;
        }

        foreach (var assembly in EnumerateResourceCandidateAssemblies())
        {
            if (assembly.IsDynamic || assembly == frameworkAssembly ||
                ReferenceEquals(assembly, hintedAssembly))
            {
                continue;
            }

            if (TryConfigureFromAssembly(assembly, dotted, fileName, directoryHint))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 枚举所有可能承载图片清单资源的程序集。
    ///
    /// <para><see cref="AppDomain.CurrentDomain"/><c>.GetAssemblies()</c> 只覆盖**默认**
    /// <see cref="System.Runtime.Loader.AssemblyLoadContext"/>。模块化 / 插件式宿主用自定义
    /// ALC 加载模块时，模块程序集不在该枚举里，于是 <c>new BitmapImage(new Uri("x.png",
    /// UriKind.Relative))</c> 找不到模块内嵌资源、静默回落到 <c>AppContext.BaseDirectory</c>
    /// 磁盘探测、最终整幅图加载失败。被这么坑过的调用方只能改去手写
    /// <c>GetManifestResourceStream</c> + <see cref="FromBytes"/>——而那条路是急切全分辨率
    /// 解码且会永久关闭 display-bucket 降采样，一张 1910x823 的 PNG 用来显示 230x118
    /// 就要付 6 MiB 常驻和数十毫秒同步解码。所以这里必须把自定义 ALC 一并纳入。</para>
    ///
    /// <para><see cref="System.Runtime.Loader.AssemblyLoadContext.All"/> 覆盖默认 ALC 与全部
    /// 自定义 ALC。NativeAOT 等不支持多 ALC 的运行时上该属性可能不可用，故整体 try 兜底
    /// 回落到原来的 AppDomain 枚举；两个来源合并后按引用去重。</para>
    /// </summary>
    private static IEnumerable<System.Reflection.Assembly> EnumerateResourceCandidateAssemblies()
    {
        var appDomainAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        List<System.Reflection.Assembly>? fromContexts = null;
        try
        {
            foreach (var context in System.Runtime.Loader.AssemblyLoadContext.All)
            {
                foreach (var assembly in context.Assemblies)
                {
                    (fromContexts ??= new List<System.Reflection.Assembly>()).Add(assembly);
                }
            }
        }
        catch
        {
            // 运行时不支持枚举 ALC——只用 AppDomain 快照，行为与修复前一致。
            fromContexts = null;
        }

        if (fromContexts is null || fromContexts.Count == 0)
        {
            return appDomainAssemblies;
        }

        var seen = new HashSet<System.Reflection.Assembly>(appDomainAssemblies);
        var merged = new List<System.Reflection.Assembly>(appDomainAssemblies);
        foreach (var assembly in fromContexts)
        {
            if (seen.Add(assembly))
            {
                merged.Add(assembly);
            }
        }

        return merged;
    }

    private bool TryConfigureFromAssembly(
        System.Reflection.Assembly assembly,
        string dotted,
        string fileName,
        string directoryHint)
    {
        string[] manifestNames;
        try
        {
            manifestNames = s_manifestNameCache.GetValue(
                assembly,
                static candidate => candidate.GetManifestResourceNames());
        }
        catch
        {
            return false;
        }

        if (manifestNames.Length == 0)
            return false;

        var assemblyName = assembly.GetName().Name ?? string.Empty;
        var assemblyDotted = string.IsNullOrEmpty(assemblyName) ? null : assemblyName + "." + dotted;
        var assemblyFileName = string.IsNullOrEmpty(assemblyName) ? null : assemblyName + "." + fileName;
        var actual = Array.Find(
            manifestNames,
            name => string.Equals(name, dotted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, assemblyDotted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, assemblyFileName, StringComparison.OrdinalIgnoreCase));
        if (actual is null)
            return false;

        s_resourceAssemblyHints[directoryHint] = new WeakReference<System.Reflection.Assembly>(assembly);
        var resourceName = actual;
        ConfigureDeferredSource(() => ReadManifestResource(assembly, resourceName));
        return true;
    }

    private static byte[] ReadManifestResource(
        System.Reflection.Assembly assembly,
        string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException(
                $"The image resource '{resourceName}' is no longer available.",
                resourceName);
        using var copy = new MemoryStream(
            stream.CanSeek && stream.Length <= int.MaxValue ? (int)stream.Length : 0);
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Points a deferred source at an on-disk file, honouring <see cref="CacheOption"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="BitmapCacheOption.OnLoad"/> means "read it all now, I am about to delete or
    /// replace this file", and it is the standard way an application makes the
    /// download-to-temp / load / delete and the load-then-overwrite-in-place idioms safe. The
    /// deferred pipeline captures a path and reads it on a decode worker some time after the first
    /// layout pass, so without honouring the option those idioms silently loaded the NEW bytes or
    /// failed outright — where before the deferred decoder existed, <c>LoadFromFile</c> read the
    /// file inside the property setter and both worked regardless of the option. Only the byte
    /// acquisition moves forward: the decode itself, the display-bucket ladder and the scheduler
    /// are untouched, so an OnLoad source still decodes off the UI thread at the size layout asks
    /// for.</para>
    /// <para>A read that fails is reported AND thrown, exactly as the missing-file check above
    /// does. Throwing is the point of the option — the caller asked to find out now — and it
    /// matches both WPF, which surfaces the <see cref="IOException"/> out of <c>EndInit</c>, and
    /// the behaviour this framework had before the source became deferred. The default option is
    /// unaffected and still reaches the file lazily, where a transient failure gets the decoder's
    /// bounded, delayed retry instead.</para>
    /// </remarks>
    private void ConfigureDeferredFileSource(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        if (!System.IO.File.Exists(filePath))
        {
            var failure = new FileNotFoundException(
                $"The image file '{filePath}' could not be found.",
                filePath);
            ReportLoadFailure(failure);
            throw failure;
        }

        byte[]? eagerBytes = null;
        if (CacheOption == BitmapCacheOption.OnLoad)
        {
            try
            {
                eagerBytes = System.IO.File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                ReportLoadFailure(ex);
                throw;
            }
        }

        ConfigureDeferredSource(() => System.IO.File.ReadAllBytes(filePath), eagerBytes);
    }

    private async Task LoadFromHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        var dispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;
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

    /// <summary>Shares CSS-fetched encoded bytes with the normal deferred decode and animation pipeline.</summary>
    internal static BitmapImage FromDeferredBytes(byte[] data)
    {
        var image = new BitmapImage();
        image.ConfigureDeferredSource(() => data, data);
        return image;
    }

    private void LoadFromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Image data is empty.", nameof(data));

        try
        {
            ResetDeferredSource();
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
    private void LoadFromStreamSource(Stream stream)
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
    private void CopyBitmapStateFrom(BitmapImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _nativeHandle = source._nativeHandle;
        _width = source._width;
        _height = source._height;
        _uriSource = source._uriSource;
        _baseUri = source._baseUri;
        _imageData = source._imageData is null ? null : (byte[])source._imageData.Clone();

        // ONE publication, not six unsynchronised field reads. The buffer, its geometry, its
        // channel order and the canonical size it was derived from were produced together, and
        // TryGetPixelSnapshot hands them over as the tuple they were written as — taking the decode
        // lock for the legacy quad, which is what makes "a decode published while this clone was
        // being assembled" unrepresentable rather than merely unlikely.
        if (source.TryGetPixelSnapshot(out var sourceSnapshot) && sourceSnapshot is not null)
        {
            // A fresh buffer is a fresh publication as far as any generation-keyed cache is
            // concerned. CopyDeferredStateFrom runs after this and re-points the clone at the
            // source's shared immutable snapshot when there is one; this covers eager sources.
            //
            // The FORMAT travels with the pixels. Hardcoding Bgra8 here relabelled an Rgba8 raster
            // — anything from a custom INativeImageDecoder, FromDecodedImage(Rgba8) or a media
            // frame — so Clone()/GetAsFrozen() of such a source came back with red and blue
            // swapped, which no later stage can detect or undo. So does the canonical size, so the
            // clone reports the same intrinsic size as its original rather than the size of
            // whatever display bucket it happened to be holding.
            PublishLegacyPixels(
                (byte[])sourceSnapshot.Pixels.Clone(),
                sourceSnapshot.Width,
                sourceSnapshot.Height,
                sourceSnapshot.Stride,
                sourceSnapshot.Format,
                sourceSnapshot.CanonicalWidth,
                sourceSnapshot.CanonicalHeight);
            return;
        }

        var sourcePixels = source._rawPixelData;
        if (sourcePixels is null)
        {
            _rawPixelData = null;
            _pixelStride = 0;
            _rasterPixelWidth = 0;
            _rasterPixelHeight = 0;
            _legacyPixelGeneration = 0;
            return;
        }

        // A buffer exists but no publication describes it: the source is mid-construction or
        // malformed, which is precisely the state TryGetPixelSnapshot refuses. Cloning a raster
        // nobody can interpret through PublishLegacyPixels would throw out of Clone(), so mirror
        // the raw fields and let the consumer's own geometry check reject it — exactly as it did
        // before the snapshot channel existed. Unlocked writes are safe here and only here: this
        // instance is a clone target that the Freezable protocol has not published yet, so no
        // reader can observe the intermediate state.
        _rawPixelData = (byte[])sourcePixels.Clone();
        _pixelStride = source._pixelStride;
        _rasterPixelWidth = source._rasterPixelWidth;
        _rasterPixelHeight = source._rasterPixelHeight;
        _legacyPixelGeneration = BitmapPixelSnapshot.NextGeneration();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>Copies from the CANONICAL raster, so <paramref name="sourceRect"/> is authored in the
    /// same coordinates <see cref="PixelWidth"/>/<see cref="PixelHeight"/> report and the caller
    /// gets the resolution it was promised. That is what makes the clipboard, the OLE drag source,
    /// the window icon, the notification backends and <c>BitmapPalette</c> correct: every one of
    /// them sizes a buffer from <c>PixelWidth</c> and then calls this method.</para>
    /// <para>It reads one immutable publication rather than <c>_rawPixelData</c> plus three
    /// separately-read numbers. The old form validated against one read of the geometry and then
    /// copied using a later re-read of the buffer and the stride, so a decode publishing in
    /// between produced silently mixed-buffer output — and, once the reported size stopped being
    /// the resident bucket's, an out-of-range read.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">No raster is resident and none can be rebuilt.</exception>
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (!TryGetCanonicalPixelSnapshot(out var snapshot) || snapshot is null)
        {
            throw new InvalidOperationException("The bitmap has not been decoded.");
        }

        int sourceWidth = snapshot.Width;
        int sourceHeight = snapshot.Height;
        int width = sourceRect.Width == 0 ? sourceWidth : sourceRect.Width;
        int height = sourceRect.Height == 0 ? sourceHeight : sourceRect.Height;
        if (sourceRect.X < 0 || sourceRect.Y < 0 || width < 0 || height < 0 ||
            sourceRect.X + width > sourceWidth || sourceRect.Y + height > sourceHeight)
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
            Buffer.BlockCopy(snapshot.Pixels,
                ((sourceRect.Y + row) * snapshot.Stride) + (sourceRect.X * 4),
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

    /// <summary>
    /// Publishes an already-decoded, natural-size image — the eager path behind
    /// <see cref="FromBytes"/>, <see cref="FromFile"/>, <see cref="FromDecodedImage"/>,
    /// <c>StreamSource</c> and the HTTP loader.
    /// </summary>
    /// <remarks>
    /// This is where the eager path applies the author's decode options, for the same reason the
    /// deferred decoder applies them inside the decode: <paramref name="decoded"/> is in NATURAL
    /// coordinates, which is the space <see cref="SourceRect"/> is authored in. Doing it here also
    /// keeps the two paths to exactly one application each — <c>ApplySource</c> used to run the
    /// transform a second time after this method, and the decode-completion handler a third time
    /// on every later publish.
    /// </remarks>
    private void AdoptDecoded(DecodedImage decoded)
    {
        var options = CaptureDecodeOptions();
        if (!options.IsIdentity)
        {
            try
            {
                decoded = ApplyDecodeOptions(decoded, options);
            }
            catch (Exception ex)
            {
                // An author error — a SourceRect outside the image, a DecodePixel size that
                // overflows — must not take the picture away. Report it through the Release-live
                // channel and publish the untransformed raster: an uncropped image the author can
                // see is diagnosable, a blank element is not. Deliberately NOT routed to
                // ReportLoadFailure: the source loaded fine, one option on it did not.
                ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "decode options", ex);
            }
        }

        // 解码器声明缓冲专属时直接接管。原先无条件再拷一份全尺寸副本是纯浪费：
        // 框架自带解码器的 CopyAndFree 已经把原生像素搬进一个刚 new 出来的托管数组，
        // 第二次拷贝等于每张全分辨率图多付一次 LOH 分配加一次全尺寸 memcpy
        // （1910x823 BGRA = 6 MiB/张），页面切换同帧解码多张时直接决定是否触发阻塞 GC。
        // 除标记外仍校验数组形状（零偏移、长度恰好），第三方解码器不声明所有权则保守拷贝。
        byte[] pixels;
        if (decoded.BufferIsExclusive &&
            System.Runtime.InteropServices.MemoryMarshal.TryGetArray(decoded.Pixels, out var segment) &&
            segment.Array is { } exclusive &&
            segment.Offset == 0 &&
            segment.Count == exclusive.Length)
        {
            pixels = exclusive;
        }
        else
        {
            var span = decoded.Pixels.Span;
            pixels = new byte[span.Length];
            span.CopyTo(pixels);
        }

        PublishLegacyPixels(pixels, decoded.Width, decoded.Height, decoded.Stride, decoded.Format);

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
        CancelDeferredDecode();
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
    /// <para>Called by the framework's idle-resource reclaimer when an
    /// <see cref="IReclaimableResource"/> element that owns this source has
    /// stayed off-screen past the configured idle window — see
    /// <c>JaliumAppExtensions.UseIdleResourceReclamation</c>. Safe to call
    /// directly to free memory under pressure.</para>
    /// <para>Refused while the source is still being drawn. The reclaimer's idle test is per
    /// ELEMENT and this resource belongs to a SOURCE, so an element that went off-screen while
    /// another element — or an image brush, or an image drawing, none of which the reclaimer can
    /// see at all — keeps painting the same bitmap would otherwise free the pixels out from under
    /// the live consumer. What the user sees is the visible image blanking for the length of one
    /// re-decode and coming back, over and over: the reclaim clears the published decode state, the
    /// live consumer's next frame finds no pixels and re-requests a full decode, that republishes,
    /// and the next scan reclaims it again. The cycle sustains itself for as long as anything keeps
    /// producing frames, which is why it reads as flicker while scrolling and stops the moment the
    /// UI goes quiet.</para>
    /// </remarks>
    public void ReclaimIdleResources()
    {
        if (WasDrawnRecently())
        {
            return;
        }

        // Always evict GPU uploads — they can be rebuilt from either
        // _rawPixelData (if still around) or _imageData (re-decode).
        RaiseGpuCacheEviction(this);

        // Drop CPU pixels only when we still have an encoded source we can
        // re-decode from; otherwise the image would be unrecoverable.
        if (_imageData != null)
        {
            // Under the decode lock: this used to race a publishing decode worker, so a reclaim
            // could null the buffer between a worker's dimension write and its pixel write.
            // Clearing the whole publication state also resets the unproductive-attempt counter,
            // without which four reclaim/restore cycles would trip the saturation bound.
            lock (_deferredDecodeLock)
            {
                ClearPublishedDecodeStateLocked(dropLegacyPixels: true);
            }
        }
    }

    /// <summary>
    /// Re-decodes <see cref="ImageData"/> back into the BGRA8 pixel buffer that
    /// <see cref="ReclaimIdleResources"/> dropped, so a reclaimed image returns
    /// to the cheap upload path instead of paying a full native decode on every
    /// subsequent GPU cache miss. No-op when pixels are already present, and
    /// returns false when there is no encoded source to rebuild from.
    /// </summary>
    /// <remarks>
    /// Only refills the decode cache — it deliberately does NOT raise
    /// <c>OnImageLoaded</c>, because nothing about the image changed from a
    /// consumer's point of view; replaying the load event would re-trigger
    /// layout for what is purely an internal cache restore.
    /// </remarks>
    internal bool TryRestorePixelData()
    {
        // Residency is a question about the PUBLICATION, not about one field of the legacy mirror.
        // Testing _rawPixelData directly read one quarter of an unsynchronized four-field tuple:
        // a buffer whose dimensions had not landed yet answered "already restored", and the caller
        // went straight on to read a geometry triple that did not describe it. TryGetPixelSnapshot
        // answers from the immutable snapshot, and its legacy fallback validates the quad under the
        // decode lock before synthesising one, so "true" here really does mean "there are pixels a
        // consumer can interpret".
        if (TryGetPixelSnapshot(out _))
        {
            return true;
        }

        bool deferred;
        int width;
        int height;
        bool cover;
        BitmapDecodeOptions options;
        byte[]? encoded;
        long version;
        lock (_deferredDecodeLock)
        {
            deferred = _isDeferredSource;

            // Re-request at the size CURRENT layout needs, not at the bucket the reclaimed decode
            // happened to publish. "Reclaimed" and "never decoded" are the same state to the
            // deferred decoder — DecodeUpgradeNeededLocked returns true whenever no snapshot is
            // published — so the restore is free to aim at the live request. Read inside the same
            // lock acquisition that answered `deferred`, so the pair cannot straddle a source swap.
            GetRequestedDecodeSizeLocked(out width, out height, out cover);

            // Latched with the pair above so a SetDecodeOptions racing this restore cannot pair one
            // source's bytes with another's transform.
            options = _decodeOptions;
            encoded = _imageData;
            version = _deferredSourceVersion;
        }

        if (deferred)
        {
            RequestDecode(width, height, cover);
            return false;
        }

        if (encoded == null || encoded.Length == 0)
        {
            return false;
        }

        lock (_pixelRestoreLock)
        {
            if (TryGetPixelSnapshot(out _))
            {
                return true;
            }

            try
            {
                var decoded = ResolveDecoder().Decode(encoded);

                // The reclaimed buffer was the TRANSFORMED raster, so the rebuild has to replay the
                // same transform. Restoring the raw decode silently republished the uncropped,
                // unrotated, full-size image under the cropped image's identity: SourceRect and
                // DecodePixelWidth were applied once at load and then quietly discarded by the first
                // reclaim/restore cycle. ApplyDecodeOptions is pure over the decoded image, so this
                // reproduces the original raster exactly rather than approximating it.
                if (!options.IsIdentity)
                {
                    decoded = ApplyDecodeOptions(decoded, options);
                }

                var span = decoded.Pixels.Span;
                var copy = new byte[span.Length];
                span.CopyTo(copy);

                lock (_deferredDecodeLock)
                {
                    if (version != _deferredSourceVersion || !ReferenceEquals(encoded, _imageData))
                        return false;
                    PublishLegacyPixels(copy, decoded.Width, decoded.Height, decoded.Stride, decoded.Format);
                }
                return true;
            }
            catch (Exception ex)
            {
                // The encoded bytes decoded once already, so a failure here is exceptional; fall
                // back to the caller's native decode path.
                //
                // NOT Debug.WriteLine. That is [Conditional("DEBUG")], so in the builds users run
                // a failed restore returned false with no counter, no event and no log line, and
                // the image went permanently blank with nothing to diagnose it by.
                ImageDiagnostics.DecodeFailed(DiagnosticSourceName, "reclaimed pixel restore", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Sets the native handle and the CANONICAL dimensions (called by the rendering backend for a
    /// bitmap whose pixels live only on the GPU).
    /// </summary>
    /// <remarks>
    /// <para>Writes the intrinsic geometry only. It deliberately does not touch
    /// <see cref="_rasterPixelWidth"/>/<see cref="_rasterPixelHeight"/>, which describe
    /// <see cref="RawPixelData"/>: a handle-backed bitmap has no managed raster, and claiming one
    /// with these dimensions would hand a large dimension pair to whatever stale buffer happened
    /// to be resident.</para>
    /// <para>Under the decode lock so the intrinsic size cannot be observed half-written by the
    /// render thread.</para>
    /// </remarks>
    internal void SetNativeImage(nint handle, double width, double height)
    {
        lock (_deferredDecodeLock)
        {
            _nativeHandle = handle;
            _width = width;
            _height = height;
        }
    }
}
