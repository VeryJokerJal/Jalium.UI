using Jalium.UI.Controls;
using Jalium.UI.Interop.Win32;

namespace Jalium.UI.Tests;

public sealed class WindowNativeCaptionAnimationTests
{
    private const long Caption = 0x00C00000;
    private const long Maximized = 0x01000000;
    private const long Minimized = 0x20000000;

    [RequiresWindowsFact]
    public void ProgrammaticStateChanges_KeepCaptionDuringNativeTransitionAndPreserveResultingState()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.RecordSizes = true;

            window.WindowState = WindowState.Maximized;
            Assert.True(Win32Methods.IsZoomed(window.Handle));
            AssertNativeTransition(window, 2, Maximized);

            window.WindowState = WindowState.Normal;
            Assert.False(Win32Methods.IsZoomed(window.Handle));
            AssertNativeTransition(window, 0, 0);

            window.WindowState = WindowState.Minimized;
            AssertNativeTransition(window, 1, Minimized);

            window.WindowState = WindowState.Normal;
            AssertNativeTransition(window, 0, 0);
        }
        finally { window.Close(); }
    }

    [RequiresWindowsFact]
    public void SystemCommands_KeepCaptionDuringNativeTransitionAndRestoreCustomChrome()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            window.RecordSizes = true;

            _ = Win32Methods.SendMessage(window.Handle, 0x0112 /* WM_SYSCOMMAND */, 0xF030 /* SC_MAXIMIZE */, 0);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            AssertNativeTransition(window, 2, Maximized);

            _ = Win32Methods.SendMessage(window.Handle, 0x0112, 0xF120 /* SC_RESTORE */, 0);
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertNativeTransition(window, 0, 0);

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

    private static AnimationProbeWindow CreateWindow() => new()
    {
        Width = 640,
        Height = 560,
        Left = -20000,
        Top = -20000,
        ShowActivated = false,
        ShowInTaskbar = false,
        TitleBarHeight = 300
    };

    private static long Style(Window window) => Win32Methods.GetWindowLong(window.Handle, -16 /* GWL_STYLE */);

    private static void AssertNativeTransition(AnimationProbeWindow window, int sizeType, long nativeState)
    {
        Assert.Contains(window.NativeSizes, entry => entry.SizeType == sizeType && (entry.Style & Caption) == Caption);
        Assert.Equal(0, Style(window) & Caption);
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
