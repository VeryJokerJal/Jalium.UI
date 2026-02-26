namespace Jalium.UI.Media;

/// <summary>
/// An ImageSource that displays a user-created Direct3D surface.
/// </summary>
public sealed class D3DImage : ImageSource
{
    private double _pixelWidth;
    private double _pixelHeight;
    private bool _isFrontBufferAvailable;

    /// <summary>
    /// Gets a value indicating whether a front buffer is available.
    /// </summary>
    public bool IsFrontBufferAvailable => _isFrontBufferAvailable;

    /// <summary>
    /// Gets the width of the D3DImage.
    /// </summary>
    public override double Width => _pixelWidth;

    /// <summary>
    /// Gets the height of the D3DImage.
    /// </summary>
    public override double Height => _pixelHeight;

    /// <summary>
    /// Gets the native handle (not applicable for D3DImage).
    /// </summary>
    public override nint NativeHandle => nint.Zero;

    /// <summary>
    /// Gets the pixel width.
    /// </summary>
    public int PixelWidth => (int)_pixelWidth;

    /// <summary>
    /// Gets the pixel height.
    /// </summary>
    public int PixelHeight => (int)_pixelHeight;

    /// <summary>
    /// Gets the DPI X of the image.
    /// </summary>
    public double DpiX { get; } = 96.0;

    /// <summary>
    /// Gets the DPI Y of the image.
    /// </summary>
    public double DpiY { get; } = 96.0;

    /// <summary>
    /// Assigns a Direct3D surface as the source of the back buffer.
    /// </summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer)
    {
    }

    /// <summary>
    /// Assigns a Direct3D surface as the source of the back buffer with a flag.
    /// </summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer, bool enableSoftwareFallback)
    {
    }

    /// <summary>
    /// Locks the D3DImage for updates.
    /// </summary>
    public void Lock()
    {
    }

    /// <summary>
    /// Unlocks the D3DImage.
    /// </summary>
    public void Unlock()
    {
    }

    /// <summary>
    /// Attempts to lock the D3DImage for updates.
    /// </summary>
    /// <param name="timeout">The timeout to wait.</param>
    /// <returns>True if the lock was acquired.</returns>
    public bool TryLock(TimeSpan timeout)
    {
        return true;
    }

    /// <summary>
    /// Specifies the area of the back buffer that changed.
    /// </summary>
    public void AddDirtyRect(Int32Rect dirtyRect)
    {
    }

    /// <summary>
    /// Occurs when the front buffer becomes available or unavailable.
    /// </summary>
    public event EventHandler? IsFrontBufferAvailableChanged;
}

/// <summary>
/// Specifies the type of Direct3D resource used with D3DImage.
/// </summary>
public enum D3DResourceType
{
    /// <summary>
    /// Specifies an IDirect3DSurface9 resource.
    /// </summary>
    IDirect3DSurface9 = 0
}
