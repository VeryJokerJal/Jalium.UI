using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ScrollViewerWpfParityTests
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
