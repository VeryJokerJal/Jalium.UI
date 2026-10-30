using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Shell;

namespace Jalium.UI.Tests;

public class WindowAppBarTests
{
    [Theory]
    [InlineData(AppBarEdge.Left, -1920, -150, -1848, 930)]
    [InlineData(AppBarEdge.Top, -1920, -150, 0, -78)]
    [InlineData(AppBarEdge.Right, -72, -150, 0, 930)]
    [InlineData(AppBarEdge.Bottom, -1920, 858, 0, 930)]
    public void Bounds_UseMonitorCoordinatesAndDpi(AppBarEdge edge, int left, int top, int right, int bottom)
    {
        var available = new RECT { left = -1920, top = -150, right = 0, bottom = 930 };
        var result = Window.CalculateAppBarBounds(available, edge, 48, 1.5);
        Assert.Equal(new RECT { left = left, top = top, right = right, bottom = bottom }, result);
    }

    [Fact]
    public void OversizedThickness_IsClampedBeforeIntegerConversion()
    {
        var monitor = new RECT { left = -1920, top = 0, right = 0, bottom = 1080 };
        Assert.Equal(monitor, Window.CalculateAppBarBounds(monitor, AppBarEdge.Left, double.MaxValue, 2));
    }

    [Fact]
    public void Configuration_ValidatesBeforeChangingValues()
    {
        var window = new Window();
        try
        {
            Assert.False(WindowAppBar.GetIsEnabled(window));
            Assert.False(WindowAppBar.GetIsRegistered(window));
            foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                Assert.ThrowsAny<ArgumentException>(() => WindowAppBar.SetThickness(window, invalid));
            Assert.ThrowsAny<ArgumentException>(() => WindowAppBar.SetEdge(window, (AppBarEdge)4));
            Assert.Equal(48, WindowAppBar.GetThickness(window));
            Assert.Equal(AppBarEdge.Top, WindowAppBar.GetEdge(window));
        }
        finally { window.Close(); }
    }

    [Fact]
    public void NativeData_UsesPointerSizedHandleAndParameter()
    {
        Assert.Equal(IntPtr.Size == 8 ? 48 : 36, Marshal.SizeOf<APPBARDATA>());
        Assert.Equal((nint)(IntPtr.Size == 8 ? 8 : 4), Marshal.OffsetOf<APPBARDATA>(nameof(APPBARDATA.hWnd)));
        Assert.Equal((nint)(IntPtr.Size == 8 ? 40 : 32), Marshal.OffsetOf<APPBARDATA>(nameof(APPBARDATA.lParam)));
    }

