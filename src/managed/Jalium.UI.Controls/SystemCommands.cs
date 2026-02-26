using System.Runtime.InteropServices;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines routed commands for common system window operations.
/// </summary>
public static class SystemCommands
{
    /// <summary>
    /// Gets a command that closes a window.
    /// </summary>
    public static RoutedCommand CloseWindowCommand { get; } = new("CloseWindow", typeof(SystemCommands));

    /// <summary>
    /// Gets a command that maximizes a window.
    /// </summary>
    public static RoutedCommand MaximizeWindowCommand { get; } = new("MaximizeWindow", typeof(SystemCommands));

    /// <summary>
    /// Gets a command that minimizes a window.
    /// </summary>
    public static RoutedCommand MinimizeWindowCommand { get; } = new("MinimizeWindow", typeof(SystemCommands));

    /// <summary>
    /// Gets a command that restores a window.
    /// </summary>
    public static RoutedCommand RestoreWindowCommand { get; } = new("RestoreWindow", typeof(SystemCommands));

    /// <summary>
    /// Gets a command that shows the system menu.
    /// </summary>
    public static RoutedCommand ShowSystemMenuCommand { get; } = new("ShowSystemMenu", typeof(SystemCommands));

    /// <summary>
    /// Closes the specified window.
    /// </summary>
    public static void CloseWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Close();
    }

    /// <summary>
    /// Maximizes the specified window.
    /// </summary>
    public static void MaximizeWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// Minimizes the specified window.
    /// </summary>
    public static void MinimizeWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Restores the specified window.
    /// </summary>
    public static void RestoreWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.WindowState = WindowState.Normal;
    }

    /// <summary>
    /// Shows the system menu for the specified window at the given screen location.
    /// </summary>
    public static void ShowSystemMenu(Window window, Point screenLocation)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Handle == IntPtr.Zero)
            return;

        var hmenu = GetSystemMenu(window.Handle, false);
        if (hmenu == IntPtr.Zero)
            return;

        var cmd = TrackPopupMenuEx(hmenu,
            0x0100 /* TPM_RETURNCMD */ | 0x0002 /* TPM_RIGHTBUTTON */,
            (int)screenLocation.X, (int)screenLocation.Y,
            window.Handle, IntPtr.Zero);

        if (cmd != 0)
        {
            PostMessage(window.Handle, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)cmd, IntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
