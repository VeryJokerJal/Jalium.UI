namespace Jalium.UI.Media;

/// <summary>
/// Represents the source of an image.
/// </summary>
public abstract class ImageSource
{
    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    public abstract double Width { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    public abstract double Height { get; }

    /// <summary>
    /// Gets the native handle of the image (platform-specific).
    /// </summary>
    public abstract nint NativeHandle { get; }
}

/// <summary>
/// Represents a bitmap image source.
/// </summary>
public sealed class BitmapImage : ImageSource, IDisposable
{
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

    /// <summary>
    /// Gets the width of the image.
    /// </summary>
    public override double Width => _width;

    /// <summary>
    /// Gets the height of the image.
    /// </summary>
    public override double Height => _height;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public override nint NativeHandle => _nativeHandle;

    /// <summary>
    /// Gets the raw image data bytes.
    /// </summary>
    public byte[]? ImageData => _imageData;

    /// <summary>
    /// Gets the raw BGRA8 pixel buffer when the image was created from pixels.
    /// </summary>
    public byte[]? RawPixelData => _rawPixelData;

    /// <summary>
    /// Gets the pixel width when raw pixel data is available.
    /// </summary>
    public int PixelWidth => (int)Math.Round(_width);

    /// <summary>
    /// Gets the pixel height when raw pixel data is available.
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
            // Cancel any in-flight HTTP load before starting a new one
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
    /// Creates a BitmapImage from a file path.
    /// </summary>
    public static BitmapImage FromFile(string filePath)
    {
        var image = new BitmapImage();
        image.LoadFromFile(filePath);
        return image;
    }

    /// <summary>
    /// Creates a BitmapImage from raw pixel data.
    /// </summary>
    /// <param name="pixels">The pixel data in BGRA format.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="stride">The number of bytes between two adjacent rows. Defaults to <c>width * 4</c>.</param>
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

    private void LoadFromUri(Uri uri)
    {
        if (uri.IsFile || uri.Scheme == "file")
        {
            LoadFromFile(uri.LocalPath);
        }
        else if (uri.Scheme == "http" || uri.Scheme == "https")
        {
            // Load asynchronously from HTTP/HTTPS URL
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
            var bytes = await httpClient.GetByteArrayAsync(uri, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() => LoadFromBytes(bytes));
            }
            else
            {
                LoadFromBytes(bytes);
            }
        }
        catch (OperationCanceledException)
        {
            // Load was cancelled (URI changed or object disposed)
        }
        catch
        {
            // Image load failed
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
        // Store the raw bytes for the native rendering backend to process
        _imageData = data;
        _rawPixelData = null;
        _pixelStride = 0;

        // Try to read image dimensions from the header
        if (TryReadImageDimensions(data, out var width, out var height))
        {
            _width = width;
            _height = height;
        }
        else
        {
            // Default dimensions if we can't read the header
            _width = 100;
            _height = 100;
        }

        // Notify that the image has been updated
        OnImageLoaded?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryReadImageDimensions(byte[] data, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (data == null || data.Length < 24)
            return false;

        // Check for PNG signature
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            // PNG: width at offset 16, height at offset 20 (big-endian)
            width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return true;
        }

        // Check for JPEG signature
        if (data[0] == 0xFF && data[1] == 0xD8)
        {
            // JPEG: need to parse markers to find SOF
            int offset = 2;
            while (offset < data.Length - 1)
            {
                if (data[offset] != 0xFF)
                {
                    offset++;
                    continue;
                }

                if (offset + 1 >= data.Length)
                    break;

                byte marker = data[offset + 1];

                // SOF0, SOF1, SOF2 (Start of Frame markers)
                if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                {
                    if (offset + 8 >= data.Length)
                        break;

                    height = (data[offset + 5] << 8) | data[offset + 6];
                    width = (data[offset + 7] << 8) | data[offset + 8];
                    return true;
                }

                // Skip to next marker
                if (offset + 3 >= data.Length)
                    break;

                int length = (data[offset + 2] << 8) | data[offset + 3];
                offset += length + 2;
            }
        }

        return false;
    }

    private void LoadFromFile(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return;
        }

        try
        {
            // Read the file bytes and load using the bytes loader
            // The native rendering backend (NativeBitmap) will decode the image
            // using Windows Imaging Component (WIC) which supports PNG, JPG, BMP, GIF, etc.
            var bytes = System.IO.File.ReadAllBytes(filePath);
            LoadFromBytes(bytes);
        }
        catch
        {
            // Image load failed silently
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
    /// Sets the native handle and dimensions (called by the rendering backend).
    /// </summary>
    internal void SetNativeImage(nint handle, double width, double height)
    {
        _nativeHandle = handle;
        _width = width;
        _height = height;
    }
}

// Note: Stretch enum is defined in Brush.cs, StretchDirection is defined in Jalium.UI.Controls.Viewbox.cs
