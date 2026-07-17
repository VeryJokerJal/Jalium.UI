using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public class ScrollViewerScrollBarMetricsTests
{
    [Fact]
    public void PublicShape_UsesContentControlContentContract()
    {
        Assert.Equal(typeof(ContentControl), typeof(ScrollViewer).BaseType);
        Assert.Null(typeof(ScrollViewer).GetProperty(
            nameof(ContentControl.Content),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(ScrollViewer).GetField(
            nameof(ContentControl.ContentProperty),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void ScalarContent_UsesInheritedContentPipeline()
    {
        var viewer = new ProbeScrollViewer { Content = "hello" };

        var text = Assert.IsType<TextBlock>(viewer.DirectContentElement);

        Assert.Equal("hello", text.Text);
        Assert.True(viewer.HasContent);
    }

    [Fact]
    public void ScrollViewer_WithScrollInfoContentMargin_ShouldIncludeMarginInScrollableExtent()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(0, 24, 0, 24)
        };
        content.Children.Add(new Border { Height = 120 });
        content.Children.Add(new Border { Height = 120 });

        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 160,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        viewer.Measure(new Size(160, 160));
        viewer.Arrange(new Rect(0, 0, 160, 160));
        viewer.ScrollToBottom();

        Assert.Equal(288, viewer.ExtentHeight, precision: 3);
        Assert.Equal(128, viewer.ScrollableHeight, precision: 3);
        Assert.Equal(128, viewer.VerticalOffset, precision: 3);
    }

    [Fact]
    public void ScrollViewer_WithNegativeContentMargin_ShouldNotThrowAndShrinkExtent()
    {
        // Regression: GetContentMargin used to funnel the per-axis margin sums
        // through the Size constructor, which throws on negatives. A content
        // element with e.g. Margin="-9,0,-9,0" (horizontal sum -18) crashed the
        // layout pass instead of shrinking the scrollable extent.
        var content = new StackPanel
        {
            Margin = new Thickness(-9, 0, -9, 0)
        };
        content.Children.Add(new Border { Height = 120 });
        content.Children.Add(new Border { Height = 120 });

        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 160,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        viewer.Measure(new Size(160, 160));
        viewer.Arrange(new Rect(0, 0, 160, 160));

        Assert.Equal(240, viewer.ExtentHeight, precision: 3);
    }

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

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public UIElement? DirectContentElement => ContentElement;
    }
}
