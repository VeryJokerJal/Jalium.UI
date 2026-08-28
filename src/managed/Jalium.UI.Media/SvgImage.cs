namespace Jalium.UI.Media;

/// <summary>
/// An ImageSource that loads and renders SVG (Scalable Vector Graphics) content
/// as vector drawings. SVG content is parsed into a <see cref="DrawingGroup"/>
/// which is rendered at any resolution without quality loss.
/// </summary>
public sealed class SvgImage : ImageSource, IDisposable
{
    /// <inheritdoc />
    public override ImageMetadata? Metadata => null;

    private double _width;
    private double _height;
    private Uri? _uriSource;
    private CancellationTokenSource? _httpCts;

    /// <summary>
    /// Occurs when the SVG has been loaded from a remote source.
    /// </summary>
    public event EventHandler? OnSvgLoaded;

    /// <summary>
    /// Gets or sets the Drawing that provides the SVG content.
    /// </summary>
    public Drawing? Drawing { get; set; }

    /// <summary>
    /// Gets the width of the SVG image.
    /// </summary>
    public override double Width => _width > 0 ? _width : (Drawing?.Bounds.Width ?? 0);

    /// <summary>
    /// Gets the height of the SVG image.
    /// </summary>
    public override double Height => _height > 0 ? _height : (Drawing?.Bounds.Height ?? 0);

    /// <summary>
    /// Gets the native handle. SvgImage does not have a native handle.
    /// </summary>
    public override nint NativeHandle => 0;

    /// <summary>
    /// Gets or sets the URI source of the SVG image.
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
                LoadFromUri(value);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgImage"/> class.
    /// </summary>
    public SvgImage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgImage"/> class with the specified URI.
    /// </summary>
    public SvgImage(Uri uriSource)
    {
        UriSource = uriSource;
    }

    /// <summary>
    /// Creates an SvgImage from an SVG content string.
    /// </summary>
    public static SvgImage FromSvgString(string svgContent)
    {
        var image = new SvgImage();
        image.LoadFromString(svgContent);
        return image;
    }

    /// <summary>
    /// Creates an SvgImage from a file path.
    /// </summary>
    public static SvgImage FromFile(string filePath)
    {
        var image = new SvgImage();
        image.LoadFromFile(filePath);
        return image;
    }

    /// <summary>
    /// Creates an SvgImage from a byte array containing SVG content
    /// (plain XML or gzip-compressed .svgz).
    /// </summary>
    public static SvgImage FromBytes(byte[] data)
    {
        var image = new SvgImage();
        image.LoadFromString(DecodeSvgText(data));
        return image;
    }

    /// <summary>
    /// Creates an SvgImage from a stream containing SVG content
    /// (plain XML or gzip-compressed .svgz).
    /// </summary>
    public static SvgImage FromStream(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var image = new SvgImage();
        image.LoadFromString(DecodeSvgText(buffer.ToArray()));
        return image;
    }

