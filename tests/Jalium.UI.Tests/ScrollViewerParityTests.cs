using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class ScrollViewerParityTests
{
    [Fact]
    public void ScrollInfoSurface_UsesPrimitivesContractAndWpfAccessibility()
    {
        Assert.Equal("Jalium.UI.Controls.Primitives", typeof(IScrollInfo).Namespace);

        var property = typeof(ScrollViewer).GetProperty(
            "ScrollInfo",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(IScrollInfo), property!.PropertyType);
        Assert.True(property.GetMethod!.IsFamilyOrAssembly);
        Assert.True(property.SetMethod!.IsFamilyOrAssembly);

        var invalidate = typeof(ScrollViewer).GetMethod(
            nameof(ScrollViewer.InvalidateScrollInfo),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(invalidate);
        Assert.Equal(typeof(void), invalidate!.ReturnType);
    }

    [Fact]
    public void ScrollInfoAssignment_TransfersOwnershipAndSynchronizesMetrics()
    {
        var viewer = new ProbeScrollViewer();
        var first = new TestScrollInfo
        {
            ExtentWidth = 300,
            ExtentHeight = 240,
            ViewportWidth = 100,
            ViewportHeight = 80,
            HorizontalOffset = 25,
            VerticalOffset = 40,
        };

        viewer.ExposedScrollInfo = first;
        Assert.Same(viewer, first.ScrollOwner);

        viewer.InvalidateScrollInfo();
        Assert.Equal(300, viewer.ExtentWidth);
        Assert.Equal(240, viewer.ExtentHeight);
        Assert.Equal(25, viewer.HorizontalOffset);
        Assert.Equal(40, viewer.VerticalOffset);

        var second = new TestScrollInfo();
        viewer.ExposedScrollInfo = second;
        Assert.Null(first.ScrollOwner);
        Assert.Same(viewer, second.ScrollOwner);
    }

    [Fact]
    public void ScrollInfoWheel_LeavesBoundaryEventForAncestorScrollViewer()
    {
        var viewer = new ProbeScrollViewer { IsScrollInertiaEnabled = false };
        var scrollInfo = new TestScrollInfo
        {
            CanVerticallyScroll = true,
            ExtentHeight = 1000,
            ViewportHeight = 100,
            VerticalOffset = 900,
        };
        viewer.ExposedScrollInfo = scrollInfo;
        viewer.InvalidateScrollInfo();

        var atBottom = CreateMouseWheel(-120);
        viewer.ExposedOnMouseWheel(atBottom);
        Assert.False(atBottom.Handled);

        scrollInfo.VerticalOffset = 400;
        viewer.InvalidateScrollInfo();
        var inMiddle = CreateMouseWheel(-120);
        viewer.ExposedOnMouseWheel(inMiddle);
        Assert.True(inMiddle.Handled);

        scrollInfo.VerticalOffset = 0;
        viewer.InvalidateScrollInfo();
        var atTop = CreateMouseWheel(120);
        viewer.ExposedOnMouseWheel(atTop);
        Assert.False(atTop.Handled);
    }

    [Fact]
    public void LayoutClip_ReusesFrozenGeometryUntilRenderSizeChanges()
    {
        var viewer = new ProbeScrollViewer { ClipToBounds = true };
        viewer.Measure(new Size(320, 240));
        viewer.Arrange(new Rect(0, 0, 320, 240));

        var first = Assert.IsType<RectangleGeometry>(viewer.ExposedLayoutClip());
        var repeated = Assert.IsType<RectangleGeometry>(viewer.ExposedLayoutClip());

        Assert.Same(first, repeated);
        Assert.True(first.IsFrozen);
        Assert.Equal(new Rect(0, 0, 320, 240), first.Rect);

        viewer.Arrange(new Rect(0, 0, 400, 240));
        var resized = Assert.IsType<RectangleGeometry>(viewer.ExposedLayoutClip());

        Assert.NotSame(first, resized);
        Assert.True(resized.IsFrozen);
        Assert.Equal(new Rect(0, 0, 400, 240), resized.Rect);
    }

    private static MouseWheelEventArgs CreateMouseWheel(int delta) => new(
        UIElement.MouseWheelEvent,
        new Point(10, 10),
        delta,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        MouseButtonState.Released,
        ModifierKeys.None,
        timestamp: 1);

    private sealed class ProbeScrollViewer : ScrollViewer
    {
        public IScrollInfo? ExposedScrollInfo
        {
            get => ScrollInfo;
            set => ScrollInfo = value;
        }

        public void ExposedOnMouseWheel(MouseWheelEventArgs e) => OnMouseWheel(e);

        public Geometry? ExposedLayoutClip() => GetLayoutClip();
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
