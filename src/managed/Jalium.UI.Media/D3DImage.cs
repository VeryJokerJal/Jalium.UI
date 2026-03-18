namespace Jalium.UI.Media;

/// <summary>
/// An ImageSource that displays a user-created Direct3D surface.
/// </summary>
public sealed class D3DImage : ImageSource
{
    private double _pixelWidth;
    private double _pixelHeight;
    private bool _isFrontBufferAvailable;
    private nint _backBuffer;
    private D3DResourceType _backBufferType;
    private bool _enableSoftwareFallback;
    private int _lockCount;
    private readonly List<Int32Rect> _dirtyRects = new();

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
    public override nint NativeHandle => _backBuffer;

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
    /// Gets whether software fallback was requested when the back buffer was assigned.
    /// </summary>
    public bool IsSoftwareFallbackEnabled => _enableSoftwareFallback;

    /// <summary>
    /// Gets whether the image is currently locked for updates.
    /// </summary>
    public bool IsLocked => _lockCount > 0;

    /// <summary>
    /// Assigns a Direct3D surface as the source of the back buffer.
    /// </summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer)
    {
        SetBackBuffer(backBufferType, backBuffer, enableSoftwareFallback: false);
    }

    /// <summary>
    /// Assigns a Direct3D surface as the source of the back buffer with a flag.
    /// </summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer, bool enableSoftwareFallback)
    {
        _backBufferType = backBufferType;
        _backBuffer = backBuffer;
        _enableSoftwareFallback = enableSoftwareFallback;
        _dirtyRects.Clear();

        var isFrontBufferAvailable = backBuffer != IntPtr.Zero;
        if (_isFrontBufferAvailable != isFrontBufferAvailable)
        {
            _isFrontBufferAvailable = isFrontBufferAvailable;
            IsFrontBufferAvailableChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Locks the D3DImage for updates.
    /// </summary>
    public void Lock()
    {
        checked
        {
            _lockCount++;
        }
    }

    /// <summary>
    /// Unlocks the D3DImage.
    /// </summary>
    public void Unlock()
    {
        if (_lockCount == 0)
        {
            throw new InvalidOperationException("The D3DImage is not locked.");
        }

        _lockCount--;
    }

    /// <summary>
    /// Attempts to lock the D3DImage for updates.
    /// Returns false if the front buffer is not available (e.g., device lost).
    /// </summary>
    /// <param name="timeout">The timeout to wait for availability.</param>
    /// <returns>True if the lock was acquired; false if the front buffer is unavailable.</returns>
    public bool TryLock(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (!_isFrontBufferAvailable)
        {
            return false;
        }

        Lock();
        return true;
    }

    /// <summary>
    /// Specifies the area of the back buffer that changed.
    /// </summary>
    public void AddDirtyRect(Int32Rect dirtyRect)
    {
        if (dirtyRect.Width < 0 || dirtyRect.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dirtyRect));
        }

        if (dirtyRect.IsEmpty)
        {
            return;
        }

        _dirtyRects.Add(dirtyRect);
    }

    /// <summary>
    /// Occurs when the front buffer becomes available or unavailable.
    /// </summary>
    public event EventHandler? IsFrontBufferAvailableChanged;

    /// <summary>
    /// Sets the pixel dimensions reported by the D3DImage.
    /// </summary>
    public void SetPixelSize(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelHeight);

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
    }
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