    /// <summary>
    /// Turns raw SVG bytes into markup text, transparently inflating gzip-compressed
    /// payloads (.svgz — magic bytes 1F 8B). Reading an .svgz with a text decoder
    /// produced garbage that failed XML parsing, so the extension was advertised by
    /// <see cref="IsSvgFile"/> but never actually loadable.
    /// </summary>
    private static string DecodeSvgText(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x1F && data[1] == 0x8B)
        {
            using var input = new MemoryStream(data);
            using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, System.Text.Encoding.UTF8);
            return reader.ReadToEnd();
        }

        return System.Text.Encoding.UTF8.GetString(data);
    }

    private void LoadFromString(string svgContent)
    {
        try
        {
            var (drawing, width, height) = SvgParser.Parse(svgContent);
            Drawing = drawing;
            _width = width;
            _height = height;

            // If dimensions still not resolved, use drawing bounds
            if (_width <= 0 || _height <= 0)
            {
                var bounds = drawing.Bounds;
                if (!bounds.IsEmpty)
                {
                    if (_width <= 0) _width = bounds.X + bounds.Width;
                    if (_height <= 0) _height = bounds.Y + bounds.Height;
                }
            }

            ClearLoadFailure();
            OnSvgLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportSvgFailure(DiagnosticSourceName, "SVG parse", ex);
        }
    }

    private void LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            // A path that does not exist used to return in silence, which made a typo in a Source
            // URI indistinguishable from a working image that happens to draw nothing — the exact
            // "renders blank, reports nothing" shape this channel exists to eliminate.
            ReportSvgFailure(
                filePath,
                "SVG file missing",
                new FileNotFoundException($"SVG file '{filePath}' was not found.", filePath));
            return;
        }

        try
        {
            // Bytes first so gzip-compressed .svgz files inflate instead of being fed
            // to the XML parser as mojibake.
            var bytes = System.IO.File.ReadAllBytes(filePath);
            LoadFromString(DecodeSvgText(bytes));
        }
        catch (Exception ex)
        {
            ReportSvgFailure(filePath, "SVG file read", ex);
        }
    }

    /// <summary>Stable identity for this source in diagnostics records.</summary>
    private string DiagnosticSourceName => _uriSource?.ToString() ?? "<svg>";

    /// <inheritdoc />
    private protected override string DiagnosticIdentity => DiagnosticSourceName;

    /// <summary>
    /// Records an SVG load or parse failure on the Release-live diagnostics channel and routes it
    /// into <see cref="ImageSource.LoadFailed"/>, and through that into <c>Image.ImageFailed</c>.
    /// </summary>
    /// <remarks>
    /// Every call site used to be a bare <c>Debug.WriteLine</c>, which is
    /// <c>[Conditional("DEBUG")]</c> and therefore absent from the builds users actually run: a
    /// malformed, missing or unreachable SVG rendered as nothing at all, with no evidence anywhere
    /// in the process and no event the application could handle. Do not put a
    /// <c>Debug.WriteLine</c> back here.
    /// </remarks>
    private void ReportSvgFailure(string source, string stage, Exception exception)
    {
        // Reported here rather than through ReportLoadFailure's own bridge because this names the
        // FILE PATH and the stage, which a bare source identity ("<svg>" for a path-loaded image)
        // cannot. ReportDiagnosedLoadFailure latches and announces without adding a second record.
        Jalium.UI.Diagnostics.ImageDiagnostics.DecodeFailed(source, stage, exception);
        ReportDiagnosedLoadFailure(exception);
    }

    /// <summary>
    /// Resolves a relative URI the same way <see cref="Imaging.BitmapImage"/> does: assembly
    /// manifest resources first (covering <c>&lt;Resource Include="..."/&gt;</c> items embedded by
    /// Jalium.UI.Build), then a disk-relative probe against <see cref="AppContext.BaseDirectory"/>
    /// for projects that ship the file as <c>&lt;Content CopyToOutputDirectory="..."/&gt;</c>.
    /// </summary>
    private void LoadFromRelativeUri(string relativePath)
    {
        var dotted = relativePath.Replace('/', '.').Replace('\\', '.').TrimStart('.');
        var lastSep = relativePath.LastIndexOfAny(new[] { '/', '\\' });
        var fileName = lastSep >= 0 ? relativePath[(lastSep + 1)..] : relativePath;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;

            string[] names;
            try { names = assembly.GetManifestResourceNames(); }
            catch { continue; }
            if (names.Length == 0) continue;

            var assemblyName = assembly.GetName().Name ?? string.Empty;
            var match = Array.Find(names, n =>
                string.Equals(n, dotted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, assemblyName + "." + dotted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, assemblyName + "." + fileName, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(match);
                if (stream is null) continue;
                using var buffer = new System.IO.MemoryStream();
                stream.CopyTo(buffer);
                LoadFromString(DecodeSvgText(buffer.ToArray()));
                return;
            }
            catch (Exception ex)
            {
                ReportSvgFailure(match, "SVG manifest resource", ex);
                return;
            }
        }

        var basePath = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(basePath))
        {
            var candidate = System.IO.Path.Combine(basePath, relativePath);
            if (System.IO.File.Exists(candidate))
            {
                LoadFromFile(candidate);
                return;
            }
        }

        ReportSvgFailure(
            relativePath,
            "SVG relative URI",
            new FileNotFoundException(
                $"The SVG resource '{relativePath}' could not be found in any loaded assembly's " +
                "manifest resources or relative to the application's base directory.",
                relativePath));
    }

    private void LoadFromUri(Uri uri)
    {
        // Relative URIs must be handled BEFORE any scheme inspection: Uri.IsFile and Uri.Scheme
        // both throw InvalidOperationException on a relative URI, so an
        // <Image Source="Assets/icon.svg"/> used to take down the XAML build rather than render.
        if (!uri.IsAbsoluteUri)
        {
            LoadFromRelativeUri(uri.OriginalString);
            return;
        }

        if (uri.IsFile || uri.Scheme == "file")
        {
            LoadFromFile(uri.LocalPath);
        }
        else if (uri.Scheme == "http" || uri.Scheme == "https")
        {
            var cts = new CancellationTokenSource();
            _httpCts = cts;
            _ = LoadFromHttpAsync(uri, cts.Token);
        }
        else
        {
            // Every other scheme used to fall out of this method in silence: no drawing, no
            // failure, no record — an <Image Source="pack://application:,,,/logo.svg"/> that
            // renders nothing and reports nothing, which is indistinguishable from a working
            // source whose content happens to be empty. Reported and latched like any other load
            // failure, so it reaches ImageFailed and the trace file.
            ReportSvgFailure(
                uri.ToString(),
                "SVG URI scheme",
                new NotSupportedException(
                    $"The SVG URI scheme '{uri.Scheme}' is not supported; use a file path, a " +
                    "file:// URI, http(s), or assign the markup through SvgImage.FromSvgString."));
        }
    }

    private async Task LoadFromHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        // Resolved before the first await, i.e. still on the thread that assigned UriSource. Hoisted
        // out of the try because the failure leg needs it too: the continuation below resumes on a
        // pool thread (ConfigureAwait(false)) and BOTH legs end in application code — a Drawing swap
        // plus OnSvgLoaded, or the LoadFailed chain and through it a routed-event raise.
        var dispatcher = Jalium.UI.Threading.Dispatcher.CurrentDispatcher;

        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            var svgContent = await httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (dispatcher != null)
            {
                // Fire-and-forget marshal back to the UI thread; BeginInvoke now returns a
                // DispatcherOperation (awaitable), so discard it to signal intentional no-await.
                _ = dispatcher.BeginInvoke(() => LoadFromString(svgContent));
            }
            else
            {
                LoadFromString(svgContent);
            }
        }
        catch (OperationCanceledException)
        {
            // Load was cancelled — UriSource was replaced or cleared. Not a failure, so it is
            // deliberately not reported: an application that swaps sources quickly would otherwise
            // see ImageFailed for every superseded load.
        }
        catch (Exception ex)
        {
            // Counters and trace records are thread-safe, so they are taken here. The LoadFailed
            // chain is not: it reaches a routed-event raise and a dependency-property read on the
            // element that owns this source, and this continuation is on a pool thread. Marshal
            // exactly that half back to the dispatcher that owned the assignment.
            Jalium.UI.Diagnostics.ImageDiagnostics.DecodeFailed(uri.ToString(), "SVG HTTP load", ex);
            if (dispatcher != null)
            {
                _ = dispatcher.BeginInvoke(() => ReportLoadFailure(ex));
            }
            else
            {
                ReportLoadFailure(ex);
            }
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
    /// Determines whether the given file path points to an SVG file.
    /// </summary>
    public static bool IsSvgFile(string path)
    {
        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the given byte data appears to be SVG content.
    /// </summary>
    public static bool IsSvgContent(byte[] data)
    {
        if (data == null || data.Length < 5)
            return false;

        // Check for UTF-8 BOM or XML declaration or SVG tag
        var span = data.AsSpan();

        // Skip UTF-8 BOM if present
        var offset = 0;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            offset = 3;

        // Skip whitespace
        while (offset < span.Length && (span[offset] == ' ' || span[offset] == '\t' || span[offset] == '\r' || span[offset] == '\n'))
            offset++;

        if (offset >= span.Length) return false;

        // Check for '<' (XML/SVG start)
        if (span[offset] != '<') return false;

        // Quick check: look for "svg" or "?xml" in the first 1024 bytes
        var checkLength = Math.Min(span.Length, 1024);
        var header = System.Text.Encoding.UTF8.GetString(data, offset, checkLength - offset);
        return header.Contains("<svg", StringComparison.OrdinalIgnoreCase) ||
               (header.Contains("<?xml", StringComparison.OrdinalIgnoreCase) &&
                data.Length < 50000 && // Only do full scan for reasonably sized files
                System.Text.Encoding.UTF8.GetString(data).Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }
}
