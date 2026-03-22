using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// Specifies the parameters used to create an HwndSource object.
/// </summary>
public sealed class HwndSourceParameters
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HwndSourceParameters"/> class with a specified name.
    /// </summary>
    public HwndSourceParameters(string name)
    {
        WindowName = name;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HwndSourceParameters"/> class with a specified name and size.
    /// </summary>
    public HwndSourceParameters(string name, int width, int height)
    {
        WindowName = name;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets or sets the name of the window.
    /// </summary>
    public string WindowName { get; set; }

    /// <summary>
    /// Gets or sets the width of the window.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the window.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Gets or sets the x-position of the window.
    /// </summary>
    public int PositionX { get; set; }

    /// <summary>
    /// Gets or sets the y-position of the window.
    /// </summary>
    public int PositionY { get; set; }

    /// <summary>
    /// Gets or sets the window style.
    /// </summary>
    public int WindowStyle { get; set; }

    /// <summary>
    /// Gets or sets the extended window style.
    /// </summary>
    public int ExtendedWindowStyle { get; set; }

    /// <summary>
    /// Gets or sets the parent window handle.
    /// </summary>
    public IntPtr ParentWindow { get; set; }

    /// <summary>
    /// Gets or sets the window class style.
    /// </summary>
    public int WindowClassStyle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use per-pixel transparency.
    /// </summary>
    public bool UsesPerPixelTransparency { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to use per-pixel opacity.
    /// </summary>
    public bool UsesPerPixelOpacity { get; set; }
}

/// <summary>
/// Presents WPF content in a Win32 window.
/// </summary>
public sealed class HwndSource : IDisposable
{
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HwndSource"/> class with the specified parameters.
    /// </summary>
    public HwndSource(HwndSourceParameters parameters)
    {
        // In a full implementation, this would create a Win32 window
        // and set up the Jalium.UI composition pipeline to render into it.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HwndSource"/> class.
    /// </summary>
    public HwndSource(int classStyle, int style, int exStyle, int x, int y, string name, IntPtr parent)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HwndSource"/> class.
    /// </summary>
    public HwndSource(int classStyle, int style, int exStyle, int x, int y, int width, int height, string name, IntPtr parent)
    {
    }

    /// <summary>
    /// Gets the window handle for this HwndSource.
    /// </summary>
    public IntPtr Handle => _hwnd;

    /// <summary>
    /// Gets or sets the visual root of this HwndSource.
    /// </summary>
    public Jalium.UI.Visual? RootVisual { get; set; }

    /// <summary>
    /// Gets a value indicating whether this instance has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Returns the HwndSource for the specified window handle.
    /// </summary>
    public static HwndSource? FromHwnd(IntPtr hwnd)
    {
        return null;
    }

    /// <summary>
    /// Adds a hook to receive window messages.
    /// </summary>
    public void AddHook(HwndSourceHook hook)
    {
    }

    /// <summary>
    /// Removes a hook.
    /// </summary>
    public void RemoveHook(HwndSourceHook hook)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the HwndSource.
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

#pragma warning disable CS0067
    public event EventHandler? Disposed;
#pragma warning restore CS0067
}

/// <summary>
/// Represents a delegate for HwndSource hooks.
/// </summary>
public delegate IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);
