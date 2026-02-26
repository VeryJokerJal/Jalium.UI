namespace Jalium.UI.Controls;

/// <summary>
/// Assists interoperation between Windows Presentation Foundation (WPF) and Win32 code.
/// </summary>
public sealed class WindowInteropHelper
{
    private readonly Window _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowInteropHelper"/> class for a specified Window.
    /// </summary>
    /// <param name="window">A Jalium.UI window object.</param>
    public WindowInteropHelper(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>
    /// Gets the window handle (HWND) for a Jalium.UI window.
    /// </summary>
    public IntPtr Handle => _window.Handle;

    /// <summary>
    /// Gets or sets the handle of the owner window.
    /// </summary>
    public IntPtr Owner { get; set; }

    /// <summary>
    /// Creates the HWND of the window if the HWND has not been created yet.
    /// </summary>
    /// <returns>The window handle (HWND).</returns>
    public IntPtr EnsureHandle()
    {
        return _window.Handle;
    }
}
