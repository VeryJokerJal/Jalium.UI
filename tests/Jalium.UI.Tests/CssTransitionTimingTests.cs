using Jalium.UI.Controls;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CssTransitionTimingTests
{
    [Theory]
    [InlineData("linear", .5, .5)]
    [InlineData("cubic-bezier(.25,.1,.25,1)", .5, .802403387584857)]
    [InlineData("ease-in-out", .5, .5)]
    [InlineData("steps(4, end)", .3, .25)]
    [InlineData("steps(4, start)", .3, .5)]
    [InlineData("steps(4, jump-both)", 0, .2)]
    [InlineData("steps(2, jump-none)", .5, 1)]
    public void TimingFunctions_EvaluateTheSpecifiedCurve(string text, double progress, double expected)
    {
        var reader = new CssTokenReader(text);
        Assert.True(CssTimingFunction.TryParse(ref reader, out var timing));
        Assert.True(reader.AtEnd);
        Assert.Equal(expected, timing!.Ease(progress), 9);
    }

    [Theory]
    [InlineData("cubic-bezier(-.1,0,1,1)")]
    [InlineData("cubic-bezier(0,0,2,1)")]
    [InlineData("steps(0)")]
    [InlineData("steps(1,jump-none)")]
    [InlineData("steps(1.5,end)")]
    public void InvalidTimingFunctions_AreRejected(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.False(CssTimingFunction.TryParse(ref reader, out _));
    }

    [Fact]
    public void TransitionLists_PreservePerPropertyDurationsDelaysAndEasings()
    {
        var element = new Border();
        Css.SetStyle(element, "transition:opacity 1s linear .2s, width 2s steps(4,end) -.5s");
        var data = Assert.IsType<CssTransitionData>(element.GetValue(CssTransitions.DataProperty));
        var opacity = data.Find(element, UIElement.OpacityProperty)!;
        var width = data.Find(element, FrameworkElement.WidthProperty)!;
        Assert.Equal(TimeSpan.FromSeconds(1), opacity.Duration);
        Assert.Equal(TimeSpan.FromSeconds(.2), opacity.Delay);
        Assert.Equal(TimeSpan.FromSeconds(2), width.Duration);
        Assert.Equal(TimeSpan.FromSeconds(-.5), width.Delay);
        Assert.Equal(.25, width.Timing.Ease(.3));
        var animation = Assert.IsType<DoubleAnimation>(CssTransitions.Create(element, FrameworkElement.WidthProperty, 0.0, 100.0, width));
        Assert.Equal(width.Delay, animation.BeginTime);
        Assert.Same(width.Timing, animation.EasingFunction);
    }

    [Fact]
    public void TransitionLonghands_CycleShorterListsAndResetOmittedShorthandComponents()
    {
        var element = new Border();
        Css.SetStyle(element, "transition-property:opacity,width,height; transition-duration:1s,2s; transition-delay:.3s; transition-timing-function:linear");
        var data = Assert.IsType<CssTransitionData>(element.GetValue(CssTransitions.DataProperty));
        Assert.Equal(TimeSpan.FromSeconds(1), data.Find(element, FrameworkElement.HeightProperty)!.Duration);
        Assert.Equal(TimeSpan.FromSeconds(.3), data.Find(element, FrameworkElement.HeightProperty)!.Delay);
        Css.SetStyle(element, "transition:opacity 1s");
        data = Assert.IsType<CssTransitionData>(element.GetValue(CssTransitions.DataProperty));
        Assert.Equal(TimeSpan.Zero, data.Find(element, UIElement.OpacityProperty)!.Delay);
        Assert.Null(data.Find(element, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void ADelayedCssTransition_UsesTheExistingFrameClock()
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var element = new Border(); var host = new Grid();
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(element, "opacity:0; transition:opacity 1s linear .5s");
            host.Children.Add(element);
            element.Dispatcher.ProcessQueue();
            Css.SetStyle(element, "opacity:1; transition:opacity 1s linear .5s");
            Assert.True(element.HasAutomaticTransition(UIElement.OpacityProperty));
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + System.Diagnostics.Stopwatch.Frequency / 4);
            Assert.Equal(0, element.Opacity, 3);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + System.Diagnostics.Stopwatch.Frequency * 3 / 4);
            Assert.Equal(.25, element.Opacity, 2);
        }
        finally
        {
            element.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Fact]
    public void CssTransitions_DoNotAnimateOverAnExplicitLocalValue()
    {
        var element = new Border { Opacity = .6 }; var host = new Grid();
        Css.SetStyle(element, "transition:opacity 1s; opacity:.1"); host.Children.Add(element);
        element.Dispatcher.ProcessQueue();
        Css.SetStyle(element, "transition:opacity 1s; opacity:.9");
        Assert.Equal(.6, element.Opacity);
        Assert.False(element.HasAutomaticTransition(UIElement.OpacityProperty));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LocalValueOrBinding_TakesOverARunningCssTransition(bool bind, bool disableAnimations)
    {
        var previousProvider = UIElement.AutomaticTransitionsEnabledProvider;
        var element = new Border();
        var host = new Grid();
        var source = new Border { Opacity = 1 };
        try
        {
            UIElement.AutomaticTransitionsEnabledProvider = static () => true;
            Css.SetStyle(element, "opacity:0; transition:opacity 1s linear");
            host.Children.Add(element);
            element.Dispatcher.ProcessQueue();
            Css.SetStyle(element, "opacity:1; transition:opacity 1s linear");
            Assert.True(element.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.Equal(0, element.Opacity);

            if (disableAnimations)
                UIElement.AutomaticTransitionsEnabledProvider = static () => false;

            var changes = new List<(object? OldValue, object? NewValue)>();
            element.PropertyChangedInternal += (property, oldValue, newValue) =>
            {
                if (property == UIElement.OpacityProperty) changes.Add((oldValue, newValue));
            };

            // The local value equals the CSS destination, but must take effect
            // immediately instead of leaving the old animation clock in control.
            var binding = bind ? element.SetBinding(UIElement.OpacityProperty,
                new Jalium.UI.Data.Binding(nameof(UIElement.Opacity)) { Source = source }) : null;
            if (!bind) element.Opacity = 1;

            Assert.Equal(1, element.Opacity);
            Assert.False(element.HasAutomaticTransition(UIElement.OpacityProperty));
            Assert.False(element.HasAnimatedValue(UIElement.OpacityProperty));
            var change = Assert.Single(changes);
            Assert.Equal(0.0, change.OldValue);
            Assert.Equal(1.0, change.NewValue);

            if (bind) source.Opacity = .6;
            else element.Opacity = .6;
            Css.SetStyle(element, "opacity:.2 !important; transition:opacity 1s linear");
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + System.Diagnostics.Stopwatch.Frequency / 2);
            Assert.Equal(.6, element.Opacity);
            if (bind) Assert.Same(binding, element.GetBindingExpression(UIElement.OpacityProperty));

            Css.SetStyle(element, string.Empty);
            Assert.Equal(.6, element.Opacity);
            if (bind) Assert.Same(binding, element.GetBindingExpression(UIElement.OpacityProperty));
        }
        finally
        {
            element.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
            UIElement.AutomaticTransitionsEnabledProvider = previousProvider;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAnimation_RetainsItsPrecedenceWhenCssAndLocalValuesChange(bool useStoryboard)
    {
        var element = new Border();
        var host = new Grid();
        var storyboard = new Storyboard();
        Css.SetStyle(element, "opacity:.2; transition:opacity 1s linear");
        host.Children.Add(element);
        element.Dispatcher.ProcessQueue();
        try
        {
            var animation = new DoubleAnimation
            {
                From = .4, To = .4, Duration = new Duration(TimeSpan.FromSeconds(1)),
                FillBehavior = FillBehavior.Stop
            };
            if (useStoryboard)
            {
                Storyboard.SetTarget(animation, element);
                Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
                storyboard.Children.Add(animation);
                storyboard.Begin(element, isControllable: true);
            }
            else element.BeginAnimation(UIElement.OpacityProperty, animation);
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start);
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + System.Diagnostics.Stopwatch.Frequency / 4);
            element.Opacity = .8;
            Css.SetStyle(element, "opacity:.9 !important; transition:opacity 1s linear");
            if (useStoryboard) Assert.Equal(ClockState.Active, storyboard.GetCurrentState(element));
            else Assert.True(element.HasExplicitAnimation(UIElement.OpacityProperty));
            Assert.Equal(.4, element.Opacity);

            if (useStoryboard) storyboard.Remove(element);
            else element.BeginAnimation(UIElement.OpacityProperty, null);
            Assert.Equal(.8, element.Opacity);
            Css.SetStyle(element, string.Empty);
            Assert.Equal(.8, element.Opacity);
        }
        finally
        {
            if (useStoryboard) storyboard.Remove(element);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RemovingStoryboard_RevealsCurrentCssOrBindingWithoutWritingLocalValues(bool bind, bool fill)
    {
        var element = new Border();
        var host = new Grid();
        var source = new Border { Opacity = .8 };
        Css.SetStyle(element, "opacity:.2; transition:opacity 1s linear");
        var binding = bind ? element.SetBinding(UIElement.OpacityProperty,
            new Jalium.UI.Data.Binding(nameof(UIElement.Opacity)) { Source = source }) : null;
        host.Children.Add(element);
        element.Dispatcher.ProcessQueue();

        var animation = new DoubleAnimation
        {
            From = .4, To = .4, Duration = new Duration(TimeSpan.FromSeconds(1)),
            FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        try
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin(element, isControllable: true));
            Jalium.UI.Animation.AnimationManager.ProcessFrame(start + System.Diagnostics.Stopwatch.Frequency / 4);
            if (fill) storyboard.SkipToFill(element);

            source.Opacity = .6;
            Css.SetStyle(element, "opacity:.9 !important; transition:opacity 1s linear");
            Assert.Equal(.4, element.Opacity);
            storyboard.Remove(element);

            Assert.Equal(bind ? .6 : .9, element.Opacity);
            Assert.Equal(.6, source.Opacity);
            if (bind) Assert.Same(binding, element.GetBindingExpression(UIElement.OpacityProperty));
            else Assert.Same(DependencyProperty.UnsetValue, element.ReadLocalValue(UIElement.OpacityProperty));

            Css.SetStyle(element, string.Empty);
            Assert.Equal(bind ? .6 : 1, element.Opacity);
        }
        finally
        {
            storyboard.Remove(element);
            element.StopAutomaticTransition(UIElement.OpacityProperty, clearAnimatedValue: true);
        }
    }
}
