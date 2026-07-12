using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Bitmap source backed by pixels obtained from an unmanaged surface. The native
/// resource remains caller-owned; <see cref="Invalidate()"/> refreshes pixels
/// through the factory-provided reader while the resource remains valid.
/// </summary>
public sealed class InteropBitmap : Jalium.UI.Media.Imaging.BitmapSource
{
    private int _pixelWidth;
    private int _pixelHeight;
    private double _dpiX = 96;
    private double _dpiY = 96;
    private PixelFormat _format = PixelFormat.Bgra32;
    private byte[] _pixels = [];
    private int _stride;
    private Func<byte[]>? _refreshPixels;

    internal InteropBitmap()
    {
    }

    internal InteropBitmap(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        PixelFormat format,
        byte[] pixels,
        int stride,
        Func<byte[]>? refreshPixels = null)
    {
        Initialize(pixelWidth, pixelHeight, dpiX, dpiY, format, pixels, stride, refreshPixels);
    }

    /// <inheritdoc />
    public override double Width => PixelsToDIPs(_dpiX, _pixelWidth);

    /// <inheritdoc />
    public override double Height => PixelsToDIPs(_dpiY, _pixelHeight);

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
    public override IntPtr NativeHandle => IntPtr.Zero;

    /// <summary>Refreshes the complete native bitmap.</summary>
    public void Invalidate() => Invalidate(dirtyRect: null);

    /// <summary>
    /// Refreshes pixels after the caller modifies the native surface. A null
    /// rectangle represents the complete image.
    /// </summary>
    /// <param name="dirtyRect">The changed rectangle, or null for the whole surface.</param>
    public void Invalidate(Int32Rect? dirtyRect)
    {
        WritePreamble();
        if (dirtyRect is Int32Rect rect &&
            (rect.X < 0 || rect.Y < 0 || rect.Width < 0 || rect.Height < 0 ||
             rect.X + rect.Width > _pixelWidth || rect.Y + rect.Height > _pixelHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(dirtyRect));
        }

        if (_refreshPixels is not null)
        {
            byte[] refreshed = _refreshPixels();
            int required = checked(_stride * _pixelHeight);
            if (refreshed.Length < required)
            {
                throw new InvalidOperationException("The native bitmap reader returned an incomplete pixel buffer.");
            }

            if (dirtyRect is not Int32Rect dirty || dirty.IsEmpty)
            {
                _pixels = refreshed[..required];
            }
            else
            {
                int bytesPerPixel = Math.Max(1, (_format.BitsPerPixel + 7) / 8);
                int rowBytes = checked(dirty.Width * bytesPerPixel);
                for (int row = 0; row < dirty.Height; row++)
                {
                    int offset = checked(((dirty.Y + row) * _stride) + (dirty.X * bytesPerPixel));
                    Buffer.BlockCopy(refreshed, offset, _pixels, offset, rowBytes);
                }
            }
        }

        WritePostscript();
    }

    /// <inheritdoc />
    public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        int x = sourceRect.IsEmpty ? 0 : sourceRect.X;
        int y = sourceRect.IsEmpty ? 0 : sourceRect.Y;
        int width = sourceRect.IsEmpty ? _pixelWidth : sourceRect.Width;
        int height = sourceRect.IsEmpty ? _pixelHeight : sourceRect.Height;
        if (x < 0 || y < 0 || width < 0 || height < 0 ||
            x + width > _pixelWidth || y + height > _pixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        }

        int bytesPerPixel = Math.Max(1, (_format.BitsPerPixel + 7) / 8);
        int rowBytes = checked(width * bytesPerPixel);
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
            Buffer.BlockCopy(
                _pixels,
                ((y + row) * _stride) + (x * bytesPerPixel),
                pixels,
                offset + (row * stride),
                rowBytes);
        }
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new InteropBitmap();

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyState((InteropBitmap)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyState((InteropBitmap)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyState((InteropBitmap)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyState((InteropBitmap)sourceFreezable);
    }

    private void Initialize(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        PixelFormat format,
        byte[] pixels,
        int stride,
        Func<byte[]>? refreshPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
        ArgumentNullException.ThrowIfNull(pixels);

        int rowBytes = checked((pixelWidth * format.BitsPerPixel + 7) / 8);
        if (stride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(stride));
        }

        int required = checked(stride * pixelHeight);
        if (pixels.Length < required)
        {
            throw new ArgumentException("The source pixel buffer is too small.", nameof(pixels));
        }

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _dpiX = dpiX > 0 && double.IsFinite(dpiX) ? dpiX : 96;
        _dpiY = dpiY > 0 && double.IsFinite(dpiY) ? dpiY : 96;
        _format = format;
        _pixels = pixels[..required];
        _stride = stride;
        _refreshPixels = refreshPixels;
    }

    private void CopyState(InteropBitmap source)
    {
        _pixelWidth = source._pixelWidth;
        _pixelHeight = source._pixelHeight;
        _dpiX = source._dpiX;
        _dpiY = source._dpiY;
        _format = source._format;
        _pixels = (byte[])source._pixels.Clone();
        _stride = source._stride;
        _refreshPixels = source._refreshPixels;
    }
}
