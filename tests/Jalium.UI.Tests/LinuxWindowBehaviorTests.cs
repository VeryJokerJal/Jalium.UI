using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class LinuxWindowBehaviorTests
{
    [Fact]
    public void PlatformWindowStyle_ReflectsWindowStyleAndResizeMode()
    {
        const uint Borderless = 0x01;
        const uint Resizable = 0x02;
        const uint Titlebar = 0x04;
        const uint Minimizable = 0x10;
        const uint Maximizable = 0x20;

        uint regular = Window.ComputePlatformWindowStyle(
            WindowStyle.SingleBorderWindow,
            ResizeMode.CanResize,
            WindowTitleBarStyle.Native,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Titlebar, regular & Titlebar);
        Assert.Equal(Resizable, regular & Resizable);
        Assert.Equal(Minimizable, regular & Minimizable);
        Assert.Equal(Maximizable, regular & Maximizable);
        Assert.Equal(0u, regular & Borderless);

        uint fixedBorderless = Window.ComputePlatformWindowStyle(
            WindowStyle.None,
            ResizeMode.NoResize,
            WindowTitleBarStyle.Native,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Borderless, fixedBorderless & Borderless);
        Assert.Equal(0u, fixedBorderless & Resizable);
        Assert.Equal(0u, fixedBorderless & Minimizable);
        Assert.Equal(0u, fixedBorderless & Maximizable);

        uint customMinimizeOnly = Window.ComputePlatformWindowStyle(
            WindowStyle.SingleBorderWindow,
            ResizeMode.CanMinimize,
            WindowTitleBarStyle.Custom,
            topmost: false,
            allowsTransparency: false);
        Assert.Equal(Borderless, customMinimizeOnly & Borderless);
        Assert.Equal(Minimizable, customMinimizeOnly & Minimizable);
        Assert.Equal(0u, customMinimizeOnly & Resizable);
        Assert.Equal(0u, customMinimizeOnly & Maximizable);
    }

    [Fact]
    public void RunModalLoop_ProcessesQueuedDispatcherWorkOnEveryPlatform()
    {
        var dispatcher = Dispatcher.GetForCurrentThread();
        bool keepRunning = true;
        bool operationRan = false;

        dispatcher.BeginInvoke(() =>
        {
            operationRan = true;
            keepRunning = false;
        });

        dispatcher.RunModalLoop(() => keepRunning);

        Assert.True(operationRan);
        Assert.False(keepRunning);
    }

    [Theory]
    [InlineData(0, WindowState.Normal)]
    [InlineData(1, WindowState.Minimized)]
    [InlineData(2, WindowState.Maximized)]
    [InlineData(3, WindowState.FullScreen)]
    public void PlatformWindowState_PreservesFullscreen(int nativeState, WindowState expected)
    {
        Assert.Equal(expected, Window.MapPlatformWindowState(nativeState));
    }
}
