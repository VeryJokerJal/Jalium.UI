using System.ComponentModel;
using System.Runtime.InteropServices;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Shell;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    // Allocate Shell state only for windows which opt into appbar behavior.
    private AppBarRegistration? _appBarRegistration;
    private bool _isAppBarEnabled;

    internal bool IsAppBarRegistered => _appBarRegistration?.IsRegistered == true;

    internal void UpdateAppBarConfiguration(bool enabled)
    {
        _isAppBarEnabled = enabled;
        UpdateAppBarRegistration();
    }

    internal void UpdateAppBarRegistration(bool? nativeVisible = null)
    {
        if (!_isAppBarEnabled)
        {
            ReleaseAppBarRegistration(restoreBounds: true);
            return;
        }

        if (!OperatingSystem.IsWindows() || _platformWindow != null || Handle == nint.Zero || _managedTeardownStarted)
            return;

        _appBarRegistration ??= new AppBarRegistration(this);
        _appBarRegistration.Update(nativeVisible ?? (!_nativeWindowHidden && IsWindowVisible(Handle)));
    }

    private void ReleaseAppBarRegistration(bool restoreBounds)
    {
        var registration = _appBarRegistration;
        _appBarRegistration = null;
        registration?.Detach(restoreBounds);
    }

    internal static RECT CalculateAppBarBounds(RECT available, AppBarEdge edge, double thickness, double dpiScale)
    {
        bool vertical = edge is AppBarEdge.Left or AppBarEdge.Right;
        int extent = Math.Max(0, vertical ? available.right - available.left : available.bottom - available.top);
        int pixels = (int)Math.Min(extent, Math.Max(1, Math.Ceiling(thickness * dpiScale)));
        switch (edge)
        {
            case AppBarEdge.Left: available.right = available.left + pixels; break;
            case AppBarEdge.Top: available.bottom = available.top + pixels; break;
            case AppBarEdge.Right: available.left = available.right - pixels; break;
            case AppBarEdge.Bottom: available.top = available.bottom - pixels; break;
        }
        return available;
    }

    private sealed class AppBarRegistration
    {
        private const uint WM_SHOWWINDOW = 0x0018;
        private const uint ABM_NEW = 0;
        private const uint ABM_REMOVE = 1;
        private const uint ABM_QUERYPOS = 2;
        private const uint ABM_SETPOS = 3;
        private const uint ABM_ACTIVATE = 6;
        private const uint ABM_WINDOWPOSCHANGED = 9;
        private const int ABN_POSCHANGED = 1;
        private const int ABN_FULLSCREENAPP = 2;

        // Register lazily: ordinary windows never initialize Shell appbar messages.
        private static readonly uint CallbackMessage = RegisterMessage("Jalium.UI.WindowAppBar.Callback");
        private static readonly uint TaskbarCreatedMessage = RegisterMessage("TaskbarCreated");
        private readonly Window _window;
        private readonly nint _handle;
        private readonly RECT _floatingBounds;
        private bool _positioning;
        private bool _shellFullScreen;
        private bool _detached;

        internal bool IsRegistered { get; private set; }

        internal AppBarRegistration(Window window)
        {
            _window = window;
            _handle = window.Handle;
            if (!GetWindowRect(_handle, out _floatingBounds))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        internal void Update(bool visible)
        {
            if (_detached)
                return;

            if (!visible || _window.WindowState != WindowState.Normal || _window._isClosing)
            {
                Unregister();
                return;
            }

            if (_positioning)
                return;

            if (!IsRegistered)
            {
                var data = CreateData();
                data.uCallbackMessage = CallbackMessage;
                if (SHAppBarMessage(ABM_NEW, ref data) == 0)
                    throw new InvalidOperationException("The Windows Shell could not register the window as an appbar.");
                IsRegistered = true;
                if (_detached)
                {
                    Unregister();
                    return;
                }
            }

            try
            {
                Position();
            }
            catch
            {
                // A partially positioned appbar must never leave a reserved work area.
                Unregister();
                throw;
            }
        }

        private void Position()
        {
            if (!IsRegistered || _positioning || _detached)
                return;

            _positioning = true;
            try
            {
                var monitor = MonitorFromWindow(_handle, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var edge = WindowAppBar.GetEdge(_window);
                double thickness = WindowAppBar.GetThickness(_window);
                var data = CreateData();
                data.uEdge = (uint)edge;
                data.rc = info.rcMonitor;
                _ = SHAppBarMessage(ABM_QUERYPOS, ref data);
                if (_detached || !IsRegistered)
                    return;
                // Shell negotiation can move an edge. Reapply the requested thickness
                // to that edge, then use the final rectangle returned by ABM_SETPOS.
                data.rc = CalculateAppBarBounds(data.rc, edge, thickness, _window._dpiScale);
                _ = SHAppBarMessage(ABM_SETPOS, ref data);
                if (_detached || !IsRegistered)
                    return;

                var bounds = data.rc;
                if (!SetWindowPos(_handle, nint.Zero, bounds.left, bounds.top,
                    bounds.right - bounds.left, bounds.bottom - bounds.top,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                _positioning = false;
            }
        }

        internal bool HandleMessage(uint message, nint wParam, nint lParam)
        {
            if (_detached)
                return false;

            if (message == CallbackMessage)
            {
                if (IsRegistered)
                {
                    switch ((int)wParam)
                    {
                        case ABN_POSCHANGED:
                            Position();
                            break;
                        case ABN_FULLSCREENAPP:
                            _shellFullScreen = lParam != nint.Zero;
                            UpdateZOrder();
                            break;
                    }
                }
                return true;
            }

            if (message == TaskbarCreatedMessage)
            {
                // Explorer has discarded its old registration list.
                IsRegistered = false;
                bool wasLowered = _shellFullScreen;
                _shellFullScreen = false;
                if (wasLowered)
                    UpdateZOrder();
                _window.UpdateAppBarRegistration();
            }
            else if (message == WM_SHOWWINDOW)
            {
                _window.UpdateAppBarRegistration(nativeVisible: wParam != nint.Zero);
            }
            else if (IsRegistered)
            {
                if (message is WM_ACTIVATE or WM_WINDOWPOSCHANGED)
                {
                    var data = CreateData();
                    _ = SHAppBarMessage(message == WM_ACTIVATE ? ABM_ACTIVATE : ABM_WINDOWPOSCHANGED, ref data);
                }
                else if (message is WM_DISPLAYCHANGE or WM_SETTINGCHANGE)
                {
                    Position();
                }
            }
            return false;
        }

        internal void Detach(bool restoreBounds)
        {
            if (_detached)
                return;
            _detached = true;
            Unregister();
            if (!restoreBounds || _window._isClosing || _window._managedTeardownStarted)
                return;

            if (_window.WindowState == WindowState.Normal)
            {
                var bounds = _floatingBounds;
                _ = SetWindowPos(_handle, nint.Zero, bounds.left, bounds.top,
                    bounds.right - bounds.left, bounds.bottom - bounds.top,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }

        private void Unregister()
        {
            if (!IsRegistered)
                return;
            IsRegistered = false;
            var data = CreateData();
            _ = SHAppBarMessage(ABM_REMOVE, ref data);
            if (_shellFullScreen)
            {
                _shellFullScreen = false;
                if (!_window._isClosing && !_window._managedTeardownStarted)
                    UpdateZOrder();
            }
        }

        private void UpdateZOrder()
        {
            nint insertAfter = _shellFullScreen ? (nint)1 /* HWND_BOTTOM */
                : _window.Topmost ? HWND_TOPMOST : HWND_NOTOPMOST;
            _ = SetWindowPos(_handle, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        private APPBARDATA CreateData() => new()
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = _handle
        };

        private static uint RegisterMessage(string name)
        {
            uint message = RegisterWindowMessage(name);
            return message != 0 ? message : throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}
