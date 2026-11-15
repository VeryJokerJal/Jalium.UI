using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Input;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssPositioningTests
{
    [Fact]
    public void StickyRetainsItsFlowSlotAndFollowsPhysicalScrollTranslations()
    {
        var content = new StackPanel();
        var leading = new Border { Height = 40 };
        var sticky = new Border { Height = 30 };
        var following = new Border { Height = 400 };
        content.Children.Add(leading);
        content.Children.Add(sticky);
        content.Children.Add(following);
        Css.SetStyle(sticky, "position: sticky; top: 8px;");
        var viewer = Viewer(content);
        Layout(viewer, 240, 120);
        Assert.Equal(40, Y(sticky));
        Assert.Equal(70, Y(following));
        Assert.Equal(470, viewer.ExtentHeight);

        foreach (var offset in new[] { 90d, 100, 20, 90, 0, 90 })
        {
            viewer.ScrollToVerticalOffset(offset);
            Assert.Equal(Math.Max(8, 40 - offset), Y(sticky), 6);
            Assert.Equal(70 - offset, Y(following), 6);
            Assert.True(content.IsMeasureValid);
            Assert.True(sticky.IsArrangeValid);
        }
        Assert.Equal(470, viewer.ExtentHeight);
    }

    [Fact]
    public void StickyStopsAtItsContainingBlockEnd()
    {
        var section = new StackPanel();
        var sticky = new Border { Height = 30 };
        section.Children.Add(sticky);
        section.Children.Add(new Border { Height = 90 });
        Css.SetStyle(sticky, "position: sticky; top: 8px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 40 });
        content.Children.Add(section);
        content.Children.Add(new Border { Height = 300 });
        var viewer = Viewer(content);
        Layout(viewer, 240, 120);
        viewer.ScrollToVerticalOffset(110);
        Assert.Equal(8, Y(sticky), 6);
        viewer.ScrollToVerticalOffset(150);
        Assert.Equal(-20, Y(sticky), 6);
        Assert.Equal(Y(section) + section.ActualHeight, Y(sticky) + sticky.ActualHeight, 6);
    }

    [Fact]
    public void StickyUsesTheNearestNestedScrollport()
    {
        var sticky = new Border { Height = 24 };
        Css.SetStyle(sticky, "position: sticky; top: 6px;");
        var innerContent = new StackPanel();
        innerContent.Children.Add(new Border { Height = 40 });
        innerContent.Children.Add(sticky);
        innerContent.Children.Add(new Border { Height = 300 });
        var inner = Viewer(innerContent);
        inner.Height = 100;
        var outerContent = new StackPanel();
        outerContent.Children.Add(new Border { Height = 30 });
        outerContent.Children.Add(inner);
        outerContent.Children.Add(new Border { Height = 400 });
        var outer = Viewer(outerContent);
        Layout(outer, 200, 160);
        inner.ScrollToVerticalOffset(80);
        Assert.Equal(Y(inner) + 6, Y(sticky), 6);
        outer.ScrollToVerticalOffset(60);
        Assert.Equal(Y(inner) + 6, Y(sticky), 6);
    }

    [Fact]
    public void HorizontalStickyPercentagesUseTheScrollportWidth()
    {
        var sticky = new Border { Width = 40, Height = 30 };
        Css.SetStyle(sticky, "position: sticky; left: 10%;");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 80, Height = 30 });
        content.Children.Add(sticky);
        content.Children.Add(new Border { Width = 500, Height = 30 });
        var viewer = Viewer(content);
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Layout(viewer, 200, 80);
        viewer.ScrollToHorizontalOffset(100);
        Assert.Equal(20, X(sticky), 6);
        viewer.ScrollToHorizontalOffset(0);
        Assert.Equal(80, X(sticky), 6);
    }

    [Fact]
    public void StickyWithAutoInsetsMovesWithItsSiblings()
    {
        var sticky = new Border { Height = 30 };
        Css.SetStyle(sticky, "position: sticky; inset: auto;");
        var content = new StackPanel();
        content.Children.Add(sticky);
        content.Children.Add(new Border { Height = 400 });
        var viewer = Viewer(content);
        Layout(viewer, 200, 100);
        viewer.ScrollToVerticalOffset(80);
        Assert.Equal(-80, Y(sticky), 6);
    }

    [Fact]
    public void FixedIsOutOfFlowAndDoesNotMoveWithPhysicalScrolling()
    {
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; left: 12px; top: 9px; width: 50px; height: 30px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 400 });
        content.Children.Add(fixedBox);
        var viewer = Viewer(content);
        Layout(viewer, 240, 120);
        Assert.Equal(400, viewer.ExtentHeight);
        Assert.Equal(12, X(fixedBox), 6);
        Assert.Equal(9, Y(fixedBox), 6);
        foreach (var offset in new[] { 50d, 150, 20, 150, 0 })
        {
            viewer.ScrollToVerticalOffset(offset);
            Assert.Equal(12, X(fixedBox), 6);
            Assert.Equal(9, Y(fixedBox), 6);
        }
    }

    [Fact]
    public void FixedPercentagesAndOpposingInsetsRespondToViewportResize()
    {
        var parent = new StackPanel { Width = 160, Height = 100, Margin = new Thickness(20, 40, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; right: 10px; bottom: 8px; width: 25%; height: 30px;");
        parent.Children.Add(fixedBox);
        var root = new Grid();
        root.Children.Add(parent);
        Layout(root, 400, 200);
        Assert.Equal(100, fixedBox.ActualWidth, 6);
        Assert.Equal(290, X(fixedBox), 6);
        Assert.Equal(162, Y(fixedBox), 6);
        Layout(root, 600, 300);
        Assert.Equal(150, fixedBox.ActualWidth, 6);
        Assert.Equal(440, X(fixedBox), 6);
        Assert.Equal(262, Y(fixedBox), 6);

        Css.SetStyle(fixedBox, "position: fixed; inset: 10px 20px 30px 40px;");
        Layout(root, 600, 300);
        Assert.Equal(540, fixedBox.ActualWidth, 6);
        Assert.Equal(260, fixedBox.ActualHeight, 6);
        Assert.Equal(40, X(fixedBox), 6);
        Assert.Equal(10, Y(fixedBox), 6);
    }

    [Fact]
    public void TransformedAncestorEstablishesTheFixedContainingBlock()
    {
        var parent = new StackPanel { Width = 200, Height = 100, RenderTransform = new ScaleTransform(2, 2),
            Margin = new Thickness(40, 50, 0, 0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; right: 10px; top: 12px; width: 50px; height: 20px;");
        parent.Children.Add(fixedBox);
        var root = new Grid();
        root.Children.Add(parent);
        Layout(root, 400, 300);
        Assert.Equal(140, fixedBox.VisualBounds.X, 6);
        Assert.Equal(12, fixedBox.VisualBounds.Y, 6);
        Layout(root, 600, 400);
        Assert.Equal(140, fixedBox.VisualBounds.X, 6);
    }

    [Theory]
    [InlineData("sticky")]
    [InlineData("fixed")]
    public void RemovingPositionRestoresTheNormalFlowBox(string position)
    {
        var positioned = new Border { Height = 30 };
        Css.SetStyle(positioned, $"position: {position}; top: 8px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 40 });
        content.Children.Add(positioned);
        content.Children.Add(new Border { Height = 400 });
        var viewer = Viewer(content);
        Layout(viewer, 200, 120);
        viewer.ScrollToVerticalOffset(90);
        Css.SetStyle(positioned, "");
        Layout(viewer, 200, 120);
        Assert.Equal(-50, Y(positioned), 6);
        Assert.Null(positioned.CssLayout);
        viewer.ScrollToVerticalOffset(100);
        Assert.Equal(-60, Y(positioned), 6);
    }

    [Theory]
    [InlineData(-40, 30, 120, 8, 0, false, 48)]
    [InlineData(160, 30, 120, 0, 8, true, -78)]
    [InlineData(-40, 200, 100, 20, 0, false, 60)]
    [InlineData(-40, 30, 120, 0, 0, false, 40)]
    public void StickyAxisHandlesTopBottomAndOversizedBoxes(double position, double extent,
        double viewport, double start, double end, bool bottomOnly, double expected)
    {
        var offset = CssPositionedLayout.StickyAxis(position, extent, 0, viewport, -500, 1000, 0, 0,
            bottomOnly ? CssLayoutLength.Auto : CssLayoutLength.Px(start),
            bottomOnly ? CssLayoutLength.Px(end) : CssLayoutLength.Auto, false);
        Assert.Equal(expected, offset, 6);
    }

    [Theory]
    [InlineData("display: block;")]
    [InlineData("display: flex; flex-direction: column;")]
    [InlineData("display: grid; grid-template-columns: 1fr; align-content: start;")]
    public void FixedChildrenAreExcludedFromEveryCssFormattingMode(string style)
    {
        var parent = new StackPanel();
        Css.SetStyle(parent, style);
        parent.Children.Add(new Border { Height = 40 });
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; top: 7px; left: 9px; width: 80px; height: 200px;");
        parent.Children.Add(fixedBox);
        var following = new Border { Height = 30 };
        parent.Children.Add(following);
        var viewer = Viewer(parent);
        Layout(viewer, 240, 120);
        Assert.Equal(70, viewer.ExtentHeight, 6);
        Assert.Equal(40, Y(following), 6);
        Assert.Equal(7, Y(fixedBox), 6);
    }

    [Fact]
    public void AddingAndRemovingATransformUpdatesTheFixedContainingBlock()
    {
        var parent = new StackPanel { Width = 160, Height = 80, Margin = new Thickness(30, 40, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; top: 10px; right: 10px; width: 50%; height: 20px;");
        parent.Children.Add(fixedBox);
        var root = new Grid(); root.Children.Add(parent);
        Layout(root, 400, 240);
        Assert.Equal(190, X(fixedBox), 6);
        parent.RenderTransform = new ScaleTransform(1, 1);
        Layout(root, 400, 240);
        Assert.Equal(100, X(fixedBox), 6);
        Assert.Equal(80, fixedBox.ActualWidth, 6);
        Assert.Equal(50, Y(fixedBox), 6);
        parent.RenderTransform = null;
        Layout(root, 400, 240);
        Assert.Equal(190, X(fixedBox), 6);
        Assert.Equal(200, fixedBox.ActualWidth, 6);
        Assert.Equal(10, Y(fixedBox), 6);
    }

    [Fact]
    public void ReparentingRecomputesTheViewportWithoutRetainingTheOldRoot()
    {
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; right: 12px; bottom: 8px; width: 50%; height: 20px;");
        var first = new StackPanel(); first.Children.Add(fixedBox);
        Layout(first, 200, 100);
        Assert.Equal(88, X(fixedBox), 6);
        first.Children.Remove(fixedBox);
        var second = new StackPanel(); second.Children.Add(fixedBox);
        Layout(second, 400, 180);
        Assert.Equal(188, X(fixedBox), 6);
        Assert.Equal(152, Y(fixedBox), 6);
        Assert.Equal(200, fixedBox.ActualWidth, 6);
    }

    [Fact]
    public void NativeScrollInfoAlsoUpdatesStickyAndFixedPositions()
    {
        var sticky = new Border { Height = 30 };
        Css.SetStyle(sticky, "position: sticky; top: 8px;");
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; right: 8px; bottom: 8px; width: 40px; height: 20px;");
        var content = new StackPanel();
        content.Children.Add(new Border { Height = 40 }); content.Children.Add(sticky);
        content.Children.Add(new Border { Height = 400 }); content.Children.Add(fixedBox);
        var viewer = Viewer(content); viewer.CanContentScroll = true;
        Layout(viewer, 240, 120);
        viewer.ScrollToVerticalOffset(90);
        Layout(viewer, 240, 120);
        Assert.Equal(8, Y(sticky), 6);
        Assert.Equal(92, Y(fixedBox), 6);
        var before = viewer.VerticalOffset;
        viewer.ScrollToElement(sticky);
        Assert.Equal(before, viewer.VerticalOffset);
        viewer.ScrollToElement(fixedBox);
        Assert.Equal(before, viewer.VerticalOffset);
    }

    [Fact]
    public void FixedRetainsKeyboardTraversalAndFocusAfterScrolling()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var first = new Button { Content = "First", Height = 30 };
        var fixedButton = new Button { Content = "Fixed" };
        Css.SetStyle(fixedButton, "position: fixed; top: 12px; right: 12px; width: 80px; height: 32px;");
        var last = new Button { Content = "Last", Height = 30 };
        var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(fixedButton); panel.Children.Add(last);
        panel.Children.Add(new Border { Height = 500 });
        var viewer = Viewer(panel);
        var window = new DisplayedTestWindow { Content = viewer, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window, 320, 180);
            Assert.True(first.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(first));
            Assert.Same(fixedButton, Keyboard.FocusedElement);
            viewer.ScrollToVerticalOffset(80);
            Assert.Same(fixedButton, Keyboard.FocusedElement);
            Assert.Equal(12, Y(fixedButton), 6);
            Assert.True(KeyboardNavigation.MoveFocus(fixedButton));
            Assert.Same(last, Keyboard.FocusedElement);
            Assert.True(KeyboardNavigation.MoveFocus(last, reverse: true));
            Assert.Same(fixedButton, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void ExplicitHostViewportMetricsInvalidateFixedPercentageSizes()
    {
        var root = new StackPanel(); var fixedBox = new Border(); root.Children.Add(fixedBox);
        Css.SetStyle(fixedBox, "position: fixed; left: 0; top: 0; width: 50%; height: 20px;");
        Css.SetViewportMetrics(root, new CssViewportMetrics(new Size(300, 200), new Size(300, 200), new Size(300, 200)));
        Layout(root, 300, 200);
        Assert.Equal(150, fixedBox.ActualWidth, 6);
        Css.SetViewportMetrics(root, new CssViewportMetrics(new Size(500, 300), new Size(500, 300), new Size(500, 300)));
        Layout(root, 300, 200);
        Assert.Equal(250, fixedBox.ActualWidth, 6);
    }

    [Fact]
    public void StickyPositionBoxReducesMarginsThatReachTheContainingEdge()
    {
        Assert.Equal(10, CssPositionedLayout.StickyAxis(60, 30, 0, 200, 0, 100, 0, 10,
            CssLayoutLength.Px(100), CssLayoutLength.Auto, false), 6);
        Assert.Equal(-10, CssPositionedLayout.StickyAxis(10, 30, 0, 200, 0, 100, 10, 0,
            CssLayoutLength.Auto, CssLayoutLength.Px(180), false), 6);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(true, 300)]
    public void FixedOverconstrainedInsetsUseTheContainingBlocksDirection(bool rtl, double expected)
    {
        var parent = new StackPanel { FlowDirection = rtl ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        var fixedBox = new Border();
        Css.SetStyle(fixedBox, "position: fixed; left: 10px; right: 20px; top: 0; width: 80px; height: 20px;");
        parent.Children.Add(fixedBox);
        var root = new Grid { FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        root.Children.Add(parent);
        Layout(root, 400, 200);
        Assert.Equal(expected, X(fixedBox), 6);
    }

    private static ScrollViewer Viewer(UIElement content) => new()
    {
        Content = content, CanContentScroll = false,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
    };

    private static void Layout(UIElement root, double width, double height)
    {
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }
    private static double X(FrameworkElement element) => element.GetAbsolutePosition().X;
    private static double Y(FrameworkElement element) => element.GetAbsolutePosition().Y;
}
