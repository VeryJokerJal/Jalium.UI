using System.Runtime.InteropServices;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// Canonical WPF-compatible bitmap source surface. The legacy
/// <see cref="Jalium.UI.Media.BitmapSource"/> remains as the storage/runtime base so existing
/// renderer integrations continue to work while public imaging APIs use the WPF namespace.
/// </summary>
public abstract class BitmapSource : Jalium.UI.Media.BitmapSource
{
    /// <summary>Occurs when decoding fails.</summary>
    public virtual event EventHandler<ExceptionEventArgs>? DecodeFailed;

    /// <summary>Occurs when an asynchronous download completes.</summary>
    public virtual event EventHandler? DownloadCompleted;

    /// <summary>Occurs when an asynchronous download fails.</summary>
    public virtual event EventHandler<ExceptionEventArgs>? DownloadFailed;

    /// <summary>Occurs when asynchronous download progress changes.</summary>
    public virtual event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    /// <summary>Gets whether this source is currently downloading.</summary>
    public virtual bool IsDownloading => false;

    /// <inheritdoc />
    public override ImageMetadata? Metadata => base.Metadata;

    /// <summary>Creates a modifiable copy of this bitmap source.</summary>
    public new BitmapSource Clone() => (BitmapSource)base.Clone();

    /// <summary>Creates a modifiable copy using current property values.</summary>
    public new BitmapSource CloneCurrentValue() => (BitmapSource)base.CloneCurrentValue();

