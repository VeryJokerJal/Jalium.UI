using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ScrollChangedEventArgsWpfParityTests
{
    [Fact]
    public void MetricPropertiesArePubliclyReadOnly()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(ScrollChangedEventArgs.HorizontalChange),
                     nameof(ScrollChangedEventArgs.VerticalChange),
                     nameof(ScrollChangedEventArgs.HorizontalOffset),
                     nameof(ScrollChangedEventArgs.VerticalOffset),
                     nameof(ScrollChangedEventArgs.ViewportWidth),
                     nameof(ScrollChangedEventArgs.ViewportHeight),
                     nameof(ScrollChangedEventArgs.ExtentWidth),
                     nameof(ScrollChangedEventArgs.ExtentHeight),
                     nameof(ScrollChangedEventArgs.ExtentWidthChange),
                     nameof(ScrollChangedEventArgs.ExtentHeightChange),
                     nameof(ScrollChangedEventArgs.ViewportWidthChange),
                     nameof(ScrollChangedEventArgs.ViewportHeightChange),
                 })
        {
            var property = typeof(ScrollChangedEventArgs).GetProperty(propertyName)!;
            Assert.True(property.CanRead);
            Assert.False(property.CanWrite);
        }

        Assert.False(typeof(ScrollChangedEventArgs).IsSealed);
        Assert.Empty(typeof(ScrollChangedEventArgs).GetConstructors());
    }

    [Fact]
    public void ExtentOnlyChangesRaiseThroughVirtualHookAndOffsetsAccumulateFromLastNotification()
    {
        var viewer = new ProbeScrollViewer();
        var scrollInfo = new TestScrollInfo
        {
            ExtentWidth = 100,
            ExtentHeight = 200
        };
        viewer.ExposedScrollInfo = scrollInfo;

        var routedCount = 0;
        ScrollChangedEventArgs? routedArgs = null;
        viewer.ScrollChanged += (_, e) =>
        {
            routedCount++;
            routedArgs = e;
        };

        viewer.InvalidateScrollInfo();
        Assert.Equal(1, routedCount);
        Assert.Equal(1, viewer.ScrollChangedHookCount);
        Assert.Equal(100, routedArgs!.ExtentWidth);
        Assert.Equal(100, routedArgs.ExtentWidthChange);
        Assert.Equal(200, routedArgs.ExtentHeightChange);

        scrollInfo.ExtentWidth = 125;
        viewer.InvalidateScrollInfo();
        Assert.Equal(2, routedCount);
        Assert.Equal(25, routedArgs!.ExtentWidthChange);
        Assert.Equal(0, routedArgs.HorizontalChange);

        scrollInfo.HorizontalOffset = 0.0005;
        viewer.InvalidateScrollInfo();
        Assert.Equal(2, routedCount);

        scrollInfo.HorizontalOffset = 0.0012;
        viewer.InvalidateScrollInfo();
        Assert.Equal(3, routedCount);
        Assert.Equal(0.0012, routedArgs!.HorizontalOffset, 8);
        Assert.Equal(0.0012, routedArgs.HorizontalChange, 8);
        Assert.Equal(3, viewer.ScrollChangedHookCount);
    }

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public int ScrollChangedHookCount { get; private set; }

        public IScrollInfo? ExposedScrollInfo
        {
            get => ScrollInfo;
            set => ScrollInfo = value;
        }

        protected override void OnScrollChanged(ScrollChangedEventArgs e)
        {
            ScrollChangedHookCount++;
            base.OnScrollChanged(e);
        }
    }

    private sealed class TestScrollInfo : IScrollInfo
    {
        public bool CanHorizontallyScroll { get; set; }
        public bool CanVerticallyScroll { get; set; }
        public double ExtentWidth { get; set; }
        public double ExtentHeight { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public double HorizontalOffset { get; set; }
        public double VerticalOffset { get; set; }
        public ScrollViewer? ScrollOwner { get; set; }

        public void LineUp() { }
        public void LineDown() { }
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() { }
        public void PageDown() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() { }
        public void MouseWheelDown() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double offset) => HorizontalOffset = offset;
        public void SetVerticalOffset(double offset) => VerticalOffset = offset;
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
