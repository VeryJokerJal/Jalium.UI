using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public class SystemParametersMonitorUnitsTests
{
    [Fact]
    public void WaylandMonitorOriginStaysLogicalWhilePhysicalSizeUsesScale()
    {
        var monitor = new NativeMethods.NativeMonitorInfo
        {
            X = 1920,
            Y = -240,
            Width = 3840,
            Height = 2160,
            WorkX = 1920,
            WorkY = -200,
            WorkWidth = 3840,
            WorkHeight = 2080,
            Scale = 2,
        };

        var bounds = SystemParameters.ConvertPlatformMonitorRect(
            monitor, workArea: false, wayland: true);
        var work = SystemParameters.ConvertPlatformMonitorRect(
            monitor, workArea: true, wayland: true);

        Assert.Equal(new Rect(1920, -240, 1920, 1080), bounds);
        Assert.Equal(new Rect(1920, -200, 1920, 1040), work);
    }

    [Fact]
    public void X11MonitorOriginAndSizeAreBothConvertedFromPhysicalPixels()
    {
        var monitor = new NativeMethods.NativeMonitorInfo
        {
            X = 3840,
            Y = -480,
            Width = 3840,
            Height = 2160,
            Scale = 2,
        };

        var bounds = SystemParameters.ConvertPlatformMonitorRect(
            monitor, workArea: false, wayland: false);

        Assert.Equal(new Rect(1920, -240, 1920, 1080), bounds);
    }
}
