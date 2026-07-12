using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Interop;

/// <summary>Provides a composition target for an existing Win32 window.</summary>
public class HwndTarget : Jalium.UI.Media.CompositionTarget
{
    private readonly IntPtr _hwnd;
    private DpiScale _dpiScale;
    private bool _disposed;
    private Color _backgroundColor;
    private RenderMode _renderMode;

    /// <summary>Initializes a composition target for a valid native window.</summary>
    public HwndTarget(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HwndTarget requires Win32 windowing support.");
        }

        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            throw new ArgumentException("The specified handle is not a valid window handle.", nameof(hwnd));
        }

        _hwnd = hwnd;
        _dpiScale = ReadDpiScale(hwnd);
    }

    /// <summary>Gets or sets the target background color.</summary>
    public Color BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            ThrowIfTargetDisposed();
            _backgroundColor = value;
        }
    }

    /// <summary>Gets or sets the rendering mode preference.</summary>
    public RenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new System.ComponentModel.InvalidEnumArgumentException(
                    nameof(value),
                    (int)value,
                    typeof(RenderMode));
            }

            ThrowIfTargetDisposed();
            _renderMode = value;
        }
    }

    /// <inheritdoc />
    public override Visual? RootVisual
    {
        get => base.RootVisual;
        set
        {
            ThrowIfTargetDisposed();
            base.RootVisual = value;
        }
    }

    /// <inheritdoc />
    public override Matrix TransformToDevice => new(
        _dpiScale.DpiScaleX,
        0,
        0,
        _dpiScale.DpiScaleY,
        0,
        0);

    /// <inheritdoc />
    public override Matrix TransformFromDevice => new(
        1d / _dpiScale.DpiScaleX,
        0,
        0,
        1d / _dpiScale.DpiScaleY,
        0,
        0);

    /// <summary>Gets whether this target uses per-pixel opacity.</summary>
    public bool UsesPerPixelOpacity { get; internal set; }

    /// <summary>Gets the current DPI scale used by the native window.</summary>
    internal DpiScale DpiScale => _dpiScale;

    /// <summary>Gets the real native handle backing this target.</summary>
    internal IntPtr CriticalHandle => _hwnd;

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        base.Dispose();
    }

    /// <summary>Updates transforms after a native DPI transition.</summary>
    internal void UpdateDpi(DpiScale dpiScale)
    {
        ThrowIfTargetDisposed();
        _dpiScale = dpiScale;
    }

    private static DpiScale ReadDpiScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
        {
            dpi = 96;
        }

        double scale = dpi / 96d;
        return new DpiScale(scale, scale);
    }

    private void ThrowIfTargetDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
