using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationElementRuntimeTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    [Fact]
    public void StoppingAReplacementClock_RevealsTheLatestBaseInsteadOfTheHandoffSnapshot()
    {
        var element = new Border { Opacity = .8 };
        var clock = (AnimationClock)((Timeline)new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))).CreateClock(true);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => element.ApplyAnimationClock(UIElement.OpacityProperty, clock));
            element.Opacity = .6;
            clock.Controller.Stop();
            Assert.Equal(.6, element.Opacity);
        }
        finally
        {
            element.ApplyAnimationClock(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void AppliedClock_ResumesAfterTheElementStopsReceivingFrames()
    {
        var element = new Border { Opacity = .8 };
        var clock = (AnimationClock)((Timeline)new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))).CreateClock(true);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.ApplyAnimationClock(UIElement.OpacityProperty, clock));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            Assert.Equal(.25, element.Opacity, 5);
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => clock.Controller.Pause());
            AnimationManager.ProcessFrame(start + Ticks(1));
            AnimationEngineTests.RunInsideFrame(start + Ticks(1), _ => clock.Controller.Resume());
            AnimationManager.ProcessFrame(start + Ticks(1.25));
            Assert.Equal(.5, element.Opacity, 5);
            clock.Controller.Remove();
            Assert.Equal(.8, element.Opacity, 5);
            Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        }
        finally
        {
            element.ApplyAnimationClock(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void SharedClock_AttachingAndRemovingAnotherElementPreservesTheOriginalClock()
    {
        var first = new Border();
        var second = new Border();
        var clock = (AnimationClock)((Timeline)new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))).CreateClock(true);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => first.ApplyAnimationClock(UIElement.OpacityProperty, clock));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => second.ApplyAnimationClock(UIElement.OpacityProperty, clock));
            Assert.Equal(.25, first.Opacity, 5);
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(.5, second.Opacity, 5);
            second.ApplyAnimationClock(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(.75, first.Opacity, 5);
        }
        finally
        {
            first.ApplyAnimationClock(UIElement.OpacityProperty, null);
            second.ApplyAnimationClock(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void AnimationCompleted_ObservesTheFinalPropertyValue()
    {
        var element = new Border { Opacity = .8 };
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        double observed = double.NaN;
        animation.Completed += (_, _) => observed = element.Opacity;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.BeginAnimation(UIElement.OpacityProperty, animation));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            AnimationManager.ProcessFrame(start + Ticks(1.5));
            Assert.Equal(1d, observed);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            Assert.Equal(.8, element.Opacity);
        }
        finally
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void NullBeginTime_HoldsTheCurrentValueUntilAnimationsAreRemoved()
    {
        var element = new Border { Opacity = .8 };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation { BeginTime = null });
            AnimationManager.ProcessFrame(start + Ticks(2));
            Assert.Equal(.25, element.Opacity, 5);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            Assert.Equal(.8, element.Opacity);
        }
        finally
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void Compose_ContinuesTheUnderlyingAnimation()
    {
        var element = new Border { Width = 10 };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))));
            AnimationManager.ProcessFrame(start + Ticks(.25));
            AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => element.BeginAnimation(
                FrameworkElement.WidthProperty, new DoubleAnimation { By = 20, Duration = TimeSpan.FromSeconds(1) }, HandoffBehavior.Compose));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(55d, element.Width, 5);
            element.BeginAnimation(FrameworkElement.WidthProperty, null, HandoffBehavior.Compose);
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(85d, element.Width, 5);
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            Assert.Equal(10d, element.Width);
        }
        finally
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void InvalidAnimationType_DoesNotReplaceTheRunningAnimation()
    {
        var element = new Border { Opacity = .8 };
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1))));
            Assert.Throws<ArgumentException>(() => element.BeginAnimation(UIElement.OpacityProperty, new ColorAnimation()));
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(.5, element.Opacity, 5);
        }
        finally
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }
}
