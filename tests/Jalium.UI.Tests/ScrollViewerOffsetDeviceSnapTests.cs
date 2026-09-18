using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

// Scroll offsets translate the whole content subtree while the renderer pixel-snaps
// text runs and axis-aligned strokes in device space. A fractional scroll translation
// therefore makes each primitive cross its integer pixel boundary on a different
// frame — neighbouring text/paths visibly jitter against each other by 1px during a
// smooth scroll. These tests pin the fix: committed offsets are quantized to whole
// physical pixels, with the 0 / max end stops kept exact.
public sealed class ScrollViewerOffsetDeviceSnapTests
{
    [Theory]
    [InlineData(123.37, 900.0, 1.0, 123.0)]
    [InlineData(123.5, 900.0, 1.0, 124.0)]     // AwayFromZero midpoint
    [InlineData(0.2, 900.0, 1.0, 0.0)]
    [InlineData(-3.7, 900.0, 1.0, 0.0)]        // top stop exact
    [InlineData(10.4, 900.0, 1.5, 10.666666666666666)]  // round(15.6)=16 → 16/1.5
    [InlineData(10.4, 900.0, 2.0, 10.5)]       // round(20.8)=21 → 21/2
    [InlineData(900.4, 900.4, 1.0, 900.4)]     // fractional bottom stop preserved exactly
    [InlineData(950.0, 900.4, 1.0, 900.4)]     // past the end clamps to the exact stop
    [InlineData(5.3, 0.0, 1.0, 5.0)]           // metrics not synced: rounded, not clamped
    [InlineData(5.3, 100.0, 0.0, 5.3)]         // unusable DPI: pass through
    public void SnapScrollOffsetToDevicePixels_QuantizesToWholeDevicePixels(
        double offset, double maxOffset, double dpiScale, double expected)
    {
        Assert.Equal(expected,
            ScrollViewer.SnapScrollOffsetToDevicePixels(offset, maxOffset, dpiScale), 12);
    }

    [Fact]
    public void ScrollToOffsets_CommitWholeDevicePixels_ThroughIScrollInfo()
    {
        var viewer = new ProbeScrollViewer();
        var info = new TestScrollInfo
        {
            ExtentWidth = 1000,
            ExtentHeight = 1000,
            ViewportWidth = 100,
            ViewportHeight = 100,
        };

        viewer.ExposedScrollInfo = info;
        viewer.InvalidateScrollInfo();

        viewer.ScrollToVerticalOffset(123.37);
        Assert.Equal(123.0, info.VerticalOffset);
        Assert.Equal(123.0, viewer.VerticalOffset);

        viewer.ScrollToHorizontalOffset(41.62);
        Assert.Equal(42.0, info.HorizontalOffset);
        Assert.Equal(42.0, viewer.HorizontalOffset);
    }

    [Fact]
    public void ScrollToVerticalOffset_FractionalBottomStop_IsReachedExactly()
    {
        var viewer = new ProbeScrollViewer();
        var info = new TestScrollInfo
        {
            ExtentHeight = 1000.4,
        };

        viewer.ExposedScrollInfo = info;
        viewer.InvalidateScrollInfo();

        // The viewer's viewport is authoritatively computed in ArrangeOverride and
        // stays 0 headless, so the scrollable maximum here is the fractional 1000.4.
        viewer.ScrollToVerticalOffset(1000.15);
        Assert.Equal(1000.0, info.VerticalOffset, 12);

        viewer.ScrollToBottom();
        Assert.Equal(1000.4, info.LastVerticalOffsetRequest, 12);
        Assert.Equal(1000.4, info.VerticalOffset, 12);
        Assert.True(viewer.IsAtVerticalEnd);
    }

    // A sub-device-pixel closing step rounds back onto the pixel the offset already
    // occupies. Without judging progress by the committed offsets the smooth-scroll
    // timer would report movement forever while never getting closer.
    [Fact]
    public void SmoothScrollTick_SubPixelRemainder_StopsInsteadOfSpinning()
    {
        var viewer = new ProbeScrollViewer();
        var info = new TestScrollInfo
        {
            ExtentHeight = 1000,
            ViewportHeight = 100,
        };

        viewer.ExposedScrollInfo = info;
        viewer.InvalidateScrollInfo();
        viewer.ScrollToVerticalOffset(100.0);

        var type = typeof(ScrollViewer);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var isSmoothScrolling = type.GetField("_isSmoothScrolling", flags);
        var smoothTargetY = type.GetField("_smoothTargetY", flags);
        var advance = type.GetMethod("AdvanceSmoothScrollByMilliseconds", flags);
        Assert.NotNull(isSmoothScrolling);
        Assert.NotNull(smoothTargetY);
        Assert.NotNull(advance);

        isSmoothScrolling!.SetValue(viewer, true);
        smoothTargetY!.SetValue(viewer, 100.3);

        advance!.Invoke(viewer, new object[] { 16L });

        Assert.Equal(100.0, viewer.VerticalOffset);
        Assert.False((bool)isSmoothScrolling.GetValue(viewer)!);
    }

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public IScrollInfo? ExposedScrollInfo
        {
            get => ScrollInfo;
            set => ScrollInfo = value;
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
        public double LastHorizontalOffsetRequest { get; private set; }
        public double LastVerticalOffsetRequest { get; private set; }
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
        public void SetHorizontalOffset(double offset)
        {
            LastHorizontalOffsetRequest = offset;
            HorizontalOffset = double.IsFinite(offset)
                ? Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth))
                : 0;
        }

        public void SetVerticalOffset(double offset)
        {
            LastVerticalOffsetRequest = offset;
            VerticalOffset = double.IsFinite(offset)
                ? Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight))
                : 0;
        }
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
    }
}
