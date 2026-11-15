using Jalium.UI.Interop.Win32;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    // ShowWindow/SetWindowPos send WM_SIZE synchronously. Intermediate native
    // states must not replace the state requested by the application.
    private bool _isApplyingWindowState;

    /// <summary>
    /// Toggles fullscreen, restoring the previous normal or maximized state on exit.
    /// This also works before the window is shown and with platform windows.
    /// </summary>
    public void ToggleFullScreen()
    {
        VerifyAccess();
        WindowState = WindowState == WindowState.FullScreen
            ? _fullScreenPreviousState
            : WindowState.FullScreen;
    }

    private void ApplyNativeWindowState(WindowState oldState, WindowState newState)
    {
        bool wasApplying = _isApplyingWindowState;
        _isApplyingWindowState = true;
        try
        {
            if (_platformWindow != null)
            {
                _platformWindow.SetState(newState);
                return;
            }

            if (_isFullScreen && newState != WindowState.FullScreen)
                ExitFullScreen();

            if (_nativeWindowHidden)
                return;

            if (newState == WindowState.FullScreen)
            {
                if (oldState is WindowState.Minimized or WindowState.Maximized)
                    _ = ShowWin32WindowState(SW_RESTORE);
                EnterFullScreen();
                _ = ShowWindow(Handle, ShowActivated ? SW_SHOW : SW_SHOWNA);
            }
            else
            {
                int command = newState switch
                {
                    WindowState.Maximized => SW_MAXIMIZE,
                    WindowState.Minimized => SW_MINIMIZE,
                    _ => SW_RESTORE
                };
                _ = ShowWin32WindowState(command);
            }
        }
        finally
        {
            _isApplyingWindowState = wasApplying;
        }
    }

    private void ShowInitialWin32Window(int command, WindowState desiredState)
    {
        bool wasApplying = _isApplyingWindowState;
        _isApplyingWindowState = true;
        try
        {
            _ = ShowWin32WindowState(command);
            if (desiredState == WindowState.FullScreen)
                EnterFullScreen();
        }
        finally
        {
            _isApplyingWindowState = wasApplying;
        }
    }

    private void ResizeFullScreenToMonitor()
    {
        if (!_isFullScreen || Handle == nint.Zero || _platformWindow != null)
            return;

        var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var bounds = info.rcMonitor;
        _ = SetWindowPos(Handle, nint.Zero, bounds.left, bounds.top,
            bounds.right - bounds.left, bounds.bottom - bounds.top,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }
}
