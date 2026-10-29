using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    private bool ShowWin32WindowState(int command)
    {
        if (command is not (SW_MAXIMIZE or SW_MINIMIZE or SW_RESTORE))
        {
            return ShowWindow(Handle, command);
        }

        using var animation = BeginNativeCaptionAnimation(Handle);
        return ShowWindow(Handle, command);
    }

    private NativeCaptionAnimationScope BeginNativeCaptionAnimation(nint hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == nint.Zero || hwnd != Handle ||
            TitleBarStyle != WindowTitleBarStyle.Custom || WindowStyle == WindowStyle.None || _isFullScreen)
        {
            return default;
        }

        long style = GetWindowLong(hwnd, GWL_STYLE);
        if ((style & WS_CAPTION) == WS_CAPTION)
        {
            return default;
        }

        // DWM selects the system animation when the state transition starts.
        // Keep the native caption only for that call; custom caption hit regions
        // must own the idle window so Snap hover works at any title-bar height.
        _ = SetWindowLong(hwnd, GWL_STYLE, style | WS_CAPTION);
        return new NativeCaptionAnimationScope(this, hwnd);
    }

    private readonly struct NativeCaptionAnimationScope(Window window, nint hwnd) : IDisposable
    {
        public void Dispose()
        {
            if (window == null || window.Handle != hwnd ||
                window.TitleBarStyle != WindowTitleBarStyle.Custom || window._isFullScreen)
            {
                return;
            }

            // Read the live style: ShowWindow/DefWindowProc changed WS_MAXIMIZE
            // or WS_MINIMIZE, and restoring the old style would undo that state.
            _ = SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~(long)WS_CAPTION);
        }
    }
}
