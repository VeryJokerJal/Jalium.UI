using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Interop.Win32;

namespace Jalium.UI.Tests;

public class WindowFullScreenTests
{
    [Theory]
    [InlineData(WindowState.Normal, WindowState.Normal)]
    [InlineData(WindowState.Maximized, WindowState.Maximized)]
    [InlineData(WindowState.Minimized, WindowState.Normal)]
    public void ToggleBeforeShow_RestoresPreviousState(WindowState initial, WindowState restored)
    {
        var window = new Window { WindowState = initial };
        try
        {
            window.ToggleFullScreen();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.Equal(Visibility.Collapsed, window.TitleBar!.Visibility);
            window.ToggleFullScreen();
            Assert.Equal(restored, window.WindowState);
            Assert.Equal(Visibility.Visible, window.TitleBar.Visibility);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void ToggleFromMaximized_KeepsRequestedState_AndRestoresNormalPlacement()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            var normal = Bounds(window);
            window.WindowState = WindowState.Maximized;
            Assert.True(Win32Methods.IsZoomed(window.Handle));

            var states = new List<WindowState>();
            window.StateChanged += (_, _) => states.Add(window.WindowState);
            window.ToggleFullScreen();
            Assert.Equal(new[] { WindowState.FullScreen }, states);
            AssertFullScreen(window);

            states.Clear();
            window.ToggleFullScreen();
            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.Equal(new[] { WindowState.Maximized }, states);
            Assert.True(Win32Methods.IsZoomed(window.Handle));
            Assert.Equal(Visibility.Visible, window.TitleBar!.Visibility);

            window.WindowState = WindowState.Normal;
            Assert.Equal(normal, Bounds(window));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void InitialFullScreen_CoversMonitorAndRestoresFrame()
    {
        var window = CreateWindow();
        window.WindowState = WindowState.FullScreen;
        try
        {
            window.Show();
            AssertFullScreen(window);
            window.ToggleFullScreen();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.NotEqual(0L, Win32Methods.GetWindowLong(window.Handle, -16) & 0x00C00000);
            Assert.Equal(Visibility.Visible, window.TitleBar!.Visibility);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void FullScreenExit_PreservesTopmostAndAppliesFrameChanges()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.ToggleFullScreen();
            window.Topmost = true;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            window.ToggleFullScreen();

            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(0L, Win32Methods.GetWindowLong(window.Handle, -16) & 0x00C40000);
            Assert.NotEqual(0L, Win32Methods.GetWindowLong(window.Handle, -20) & 8);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void NativeMinimizeAndRestore_ResumeFullScreen()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.ToggleFullScreen();
            _ = Win32Methods.ShowWindow(window.Handle, 6 /* SW_MINIMIZE */);
            Assert.Equal(WindowState.Minimized, window.WindowState);
            _ = Win32Methods.ShowWindow(window.Handle, 9 /* SW_RESTORE */);
            AssertFullScreen(window);
            window.ToggleFullScreen();
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void HiddenWindow_StateChangesDoNotShowIt()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.Hide();
            window.ToggleFullScreen();
            Assert.False(Win32Methods.IsWindowVisible(window.Handle));
            window.Show();
            AssertFullScreen(window);
            window.Hide();
            window.ToggleFullScreen();
            Assert.False(Win32Methods.IsWindowVisible(window.Handle));
            window.Show();
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally { window.Close(); }
    }

    private static Window CreateWindow() => new()
    {
        Width = 640, Height = 400, Left = 100, Top = 100,
        ShowActivated = false, ShowInTaskbar = false
    };

    private static RECT Bounds(Window window)
    {
        Assert.True(Win32Methods.GetWindowRect(window.Handle, out var bounds));
        return bounds;
    }

    private static void AssertFullScreen(Window window)
    {
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        var monitor = Win32Methods.MonitorFromWindow(window.Handle, 2);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Assert.True(Win32Methods.GetMonitorInfo(monitor, ref info));
        var bounds = Bounds(window);
        Assert.Equal(info.rcMonitor, bounds);
        Assert.True(Win32Methods.GetClientRect(window.Handle, out var client));
        Assert.Equal(bounds.right - bounds.left, client.right);
        Assert.Equal(bounds.bottom - bounds.top, client.bottom);
        Assert.Equal(0L, Win32Methods.GetWindowLong(window.Handle, -16) & 0x00C40000);
        Assert.Equal(Visibility.Collapsed, window.TitleBar!.Visibility);

        nint point = (nint)(((bounds.top + 1) & 0xffff) << 16 | ((bounds.left + 1) & 0xffff));
        Assert.Equal((nint)1 /* HTCLIENT */, Win32Methods.SendMessage(window.Handle, 0x84, 0, point));
    }
}