    [Fact]
    public void Xaml_AttachedConfigurationResolvesWithoutExplicitRegistration()
    {
        const string xaml = """
            <Window xmlns="http://schemas.jalium.com/jalxaml"
                    WindowAppBar.Edge="Right" WindowAppBar.Thickness="64" />
            """;
        var window = Assert.IsType<Window>(Jalium.UI.Markup.XamlReader.Parse(xaml));
        try
        {
            Assert.Equal(AppBarEdge.Right, WindowAppBar.GetEdge(window));
            Assert.Equal(64, WindowAppBar.GetThickness(window));
            Assert.False(WindowAppBar.GetIsRegistered(window));
            Assert.Same(typeof(WindowAppBar), Jalium.UI.Markup.XamlTypeRegistry.GetType("WindowAppBar"));
            Assert.Same(typeof(AppBarEdge), Jalium.UI.Markup.XamlTypeRegistry.GetType("AppBarEdge"));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void Registration_ReservesWorkArea_UpdatesConfiguration_AndRestoresFloatingBounds()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            var floating = Bounds(window);
            var original = Monitor(window);
            WindowAppBar.SetThickness(window, 37.5);
            WindowAppBar.SetEdge(window, AppBarEdge.Right);
            WindowAppBar.SetIsEnabled(window, true);
            AssertReserved(window, AppBarEdge.Right, 37.5);

            WindowAppBar.SetThickness(window, 56);
            WindowAppBar.SetEdge(window, AppBarEdge.Top);
            AssertReserved(window, AppBarEdge.Top, 56);
            Assert.Equal(original.rcWork.right, Monitor(window).rcWork.right);

            WindowAppBar.SetIsEnabled(window, false);
            Assert.False(WindowAppBar.GetIsRegistered(window));
            Assert.Equal(original.rcWork, Monitor(window).rcWork);
            Assert.Equal(floating, Bounds(window));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void HiddenAndNonNormalStates_ReleaseReservation_AndResumeWhenRestored()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            var original = Monitor(window);
            WindowAppBar.SetIsEnabled(window, true);
            AssertReserved(window, AppBarEdge.Top, 48);

            window.Hide();
            AssertReleased(window, original);
            window.Show();
            AssertReserved(window, AppBarEdge.Top, 48);

            foreach (var state in new[] { WindowState.Minimized, WindowState.Maximized, WindowState.FullScreen })
            {
                window.WindowState = state;
                Assert.Equal(state, window.WindowState);
                AssertReleased(window, original);
                window.WindowState = WindowState.Normal;
                AssertReserved(window, AppBarEdge.Top, 48);
            }

            window.Visibility = Visibility.Hidden;
            AssertReleased(window, original);
            window.Visibility = Visibility.Visible;
            AssertReserved(window, AppBarEdge.Top, 48);

            window.Close();
            Assert.False(WindowAppBar.GetIsRegistered(window));
            Assert.Equal(original.rcWork, Monitor(original).rcWork);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void PreShowConfiguration_AndNativeDestruction_HonorRegistrationLifetime()
    {
        var window = CreateWindow();
        WindowAppBar.SetIsEnabled(window, true);
        Assert.False(WindowAppBar.GetIsRegistered(window));
        try
        {
            window.Show();
            AssertReserved(window, AppBarEdge.Top, 48);
            window.Hide();
            var original = Monitor(window);
            window.Show();
            AssertReserved(window, AppBarEdge.Top, 48);
            Assert.True(Win32Methods.DestroyWindow(window.Handle));
            Assert.False(WindowAppBar.GetIsRegistered(window));
            Assert.Equal(original.rcWork, Monitor(original).rcWork);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void Notifications_RestoreTopmost_AndReregisterAfterShellLosesRegistration()
    {
        var window = CreateWindow();
        window.Topmost = true;
        try
        {
            window.Show();
            var original = Monitor(window);
            WindowAppBar.SetIsEnabled(window, true);
            uint callback = Win32Methods.RegisterWindowMessage("Jalium.UI.WindowAppBar.Callback");

            _ = Win32Methods.SendMessage(window.Handle, callback, 2 /* ABN_FULLSCREENAPP */, 1);
            Assert.Equal(0L, Win32Methods.GetWindowLong(window.Handle, -20) & 8);
            _ = Win32Methods.SendMessage(window.Handle, callback, 2, 0);
            Assert.NotEqual(0L, Win32Methods.GetWindowLong(window.Handle, -20) & 8);

            _ = Win32Methods.SendMessage(window.Handle, callback, 2, 1);
            window.Hide();
            window.Show();
            Assert.NotEqual(0L, Win32Methods.GetWindowLong(window.Handle, -20) & 8);

            bool cancelClose = true;
            window.Closing += (_, e) => e.Cancel = cancelClose;
            window.Close();
            AssertReserved(window, AppBarEdge.Top, 48);
            cancelClose = false;

            // Simulate Explorer discarding its registration list, then broadcasting TaskbarCreated.
            var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = window.Handle };
            _ = Win32Methods.SHAppBarMessage(1 /* ABM_REMOVE */, ref data);
            Assert.Equal(original.rcWork, Monitor(window).rcWork);
            uint restarted = Win32Methods.RegisterWindowMessage("TaskbarCreated");
            _ = Win32Methods.SendMessage(window.Handle, restarted, 0, 0);
            AssertReserved(window, AppBarEdge.Top, 48);
            _ = Win32Methods.SendMessage(window.Handle, callback, 1 /* ABN_POSCHANGED */, 0);
            AssertReserved(window, AppBarEdge.Top, 48);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void MultipleAppBarsOnSameEdge_NegotiateWithoutOverlap()
    {
        var first = CreateWindow();
        var second = CreateWindow();
        try
        {
            first.Show();
            second.Show();
            WindowAppBar.SetThickness(first, 40);
            WindowAppBar.SetThickness(second, 60);
            WindowAppBar.SetIsEnabled(first, true);
            WindowAppBar.SetIsEnabled(second, true);
            var firstBounds = Bounds(first);
            var secondBounds = Bounds(second);
            Assert.True(secondBounds.top >= firstBounds.bottom || firstBounds.top >= secondBounds.bottom);
            Assert.Equal((int)Math.Ceiling(60 * Win32Methods.GetDpiForWindow(second.Handle) / 96.0),
                secondBounds.bottom - secondBounds.top);
        }
        finally
        {
            second.Close();
            first.Close();
        }
    }

    private static Window CreateWindow() => new()
    {
        Width = 640, Height = 400, Left = 100, Top = 100,
        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowActivated = false, ShowInTaskbar = false
    };

    private static RECT Bounds(Window window)
    {
        Assert.True(Win32Methods.GetWindowRect(window.Handle, out var bounds));
        return bounds;
    }

    private static MONITORINFO Monitor(Window window)
    {
        var monitor = Win32Methods.MonitorFromWindow(window.Handle, 2);
        return ReadMonitor(monitor);
    }

    private static MONITORINFO Monitor(MONITORINFO previous)
    {
        var bounds = previous.rcMonitor;
        return ReadMonitor(MonitorFromRect(ref bounds, 2));
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref RECT bounds, uint flags);

    private static MONITORINFO ReadMonitor(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        Assert.True(Win32Methods.GetMonitorInfo(monitor, ref info));
        return info;
    }

    private static void AssertReleased(Window window, MONITORINFO original)
    {
        Assert.False(WindowAppBar.GetIsRegistered(window));
        Assert.Equal(original.rcWork, Monitor(original).rcWork);
    }

    private static void AssertReserved(Window window, AppBarEdge edge, double thickness)
    {
        Assert.True(WindowAppBar.GetIsRegistered(window));
        Assert.Equal(WindowState.Normal, window.WindowState);
        var bounds = Bounds(window);
        var info = Monitor(window);
        int pixels = (int)Math.Ceiling(thickness * Win32Methods.GetDpiForWindow(window.Handle) / 96.0);
        if (edge == AppBarEdge.Right)
        {
            Assert.Equal(pixels, bounds.right - bounds.left);
            Assert.Equal(bounds.left, info.rcWork.right);
        }
        else
        {
            Assert.Equal(pixels, bounds.bottom - bounds.top);
            Assert.Equal(bounds.bottom, info.rcWork.top);
        }
    }
}
