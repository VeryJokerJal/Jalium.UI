namespace Jalium.UI.Interop;

/// <summary>
/// Represents a native bitmap for drawing images.
/// </summary>
public sealed class NativeBitmap : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the bitmap is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>
    /// Gets the width of the bitmap.
    /// </summary>
    public uint Width { get; }

    /// <summary>
    /// Gets the height of the bitmap.
    /// </summary>
    public uint Height { get; }

    internal NativeBitmap(RenderContext context, byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            throw new ArgumentException("Image data cannot be null or empty", nameof(imageData));

        _handle = NativeMethods.BitmapCreateFromMemory(context.Handle, imageData, (uint)imageData.Length);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create bitmap from image data");
        }

        Width = NativeMethods.BitmapGetWidth(_handle);
        Height = NativeMethods.BitmapGetHeight(_handle);
    }

    internal NativeBitmap(RenderContext context, byte[] pixelData, int width, int height, int stride)
    {
        ArgumentNullException.ThrowIfNull(pixelData);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (stride <= 0)
        {
            stride = checked(width * 4);
        }

        if (stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride must be at least width * 4 bytes.");
        }

        var requiredBytes = checked(stride * height);
        if (pixelData.Length < requiredBytes)
        {
            throw new ArgumentException("Pixel buffer is smaller than the specified dimensions and stride.", nameof(pixelData));
        }

        _handle = NativeMethods.BitmapCreateFromPixels(context.Handle, pixelData, (uint)width, (uint)height, (uint)stride);
        if (_handle == nint.Zero)
        {
            var bmpBytes = EncodeBgraPixelsAsBmp(pixelData, width, height, stride);
            _handle = NativeMethods.BitmapCreateFromMemory(context.Handle, bmpBytes, (uint)bmpBytes.Length);
        }

        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create bitmap from raw pixel data");
        }

        Width = NativeMethods.BitmapGetWidth(_handle);
        Height = NativeMethods.BitmapGetHeight(_handle);
    }

    private static byte[] EncodeBgraPixelsAsBmp(byte[] pixelData, int width, int height, int stride)
    {
        const int fileHeaderSize = 14;
        const int infoHeaderSize = 40;
        var rowBytes = checked(width * 4);
        var pixelBytes = checked(rowBytes * height);
        var totalBytes = checked(fileHeaderSize + infoHeaderSize + pixelBytes);
        var bmp = new byte[totalBytes];

        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteUInt32(bmp, 2, (uint)totalBytes);
        WriteUInt32(bmp, 10, (uint)(fileHeaderSize + infoHeaderSize));
        WriteUInt32(bmp, 14, infoHeaderSize);
        WriteInt32(bmp, 18, width);
        WriteInt32(bmp, 22, height);
        WriteUInt16(bmp, 26, 1);
        WriteUInt16(bmp, 28, 32);
        WriteUInt32(bmp, 30, 0);
        WriteUInt32(bmp, 34, (uint)pixelBytes);

        var destOffset = fileHeaderSize + infoHeaderSize;
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = (height - 1 - row) * stride;
            Buffer.BlockCopy(pixelData, sourceOffset, bmp, destOffset + row * rowBytes, rowBytes);
        }

        return bmp;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset + 0] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
        => WriteUInt32(buffer, offset, unchecked((uint)value));

    /// <summary>
    /// Updates the bitmap's pixels in place. The dimensions must match the bitmap's existing
    /// width / height — size changes still require destroying and recreating the bitmap.
    /// Returns true if the native backend accepted the update (D3D12 / Vulkan), false otherwise
    /// (caller should fall back to recreate).
    /// </summary>
    public bool TryUpdatePixels(byte[] pixelData, int width, int height, int stride)
    {
        if (_disposed || _handle == nint.Zero) return false;
        ArgumentNullException.ThrowIfNull(pixelData);
        if (width <= 0 || height <= 0) return false;
        if (stride <= 0) stride = checked(width * 4);
        if (stride < width * 4) return false;
        if ((uint)width != Width || (uint)height != Height) return false;
        if (pixelData.Length < checked(stride * height)) return false;

        var ok = NativeMethods.BitmapUpdatePixels(_handle, pixelData, (uint)width, (uint)height, (uint)stride);
        return ok != 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_handle != nint.Zero)
        {
            NativeMethods.BitmapDestroy(_handle);
            _handle = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~NativeBitmap()
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.BitmapDestroy(_handle);
        }
        _handle = nint.Zero;
        _disposed = true;
    }
}
