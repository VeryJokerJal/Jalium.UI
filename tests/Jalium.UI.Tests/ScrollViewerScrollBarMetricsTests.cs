using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class ScrollViewerScrollBarMetricsTests
{
    [Fact]
    public void ConfigureScrollBar_NonFiniteMetrics_ShouldClampToSafeDefaults()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical
        };

        InvokeConfigureScrollBar(
            scrollBar,
            maxOffset: double.PositiveInfinity,
            viewportSize: double.NaN,
            offset: double.PositiveInfinity,
            visibilityMode: ScrollBarVisibility.Visible,
            canScroll: true);

        Assert.Equal(0, scrollBar.Minimum);
        Assert.Equal(0, scrollBar.Maximum);
        Assert.Equal(0, scrollBar.ViewportSize);
        Assert.Equal(1, scrollBar.LargeChange);
        Assert.Equal(0, scrollBar.Value);
        Assert.Equal(Visibility.Visible, scrollBar.Visibility);
    }

    [Fact]
    public void ConfigureScrollBar_FiniteMetrics_ShouldPreserveExpectedValues()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical
        };

        InvokeConfigureScrollBar(
            scrollBar,
            maxOffset: 400,
            viewportSize: 120,
            offset: 180,
            visibilityMode: ScrollBarVisibility.Auto,
            canScroll: true);

        Assert.Equal(0, scrollBar.Minimum);
        Assert.Equal(400, scrollBar.Maximum);
        Assert.Equal(120, scrollBar.ViewportSize);
        Assert.Equal(120, scrollBar.LargeChange);
        Assert.Equal(180, scrollBar.Value);
        Assert.Equal(Visibility.Visible, scrollBar.Visibility);
    }

    private static void InvokeConfigureScrollBar(
        ScrollBar scrollBar,
        double maxOffset,
        double viewportSize,
        double offset,
        ScrollBarVisibility visibilityMode,
        bool canScroll)
    {
        var method = typeof(ScrollViewer).GetMethod("ConfigureScrollBar", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(null, [scrollBar, maxOffset, viewportSize, offset, visibilityMode, canScroll]);
    }
}
