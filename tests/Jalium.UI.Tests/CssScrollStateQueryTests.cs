using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class CssScrollStateQueryTests
{
    private static (Border Root, ScrollViewer Viewer, Grid Content, Border Target) Tree(string rules,
        string containerType = "scroll-state")
    {
        var target = new Border();
        var content = new Grid { Width = 240, Height = 200 };
        content.Children.Add(target);
        var viewer = new ScrollViewer
        {
            Width = 120,
            Height = 100,
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var root = new Border { Child = viewer };
        Css.SetClass(target, "target");
        Css.SetStyle(viewer, "container: scroller / " + containerType);
        Css.SetStyleSheet(root, ".target { opacity: .9; width: 10px; height: 10px } " + rules);
        Layout(root);
        Assert.True(viewer.ScrollableWidth > 0);
        Assert.True(viewer.ScrollableHeight > 0);
        return (root, viewer, content, target);
    }

    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(400, 300));
        root.Arrange(new Rect(0, 0, 400, 300));
        root.UpdateLayout();
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    [Theory]
    [InlineData("scroll-state(scrollable: top)", false)]
    [InlineData("scroll-state(scrollable: right)", true)]
    [InlineData("scroll-state(scrollable: bottom)", true)]
    [InlineData("scroll-state(scrollable: left)", false)]
    [InlineData("scroll-state(scrollable: x)", true)]
    [InlineData("scroll-state(scrollable: y)", true)]
    [InlineData("scroll-state(scrollable: block)", true)]
    [InlineData("scroll-state(scrollable: inline)", true)]
    [InlineData("scroll-state(scrollable: block-start)", false)]
    [InlineData("scroll-state(scrollable: block-end)", true)]
    [InlineData("scroll-state(scrollable: inline-start)", false)]
    [InlineData("scroll-state(scrollable: inline-end)", true)]
    [InlineData("scroll-state(scrollable: none)", false)]
    [InlineData("scroll-state(scrollable)", true)]
    [InlineData("scroll-state((scrollable: right) and (scrollable: bottom))", true)]
    [InlineData("scroll-state((scrollable: left) or (scrollable: top))", false)]
    [InlineData("not scroll-state(scrollable: top)", true)]
    [InlineData("not scroll-state(stuck: top)", false)]
    [InlineData("not scroll-state(scrollable: diagonal)", false)]
    public void ScrollableDirectionsUseTheSelectedNativeViewport(string condition, bool matches)
    {
        var (_, _, _, target) = Tree("@container scroller " + condition +
            " { .target { opacity: .2 } }");
        Assert.Equal(matches ? .2 : .9, target.Opacity);
    }

    [Theory]
    [InlineData("scroll-state(scrolled: none)", true)]
    [InlineData("scroll-state(scrolled)", false)]
    [InlineData("scroll-state(scrolled: top)", false)]
    [InlineData("scroll-state(scrolled: right)", false)]
    [InlineData("scroll-state(scrolled: x)", false)]
    [InlineData("scroll-state(scrolled: block)", false)]
    [InlineData("scroll-state((scrollable: bottom) and (scrolled: none))", true)]
    [InlineData("scroll-state((scrollable: bottom) and (scrolled: top))", false)]
    [InlineData("scroll-state(scrolled: diagonal)", false)]
    [InlineData("not scroll-state(scrolled: diagonal)", false)]
    public void ScrolledStartsWithNoRelativeDirection(string condition, bool matches)
    {
        var (_, _, _, target) = Tree("@container scroller " + condition +
            " { .target { opacity: .2 } }");
        Assert.Equal(matches ? .2 : .9, target.Opacity);
    }

    [Fact]
    public void AbsoluteOffsetsDoNotReplaceTheLastRelativeDirection()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: none) { .target { opacity: .2 } }
            @container scroller scroll-state(scrolled: bottom) { .target { width: 30px } }
            @container scroller scroll-state(scrolled: top) { .target { height: 30px } }
            """);
        Assert.Equal(.2, target.Opacity);

        viewer.ScrollToVerticalOffset(40);
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);
        Assert.Equal(10, target.Width);

        viewer.LineDown();
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
        viewer.LineDown(); // A blocked relative operation did not move the viewport.
        root.UpdateLayout();
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        viewer.PageUp();
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(30, target.Height);

        viewer.ScrollToTop();
        root.UpdateLayout();
        Assert.Equal(30, target.Height);

        viewer.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.End,
            ModifierKeys.Control, isDown: true, isRepeat: false, timestamp: 2));
        root.UpdateLayout();
        Assert.Equal(30, target.Height);
    }

    [Fact]
    public void HorizontalRelativeScrollUsesTheContainersLogicalDirection()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: inline-start) { .target { width: 30px } }
            @container scroller scroll-state(scrolled: inline-end) { .target { height: 30px } }
            @container scroller scroll-state(scrolled: x) { .target { opacity: .2 } }
            """);
        Css.SetStyle(viewer, "container: scroller / scroll-state; direction: rtl");
        Layout(root);

        viewer.LineRight();
        root.UpdateLayout();
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);
        Assert.Equal(.2, target.Opacity);

        viewer.PageLeft();
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(30, target.Height);
        Assert.Equal(.2, target.Opacity);

        Css.SetStyle(viewer, "container: scroller / scroll-state; direction: ltr");
        Layout(root);
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);
    }

    [Fact]
    public void HorizontalAndVerticalDirectionsPersistIndependently()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: right) { .target { width: 30px } }
            @container scroller scroll-state(scrolled: left) { .target { width: 50px } }
            @container scroller scroll-state(scrolled: bottom) { .target { height: 30px } }
            @container scroller scroll-state(scrolled: top) { .target { height: 50px } }
            """);
        Assert.Equal(10, target.Width);
        Assert.Equal(10, target.Height);

        viewer.LineRight();
        root.UpdateLayout();
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        viewer.LineDown();
        root.UpdateLayout();
        Assert.Equal(30, target.Width);
        Assert.Equal(30, target.Height);

        viewer.PageLeft();
        root.UpdateLayout();
        Assert.Equal(50, target.Width);
        Assert.Equal(30, target.Height);

        viewer.PageUp();
        root.UpdateLayout();
        Assert.Equal(50, target.Width);
        Assert.Equal(50, target.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WheelUpdatesScrolledWhenItsMovementCommits(bool smooth)
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: none) { .target { opacity: .2 } }
            @container scroller scroll-state(scrolled: bottom) { .target { width: 30px } }
            """);
        viewer.IsScrollInertiaEnabled = smooth;
        viewer.RaiseEvent(new MouseWheelEventArgs(
            UIElement.MouseWheelEvent, new Point(8, 8), -120,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 1));

        if (smooth)
        {
            root.UpdateLayout();
            Assert.Equal(.2, target.Opacity);
            typeof(ScrollViewer).GetMethod("AdvanceSmoothScrollByMilliseconds",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(viewer, [32L]);
        }
        root.UpdateLayout();
        Assert.True(viewer.VerticalOffset > 0);
        Assert.Equal(.9, target.Opacity);
        Assert.Equal(30, target.Width);
    }

    [Fact]
    public void QueryCreatedAfterScrollingReadsTheStoredDirection()
    {
        var (root, viewer, _, target) = Tree(string.Empty);
        viewer.LineDown();
        Css.SetStyleSheet(root, """
            .target { opacity: .9; width: 10px; height: 10px }
            @container scroller scroll-state(scrolled: bottom) { .target { opacity: .2 } }
            """);
        Layout(root);
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void ScrollBarLineAndPageClicksAreRelativeButThumbMovesAreAbsolute()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: bottom) { .target { width: 30px } }
            @container scroller scroll-state(scrolled: top) { .target { height: 30px } }
            @container scroller scroll-state(scrolled: none) { .target { opacity: .2 } }
            """);
        var bar = (ScrollBar)typeof(ScrollViewer).GetField("_verticalScrollBar",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewer)!;

        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent,
            ScrollEventType.ThumbPosition, 40) { Source = bar });
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);

        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent,
            ScrollEventType.SmallIncrement, 60) { Source = bar });
        root.UpdateLayout();
        Assert.Equal(30, target.Width);

        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent,
            ScrollEventType.ThumbPosition, 90) { Source = bar });
        root.UpdateLayout();
        Assert.Equal(30, target.Width);

        bar.RaiseEvent(new ScrollEventArgs(ScrollBar.ScrollEvent,
            ScrollEventType.LargeDecrement, 20) { Source = bar });
        root.UpdateLayout();
        Assert.Equal(30, target.Height);
    }

    [Theory]
    [InlineData("scroll-state(scrollable: var(--edge))", "--edge: right", true)]
    [InlineData("scroll-state(scrollable: var(--edge))", "--edge: 13", false)]
    [InlineData("scroll-state(scrollable: var(--missing, right))", "", true)]
    [InlineData("scroll-state(scrollable: var(--missing))", "", false)]
    [InlineData("not scroll-state(scrollable: var(--missing))", "", false)]
    [InlineData("scroll-state(scrolled: var(--edge))", "--edge: none", true)]
    [InlineData("scroll-state(scrolled: var(--edge))", "--edge: bottom", false)]
    public void ScrollStateValuesSubstituteContainerVariables(string condition, string declaration, bool matches)
    {
        var (root, viewer, _, target) = Tree("@container scroller " + condition +
            " { .target { opacity: .2 } }");
        Css.SetStyle(viewer, "container: scroller / scroll-state; " + declaration);
        Layout(root);
        Assert.Equal(matches ? .2 : .9, target.Opacity);
    }

    [Fact]
    public void VariableDirectionReSelectsAnEligibleAncestor()
    {
        var target = new Border();
        var innerContent = new Grid { Width = 160, Height = 200 };
        innerContent.Children.Add(target);
        var inner = new ScrollViewer
        {
            Width = 100, Height = 80, Content = innerContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var outerContent = new Grid { Width = 240, Height = 90 };
        outerContent.Children.Add(inner);
        var outer = new ScrollViewer
        {
            Width = 120, Height = 100, Content = outerContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var root = new Border { Child = outer };
        Css.SetClass(target, "target");
        Css.SetStyle(outer, "container-type: scroll-state; --axis: right");
        Css.SetStyle(inner, "container-type: scroll-state; --axis: right");
        Css.SetStyleSheet(root, """
            .target { opacity: .9 }
            @container scroll-state(scrollable: var(--axis)) { .target { opacity: .2 } }
            """);
        Layout(root);
        Assert.True(outer.ScrollableWidth > 0);
        Assert.True(inner.ScrollableHeight > 0);

        outer.ScrollToHorizontalOffset(outer.ScrollableWidth);
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);

        Css.SetStyle(inner, "container-type: scroll-state; --axis: bottom");
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);

        Css.SetStyle(inner, "container-type: scroll-state; --axis: right");
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);

        Css.SetStyle(outer, "container-type: scroll-state; --axis: left");
        root.UpdateLayout();
        Assert.Equal("left", CssNode.Get(outer).CssRuntimeState!.CustomProperties["--axis"]);
        Assert.True((CssContainerQueries.Container(CssNode.Get(outer)).ScrollableDirections &
            CssScrollableDirection.Left) != 0);
        Assert.True(CssContainerQuery.Parse("scroll-state(scrollable: var(--axis))")!
            .Evaluate(CssNode.Get(target)));
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void RegisteredDirectionUsesItsComputedValueAndInvalidValueFallback()
    {
        var (root, viewer, _, target) = Tree(string.Empty);
        Css.SetStyleSheet(root, """
            @property --edge { syntax: 'none | right'; inherits: false; initial-value: none }
            .target { opacity: .9; width: 10px; height: 10px }
            @container scroller scroll-state(scrollable: var(--edge)) { .target { opacity: .2 } }
            """);
        Css.SetStyle(viewer, "container: scroller / scroll-state; --edge: right");
        Layout(root);
        Assert.Equal(.2, target.Opacity);

        Css.SetStyle(viewer, "container: scroller / scroll-state; --edge: 13");
        root.UpdateLayout();
        Assert.Equal("none", CssNode.Get(viewer).CssRuntimeState!.CustomProperties["--edge"]);
        Assert.Equal(.9, target.Opacity);
    }

    [Fact]
    public void ScrolledVariableChangesWithoutAnotherScroll()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroller scroll-state(scrolled: var(--edge)) { .target { opacity: .2 } }
            """);
        Css.SetStyle(viewer, "container: scroller / scroll-state; --edge: bottom");
        Layout(root);
        Assert.Equal(.9, target.Opacity);

        viewer.LineDown();
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);

        Css.SetStyle(viewer, "container: scroller / scroll-state; --edge: top");
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);

        Css.SetStyle(viewer, "container: scroller / scroll-state; --edge: bottom");
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void ScrollOffsetsAndExtentChangesRefreshDescendantQueries()
    {
        var (root, viewer, content, target) = Tree("""
            @container scroll-state(scrollable: bottom) { .target { width: 30px } }
            @container scroll-state(scrollable: top) { .target { height: 30px } }
            @container scroll-state(scrollable: none) { .target { opacity: .2 } }
            """);
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(30, target.Height);

        content.Width = 60;
        content.Height = 50;
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(10, target.Height);
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void AxisQueriesTrackIndependentHorizontalAndVerticalOverflow()
    {
        var (root, _, content, target) = Tree("""
            @container scroll-state(scrollable: x) { .target { width: 30px } }
            @container scroll-state(scrollable: y) { .target { height: 30px } }
            """);
        content.Height = 20;
        root.UpdateLayout();
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        content.Width = 20;
        content.Height = 200;
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(30, target.Height);
    }

    [Fact]
    public void LogicalInlineEdgesFollowTheContainersDirection()
    {
        var (root, viewer, _, target) = Tree("""
            @container scroll-state(scrollable: inline-start) { .target { width: 30px } }
            @container scroll-state(scrollable: inline-end) { .target { height: 30px } }
            """);
        Css.SetStyle(viewer, "container-type: scroll-state; direction: rtl");
        Layout(root);
        Assert.Equal(30, target.Width);
        Assert.Equal(10, target.Height);

        viewer.ScrollToHorizontalOffset(viewer.ScrollableWidth);
        root.UpdateLayout();
        Assert.Equal(10, target.Width);
        Assert.Equal(30, target.Height);
    }

    [Fact]
    public void ADisabledInnerAxisSelectsTheOuterScrollerAndReSelectsWhenEnabled()
    {
        var target = new Border();
        var innerContent = new Grid { Width = 160, Height = 200 };
        innerContent.Children.Add(target);
        var inner = new ScrollViewer
        {
            Width = 100, Height = 80, Content = innerContent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var outerContent = new Grid { Width = 200, Height = 300 };
        outerContent.Children.Add(inner);
        var outer = new ScrollViewer { Width = 120, Height = 100, Content = outerContent };
        var root = new Border { Child = outer };
        Css.SetClass(target, "target");
        Css.SetStyle(outer, "container-type: scroll-state");
        Css.SetStyle(inner, "container-type: scroll-state");
        Css.SetStyleSheet(root,
            ".target { opacity: .9 } @container scroll-state(scrollable: bottom) { .target { opacity: .2 } }");
        Layout(root);
        Assert.Equal(.2, target.Opacity);

        outer.ScrollToVerticalOffset(outer.ScrollableHeight);
        root.UpdateLayout();
        Assert.Equal(.9, target.Opacity);

        inner.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        root.UpdateLayout();
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void CssHiddenOverflowExcludesThatAxisUntilRestored()
    {
        var (root, viewer, _, target) = Tree(
            "@container scroll-state(scrollable: bottom) { .target { opacity: .2 } }");
        Assert.Equal(.2, target.Opacity);
        Css.SetStyle(viewer, "container-type: scroll-state; overflow-y: hidden");
        Layout(root);
        Assert.Equal(.9, target.Opacity);
        Css.SetStyle(viewer, "container-type: scroll-state; overflow-y: auto");
        Layout(root);
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void TheScrollStateContainerTypeControlsEligibility()
    {
        var (root, viewer, _, target) = Tree(
            "@container scroll-state(scrollable: bottom) { .target { opacity: .2 } }", "normal");
        Assert.Equal(.9, target.Opacity);
        Css.SetStyle(viewer, "container-type: scroll-state");
        Layout(root);
        Assert.Equal(.2, target.Opacity);
    }

    [Fact]
    public void SizeAndScrollStateFeaturesSelectTheSameAncestor()
    {
        var (root, viewer, _, target) = Tree(
            "@container scroller (width > 50px) and scroll-state(scrollable: bottom) " +
            "{ .target { opacity: .2 } }", "size scroll-state");
        Assert.Equal(.2, target.Opacity);
        Css.SetStyle(viewer, "container: scroller / scroll-state");
        Layout(root);
        Assert.Equal(.9, target.Opacity);
    }

    [Fact]
    public void ScrollStateAloneDoesNotImposeSizeContainment()
    {
        var (_, viewer, _, _) = Tree(string.Empty);
        Assert.Equal(CssContainerType.ScrollState, CssContainerProperties.Type(viewer));
        Assert.False(CssContainerProperties.HasSizeContainment(viewer));
        Css.SetStyle(viewer, "container-type: size scroll-state");
        Assert.True(CssContainerProperties.HasSizeContainment(viewer));
    }
}
