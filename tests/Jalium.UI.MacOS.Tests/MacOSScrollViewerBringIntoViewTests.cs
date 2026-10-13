using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSScrollViewerBringIntoViewTests
{
    [Theory]
    [InlineData(false, false, -12)]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 32)]
    [InlineData(false, true, -12)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 32)]
    [InlineData(true, false, -12)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 32)]
    [InlineData(true, true, -12)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 32)]
    public void NestedTargetIsFullyVisibleAfterScrollingInBothDirections(
        bool providerScrolls, bool horizontal, int margin)
    {
        var target = new Border { Width = 44, Height = 42 };
        var content = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            Margin = new Thickness(margin),
            Spacing = 18
        };
        content.Children.Add(new Border { Width = horizontal ? 260 : 44, Height = horizontal ? 42 : 260 });
        content.Children.Add(new Border
        {
            Padding = new Thickness(11, 13, 11, 13),
            Margin = new Thickness(7, 9, 11, 13),
            Child = target
        });
        content.Children.Add(new Border { Width = horizontal ? 300 : 44, Height = horizontal ? 42 : 300 });
        var viewer = new ScrollViewer
        {
            Content = content,
            Padding = new Thickness(5, 7, 11, 13),
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto
        };
        if (providerScrolls) viewer.ScrollInfo = content;
        Layout();
        ScrollTo(55);
        Layout();

        Rect viewport = new(viewer.Padding.Left, viewer.Padding.Top, viewer.ViewportWidth, viewer.ViewportHeight);
        Rect before = Bounds();
        double expected = Offset() + (horizontal ? before.Right - viewport.Right : before.Bottom - viewport.Bottom);
        target.BringIntoView();
        Layout();
        Assert.Equal(expected, Offset(), precision: 3);
        AssertVisible();

        ScrollTo(horizontal ? viewer.ScrollableWidth : viewer.ScrollableHeight);
        Layout();
        before = Bounds();
        expected = Offset() + (horizontal ? before.Left - viewport.Left : before.Top - viewport.Top);
        target.BringIntoView();
        Layout();
        Assert.Equal(expected, Offset(), precision: 3);
        AssertVisible();

        double settled = Offset();
        target.BringIntoView();
        Layout();
        Assert.Equal(settled, Offset(), precision: 3);
        AssertVisible();

        void Layout()
        {
            viewer.Measure(new Size(160, 160));
            viewer.Arrange(new Rect(0, 0, 160, 160));
        }
        void ScrollTo(double offset)
        {
            if (horizontal) viewer.ScrollToHorizontalOffset(offset);
            else viewer.ScrollToVerticalOffset(offset);
        }
        double Offset() => horizontal ? viewer.HorizontalOffset : viewer.VerticalOffset;
        Rect Bounds() => new(target.TranslatePoint(new Point(0, 0), viewer), target.RenderSize);
        void AssertVisible()
        {
            Rect actual = Bounds();
            Assert.True(actual.Left >= viewport.Left - .01 && actual.Right <= viewport.Right + .01 &&
                actual.Top >= viewport.Top - .01 && actual.Bottom <= viewport.Bottom + .01,
                $"Target {actual} is clipped by viewport {viewport}");
        }
    }
}
