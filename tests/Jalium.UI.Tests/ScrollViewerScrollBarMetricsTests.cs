using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

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
        Assert.Equal(1, content.MeasureCount);

        var beforeResize = content.MeasureCount;
        viewer.Width = 260;
        viewer.Measure(new Size(260, 160));
        viewer.Arrange(new Rect(0, 0, 260, 160));

        Assert.Equal(beforeResize + 1, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(1000, viewer.ExtentHeight);
    }

    [Fact]
    public void ScrollableAxis_UsesStableInfiniteConstraintAcrossOverflowTransitions()
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
        Assert.Equal(1, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(1000, viewer.ExtentHeight);

        content.DesiredHeight = 80;
        // Detached test trees have no LayoutManager to propagate the child's invalidation.
        viewer.InvalidateMeasure();
        var beforeFit = content.MeasureCount;
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(beforeFit + 1, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(80, viewer.ExtentHeight);
        Assert.Equal(0, viewer.ScrollableHeight);

        content.DesiredHeight = 1000;
        viewer.InvalidateMeasure();
        var beforeOverflow = content.MeasureCount;
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(beforeOverflow + 1, content.MeasureCount);
        Assert.True(double.IsPositiveInfinity(content.LastAvailableSize.Height));
        Assert.Equal(1000, viewer.ExtentHeight);
    }

    [Fact]
    public void SwitchingBetweenShortAndTallContent_ReusesTallPageMeasureCache()
    {
        var tall = new MutableOverflowMeasureProbe { DesiredHeight = 1000 };
        var shortPage = new MutableOverflowMeasureProbe { DesiredHeight = 80 };
        var content = new SwitchingMeasureHost(tall, shortPage);
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
        Assert.Equal(1, tall.MeasureCount);
        Assert.True(double.IsPositiveInfinity(tall.LastAvailableSize.Height));

        content.Activate(shortPage);
        viewer.InvalidateMeasure();
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));
        Assert.Equal(1, shortPage.MeasureCount);
        Assert.True(double.IsPositiveInfinity(shortPage.LastAvailableSize.Height));

        content.Activate(tall);
        viewer.InvalidateMeasure();
        viewer.Measure(new Size(240, 160));
        viewer.Arrange(new Rect(0, 0, 240, 160));

        Assert.Equal(1, tall.MeasureCount);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThumbDrag_AtFrozenVerticalMaximum_FollowsExpandedRangeToBottom(
        bool isDeferredScrollingEnabled)
    {
        const double initialExtentHeight = 520;
        const double expandedExtentHeight = 920;
        const double viewportHeight = 120;

        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = false,
            IsDeferredScrollingEnabled = isDeferredScrollingEnabled
        };
        var verticalScrollBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");

        SetPrivateField(viewer, "_extentHeight", initialExtentHeight);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        InvokeUpdateScrollBarMetrics(viewer);
        Assert.Equal(400, verticalScrollBar.Maximum);

        // Virtualized content can refine its estimated extent while the captured thumb keeps
        // the pointer-to-value mapping it started with. The thumb is at that frozen maximum,
        // even though the live scrollable range has already grown behind it.
        SetPrivateField(verticalScrollBar, "_isDragging", true);
        verticalScrollBar.Value = verticalScrollBar.Maximum;
        verticalScrollBar.RaiseEvent(new ScrollEventArgs(
            ScrollBar.ScrollEvent,
            ScrollEventType.ThumbTrack,
            verticalScrollBar.Value)
        {
            Source = verticalScrollBar
        });

        if (isDeferredScrollingEnabled)
        {
            Assert.True(GetPrivateField<bool>(viewer, "_isDeferredScrolling"));
        }
        else
        {
            Assert.Equal(400, GetPrivateField<double>(viewer, "_pendingDragVerticalOffset"));
            Assert.True(GetPrivateField<bool>(viewer, "_pendingDragVerticalEndAnchor"));
        }

        SetPrivateField(viewer, "_extentHeight", expandedExtentHeight);
        InvokeUpdateScrollBarMetrics(viewer);

        Assert.Equal(800, viewer.ScrollableHeight);
        Assert.Equal(400, verticalScrollBar.Maximum);
        Assert.Equal(400, verticalScrollBar.Value);

        SetPrivateField(verticalScrollBar, "_isDragging", false);
        verticalScrollBar.RaiseEvent(new ScrollEventArgs(
            ScrollBar.ScrollEvent,
            ScrollEventType.EndScroll,
            verticalScrollBar.Value)
        {
            Source = verticalScrollBar
        });

        Assert.Equal(800, viewer.VerticalOffset);
        Assert.Equal(800, verticalScrollBar.Maximum);
        Assert.Equal(800, verticalScrollBar.Value);
    }

    [Fact]
    public void ThumbDrag_EndAnchor_FollowsGrowingExtent_WithFiniteProviderRequests()
    {
        const double viewportHeight = 120;
        var info = new FiniteOnlyScrollInfo
        {
            ExtentHeight = 520,
            ViewportHeight = viewportHeight
        };
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = false
        };
        info.ScrollOwner = viewer;
        SetPrivateField(viewer, "_scrollInfo", info);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        viewer.InvalidateScrollInfo();
        InvokeUpdateScrollBarMetrics(viewer);

        var verticalScrollBar = GetPrivateField<ScrollBar>(viewer, "_verticalScrollBar");
        Assert.Equal(400, verticalScrollBar.Maximum);

        SetPrivateField(verticalScrollBar, "_isDragging", true);
        verticalScrollBar.Value = verticalScrollBar.Maximum;
        verticalScrollBar.RaiseEvent(new ScrollEventArgs(
            ScrollBar.ScrollEvent,
            ScrollEventType.ThumbTrack,
            verticalScrollBar.Value)
        {
            Source = verticalScrollBar
        });

        viewer.InvalidateScrollInfo();
        Assert.Equal(400, info.VerticalOffset);

        // A replacement realization window can briefly report no extent. The endpoint anchor
        // must ignore that contraction instead of interpreting it as a request for the top.
        info.ExtentHeight = 0;
        viewer.InvalidateScrollInfo();
        Assert.Equal(400, info.VerticalOffset);

        // Each notification represents a virtualized measure refining the extent. The frozen
        // thumb still says 400, but the content must follow the live finite maxima 800 then 1000.
        info.ExtentHeight = 920;
        viewer.InvalidateScrollInfo();
        Assert.Equal(800, info.VerticalOffset);

        info.ExtentHeight = 1120;
        viewer.InvalidateScrollInfo();
        Assert.Equal(1000, info.VerticalOffset);

        SetPrivateField(verticalScrollBar, "_isDragging", false);
        verticalScrollBar.RaiseEvent(new ScrollEventArgs(
            ScrollBar.ScrollEvent,
            ScrollEventType.EndScroll,
            verticalScrollBar.Value)
        {
            Source = verticalScrollBar
        });

        Assert.Equal(1000, viewer.VerticalOffset);
        Assert.DoesNotContain(info.VerticalOffsetRequests, value => !double.IsFinite(value));

        // Any explicit move away from the end cancels the temporary anchor.
        viewer.ScrollToVerticalOffset(200);
        info.ExtentHeight = 1320;
        viewer.InvalidateScrollInfo();
        Assert.Equal(200, info.VerticalOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WheelDown_ReachingEstimatedEnd_FollowsGrowingExtent(
        bool useSmoothInertia)
    {
        const double viewportHeight = 120;
        var info = new FiniteOnlyScrollInfo
        {
            ExtentHeight = 520,
            ViewportHeight = viewportHeight,
            WheelStep = 100
        };
        info.SetVerticalOffset(360);

        var viewer = new ScrollViewer
        {
            IsScrollInertiaEnabled = useSmoothInertia,
            ScrollInertiaDurationMs = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsScrollBarAutoHideEnabled = false
        };
        info.ScrollOwner = viewer;
        SetPrivateField(viewer, "_scrollInfo", info);
        SetPrivateField(viewer, "_viewportHeight", viewportHeight);
        viewer.InvalidateScrollInfo();
        InvokeUpdateScrollBarMetrics(viewer);

        var wheel = new MouseWheelEventArgs(
            UIElement.MouseWheelEvent,
            new Point(8, 8),
            delta: -120,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
        viewer.RaiseEvent(wheel);

        for (var frame = 0;
             frame < 128 && GetPrivateField<bool>(viewer, "_isSmoothScrolling");
             frame++)
        {
            InvokePrivateMethod(viewer, "AdvanceSmoothScrollByMilliseconds", 16L);
        }

        Assert.True(wheel.Handled);
        Assert.Equal(400, info.VerticalOffset);
        Assert.False(GetPrivateField<bool>(viewer, "_isSmoothScrolling"));

        info.ExtentHeight = 0;
        viewer.InvalidateScrollInfo();
        Assert.Equal(400, info.VerticalOffset);

        info.ExtentHeight = 920;
        viewer.InvalidateScrollInfo();
        Assert.Equal(800, info.VerticalOffset);
        Assert.DoesNotContain(info.VerticalOffsetRequests, value => !double.IsFinite(value));
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

    private static void InvokePrivateMethod(
        ScrollViewer viewer,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(ScrollViewer).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(viewer, arguments);
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

    private sealed class FiniteOnlyScrollInfo : IScrollInfo
    {
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth { get; set; }
        public double ExtentHeight { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public double HorizontalOffset { get; private set; }
        public double VerticalOffset { get; private set; }
        public double WheelStep { get; set; } = 48;
        public List<double> VerticalOffsetRequests { get; } = [];
        public ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() { }
        public void LineDown() { }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() { }
        public void PageDown() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - WheelStep);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + WheelStep);
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public void SetHorizontalOffset(double offset)
        {
            HorizontalOffset = double.IsFinite(offset)
                ? Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth))
                : 0;
        }

        public void SetVerticalOffset(double offset)
        {
            VerticalOffsetRequests.Add(offset);
            VerticalOffset = double.IsFinite(offset)
                ? Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight))
                : 0;
        }

        public Rect MakeVisible(Jalium.UI.Media.Visual visual, Rect rectangle) => rectangle;
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

    private sealed class SwitchingMeasureHost : Panel
    {
        private UIElement _active;

        public SwitchingMeasureHost(params UIElement[] pages)
        {
            _active = pages[0];
            foreach (var page in pages)
            {
                Children.Add(page);
            }
        }

        public void Activate(UIElement page)
        {
            _active = page;
            InvalidateMeasure();
            InvalidateArrange();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _active.Measure(availableSize);
            return _active.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _active.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
