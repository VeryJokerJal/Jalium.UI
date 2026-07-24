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
    public void PersistentlyOverflowingContent_ResizeUsesSingleMeasurePass()
    {
        var content = new OverflowMeasureProbe();
        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 240,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(2, content.MeasureCount); // initial finite probe + overflow measure

        var beforeResize = content.MeasureCount;
        viewer.Width = 260;
        viewer.Measure(new Size(260, 160));
        viewer.Arrange(new Rect(0, 0, 260, 160));

        Assert.Equal(beforeResize + 1, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(1000, viewer.ExtentHeight);
    }

    [Fact]
    public void StickyOverflow_TransitionsBetweenOverflowAndFit()
    {
        var content = new MutableOverflowMeasureProbe { DesiredHeight = 1000 };
        var viewer = new ScrollViewer
        {
            Content = content,
            Width = 240,
            Height = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(2, content.MeasureCount);
        Assert.Equal(1000, viewer.ExtentHeight);

        content.DesiredHeight = 80;
        // Detached test trees have no LayoutManager to propagate the child's invalidation.
        viewer.InvalidateMeasure();
        var beforeFit = content.MeasureCount;
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(beforeFit + 2, content.MeasureCount);
        Assert.False(double.IsInfinity(content.LastAvailableSize.Height));
        Assert.Equal(80, viewer.ExtentHeight);
        Assert.Equal(0, viewer.ScrollableHeight);

        content.DesiredHeight = 1000;
        viewer.InvalidateMeasure();
        var beforeOverflow = content.MeasureCount;
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(beforeOverflow + 2, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(1000, viewer.ExtentHeight);
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

    [Fact]
    public void ConfigureScrollBar_ActiveThumbDrag_DoesNotOverwritePointerValueWithTrailingContentOffset()
    {
        var scrollBar = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 400,
            Value = 300
        };
        var draggingField = typeof(ScrollBar).GetField("_isDragging", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(draggingField);
        draggingField!.SetValue(scrollBar, true);

        InvokeConfigureScrollBar(
            scrollBar,
            maxOffset: 400,
            viewportSize: 120,
            offset: 100,
            visibilityMode: ScrollBarVisibility.Auto,
            canScroll: true);

        Assert.Equal(300, scrollBar.Value);
    }

    [Theory]
    [InlineData(180.0)] // extent == viewport: raw scrollable range reaches zero
    [InlineData(80.0)]  // extent < viewport: raw scrollable range becomes negative
    public void UpdateScrollBarMetrics_ActiveVerticalThumbDrag_FreezesMetricsUntilRelease(
        double transientExtentHeight)
    {
        const double initialExtentHeight = 520;
        const double initialViewportHeight = 120;
        const double initialPointerValue = 300;
        const double transientViewportHeight = 180;

        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = false
        };
        var verticalScrollBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        SetPrivateField(viewer, "_extentHeight", initialExtentHeight);
        SetPrivateField(viewer, "_viewportHeight", initialViewportHeight);
        SetPrivateField(viewer, "_verticalOffset", initialPointerValue);
        InvokeUpdateScrollBarMetrics(viewer);

        Assert.Equal(400, verticalScrollBar.Maximum);
        Assert.Equal(initialViewportHeight, verticalScrollBar.ViewportSize);
        Assert.Equal(initialPointerValue, verticalScrollBar.Value);
        Assert.Equal(Visibility.Visible, verticalScrollBar.Visibility);

        // A virtualizing panel can briefly report a collapsed extent while a new realization
        // window is being measured. The content offset may already have been coerced to zero,
        // but the captured Thumb must keep its original pointer-to-range mapping until release.
        SetPrivateField(verticalScrollBar, "_isDragging", true);
        SetPrivateField(viewer, "_extentHeight", transientExtentHeight);
        SetPrivateField(viewer, "_viewportHeight", transientViewportHeight);
        SetPrivateField(viewer, "_verticalOffset", 0.0);
        InvokeUpdateScrollBarMetrics(viewer);

        Assert.Equal(0, viewer.ScrollableHeight);
        Assert.Equal(400, verticalScrollBar.Maximum);
        Assert.Equal(initialViewportHeight, verticalScrollBar.ViewportSize);
        Assert.Equal(initialPointerValue, verticalScrollBar.Value);
        Assert.Equal(Visibility.Visible, verticalScrollBar.Visibility);

        // ScrollBar raises EndScroll after clearing its dragging flag. Even when the content is
        // already at the final clamped offset (so ScrollToVerticalOffset is otherwise a no-op),
        // release must publish the latest metrics that were held back during the drag.
        SetPrivateField(verticalScrollBar, "_isDragging", false);
        verticalScrollBar.RaiseEvent(new ScrollEventArgs(
            ScrollBar.ScrollEvent,
            ScrollEventType.EndScroll,
            verticalScrollBar.Value)
        {
            Source = verticalScrollBar
        });

        Assert.Equal(0, verticalScrollBar.Maximum);
        Assert.Equal(transientViewportHeight, verticalScrollBar.ViewportSize);
        Assert.Equal(0, verticalScrollBar.Value);
        Assert.Equal(Visibility.Collapsed, verticalScrollBar.Visibility);
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

    private static void InvokeUpdateScrollBarMetrics(ScrollViewer viewer)
    {
        var method = typeof(ScrollViewer).GetMethod(
            "UpdateScrollBarMetrics",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(viewer, null);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        return Assert.IsType<T>(field!.GetValue(instance));
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        field!.SetValue(instance, value);
    }

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public UIElement? DirectContentElement => ContentElement;
    }

    private sealed class OverflowMeasureProbe : FrameworkElement
    {
        public int MeasureCount { get; private set; }
        public Size LastAvailableSize { get; private set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            LastAvailableSize = availableSize;
            return new Size(120, 1000);
        }
    }

    private sealed class MutableOverflowMeasureProbe : FrameworkElement
    {
        private double _desiredHeight;

        public int MeasureCount { get; private set; }
        public Size LastAvailableSize { get; private set; }

        public double DesiredHeight
        {
            get => _desiredHeight;
            set
            {
                if (Math.Abs(_desiredHeight - value) <= 0.01)
                {
                    return;
                }

                _desiredHeight = value;
                InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureCount++;
            LastAvailableSize = availableSize;
            return new Size(120, DesiredHeight);
        }
    }
}
