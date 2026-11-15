using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class WindowInitialSizeConstraintTests : IDisposable
{
    public WindowInitialSizeConstraintTests() => RenderContextEmptyWindowTests.DrainAllContexts();
    public void Dispose() => RenderContextEmptyWindowTests.DrainAllContexts();

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Show_BelowMinimum_ConstrainsTheNativeClientBeforeSourceInitialized()
    {
        AssertInitialClientSize(80, 80, 100, 100, WindowStyle.None, WindowTitleBarStyle.Custom);
        AssertInitialClientSize(240, 180, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Custom);
        AssertInitialClientSize(240, 180, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Native);
        AssertInitialClientSize(240, 180, 320.1, 240.1, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Custom);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Show_AtMinimum_ReservesTheNonClientFrame()
    {
        AssertInitialClientSize(320, 240, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Custom);
        AssertInitialClientSize(320, 240, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Native);
    }

    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Show_AboveMaximum_ConstrainsTheNativeClientBeforeSourceInitialized()
    {
        AssertInitialClientSize(640, 480, 320, 240, WindowStyle.None, WindowTitleBarStyle.Custom, isMaximum: true);
        AssertInitialClientSize(640, 480, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Custom, isMaximum: true);
        AssertInitialClientSize(640, 480, 320, 240, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Native, isMaximum: true);
        AssertInitialClientSize(640, 480, 320.1, 240.1, WindowStyle.SingleBorderWindow, WindowTitleBarStyle.Custom, isMaximum: true);
    }

    [Theory]
    [InlineData(80, 100, double.PositiveInfinity, 1, 100)]
    [InlineData(125, 100.1, double.PositiveInfinity, 1.25, 126)]
    [InlineData(126, 0, 100.1, 1.25, 125)]
    [InlineData(150, 100, 200, 1.25, 150)]
    public void PixelConstraints_RespectFractionalDpiAndPreserveSizesWithinBounds(
        int requested, double minimum, double maximum, double scale, int expected)
    {
        Assert.Equal(expected, Window.ConstrainClientSizeInPixels(requested, minimum, maximum, scale));
    }

    private static void AssertInitialClientSize(
        double width, double height, double boundWidth, double boundHeight,
        WindowStyle style, WindowTitleBarStyle titleBarStyle, bool isMaximum = false)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            WindowStyle = style,
            TitleBarStyle = titleBarStyle,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        if (isMaximum)
        {
            window.MaxWidth = boundWidth;
            window.MaxHeight = boundHeight;
        }
        else
        {
            window.MinWidth = boundWidth;
            window.MinHeight = boundHeight;
        }

        bool sourceInitialized = false;
        window.SourceInitialized += (_, _) =>
        {
            sourceInitialized = true;
            AssertSize();
        };
        try
        {
            window.Show();
            Assert.True(sourceInitialized);
            AssertSize();
        }
        finally
        {
            window.Close();
        }

        void AssertSize()
        {
            Assert.True(GetClientRect(window.Handle, out var client));
            double scale = GetDpiForWindow(window.Handle) / 96.0;
            int expectedWidth = (int)(isMaximum ? Math.Floor(boundWidth * scale) : Math.Ceiling(boundWidth * scale));
            int expectedHeight = (int)(isMaximum ? Math.Floor(boundHeight * scale) : Math.Ceiling(boundHeight * scale));
            Assert.Equal(expectedWidth, client.Right - client.Left);
            Assert.Equal(expectedHeight, client.Bottom - client.Top);
            Assert.Equal(expectedWidth / scale, window.Width, precision: 10);
            Assert.Equal(expectedHeight / scale, window.Height, precision: 10);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out PixelRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
