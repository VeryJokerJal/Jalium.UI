using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssVisibilityTests
{
    [Fact]
    public void HiddenBoxKeepsLayoutAndVisibleDescendantPaintsAndReceivesInput()
    {
        var (root, back, front, child) = CreateTree();
        Css.SetStyle(front, "visibility:hidden");
        Layout(root);

        Assert.Equal(Visibility.Visible, front.Visibility);
        Assert.False(front.IsVisible);
        Assert.False(child.IsVisible);
        front.Focusable = true;
        Assert.False(front.Focus());
        Assert.False(front.CaptureMouse());
        Assert.Same(back, Hit(root, 10, 10));
        Assert.Same(back, root.InputHitTest(new Point(10, 10)));
        Assert.Equal((byte)255, Blue(Pixels(root), 40, 10, 10));

        Css.SetStyle(child, "visibility:visible");
        Assert.False(front.IsVisible);
        Assert.True(child.IsVisible);
        Assert.Same(child, Hit(root, 10, 10));
        Assert.Same(child, root.InputHitTest(new Point(10, 10)));
        Assert.Same(back, Hit(root, 30, 10));
        var callbackHits = new List<DependencyObject>();
        VisualTreeHelper.HitTest(root, null, result =>
        {
            callbackHits.Add(result.VisualHit);
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(new Point(10, 10)));
        Assert.Contains(child, callbackHits);
        Assert.DoesNotContain(front, callbackHits);
        var pixels = Pixels(root);
        Assert.Equal((byte)128, Green(pixels, 40, 10, 10));
        Assert.Equal((byte)255, Blue(pixels, 40, 30, 10));

        Css.SetStyle(front, "visibility:visible");
        Assert.True(front.IsVisible);
        Assert.Same(front, Hit(root, 30, 10));
        Assert.Equal((byte)255, Red(Pixels(root), 40, 30, 10));
    }

    [Fact]
    public void InheritUnsetAndNativeVisibilityPrecedenceUpdateWithoutRelayout()
    {
        var (root, back, front, child) = CreateTree();
        Css.SetStyle(front, "visibility:hidden");
        Css.SetStyle(child, "visibility:inherit");
        Layout(root);
        Assert.Same(back, Hit(root, 10, 10));

        Css.SetStyle(child, "visibility:initial");
        Assert.Same(child, Hit(root, 10, 10));
        Css.SetStyle(child, "visibility:unset");
        Assert.Same(back, Hit(root, 10, 10));

        front.Visibility = Visibility.Visible;
        Assert.True(front.IsVisible);
        Assert.Same(front, Hit(root, 30, 10));
        front.ClearValue(UIElement.VisibilityProperty);
        Assert.False(front.IsVisible);
        Assert.Same(back, Hit(root, 30, 10));

        Css.SetStyle(child, "visibility:visible");
        front.Visibility = Visibility.Hidden;
        Assert.False(child.IsVisible);
        Assert.Same(back, Hit(root, 10, 10));
        front.ClearValue(UIElement.VisibilityProperty);
        Assert.True(child.IsVisible);
        Assert.Same(child, Hit(root, 10, 10));
    }

    [Fact]
    public void CollapseOutsideSpecialLayoutKeepsSpaceWhileDisplayNoneRemovesIt()
    {
        var root = new StackPanel { Width = 40 };
        var first = new Border { Width = 40, Height = 20, Background = Brushes.Red };
        var second = new Border { Width = 40, Height = 20, Background = Brushes.Blue };
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyle(first, "visibility:collapse");
        root.Measure(new Size(40, 100));
        root.Arrange(new Rect(0, 0, 40, 100));
        Assert.Equal(20, second.VisualBounds.Y);
        Assert.False(first.IsVisible);

        Css.SetStyle(first, "display:none");
        root.Measure(new Size(40, 100));
        root.Arrange(new Rect(0, 0, 40, 100));
        Assert.Equal(0, second.VisualBounds.Y);
    }

    [Fact]
    public void CollapsedFlexItemRemovesMainSpaceAndGapButKeepsItsOriginalLineHeight()
    {
        var first = new Border { Width = 40, Height = 20, Background = Brushes.Blue };
        var descendant = new Border { Width = 15, Height = 15, Background = Brushes.Green };
        var collapsed = new Border { Width = 50, Height = 70, Background = Brushes.Red,
            Child = descendant };
        var last = new Border { Width = 40, Height = 25, Background = Brushes.Blue };
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexStart, ColumnSpacing = 10 };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");
        Css.SetStyle(descendant, "visibility:visible");

        panel.Measure(new Size(200, double.PositiveInfinity));
        Assert.Equal(90, panel.DesiredSize.Width, 6);
        Assert.Equal(70, panel.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 200, 70));
        Assert.Equal(50, last.VisualBounds.X, 6);
        Assert.False(collapsed.IsVisible);
        Assert.False(descendant.IsVisible);
        Assert.NotSame(descendant, panel.HitTest(new Point(5, 5))?.VisualHit);
        var callbackHits = new List<DependencyObject>();
        VisualTreeHelper.HitTest(panel, null, result =>
        {
            callbackHits.Add(result.VisualHit);
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(new Point(5, 5)));
        Assert.DoesNotContain(collapsed, callbackHits);
        Assert.DoesNotContain(descendant, callbackHits);
        callbackHits.Clear();
        VisualTreeHelper.HitTest(panel, null, result =>
        {
            callbackHits.Add(result.VisualHit);
            return HitTestResultBehavior.Continue;
        }, new GeometryHitTestParameters(new RectangleGeometry(new Rect(0, 0, 20, 20))));
        Assert.DoesNotContain(collapsed, callbackHits);
        Assert.DoesNotContain(descendant, callbackHits);
        var pixels = CssBorderRadiusTests.Pixels(panel, 200, 70);
        Assert.Equal((byte)255, pixels[(5 * 200 + 5) * 4]);
        Assert.Equal((byte)0, pixels[(5 * 200 + 5) * 4 + 1]);
        Assert.Equal((byte)0, pixels[(10 * 200 + 100) * 4 + 3]);

        Css.SetStyle(collapsed, "visibility:hidden");
        panel.Measure(new Size(200, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 200, 70));
        Assert.Equal(110, last.VisualBounds.X, 6);
        Assert.Equal(150, panel.DesiredSize.Width, 6);

        Css.SetStyle(collapsed, "display:none");
        panel.Measure(new Size(200, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 200, 70));
        Assert.Equal(50, last.VisualBounds.X, 6);
        Assert.Equal(25, panel.DesiredSize.Height, 6);
    }

    [Fact]
    public void CollapsedFlexStrutFollowsOriginalLineWhenItemsRewrap()
    {
        var first = new Border { Width = 120, Height = 20 };
        var collapsed = new Border { Width = 60, Height = 10 };
        var last = new Border { Width = 60, Height = 80 };
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap, AlignContent = FlexContentAlign.FlexStart };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(120, double.PositiveInfinity));
        Assert.Equal(160, panel.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 120, 160));
        Assert.Equal(80, last.VisualBounds.Y, 6);
        Assert.Equal(0, last.VisualBounds.X, 6);
    }

    [Fact]
    public void CollapsedFlexStrutRetainsOriginalAlignContentStretch()
    {
        var first = new Border { Width = 120, Height = 20 };
        var collapsed = new Border { Width = 60, Height = 10 };
        var last = new Border { Width = 60, Height = 20 };
        FlexPanel.SetAlignSelf(last, FlexAlign.FlexStart);
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap,
            AlignContent = FlexContentAlign.Stretch };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(120, 100));
        panel.Arrange(new Rect(0, 0, 120, 100));
        // The original two 20px lines stretch to 50px each. The collapsed
        // item moves to line one with a 50px strut; the second round then
        // stretches its 50px and 20px lines by another 15px each.
        Assert.Equal(65, last.VisualBounds.Y, 6);
    }

    [Fact]
    public void CollapsedBaselineItemKeepsLineStrutButLeavesAlignmentGroup()
    {
        var first = new Border { Width = 40, Height = 20 };
        var collapsed = new Border { Width = 40, Height = 40 };
        var last = new Border { Width = 40, Height = 20 };
        TextBlock.SetBaselineOffset(first, 10);
        TextBlock.SetBaselineOffset(collapsed, 30);
        TextBlock.SetBaselineOffset(last, 5);
        var panel = new FlexPanel { AlignItems = FlexAlign.Baseline };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(120, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 120, 45));

        Assert.Equal(45, panel.DesiredSize.Height, 6);
        Assert.Equal(0, first.VisualBounds.Y, 6);
        Assert.Equal(5, last.VisualBounds.Y, 6);
        Assert.False(collapsed.IsVisible);
    }

    [Fact]
    public void CollapsedFlexItemDoesNotAbsorbAutoMarginSpace()
    {
        var first = new Border { Width = 40, Height = 20 };
        var collapsed = new Border { Width = 40, Height = 20 };
        var last = new Border { Width = 40, Height = 20 };
        var panel = new FlexPanel { JustifyContent = FlexJustify.Center };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse; margin-left:auto");

        panel.Measure(new Size(200, 40));
        panel.Arrange(new Rect(0, 0, 200, 40));

        Assert.Equal(60, first.VisualBounds.X, 6);
        Assert.Equal(100, last.VisualBounds.X, 6);
    }

    [Fact]
    public void ArrangeWidthChangeRemeasuresFlexItemCrossSize()
    {
        var reflowing = new WidthSensitiveBox();
        FlexPanel.SetBasis(reflowing, 100);
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexStart };
        panel.Children.Add(reflowing);

        panel.Measure(new Size(120, 100));
        Assert.Equal(20, reflowing.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 60, 100));
        Assert.Equal(40, reflowing.DesiredSize.Height, 6);
        Assert.Equal(40, reflowing.VisualBounds.Height, 6);

        panel.Arrange(new Rect(0, 0, 120, 100));
        Assert.Equal(20, reflowing.DesiredSize.Height, 6);
        Assert.Equal(20, reflowing.VisualBounds.Height, 6);
    }

    [Fact]
    public void DisplayFlexAdapterRemeasuresCrossSizeAtArrangeWidth()
    {
        var reflowing = new WidthSensitiveBox();
        FlexPanel.SetBasis(reflowing, 100);
        var host = new StackPanel();
        host.Children.Add(reflowing);
        Css.SetStyle(host, "display:flex; align-items:flex-start");
        CssEvaluationScheduler.FlushIfPending(host.Dispatcher);

        host.Measure(new Size(120, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, 60, 100));
        Assert.Equal(40, reflowing.DesiredSize.Height, 6);
        Assert.Equal(40, reflowing.VisualBounds.Height, 6);
        Assert.Same(host, reflowing.VisualParent);
    }

    [Fact]
    public void ArrangeCrossConstraintChangeRemeasuresFlexItems()
    {
        var crossSensitive = new CrossSensitiveBox();
        FlexPanel.SetBasis(crossSensitive, 40);
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlign.FlexStart, AlignContent = FlexContentAlign.FlexStart };
        panel.Children.Add(crossSensitive);

        panel.Measure(new Size(120, 100));
        Assert.Equal(25, crossSensitive.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 120, 200));
        Assert.Equal(50, crossSensitive.DesiredSize.Height, 6);
        Assert.Equal(50, crossSensitive.VisualBounds.Height, 6);

        panel.Arrange(new Rect(0, 0, 120, 100));
        Assert.Equal(25, crossSensitive.DesiredSize.Height, 6);
        Assert.Equal(25, crossSensitive.VisualBounds.Height, 6);
    }

    [Fact]
    public void ArrangeCrossConstraintChangeUpdatesOriginalCollapsedLine()
    {
        var first = new Border { Width = 120, Height = 20 };
        var collapsed = new CrossSensitiveBox();
        var last = new Border { Width = 60, Height = 20 };
        FlexPanel.SetBasis(collapsed, 60);
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlign.FlexStart, AlignContent = FlexContentAlign.FlexStart };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(120, 100));
        Assert.Equal(25, collapsed.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 120, 200));
        Assert.Equal(50, collapsed.DesiredSize.Height, 6);
        Assert.Equal(50, last.VisualBounds.Y, 6);

        panel.Arrange(new Rect(0, 0, 120, 100));
        Assert.Equal(25, collapsed.DesiredSize.Height, 6);
        Assert.Equal(25, last.VisualBounds.Y, 6);
    }

    [Fact]
    public void ArrangeWidthChangeRemeasuresOriginalLineBeforeCollapse()
    {
        var first = new Border { Width = 60, Height = 20 };
        var collapsed = new WidthSensitiveBox();
        var last = new Border { Width = 60, Height = 20 };
        FlexPanel.SetBasis(collapsed, 100);
        var panel = new FlexPanel { Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlign.FlexStart, AlignContent = FlexContentAlign.FlexStart };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(120, double.PositiveInfinity));
        Assert.Equal(20, collapsed.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 60, 100));
        Assert.Equal(40, collapsed.DesiredSize.Height, 6);
        Assert.Equal(40, last.VisualBounds.Y, 6);

        panel.Arrange(new Rect(0, 0, 120, 100));
        Assert.Equal(20, collapsed.DesiredSize.Height, 6);
        Assert.Equal(0, last.VisualBounds.Y, 6);
    }

    [Fact]
    public void CollapsedFlexItemDoesNotConsumeJustificationOrGrowth()
    {
        var first = new Border { Height = 20 };
        var collapsed = new Border { Height = 20 };
        var last = new Border { Height = 20 };
        FlexPanel.SetBasis(first, 40);
        FlexPanel.SetBasis(collapsed, 50);
        FlexPanel.SetBasis(last, 40);
        FlexPanel.SetGrow(collapsed, 100);
        var panel = new FlexPanel { JustifyContent = FlexJustify.SpaceBetween,
            ColumnSpacing = 10 };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(200, 40));
        panel.Arrange(new Rect(0, 0, 200, 40));
        Assert.Equal(160, last.VisualBounds.X, 6);

        FlexPanel.SetGrow(first, 1);
        FlexPanel.SetGrow(last, 1);
        panel.Measure(new Size(200, 40));
        panel.Arrange(new Rect(0, 0, 200, 40));
        Assert.Equal(95, first.VisualBounds.Width, 6);
        Assert.Equal(105, last.VisualBounds.X, 6);
        Assert.Equal(95, last.VisualBounds.Width, 6);
    }

    [Fact]
    public void CollapsedColumnFlexItemKeepsOriginalLineWidth()
    {
        var first = new Border { Width = 20, Height = 40 };
        var collapsed = new Border { Width = 70, Height = 60 };
        var last = new Border { Width = 25, Height = 40 };
        var panel = new FlexPanel { Direction = FlexDirection.Column, RowSpacing = 10 };
        panel.Children.Add(first);
        panel.Children.Add(collapsed);
        panel.Children.Add(last);
        Css.SetStyle(collapsed, "visibility:collapse");

        panel.Measure(new Size(double.PositiveInfinity, 200));
        Assert.Equal(70, panel.DesiredSize.Width, 6);
        Assert.Equal(90, panel.DesiredSize.Height, 6);
        panel.Arrange(new Rect(0, 0, 70, 200));
        Assert.Equal(50, last.VisualBounds.Y, 6);
    }

    [Fact]
    public void DisplayFlexAdapterUpdatesCollapsedItemsWithoutReparentingAndHonorsNativeVisibility()
    {
        var host = new StackPanel();
        var first = new Border { Width = 40, Height = 20 };
        var visibleChild = new Border { Width = 10, Height = 10 };
        var collapsed = new Border { Width = 50, Height = 60, Child = visibleChild };
        var last = new Border { Width = 40, Height = 20 };
        host.Children.Add(first);
        host.Children.Add(collapsed);
        host.Children.Add(last);
        Css.SetStyle(host, "display:flex; column-gap:10px");
        Css.SetStyle(visibleChild, "visibility:visible");
        Layout(host, 200, 120);
        Assert.Equal(110, last.VisualBounds.X, 6);

        Css.SetStyle(collapsed, "visibility:collapse");
        Layout(host, 200, 120);
        Assert.Equal(50, last.VisualBounds.X, 6);
        Assert.Same(host, collapsed.VisualParent);
        Assert.False(visibleChild.IsVisible);

        collapsed.Visibility = Visibility.Visible;
        Layout(host, 200, 120);
        Assert.Equal(110, last.VisualBounds.X, 6);
        collapsed.ClearValue(UIElement.VisibilityProperty);
        Layout(host, 200, 120);
        Assert.Equal(50, last.VisualBounds.X, 6);

        collapsed.Visibility = Visibility.Collapsed;
        Layout(host, 200, 120);
        Assert.Equal(20, host.DesiredSize.Height, 6);
        collapsed.ClearValue(UIElement.VisibilityProperty);
        Layout(host, 200, 120);

        Css.SetStyle(host, "");
        Layout(host, 200, 120);
        Assert.Equal(80, last.VisualBounds.Y, 6);
        Assert.True(visibleChild.IsVisible);
    }

    [Fact]
    public void AncestorCollapseInvalidatesNestedFlexItemLayout()
    {
        var root = new StackPanel();
        var panel = new FlexPanel();
        panel.Children.Add(new Border { Width = 40, Height = 20 });
        panel.Children.Add(new Border { Width = 40, Height = 20 });
        root.Children.Add(panel);

        Css.SetStyle(root, "visibility:collapse");
        Layout(root, 200, 100);
        Assert.Equal(0, panel.DesiredSize.Width, 6);
        Assert.Equal(20, panel.DesiredSize.Height, 6);

        Css.SetStyle(root, "visibility:visible");
        Layout(root, 200, 100);
        Assert.Equal(80, panel.DesiredSize.Width, 6);
    }

    [Fact]
    public void AbsolutelyPositionedFlexChildUsesOrdinaryCollapseVisibility()
    {
        var descendant = new Border { Width = 15, Height = 15, Background = Brushes.Green };
        var absolute = new Border { Width = 30, Height = 30, Child = descendant };
        var panel = new FlexPanel();
        panel.Children.Add(absolute);
        Css.SetStyle(absolute, "position:absolute; left:0; top:0; visibility:collapse");
        Css.SetStyle(descendant, "visibility:visible");

        Layout(panel, 40, 30);
        Assert.True(descendant.IsVisible);
        Assert.Same(descendant, panel.HitTest(new Point(10, 10))?.VisualHit);
        Assert.Equal(0, panel.DesiredSize.Width, 6);
        Assert.Equal((byte)128, Green(CssBorderRadiusTests.Pixels(panel, 40, 30), 40, 10, 10));

        Css.SetStyle(absolute, "visibility:collapse");
        Layout(panel, 40, 30);
        Assert.NotEqual(CssPositionMode.Absolute, absolute.CssLayout?.Position);
        Assert.True(CssDisplayProperties.IsCollapsedFlexItem(absolute));
        Assert.False(descendant.IsVisible);
        Assert.NotSame(descendant, panel.HitTest(new Point(10, 10))?.VisualHit);
        Assert.Equal((byte)0, CssBorderRadiusTests.Pixels(panel, 40, 30)[(10 * 40 + 10) * 4 + 3]);

        Css.SetStyle(absolute, "position:absolute; left:0; top:0; visibility:collapse");
        Layout(panel, 40, 30);
        Assert.True(descendant.IsVisible);
    }

    [Fact]
    public void WindowRechecksTheSamePointAfterVisibilityChanges()
    {
        var (root, back, front, child) = CreateTree();
        var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 40, Height = 30, Content = root };
        Layout(window);
        Css.SetStyle(front, "visibility:hidden");
        Assert.Same(back, WindowHit(window, 10, 10));
        Css.SetStyle(child, "visibility:visible");
        Assert.Same(child, WindowHit(window, 10, 10));
        Css.SetStyle(child, "visibility:inherit");
        Assert.Same(back, WindowHit(window, 10, 10));
        Css.SetStyle(front, "visibility:visible");
        Assert.Same(child, WindowHit(window, 10, 10));
        Css.SetStyle(front, "visibility:hidden");
        Assert.Same(back, WindowHit(window, 10, 10));
        front.Visibility = Visibility.Visible;
        Assert.Same(child, WindowHit(window, 10, 10));
        front.ClearValue(UIElement.VisibilityProperty);
        Assert.Same(back, WindowHit(window, 10, 10));
    }

    [Fact]
    public void HidingACapturedBoxReleasesItsMouseCapture()
    {
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        try
        {
            Assert.True(front.CaptureMouse());
            Assert.Same(front, UIElement.MouseCapturedElement);
            Css.SetStyle(front, "visibility:hidden");
            Assert.Null(UIElement.MouseCapturedElement);
        }
        finally
        {
            front.ReleaseMouseCapture();
        }
    }

    private static (Grid Root, Border Back, Border Front, Border Child) CreateTree()
    {
        var back = new Border { Width = 40, Height = 30, Background = Brushes.Blue };
        var child = new Border { Width = 20, Height = 20, Background = Brushes.Green,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Red, Child = child };
        var root = new Grid { Width = 40, Height = 30 };
        root.Children.Add(back);
        root.Children.Add(front);
        return (root, back, front, child);
    }

    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(40, 30));
        root.Arrange(new Rect(0, 0, 40, 30));
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
    }

    private static UIElement? Hit(FrameworkElement root, double x, double y)
        => root.HitTest(new Point(x, y))?.VisualHit as UIElement;

    private static UIElement? WindowHit(Window window, double x, double y)
    {
        var method = typeof(Window).GetMethod("HitTestElement", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (UIElement?)method.Invoke(window, [new Point(x, y), "css-visibility-test"]);
    }

    private static byte[] Pixels(FrameworkElement root) => CssBorderRadiusTests.Pixels(root, 40, 30);
    private static byte Red(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 2];
    private static byte Green(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4 + 1];
    private static byte Blue(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4];

    private sealed class WidthSensitiveBox : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) =>
            new(Math.Min(100, availableSize.Width), availableSize.Width < 80 ? 40 : 20);
    }

    private sealed class CrossSensitiveBox : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize) =>
            new(40, availableSize.Height / 4);
    }
}
