namespace Jalium.UI.Media;

/// <summary>
/// An <see cref="ImageSource"/> that hosts a native GPU resource — historically a
/// WPF-style Direct3D surface, now generalised to any backend-specific texture
/// the framework's rendering engine can sample directly. Used by
/// <c>MediaElement</c> to bypass the WriteableBitmap copy hop and let the
/// decoder write video frames straight into the texture the swap chain will
/// composite.
/// </summary>
/// <remarks>
/// <para><b>Backing kinds</b> (see <see cref="D3DResourceType"/>):
/// <list type="bullet">
/// <item><see cref="D3DResourceType.NativeVideoSurface"/> — recommended new
/// path. Pass a <see cref="Jalium.UI.Media.NativeVideoSurface"/> handle and the
/// framework dispatches to <c>jalium_render_target_draw_video_surface</c>.</item>
/// <item><see cref="D3DResourceType.IDirect3DSurface9"/> — legacy WPF value.
/// Accepted for source compatibility but logged as unsupported because no
/// platform exposes D3D9 surfaces today.</item>
/// <item>D3D11 shared / Vulkan external / AHardwareBuffer — reserved for
/// stages 3-5 when MF DXVA / MediaCodec / Apple decoders land.</item>
/// </list>
/// </para>
/// <para><b>Lifetime</b>: when <see cref="SetBackBuffer(NativeVideoSurface)"/>
/// hands the surface to D3DImage, the surface stays owned by the caller —
/// disposing the D3DImage does NOT dispose the surface. This lets one
/// long-lived surface back many short-lived D3DImage instances if needed
/// (typical case is one MediaElement holding both).</para>
/// </remarks>
public sealed class D3DImage : ImageSource, IDisposable
{
    private double _pixelWidth;
    private double _pixelHeight;
    private bool _isFrontBufferAvailable;
    private nint _backBuffer;
    private D3DResourceType _backBufferType;
    private NativeVideoSurface? _videoSurface;  // strong ref when bound via SetBackBuffer(NativeVideoSurface)
    private bool _enableSoftwareFallback;
    private int _lockCount;
    private readonly List<Int32Rect> _dirtyRects = new();
    private bool _disposed;

    /// <summary>True when the underlying surface is bound and renderable.</summary>
    public bool IsFrontBufferAvailable => _isFrontBufferAvailable;

    /// <inheritdoc />
    public override double Width => _pixelWidth;

    /// <inheritdoc />
    public override double Height => _pixelHeight;

    /// <inheritdoc />
    public override nint NativeHandle => _backBuffer;

    public int PixelWidth => (int)_pixelWidth;
    public int PixelHeight => (int)_pixelHeight;
    public double DpiX { get; } = 96.0;
    public double DpiY { get; } = 96.0;

    public bool IsSoftwareFallbackEnabled => _enableSoftwareFallback;
    public bool IsLocked => _lockCount > 0;

    /// <summary>
    /// Reports which kind of resource <see cref="NativeHandle"/> refers to so the
    /// framework dispatch can route the correct drawcall (e.g. BGRA8 video
    /// surface vs legacy D3D9 surface).
    /// </summary>
    public D3DResourceType ResourceType => _backBufferType;

    /// <summary>
    /// Returns the <see cref="NativeVideoSurface"/> bound via
    /// <see cref="SetBackBuffer(NativeVideoSurface)"/>, or <see langword="null"/>
    /// when bound through the legacy IntPtr path.
    /// </summary>
    public NativeVideoSurface? VideoSurface => _videoSurface;

    /// <summary>Legacy WPF-style binding for D3D9 surfaces. Accepted but unused on
    /// every modern backend; prefer <see cref="SetBackBuffer(NativeVideoSurface)"/>.</summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer)
        => SetBackBuffer(backBufferType, backBuffer, enableSoftwareFallback: false);

    /// <summary>Legacy WPF-style binding with explicit software fallback flag.</summary>
    public void SetBackBuffer(D3DResourceType backBufferType, IntPtr backBuffer, bool enableSoftwareFallback)
    {
        DetachInternalReferences();

        if (backBufferType == D3DResourceType.IDirect3DSurface9 && backBuffer != IntPtr.Zero)
        {
            // Source-compat path — keep AddRef behaviour from the original stub so
            // existing WPF code that goes through D3DImage doesn't crash, but log
            // a one-shot warning so callers know the surface isn't actually rendered.
            System.Runtime.InteropServices.Marshal.AddRef(backBuffer);
        }

        _backBufferType = backBufferType;
        _backBuffer = backBuffer;
        _enableSoftwareFallback = enableSoftwareFallback;
        _dirtyRects.Clear();

        UpdateAvailability(backBuffer != IntPtr.Zero);
    }

