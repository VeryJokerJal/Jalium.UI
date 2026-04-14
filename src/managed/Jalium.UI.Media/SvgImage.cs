namespace Jalium.UI.Media;

/// <summary>
/// An ImageSource that loads and renders SVG (Scalable Vector Graphics) content
/// as vector drawings. SVG content is parsed into a <see cref="DrawingGroup"/>
/// which is rendered at any resolution without quality loss.
/// </summary>
public sealed class SvgImage : ImageSource, IDisposable
{
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
    /// Creates an SvgImage from a byte array containing SVG content.
    /// </summary>
    public static SvgImage FromBytes(byte[] data)
    {
        var image = new SvgImage();
        var svgContent = System.Text.Encoding.UTF8.GetString(data);
        image.LoadFromString(svgContent);
        return image;
    }

    /// <summary>
    /// Creates an SvgImage from a stream containing SVG content.
    /// </summary>
    public static SvgImage FromStream(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        var svgContent = reader.ReadToEnd();
        var image = new SvgImage();
        image.LoadFromString(svgContent);
        return image;
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

            OnSvgLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgImage] Failed to parse SVG: {ex.Message}");
        }
    }

    private void LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            return;

        try
        {
            var svgContent = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            LoadFromString(svgContent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgImage] Failed to load SVG file: {ex.Message}");
        }
    }

    private void LoadFromUri(Uri uri)
    {
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
    }

    private async Task LoadFromHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var httpClient = new System.Net.Http.HttpClient();
            var svgContent = await httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() => LoadFromString(svgContent));
            }
            else
            {
                LoadFromString(svgContent);
            }
        }
        catch (OperationCanceledException)
        {
            // Load was cancelled
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgImage] Failed to load SVG from HTTP: {ex.Message}");
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
