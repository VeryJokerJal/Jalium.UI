using Jalium.UI.Controls;
using Jalium.UI.Interop.Win32;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowNativeCaptionAnimationTests
{
    private const long Caption = 0x00C00000;
    private const long Maximized = 0x01000000;
    private const long Minimized = 0x20000000;

    [RequiresWindowsFact]
    public void SizingEdgeHint_FollowsNativeSizingAndIsClearedBeforeStateTransitions()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            EnterInteractiveMode(window, true);
            foreach (int edge in new[] { 4, 1, 3 })
            {
                _ = Win32Methods.SendMessage(window.Handle, 0x0214 /* WM_SIZING */, edge, 0);
                Assert.Equal((nint)edge, GetResizeEdge(window.Handle, "Jalium.UI.LiveResizeEdge"));
            }
            window.WindowState = WindowState.Maximized;
            Assert.Equal(0, GetResizeEdge(window.Handle, "Jalium.UI.LiveResizeEdge"));
            window.WindowState = WindowState.Normal;
            EnterInteractiveMode(window, false);
            _ = Win32Methods.SendMessage(window.Handle, 0x0214, 4, 0);
            Assert.Equal(0, GetResizeEdge(window.Handle, "Jalium.UI.LiveResizeEdge"));
            _ = Win32Methods.SendMessage(window.Handle, 0x0232, 0, 0);
        }
        finally { window.Close(); }
    }

    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)]
    private static extern nint GetResizeEdge(nint window, string name);

    [RequiresWindowsFact]
    public void CustomChrome_NativeHitTestingResizesFromAllFourClientEdgesAndCorners()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            double width = window.Width, height = window.Height;
            foreach (var (point, hit) in new[]
            {
                (new Point(1, height / 2), 10),
                (new Point(width - 2, height / 2), 11),
                (new Point(width / 2, 1), 12),
                (new Point(width / 2, height - 2), 15),
                (new Point(1, 1), 13),
                (new Point(width - 2, 1), 14),
                (new Point(1, height - 2), 16),
                (new Point(width - 2, height - 2), 17),
            })
                Assert.Equal((nint)hit, Hit(window, point));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void SizingCaptionChange_KeepsStationaryClientEdgesAtTheSameScreenPixels()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            AssertAllEdgesCovered();
            Assert.True(Win32Methods.GetWindowRect(window.Handle, out var bounds));
            var before = ClientCorner(window);
            EnterInteractiveMode(window, true);
            Assert.True(Win32Methods.SetWindowPos(window.Handle, 0, bounds.left - 20, bounds.top - 20,
                bounds.right - bounds.left + 20, bounds.bottom - bounds.top + 20, 0x0014));
            var after = ClientCorner(window);
            AssertAllEdgesCovered();
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
            _ = Win32Methods.SendMessage(window.Handle, 0x0232, 0, 0);
            after = ClientCorner(window);
            AssertAllEdgesCovered();
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
        }
        finally { window.Close(); }

        void AssertAllEdgesCovered()
        {
            Assert.True(Win32Methods.GetWindowRect(window.Handle, out var outer));
            Assert.True(Win32Methods.GetClientRect(window.Handle, out var client));
            var origin = new POINT();
            Assert.True(Win32Methods.ClientToScreen(window.Handle, ref origin));
            Assert.Equal(outer.left, origin.X);
            Assert.Equal(outer.top, origin.Y);
            Assert.Equal(outer.right, origin.X + client.right);
            Assert.Equal(outer.bottom, origin.Y + client.bottom);
        }

        static POINT ClientCorner(Window window)
        {
            Assert.True(Win32Methods.GetClientRect(window.Handle, out var rect));
            var corner = new POINT { X = rect.right, Y = rect.bottom };
            Assert.True(Win32Methods.ClientToScreen(window.Handle, ref corner));
            return corner;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InteractiveSizing_SuppressesCaptionUntilExit_WhileMovingKeepsCaption(bool resizing)
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindow();
        try
        {
            window.Show();
            EnterInteractiveMode(window, resizing);
            Assert.Equal(resizing ? 0 : Caption, Style(window) & Caption);
            _ = Hit(window, new Point(30, 400));
            _ = Win32Methods.SendMessage(window.Handle, 0x02A2 /* WM_NCMOUSELEAVE */, 0, 0);
            Assert.Equal(resizing ? 0 : Caption, Style(window) & Caption);
            _ = Win32Methods.SendMessage(window.Handle, 0x0232 /* WM_EXITSIZEMOVE */, 0, 0);
            Assert.Equal(Caption, Style(window) & Caption);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void StateChangesDuringSizing_RestoreCaptionBeforeBothProgrammaticAndNativeTransitions()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.RecordSizes = true;
            EnterInteractiveMode(window, true);
            window.WindowState = WindowState.Maximized;
            AssertNativeTransition(window, 2, Maximized);
            window.WindowState = WindowState.Normal;

            EnterInteractiveMode(window, true);
            window.NativeSizes.Clear();
            _ = Win32Methods.SendMessage(window.Handle, 0x0112 /* WM_SYSCOMMAND */, 0xF030 /* SC_MAXIMIZE */, 0);
            AssertNativeTransition(window, 2, Maximized);
            _ = Win32Methods.SendMessage(window.Handle, 0x0232 /* WM_EXITSIZEMOVE */, 0, 0);
        }
        finally { window.Close(); }
    }

    private static void EnterInteractiveMode(Window window, bool resizing)
    {
        typeof(Window).GetField("_nativeSizingCommand", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, resizing);
        _ = Win32Methods.SendMessage(window.Handle, 0x0231 /* WM_ENTERSIZEMOVE */, 0, 0);
    }

    [RequiresWindowsFact]
    public void ProgrammaticStateChangesFromSnapHover_KeepCaptionThroughoutTransitionAndAfterward()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Assert.Equal(Caption, Style(window) & Caption);
            window.RecordSizes = true;

            HoverMaximize(window);
            window.WindowState = WindowState.Maximized;
            Assert.True(Win32Methods.IsZoomed(window.Handle));
            AssertNativeTransition(window, 2, Maximized);

            HoverMaximize(window);
            window.WindowState = WindowState.Normal;
            Assert.False(Win32Methods.IsZoomed(window.Handle));
            AssertNativeTransition(window, 0, 0);

            HoverMaximize(window);
            window.WindowState = WindowState.Minimized;
            AssertNativeTransition(window, 1, Minimized);

            window.WindowState = WindowState.Normal;
            AssertNativeTransition(window, 0, 0);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void SystemCommandsFromSnapHover_KeepCaptionThroughoutTransitionAndAfterward()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.RecordSizes = true;

            HoverMaximize(window);
            _ = Win32Methods.SendMessage(window.Handle, 0x0112 /* WM_SYSCOMMAND */, 0xF030 /* SC_MAXIMIZE */, 0);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            AssertNativeTransition(window, 2, Maximized);

            HoverMaximize(window);
            _ = Win32Methods.SendMessage(window.Handle, 0x0112, 0xF120 /* SC_RESTORE */, 0);
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertNativeTransition(window, 0, 0);

            HoverMaximize(window);
            _ = Win32Methods.SendMessage(window.Handle, 0x0112, 0xF020 /* SC_MINIMIZE */, 0);
            Assert.Equal(WindowState.Minimized, window.WindowState);
            AssertNativeTransition(window, 1, Minimized);

            _ = Win32Methods.SendMessage(window.Handle, 0x0112, 0xF120, 0);
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertNativeTransition(window, 0, 0);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void NativeTitleBar_LeavesItsCaptionStyleIntactAfterStateChanges()
    {
        var window = CreateWindow();
        window.TitleBarStyle = WindowTitleBarStyle.Native;
        try
        {
            window.Show();
            window.WindowState = WindowState.Maximized;
            Assert.Equal(Caption, Style(window) & Caption);
            Assert.True(Win32Methods.IsZoomed(window.Handle));

            window.WindowState = WindowState.Normal;
            Assert.Equal(Caption, Style(window) & Caption);
            Assert.False(Win32Methods.IsZoomed(window.Handle));
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void WideTallMaximizeButton_RemovesCaptionOnlyInsideItsCurrentHitRegion()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            Assert.Equal(Caption, Style(window) & Caption);
            foreach (double fraction in new[] { 0.04, 0.5, 0.98 })
            {
                HoverMaximize(window, fraction);
                Assert.Equal((nint)8, HitButton(window, TitleBarButtonKind.Minimize, fraction));
                Assert.Equal(Caption, Style(window) & Caption);

                HoverMaximize(window, fraction);
                Assert.Equal((nint)20, HitButton(window, TitleBarButtonKind.Close, fraction));
                Assert.Equal(Caption, Style(window) & Caption);
            }

            HoverMaximize(window);
            Assert.Equal((nint)1, Hit(window, new Point(30, 400)));
            Assert.Equal(Caption, Style(window) & Caption);

            HoverMaximize(window);
            Assert.Equal((nint)2, Hit(window, new Point(30, 150)));
            Assert.Equal(Caption, Style(window) & Caption);

            HoverMaximize(window);
            Assert.Equal((nint)12, Hit(window, new Point(200, 2)));
            Assert.Equal(Caption, Style(window) & Caption);

            window.TitleBarHeight = 40;
            ((ILayoutManagerHost)window).LayoutManager.UpdateLayout(window, new Size(window.Width, window.Height));
            Assert.Equal((nint)1, Hit(window, new Point(window.Width - 150, 150)));
            Assert.Equal(Caption, Style(window) & Caption);
            HoverMaximize(window);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void SnapHover_LeaveCancelDeactivateAndHideRestoreCaption()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            foreach (uint message in new uint[] { 0x02A2 /* WM_NCMOUSELEAVE */, 0x001F /* WM_CANCELMODE */,
                         0x0006 /* WM_ACTIVATE, WA_INACTIVE */, 0x0215 /* WM_CAPTURECHANGED */ })
            {
                HoverMaximize(window);
                _ = Win32Methods.SendMessage(window.Handle, message, 0, 0);
                Assert.Equal(Caption, Style(window) & Caption);
            }

            HoverMaximize(window);
            window.IsShowTitleBar = false;
            Assert.Equal(Caption, Style(window) & Caption);
            window.IsShowTitleBar = true;
            ((ILayoutManagerHost)window).LayoutManager.UpdateLayout(window, new Size(window.Width, window.Height));

            HoverMaximize(window);
            window.Hide();
            Assert.Equal(Caption, Style(window) & Caption);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void SnapHover_NativeAndBorderlessStylesKeepTheirOwnCaptionPolicy()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            HoverMaximize(window);
            window.TitleBarStyle = WindowTitleBarStyle.Native;
            _ = Win32Methods.SendMessage(window.Handle, 0x02A2, 0, 0);
            Assert.Equal(Caption, Style(window) & Caption);

            window.TitleBarStyle = WindowTitleBarStyle.Custom;
            Assert.Equal(Caption, Style(window) & Caption);
            window.WindowStyle = WindowStyle.None;
            _ = Hit(window, new Point(30, 400));
            _ = Win32Methods.SendMessage(window.Handle, 0x02A2, 0, 0);
            Assert.Equal(0, Style(window) & Caption);

            window.WindowStyle = WindowStyle.SingleBorderWindow;
            Assert.Equal(Caption, Style(window) & Caption);
        }
        finally { window.Close(); }
    }

    private static AnimationProbeWindow CreateWindow()
    {
        var window = new AnimationProbeWindow
        {
            Width = 640,
            Height = 560,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false,
            TitleBarHeight = 300
        };
        var template = new ControlTemplate(typeof(TitleBar));
        template.SetVisualTree(() =>
        {
            var panel = new DockPanel { LastChildFill = false };
            foreach (var kind in new[] { TitleBarButtonKind.Close, TitleBarButtonKind.Maximize, TitleBarButtonKind.Minimize })
            {
                var button = new TitleBarButton { Name = $"PART_{kind}Button", Kind = kind, Width = 100 };
                DockPanel.SetDock(button, Dock.Right);
                panel.Children.Add(button);
            }
            return panel;
        });
        window.TitleBar!.Template = template;
        return window;
    }

    private static void HoverMaximize(Window window, double heightFraction = 0.5)
    {
        Assert.Equal((nint)9, HitButton(window, TitleBarButtonKind.Maximize, heightFraction));
        Assert.Equal(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? 0 : Caption, Style(window) & Caption);
    }

    private static nint HitButton(Window window, TitleBarButtonKind kind, double heightFraction)
    {
        var button = window.TitleBar!.GetButtonByKind(kind)!;
        return Hit(window, button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight * heightFraction), window));
    }

    private static nint Hit(Window window, Point clientPoint)
    {
        var point = new POINT { X = (int)Math.Round(clientPoint.X * window.DpiScale), Y = (int)Math.Round(clientPoint.Y * window.DpiScale) };
        Assert.True(Win32Methods.ClientToScreen(window.Handle, ref point));
        nint coordinates = unchecked((nint)((ushort)point.X | ((int)(ushort)point.Y << 16)));
        return Win32Methods.SendMessage(window.Handle, 0x0084 /* WM_NCHITTEST */, 0, coordinates);
    }

    private static long Style(Window window) => Win32Methods.GetWindowLong(window.Handle, -16 /* GWL_STYLE */);

    private static void AssertNativeTransition(AnimationProbeWindow window, int sizeType, long nativeState)
    {
        Assert.Contains(window.NativeSizes, entry => entry.SizeType == sizeType && (entry.Style & Caption) == Caption);
        Assert.Equal(Caption, Style(window) & Caption);
        Assert.Equal(nativeState, Style(window) & (Maximized | Minimized));
        window.NativeSizes.Clear();
    }

    private sealed class AnimationProbeWindow : Window
    {
        internal bool RecordSizes;
        internal readonly List<(int SizeType, long Style)> NativeSizes = [];

        protected override nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
        {
            if (RecordSizes && message == 0x0005 /* WM_SIZE */)
                NativeSizes.Add(((int)wParam, Win32Methods.GetWindowLong(hwnd, -16)));
            return base.WndProc(hwnd, message, wParam, lParam);
        }
    }
}