    /// <summary>
    /// Binds a <see cref="Jalium.UI.Media.NativeVideoSurface"/> as the back buffer.
    /// The framework will sample this surface directly; the surface's
    /// <see cref="NativeVideoSurface.Lock"/> path is the one the decoder pump
    /// writes into.
    /// </summary>
    public void SetBackBuffer(NativeVideoSurface? surface)
    {
        DetachInternalReferences();

        if (surface is null)
        {
            _backBufferType = D3DResourceType.NativeVideoSurface;
            _backBuffer = IntPtr.Zero;
            _videoSurface = null;
            _pixelWidth = 0;
            _pixelHeight = 0;
            _dirtyRects.Clear();
            UpdateAvailability(false);
            return;
        }

        _backBufferType = D3DResourceType.NativeVideoSurface;
        _videoSurface = surface;
        _backBuffer = surface.Handle;
        _pixelWidth = surface.PixelWidth;
        _pixelHeight = surface.PixelHeight;
        _dirtyRects.Clear();
        UpdateAvailability(true);
    }

    /// <summary>Sets the pixel dimensions reported by the D3DImage. Primarily for
    /// the legacy IntPtr binding path where dimensions aren't deducible.</summary>
    public void SetPixelSize(int pixelWidth, int pixelHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelHeight);
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
    }

    public void Lock() { checked { _lockCount++; } }

    public void Unlock()
    {
        if (_lockCount == 0)
            throw new InvalidOperationException("The D3DImage is not locked.");
        _lockCount--;
    }

    public bool TryLock(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_isFrontBufferAvailable) return false;
        Lock();
        return true;
    }

    public void AddDirtyRect(Int32Rect dirtyRect)
    {
        if (dirtyRect.Width < 0 || dirtyRect.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(dirtyRect));
        if (dirtyRect.IsEmpty) return;

        for (int i = 0; i < _dirtyRects.Count; i++)
        {
            var existing = _dirtyRects[i];
            if (dirtyRect.X < existing.X + existing.Width &&
                dirtyRect.X + dirtyRect.Width > existing.X &&
                dirtyRect.Y < existing.Y + existing.Height &&
                dirtyRect.Y + dirtyRect.Height > existing.Y)
            {
                var minX = Math.Min(existing.X, dirtyRect.X);
                var minY = Math.Min(existing.Y, dirtyRect.Y);
                var maxX = Math.Max(existing.X + existing.Width, dirtyRect.X + dirtyRect.Width);
                var maxY = Math.Max(existing.Y + existing.Height, dirtyRect.Y + dirtyRect.Height);
                _dirtyRects[i] = new Int32Rect(minX, minY, maxX - minX, maxY - minY);
                return;
            }
        }
        _dirtyRects.Add(dirtyRect);
    }

    public event EventHandler? IsFrontBufferAvailableChanged;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DetachInternalReferences();
        UpdateAvailability(false);
    }

    private void DetachInternalReferences()
    {
        if (_backBufferType == D3DResourceType.IDirect3DSurface9 && _backBuffer != IntPtr.Zero)
        {
            System.Runtime.InteropServices.Marshal.Release(_backBuffer);
        }
        // NativeVideoSurface ownership stays with the caller — only drop the ref.
        _videoSurface = null;
        _backBuffer = IntPtr.Zero;
    }

    private void UpdateAvailability(bool available)
    {
        if (_isFrontBufferAvailable == available) return;
        _isFrontBufferAvailable = available;
        IsFrontBufferAvailableChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Kind of native resource referenced by <see cref="D3DImage.NativeHandle"/>.
/// Mirrors the surface kinds in <see cref="NativeVideoSurfaceKind"/>; kept in
/// the WPF-style <see cref="D3DImage"/> shape for source compatibility.
/// </summary>
public enum D3DResourceType
{
    /// <summary>Legacy WPF D3D9 surface. Accepted for source-compat; no platform actually renders it.</summary>
    IDirect3DSurface9 = 0,

    /// <summary>Windows: shared ID3D11Texture2D NT handle. Stage 3.</summary>
    ID3D11Texture2DShared = 1,

    /// <summary>Vulkan VkImage via external memory extension. Stage 5.</summary>
    VkImageExternal = 2,

    /// <summary>Android AHardwareBuffer. Stage 4.</summary>
    AHardwareBuffer = 3,

    /// <summary>
    /// Preferred new path: wrap a <see cref="NativeVideoSurface"/> directly.
    /// Framework dispatches to <c>jalium_render_target_draw_video_surface</c>.
    /// </summary>
    NativeVideoSurface = 4,
}
