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
        _disposed = true;
        _handle = nint.Zero;
    }
}
