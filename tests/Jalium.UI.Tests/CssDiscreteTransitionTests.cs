using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssDiscreteTransitionTests
{
    [Theory]
    [InlineData("normal", true)]
    [InlineData("allow-discrete, normal", true)]
    [InlineData("normal, allow-discrete", true)]
    [InlineData("allow-discrete normal", false)]
    [InlineData("auto", false)]
    public void TransitionBehaviorAcceptsOnlyItsKeywordList(string value, bool valid)
        => Assert.Equal(valid, Css.Supports("transition-behavior", value));

    [Fact]
    public void LonghandBehaviorCyclesAndShorthandResetsItsOmittedBehavior()
    {
        var viewer = CreateViewer();
        Css.SetStyle(viewer,
            "transition-property:scrollbar-width,scrollbar-gutter,scrollbar-color; " +
            "transition-duration:1s; transition-behavior:allow-discrete,normal");
        var data = Assert.IsType<CssTransitionData>(
            viewer.GetValue(CssTransitions.DataProperty));
        Assert.True(data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!.AllowDiscrete);
        Assert.False(data.Find(viewer, ScrollViewer.CssScrollBarGutterProperty)!.AllowDiscrete);
        Assert.True(data.Find(viewer, ScrollViewer.CssScrollBarColorsProperty)!.AllowDiscrete);

        Css.SetStyle(viewer,
            "transition-behavior:allow-discrete; transition:scrollbar-width 1s linear");
        data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        Assert.False(data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!.AllowDiscrete);

        Css.SetStyle(viewer, "transition:allow-discrete 1s scrollbar-width linear");
        data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        Assert.True(data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!.AllowDiscrete);
    }

    [Fact]
    public void CssWideTransitionKeywordsResetBehaviorToNormal()
    {
        var viewer = CreateViewer();
        Css.SetStyle(viewer, "transition:scrollbar-width 1s allow-discrete");
        var data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        Assert.True(data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!.AllowDiscrete);

        Css.SetStyle(viewer, "transition-behavior:allow-discrete; transition:initial");
        data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        var transition = data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!;
        Assert.False(transition.AllowDiscrete);
        Assert.Equal(TimeSpan.Zero, transition.Duration);

        Css.SetStyle(viewer,
            "transition:scrollbar-width 1s allow-discrete; transition-behavior:unset");
        data = Assert.IsType<CssTransitionData>(viewer.GetValue(CssTransitions.DataProperty));
        Assert.False(data.Find(viewer, ScrollViewer.CssScrollBarWidthProperty)!.AllowDiscrete);
    }

    [Fact]
    public void VisibilityTransitionChangesPaintAndHitStateAtHalf()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        var host = new Grid { Width = 40, Height = 30 };
        host.Children.Add(child);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child,
                "visibility:visible; transition:visibility 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 40, 30);

            Css.SetStyle(child,
                "visibility:hidden; transition:visibility 1s linear allow-discrete");
            Assert.True(child.HasAutomaticTransition(CssDisplayProperties.VisibilityProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.True(child.IsVisible);
            Assert.Same(child, host.InputHitTest(new Point(10, 10)));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.False(child.IsVisible);
            Assert.NotSame(child, host.InputHitTest(new Point(10, 10)));
            Assert.Equal(40, child.ActualWidth);
        }
        finally
        {
            child.StopAutomaticTransition(CssDisplayProperties.VisibilityProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void VisibilityCollapseTransitionReflowsFlexAtHalf()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var first = new Border { Width = 40, Height = 20 };
        var collapsing = new Border { Width = 50, Height = 70 };
        var last = new Border { Width = 40, Height = 25 };
        var panel = new FlexPanel { AlignItems = FlexAlign.FlexStart,
            ColumnSpacing = 10 };
        panel.Children.Add(first);
        panel.Children.Add(collapsing);
        panel.Children.Add(last);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(collapsing,
                "visibility:visible; transition:visibility 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(panel, 200, 70);
            Assert.Equal(110, last.VisualBounds.X, 6);

            Css.SetStyle(collapsing,
                "visibility:collapse; transition:visibility 1s linear allow-discrete");
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            CssBorderRadiusTests.Layout(panel, 200, 70);
            Assert.Equal(110, last.VisualBounds.X, 6);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            CssBorderRadiusTests.Layout(panel, 200, 70);
            Assert.False(collapsing.IsVisible);
            Assert.Equal(50, last.VisualBounds.X, 6);
        }
        finally
        {
            collapsing.StopAutomaticTransition(CssDisplayProperties.VisibilityProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void PointerEventsTransitionChangesHitTargetAtHalf()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var back = new Border { Width = 40, Height = 30, Background = Brushes.Blue };
        var front = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        var host = new Grid { Width = 40, Height = 30 };
        host.Children.Add(back);
        host.Children.Add(front);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(front,
                "pointer-events:auto; transition:pointer-events 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 40, 30);

            Css.SetStyle(front,
                "pointer-events:none; transition:pointer-events 1s linear allow-discrete");
            Assert.True(front.HasAutomaticTransition(CssPointerEventsProperties.ValueProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.Same(front, host.InputHitTest(new Point(10, 10)));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Same(back, host.InputHitTest(new Point(10, 10)));
        }
        finally
        {
            front.StopAutomaticTransition(CssPointerEventsProperties.ValueProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void CursorTransitionChangesAtHalf()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var child = new Border { Width = 40, Height = 30 };
        var host = new Grid { Width = 40, Height = 30 };
        host.Children.Add(child);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(child,
                "cursor:pointer; transition:cursor 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 40, 30);

            Css.SetStyle(child,
                "cursor:wait; transition:cursor 1s linear allow-discrete");
            Assert.True(child.HasAutomaticTransition(FrameworkElement.CursorProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.Same(Cursors.Hand, child.Cursor);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Same(Cursors.Wait, child.Cursor);
        }
        finally
        {
            child.StopAutomaticTransition(FrameworkElement.CursorProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void NormalBehaviorDoesNotStartAWidthTransition()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = CreateViewer();
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer, "scrollbar-width:auto; transition:scrollbar-width 1s linear");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Css.SetStyle(viewer, "scrollbar-width:none; transition:scrollbar-width 1s linear");
            Assert.False(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty));
            Assert.Equal(CssScrollBarWidthMode.None, viewer.CssScrollBarWidth);
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void FirstCssDeclarationAfterLayoutCanTransitionFromThePresentedDefault()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = CreateViewer();
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.True(viewer.IsArrangeValid);
            Assert.Equal(CssScrollBarWidthMode.Auto, viewer.CssScrollBarWidth);

            Css.SetStyle(viewer,
                "scrollbar-width:none; transition:scrollbar-width 1s linear allow-discrete");
            Assert.True(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty));
            Assert.Equal(CssScrollBarWidthMode.Auto, viewer.CssScrollBarWidth);
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void AllowDiscreteWidthFlipsAtHalfAndKeepsScrolling()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = CreateViewer();
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer,
                "scrollbar-width:auto; transition:scrollbar-width 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 200, 140);
            var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
            Assert.Equal(Visibility.Visible, bar.Visibility);

            Css.SetStyle(viewer,
                "scrollbar-width:none; transition:scrollbar-width 1s linear allow-discrete");
            Assert.True(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(CssScrollBarWidthMode.Auto, viewer.CssScrollBarWidth);
            Assert.Equal(Visibility.Visible, bar.Visibility);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(CssScrollBarWidthMode.None, viewer.CssScrollBarWidth);
            Assert.Equal(Visibility.Collapsed, bar.Visibility);
            Assert.True(viewer.CanScrollVertically);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 2);
            Assert.False(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty));
            Assert.Equal(CssScrollBarWidthMode.None, viewer.CssScrollBarWidth);
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void DiscreteWidthUsesEasedProgressForTheFlip()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = CreateViewer();
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer,
                "scrollbar-width:auto; transition:scrollbar-width 1s ease-in allow-discrete");
            CssBorderRadiusTests.Layout(host, 200, 140);

            Css.SetStyle(viewer,
                "scrollbar-width:none; transition:scrollbar-width 1s ease-in allow-discrete");
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Equal(CssScrollBarWidthMode.Auto, viewer.CssScrollBarWidth);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(
                start + Stopwatch.Frequency * 3 / 4);
            Assert.Equal(CssScrollBarWidthMode.None, viewer.CssScrollBarWidth);
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarWidthProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void AllowDiscreteGutterReflowsAtHalf()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 80, Height = 40 },
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            IsOverlayScrollBarEnabled = false,
        };
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer,
                "scrollbar-gutter:auto; transition:scrollbar-gutter 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(200, viewer.ViewportWidth);

            Css.SetStyle(viewer,
                "scrollbar-gutter:stable both-edges; " +
                "transition:scrollbar-gutter 1s linear allow-discrete");
            Assert.True(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarGutterProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(200, viewer.ViewportWidth);

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.Equal(CssScrollBarGutterMode.StableBothEdges, viewer.CssScrollBarGutter);
            Assert.Equal(176, viewer.ViewportWidth);
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarGutterProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void AllowDiscreteColorToAutoRestoresThemeAtHalf()
    {
        ResetApplicationState();
        _ = new Application();
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var viewer = CreateViewer();
        var host = new Grid();
        host.Children.Add(viewer);
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(viewer,
                "scrollbar-color:red blue; transition:scrollbar-color 1s linear allow-discrete");
            CssBorderRadiusTests.Layout(host, 200, 140);
            Assert.False(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarColorsProperty));
            Assert.NotNull(viewer.CssScrollBarColors);
            var bar = Assert.IsType<ScrollBar>(viewer.GetVisualChild(1));
            Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));

            Css.SetStyle(viewer,
                "scrollbar-color:auto; transition:scrollbar-color 1s linear allow-discrete");
            Assert.True(viewer.HasAutomaticTransition(ScrollViewer.CssScrollBarColorsProperty));
            var start = Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 4);
            Assert.NotNull(viewer.CssScrollBarColors);
            Assert.Equal(Colors.Blue.ToArgb(), ColorOf(bar.Background));

            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + Stopwatch.Frequency / 2);
            Assert.Null(viewer.CssScrollBarColors);
            Assert.NotEqual(Colors.Blue.ToArgb(), ColorOf(bar.Background));
        }
        finally
        {
            viewer.StopAutomaticTransition(ScrollViewer.CssScrollBarColorsProperty,
                clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
            ResetApplicationState();
        }
    }

    private static ScrollViewer CreateViewer() => new()
    {
        Content = new Border { Width = 300, Height = 240 },
        VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
        IsOverlayScrollBarEnabled = false,
        IsScrollBarAutoHideEnabled = false,
    };

    private static uint ColorOf(Brush? brush)
        => Assert.IsType<SolidColorBrush>(brush).Color.ToArgb();

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
