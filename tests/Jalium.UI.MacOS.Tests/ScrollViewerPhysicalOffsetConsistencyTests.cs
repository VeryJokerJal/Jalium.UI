using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ScrollViewerPhysicalOffsetConsistencyTests : IDisposable
{
    public ScrollViewerPhysicalOffsetConsistencyTests() => ScrollBarTemplateTestHelper.Register();
    public void Dispose() => ScrollBarTemplateTestHelper.Reset();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnlargingPhysicalViewportSynchronizesRequestedAndCommittedOffset(bool horizontal)
    {
        var content = new Grid { MinWidth = 500, MinHeight = 400 };
        ScrollViewer.SetFillViewport(content, FillViewportMode.Both);
        var viewer = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Layout(viewer, content, 240, 160);
        if (horizontal) viewer.ScrollToHorizontalOffset(viewer.ScrollableWidth);
        else viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
        Assert.True(horizontal ? viewer.HorizontalOffset > 0 : viewer.VerticalOffset > 0);
        Layout(viewer, content, 800, 700);
        Assert.Equal(0, viewer.ScrollableWidth, 1);
        Assert.Equal(0, viewer.ScrollableHeight, 1);
        Assert.Equal(0, viewer.HorizontalOffset, 1);
        Assert.Equal(0, viewer.VerticalOffset, 1);
        Assert.Equal(viewer.GetValue(ScrollViewer.ContentHorizontalOffsetProperty), viewer.GetValue(ScrollViewer.HorizontalOffsetProperty));
        Assert.Equal(viewer.GetValue(ScrollViewer.ContentVerticalOffsetProperty), viewer.GetValue(ScrollViewer.VerticalOffsetProperty));
    }

    [Fact]
    public void PhysicalArrangePreservesDeferredThumbPreviewUntilRelease()
    {
        var content = new Grid { MinHeight = 1200 };
        ScrollViewer.SetFillViewport(content, FillViewportMode.Both);
        var viewer = new ScrollViewer
        {
            Content = content, IsDeferredScrollingEnabled = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Layout(viewer, content, 300, 200);
        var field = typeof(ScrollViewer).GetField("_verticalScrollBar", BindingFlags.NonPublic | BindingFlags.Instance);
        var bar = Assert.IsType<ScrollBar>(field!.GetValue(viewer));
        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent, ScrollEventType.ThumbTrack, 300) { Source = bar });
        Assert.Equal(300, viewer.VerticalOffset, 1);
        Assert.Equal(0.0, viewer.GetValue(ScrollViewer.ContentVerticalOffsetProperty));
        Layout(viewer, content, 300, 220);
        Assert.Equal(300, viewer.VerticalOffset, 1);
        Assert.Equal(0.0, viewer.GetValue(ScrollViewer.ContentVerticalOffsetProperty));
        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent, ScrollEventType.EndScroll, 300) { Source = bar });
        Assert.Equal(300.0, viewer.GetValue(ScrollViewer.ContentVerticalOffsetProperty));
        Assert.Equal(300, viewer.VerticalOffset, 1);
    }

    private static void Layout(ScrollViewer viewer, Grid content, double width, double height)
    {
        // Detached control trees have no Window LayoutManager for ancestor invalidation.
        for (var pass = 0; pass < 2; pass++)
        {
            content.InvalidateMeasure(); viewer.InvalidateMeasure();
            viewer.Measure(new Size(width, height));
            viewer.Arrange(new Rect(0, 0, width, height));
        }
    }
}
