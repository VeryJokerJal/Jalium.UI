using System.Runtime.InteropServices;

namespace Jalium.UI.Media.Imaging;

/// <summary>Canonical WPF-compatible render target bitmap.</summary>
public sealed partial class RenderTargetBitmap
{
    /// <summary>Clears the render target to transparent pixels.</summary>
    public void Clear() => Clear(Color.FromArgb(0, 0, 0, 0));

    /// <summary>Creates a modifiable copy.</summary>
    public new RenderTargetBitmap Clone() => (RenderTargetBitmap)base.Clone();

    /// <summary>Creates a modifiable copy with current values.</summary>
    public new RenderTargetBitmap CloneCurrentValue() => (RenderTargetBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() =>
        new RenderTargetBitmap(PixelWidth, PixelHeight, DpiX, DpiY, Format);

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyPixelsFrom((RenderTargetBitmap)sourceFreezable);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyPixelsFrom((RenderTargetBitmap)sourceFreezable);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyPixelsFrom((RenderTargetBitmap)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyPixelsFrom((RenderTargetBitmap)sourceFreezable);
    }

    private void CopyPixelsFrom(RenderTargetBitmap source) =>
        Buffer.BlockCopy(source.GetPixelBuffer(), 0, GetPixelBuffer(), 0, GetPixelBuffer().Length);
}

/// <summary>Canonical WPF-compatible writable bitmap.</summary>
public sealed partial class WriteableBitmap
{
    public WriteableBitmap(
        int pixelWidth,
        int pixelHeight,
        double dpiX,
        double dpiY,
        PixelFormat pixelFormat,
        BitmapPalette? palette)
        : this(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, (object?)palette)
    {
    }

    /// <summary>Attempts to lock the back buffer for the specified duration.</summary>
    public bool TryLock(Duration timeout)
    {
        if (!timeout.HasTimeSpan)
        {
            throw new ArgumentException("The lock timeout must contain a finite TimeSpan.", nameof(timeout));
        }

        return TryLock(timeout.TimeSpan);
    }

    /// <summary>Writes a source-buffer rectangle at the requested destination.</summary>
    public void WritePixels(
        Int32Rect sourceRect,
        Array sourceBuffer,
        int sourceBufferStride,
        int destinationX,
        int destinationY)
    {
        ArgumentNullException.ThrowIfNull(sourceBuffer);
        if (sourceBuffer.Rank != 1 || sourceBuffer.GetType().GetElementType()?.IsPrimitive != true)
        {
            throw new ArgumentException("The source must be a one-dimensional primitive array.", nameof(sourceBuffer));
        }

        var bytes = new byte[Buffer.ByteLength(sourceBuffer)];
        Buffer.BlockCopy(sourceBuffer, 0, bytes, 0, bytes.Length);
        WritePixelsCore(sourceRect, bytes, sourceBufferStride, destinationX, destinationY);
    }

    /// <summary>Writes a source-buffer rectangle from unmanaged memory.</summary>
    public void WritePixels(
        Int32Rect sourceRect,
        IntPtr sourceBuffer,
        int sourceBufferSize,
        int sourceBufferStride,
        int destinationX,
        int destinationY)
    {
        if (sourceBuffer == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(sourceBuffer));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sourceBufferSize);
        var bytes = new byte[sourceBufferSize];
        Marshal.Copy(sourceBuffer, bytes, 0, sourceBufferSize);
        WritePixelsCore(sourceRect, bytes, sourceBufferStride, destinationX, destinationY);
    }

    /// <summary>Creates a modifiable copy.</summary>
    public new WriteableBitmap Clone() => (WriteableBitmap)base.Clone();

    /// <summary>Creates a modifiable copy with current values.</summary>
    public new WriteableBitmap CloneCurrentValue() => (WriteableBitmap)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() =>
        new WriteableBitmap(PixelWidth, PixelHeight, DpiX, DpiY, Format, Palette);

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyPixelsFrom((WriteableBitmap)sourceFreezable);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyPixelsFrom((WriteableBitmap)sourceFreezable);
    }

    protected override bool FreezeCore(bool isChecking) => false;

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyPixelsFrom((WriteableBitmap)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyPixelsFrom((WriteableBitmap)sourceFreezable);
    }

    private void WritePixelsCore(
        Int32Rect sourceRect,
        byte[] sourceBuffer,
        int sourceBufferStride,
        int destinationX,
        int destinationY)
    {
        if (sourceRect.X < 0 || sourceRect.Y < 0 || sourceRect.Width < 0 || sourceRect.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRect));
        }

        int bytesPerPixel = checked((Format.BitsPerPixel + 7) / 8);
        int rowBytes = checked(sourceRect.Width * bytesPerPixel);
        if (sourceBufferStride < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceBufferStride));
        }

        int sourceOffset = checked((sourceRect.Y * sourceBufferStride) + (sourceRect.X * bytesPerPixel));
        int required = sourceRect.Height == 0
            ? sourceOffset
            : checked(sourceOffset + ((sourceRect.Height - 1) * sourceBufferStride) + rowBytes);
        if (required > sourceBuffer.Length)
        {
            throw new ArgumentException("The source buffer is too small for sourceRect.", nameof(sourceBuffer));
        }

        var contiguous = new byte[checked(rowBytes * sourceRect.Height)];
        for (int row = 0; row < sourceRect.Height; row++)
        {
            Buffer.BlockCopy(sourceBuffer, sourceOffset + (row * sourceBufferStride),
                contiguous, row * rowBytes, rowBytes);
        }

        WritePixels(
            new Int32Rect(destinationX, destinationY, sourceRect.Width, sourceRect.Height),
            contiguous,
            rowBytes,
            0);
    }

    private void CopyPixelsFrom(WriteableBitmap source) =>
        Buffer.BlockCopy(source.BackBufferArray, 0, BackBufferArray, 0, BackBufferArray.Length);
}