    /// <summary>Copies all bitmap pixels into a primitive array.</summary>
    public virtual void CopyPixels(Array pixels, int stride, int offset) =>
        CopyPixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), pixels, stride, offset);

    /// <summary>Copies a bitmap region into a primitive array.</summary>
    public virtual void CopyPixels(Int32Rect sourceRect, Array pixels, int stride, int offset)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Rank != 1 || pixels.GetType().GetElementType()?.IsPrimitive != true)
        {
            throw new ArgumentException("The destination must be a one-dimensional primitive array.", nameof(pixels));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (pixels is byte[] bytes)
        {
            ((Jalium.UI.Media.BitmapSource)this).CopyPixels(sourceRect, bytes, stride, offset);
            return;
        }

        int byteLength = Buffer.ByteLength(pixels);
        var buffer = new byte[byteLength];
        ((Jalium.UI.Media.BitmapSource)this).CopyPixels(sourceRect, buffer, stride, offset);
        Buffer.BlockCopy(buffer, 0, pixels, 0, byteLength);
    }

    /// <summary>Copies a bitmap region to unmanaged memory.</summary>
    public virtual void CopyPixels(Int32Rect sourceRect, IntPtr buffer, int bufferSize, int stride)
    {
        if (buffer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
        var pixels = new byte[bufferSize];
        ((Jalium.UI.Media.BitmapSource)this).CopyPixels(sourceRect, pixels, stride, 0);
        Marshal.Copy(pixels, 0, buffer, pixels.Length);
    }

    /// <summary>Creates a managed bitmap source from a primitive pixel array.</summary>
    public static BitmapSource Create(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        PixelFormat pixelFormat,
        BitmapPalette? palette,
        Array pixels,
        int stride)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Rank != 1 || pixels.GetType().GetElementType()?.IsPrimitive != true)
        {
            throw new ArgumentException("The pixel source must be a one-dimensional primitive array.", nameof(pixels));
        }

        var bytes = new byte[Buffer.ByteLength(pixels)];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return new ManagedBitmapSource(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, palette, bytes, stride);
    }

    /// <summary>Creates a managed bitmap source from unmanaged pixel memory.</summary>
    public static BitmapSource Create(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        PixelFormat pixelFormat,
        BitmapPalette? palette,
        IntPtr buffer,
        int bufferSize,
        int stride)
    {
        if (buffer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);
        var pixels = new byte[bufferSize];
        Marshal.Copy(buffer, pixels, 0, bufferSize);
        return new ManagedBitmapSource(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, palette, pixels, stride);
    }

    /// <summary>Compatibility hook used by pack-site bitmap sources.</summary>
    protected void CheckIfSiteOfOrigin()
    {
    }

    /// <summary>Raises <see cref="DecodeFailed"/>.</summary>
    protected void OnDecodeFailed(Exception exception) =>
        DecodeFailed?.Invoke(this, new ExceptionEventArgs(exception));

    /// <summary>Raises <see cref="DownloadCompleted"/>.</summary>
    protected void OnDownloadCompleted() => DownloadCompleted?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="DownloadFailed"/>.</summary>
    protected void OnDownloadFailed(Exception exception) =>
        DownloadFailed?.Invoke(this, new ExceptionEventArgs(exception));

    /// <summary>Raises <see cref="DownloadProgress"/>.</summary>
    protected void OnDownloadProgress(int progress) =>
        DownloadProgress?.Invoke(this, new DownloadProgressEventArgs(progress));

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable) => base.CloneCore(sourceFreezable);

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) => base.CloneCurrentValueCore(sourceFreezable);

    /// <inheritdoc />
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable) => base.GetAsFrozenCore(sourceFreezable);

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) => base.GetCurrentValueAsFrozenCore(sourceFreezable);

    private sealed class ManagedBitmapSource : BitmapSource
    {
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;
        private readonly double _dpiX;
        private readonly double _dpiY;
        private readonly PixelFormat _format;
        private readonly BitmapPalette? _palette;
        private readonly byte[] _pixels;
        private readonly int _stride;

        public ManagedBitmapSource(
            int pixelWidth,
            int pixelHeight,
            double dpiX,
            double dpiY,
            PixelFormat format,
            BitmapPalette? palette,
            byte[] pixels,
            int stride)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);

            int rowBytes = checked((pixelWidth * format.BitsPerPixel + 7) / 8);
            if (stride < rowBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than one pixel row.");
            }

            int requiredLength = checked(stride * pixelHeight);
            if (pixels.Length < requiredLength)
            {
                throw new ArgumentException("The pixel buffer is smaller than the requested bitmap.", nameof(pixels));
            }

            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
            _dpiX = dpiX > 0 ? dpiX : 96;
            _dpiY = dpiY > 0 ? dpiY : 96;
            _format = format;
            _palette = palette;
            _pixels = pixels[..requiredLength];
            _stride = stride;
        }

        public override double Width => PixelsToDIPs(_dpiX, _pixelWidth);
        public override double Height => PixelsToDIPs(_dpiY, _pixelHeight);
        public override nint NativeHandle => nint.Zero;
        public override int PixelWidth => _pixelWidth;
        public override int PixelHeight => _pixelHeight;
        public override double DpiX => _dpiX;
        public override double DpiY => _dpiY;
        public override PixelFormat Format => _format;
        public override BitmapPalette? Palette => _palette;

        public override void CopyPixels(Int32Rect sourceRect, byte[] pixels, int stride, int offset)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            int width = sourceRect.Width == 0 ? _pixelWidth : sourceRect.Width;
            int height = sourceRect.Height == 0 ? _pixelHeight : sourceRect.Height;
            int x = sourceRect.X;
            int y = sourceRect.Y;
            if (x < 0 || y < 0 || width < 0 || height < 0 || x + width > _pixelWidth || y + height > _pixelHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceRect));
            }

            int bytesPerPixel = (_format.BitsPerPixel + 7) / 8;
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

        protected override Freezable CreateInstanceCore() =>
            new ManagedBitmapSource(_pixelWidth, _pixelHeight, _dpiX, _dpiY, _format, _palette, _pixels, _stride);
    }
}

/// <summary>Provides data for bitmap download progress notifications.</summary>
public sealed class DownloadProgressEventArgs : EventArgs
{
    /// <summary>Initializes a new instance with a percentage from 0 through 100.</summary>
    public DownloadProgressEventArgs(int progress)
    {
        if (progress is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        Progress = progress;
    }

    /// <summary>Gets the completed download percentage.</summary>
    public int Progress { get; }
}
