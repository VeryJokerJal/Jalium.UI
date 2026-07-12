using Jalium.UI.Media;
using ImagingBitmapSource = Jalium.UI.Media.Imaging.BitmapSource;

namespace Jalium.UI.Interop;

/// <summary>Identifies the native resource accepted by <see cref="D3DImage"/>.</summary>
public enum D3DResourceType
{
    IDirect3DSurface9 = 0,

    // Jalium extensions used by the modern cross-platform media pipeline.
    ID3D11Texture2DShared = 1,
    VkImageExternal = 2,
    AHardwareBuffer = 3,
    NativeVideoSurface = 4,
}

/// <summary>
/// WPF-compatible D3D image surface with Jalium's native-video-surface extension.
/// Native resources remain caller-owned, avoiding unsafe reference-count calls on
/// opaque backend handles.
/// </summary>
public class D3DImage : ImageSource, IDisposable
{
    private static readonly DependencyPropertyKey IsFrontBufferAvailablePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsFrontBufferAvailable),
            typeof(bool),
            typeof(D3DImage),
            new PropertyMetadata(true));

    /// <summary>Identifies <see cref="IsFrontBufferAvailable"/>.</summary>
    public static readonly DependencyProperty IsFrontBufferAvailableProperty =
        IsFrontBufferAvailablePropertyKey.DependencyProperty;

    private readonly double _dpiX;
    private readonly double _dpiY;
    private readonly List<Int32Rect> _dirtyRects = [];
    private int _pixelWidth;
    private int _pixelHeight;
    private IntPtr _backBuffer;
    private D3DResourceType _resourceType;
    private NativeVideoSurface? _videoSurface;
    private bool _softwareFallback;
    private int _lockCount;
    private bool _disposed;

    /// <summary>Initializes a surface at 96 DPI.</summary>
    public D3DImage()
        : this(96, 96)
    {
    }

    /// <summary>Initializes a surface with the requested logical DPI.</summary>
    public D3DImage(double dpiX, double dpiY)
    {
        if (dpiX < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiX), "The DPI value cannot be negative.");
        }

        if (dpiY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiY), "The DPI value cannot be negative.");
        }

        _dpiX = dpiX;
        _dpiY = dpiY;
    }

    /// <summary>Gets whether the D3D front buffer can currently be composed.</summary>
    public bool IsFrontBufferAvailable => (bool)(GetValue(IsFrontBufferAvailableProperty) ?? true);

    /// <summary>Gets the native pixel width.</summary>
    public int PixelWidth => _pixelWidth;

    /// <summary>Gets the native pixel height.</summary>
    public int PixelHeight => _pixelHeight;

    /// <inheritdoc />
    public sealed override double Width => PixelsToDIPs(_dpiX, _pixelWidth);

    /// <inheritdoc />
    public sealed override double Height => PixelsToDIPs(_dpiY, _pixelHeight);

    /// <inheritdoc />
    public sealed override ImageMetadata? Metadata => null;

    /// <inheritdoc />
    public override IntPtr NativeHandle => _backBuffer;

    /// <summary>Gets the bound Jalium resource kind.</summary>
    public D3DResourceType ResourceType => _resourceType;

    /// <summary>Gets the native video surface bound through the Jalium extension.</summary>
    public NativeVideoSurface? VideoSurface => _videoSurface;

    /// <summary>Gets whether software fallback was requested for the legacy surface.</summary>
    public bool IsSoftwareFallbackEnabled => _softwareFallback;

    /// <summary>Gets whether at least one matching <see cref="Lock"/> is active.</summary>
    public bool IsLocked => _lockCount > 0;

    /// <summary>Occurs when <see cref="IsFrontBufferAvailable"/> changes.</summary>
    public event DependencyPropertyChangedEventHandler? IsFrontBufferAvailableChanged;

    /// <summary>Binds a caller-owned native surface.</summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer) =>
        SetBackBuffer(backBufferType, backBuffer, enableSoftwareFallback: false);

    /// <summary>Binds a caller-owned native surface and selects fallback behavior.</summary>
    public void SetBackBuffer(
        D3DResourceType backBufferType,
        IntPtr backBuffer,
        bool enableSoftwareFallback)
    {
        WritePreamble();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lockCount == 0)
        {
            throw new InvalidOperationException("The D3DImage must be locked before its back buffer is changed.");
        }

        if (!Enum.IsDefined(backBufferType))
        {
            throw new ArgumentOutOfRangeException(nameof(backBufferType));
        }

        _videoSurface = null;
        _resourceType = backBufferType;
        _backBuffer = backBuffer;
        _softwareFallback = enableSoftwareFallback;
        _dirtyRects.Clear();
        WritePostscript();
    }

    /// <summary>Binds a Jalium native video surface without transferring ownership.</summary>
    public void SetBackBuffer(NativeVideoSurface? surface)
    {
        WritePreamble();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _videoSurface = surface;
        _resourceType = D3DResourceType.NativeVideoSurface;
        _backBuffer = surface?.Handle ?? IntPtr.Zero;
        _pixelWidth = surface?.PixelWidth ?? 0;
        _pixelHeight = surface?.PixelHeight ?? 0;
        _dirtyRects.Clear();
        WritePostscript();
    }

    /// <summary>Supplies dimensions for opaque legacy handles whose size cannot be queried safely.</summary>
    public void SetPixelSize(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelHeight);
        WritePreamble();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        WritePostscript();
    }

    /// <summary>Acquires the image for a batch of surface updates.</summary>
    public void Lock()
    {
        WritePreamble();
        ObjectDisposedException.ThrowIf(_disposed, this);
        checked
        {
            _lockCount++;
        }
    }

    /// <summary>Attempts to acquire the image within <paramref name="timeout"/>.</summary>
    public bool TryLock(Duration timeout)
    {
        if (timeout == Duration.Automatic)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        Lock();
        return true;
    }

    /// <summary>Completes one update batch.</summary>
    public void Unlock()
    {
        WritePreamble();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lockCount == 0)
        {
            throw new InvalidOperationException("The D3DImage is not locked.");
        }

        _lockCount--;
        if (_lockCount == 0)
        {
            WritePostscript();
        }
    }

    /// <summary>Adds a dirty rectangle to the current update batch.</summary>
    public void AddDirtyRect(Int32Rect dirtyRect)
    {
        WritePreamble();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lockCount == 0)
        {
            throw new InvalidOperationException("The D3DImage must be locked before dirty rectangles are added.");
        }

        if (dirtyRect.X < 0 || dirtyRect.Y < 0 || dirtyRect.Width < 0 || dirtyRect.Height < 0 ||
            dirtyRect.X + dirtyRect.Width > _pixelWidth ||
            dirtyRect.Y + dirtyRect.Height > _pixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(dirtyRect));
        }

        if (!dirtyRect.IsEmpty)
        {
            _dirtyRects.Add(dirtyRect);
        }
    }

    /// <summary>Creates a modifiable clone.</summary>
    public new D3DImage Clone() => (D3DImage)base.Clone();

    /// <summary>Creates a modifiable clone from current values.</summary>
    public new D3DImage CloneCurrentValue() => (D3DImage)base.CloneCurrentValue();

    /// <summary>Copies the CPU-visible back buffer into a bitmap snapshot.</summary>
    protected internal virtual ImagingBitmapSource CopyBackBuffer()
    {
        if (_pixelWidth <= 0 || _pixelHeight <= 0)
        {
            throw new InvalidOperationException("No sized back buffer is available.");
        }

        int stride = checked(_pixelWidth * 4);
        return ImagingBitmapSource.Create(
            _pixelWidth,
            _pixelHeight,
            _dpiX,
            _dpiY,
            PixelFormat.Bgra32,
            palette: null,
            new byte[checked(stride * _pixelHeight)],
            stride);
    }

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new D3DImage(_dpiX, _dpiY);

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyState((D3DImage)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyState((D3DImage)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyState((D3DImage)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyState((D3DImage)sourceFreezable);
    }

    /// <inheritdoc />
    protected sealed override bool FreezeCore(bool isChecking) => false;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _videoSurface = null;
        _backBuffer = IntPtr.Zero;
        _lockCount = 0;
        _dirtyRects.Clear();
        SetFrontBufferAvailability(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Allows the platform compositor to publish front-buffer loss or restoration.</summary>
    internal void SetFrontBufferAvailability(bool available)
    {
        bool oldValue = IsFrontBufferAvailable;
        if (oldValue == available)
        {
            return;
        }

        SetValue(IsFrontBufferAvailablePropertyKey, available);
        IsFrontBufferAvailableChanged?.Invoke(
            this,
            new DependencyPropertyChangedEventArgs(
                IsFrontBufferAvailableProperty,
                oldValue,
                available));
    }

    private void CopyState(D3DImage source)
    {
        _pixelWidth = source._pixelWidth;
        _pixelHeight = source._pixelHeight;
        _backBuffer = source._backBuffer;
        _resourceType = source._resourceType;
        _videoSurface = source._videoSurface;
        _softwareFallback = source._softwareFallback;
        _dirtyRects.Clear();
        _dirtyRects.AddRange(source._dirtyRects);
    }
}
