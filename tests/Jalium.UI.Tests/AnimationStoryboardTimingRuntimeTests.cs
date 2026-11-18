using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationStoryboardTimingRuntimeTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    [Theory]
    [InlineData(false, 1.25, .25)]
    [InlineData(true, 1.25, .75)]
    public void RootRepeatAndAutoReverse_ControlTheChildTimeline(bool autoReverse, double seconds, double expected)
    {
        var target = new Border { Opacity = .8 };
        var storyboard = Create(target);
        storyboard.RepeatBehavior = new RepeatBehavior(2);
        storyboard.AutoReverse = autoReverse;
        long start = Stopwatch.GetTimestamp();
        int completed = 0;
        storyboard.Completed += (_, _) => completed++;
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.5));
            AnimationManager.ProcessFrame(start + Ticks(seconds));
            Assert.Equal(expected, target.Opacity, 5);
            Assert.Equal(0, completed);
        }
        finally { Stop(storyboard); }
    }

    [Fact]
    public void DisabledChild_RemainsInactiveWhenItsParentRuns()
    {
        var target = new Border { Opacity = .8 };
        var storyboard = Create(target);
        storyboard.Children[0].BeginTime = null;
        int completed = 0;
        storyboard.Children[0].Completed += (_, _) => completed++;
        var sibling = new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1));
        Storyboard.SetTarget(sibling, target);
        Storyboard.SetTargetProperty(sibling, new PropertyPath(FrameworkElement.WidthProperty));
        storyboard.Children.Add(sibling);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(50d, target.Width, 5);
            Assert.Equal(.8, target.Opacity);
            AnimationManager.ProcessFrame(start + Ticks(1.5));
            Assert.Equal(.8, target.Opacity);
            Assert.Equal(0, completed);
        }
        finally { Stop(storyboard); }
    }

    [Fact]
    public void RootBeginTime_DelaysAllChildren()
    {
        var target = new Border { Opacity = .8 };
        var storyboard = Create(target);
        storyboard.BeginTime = TimeSpan.FromSeconds(.5);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.25));
            Assert.Equal(.8, target.Opacity);
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(.25, target.Opacity, 5);
        }
        finally { Stop(storyboard); }
    }

    [Fact]
    public void RootSpeedRatio_ScalesChildTime()
    {
        var target = new Border();
        var storyboard = Create(target);
        storyboard.SpeedRatio = 2;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.25));
            Assert.Equal(.5, target.Opacity, 5);
        }
        finally { Stop(storyboard); }
    }

    [Fact]
    public void ExplicitRootDuration_TruncatesAndHoldsTheChildAtThatTime()
    {
        var target = new Border();
        var storyboard = Create(target);
        storyboard.Duration = TimeSpan.FromSeconds(.5);
        int completed = 0;
        storyboard.Completed += (_, _) => completed++;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(.5, target.Opacity, 5);
            Assert.Equal(1, completed);
            AnimationManager.ProcessFrame(start + Ticks(2));
            Assert.Equal(.5, target.Opacity, 5);
            Assert.Equal(1, completed);
        }
        finally { Stop(storyboard); }
    }

    [Fact]
    public void NestedGroupDelayAndSpeed_CombineWithTheRootDelay()
    {
        var target = new Border { Opacity = .8 };
        var child = Create(target).Children[0];
        var group = new ParallelTimeline { BeginTime = TimeSpan.FromSeconds(.25), SpeedRatio = 2 };
        group.Children.Add(child);
        var storyboard = new Storyboard { BeginTime = TimeSpan.FromSeconds(.25) };
        storyboard.Children.Add(group);
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(.4));
            Assert.Equal(.8, target.Opacity);
            AnimationManager.ProcessFrame(start + Ticks(.75));
            Assert.Equal(.5, target.Opacity, 5);
        }
        finally { Stop(storyboard); }
    }

    [Theory]
    [InlineData(FillBehavior.HoldEnd)]
    [InlineData(FillBehavior.Stop)]
    public void SeekingACompletedStoryboard_RearmsItsFrameSourceAndCompletion(FillBehavior fillBehavior)
    {
        var target = new Border { Opacity = .8 };
        var storyboard = Create(target);
        storyboard.Children[0].FillBehavior = fillBehavior;
        int completed = 0;
        storyboard.Completed += (_, _) => completed++;
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ => storyboard.Begin());
            AnimationManager.ProcessFrame(start + Ticks(1.5));
            Assert.Equal(1, completed);
            AnimationEngineTests.RunInsideFrame(start + Ticks(2), _ =>
                storyboard.SeekAlignedToLastTick(TimeSpan.FromSeconds(.25), TimeSeekOrigin.BeginTime));
            Assert.Equal(.25, target.Opacity, 5);
            AnimationManager.ProcessFrame(start + Ticks(3));
            Assert.Equal(fillBehavior == FillBehavior.HoldEnd ? 1d : .8d, target.Opacity);
            Assert.Equal(2, completed);
        }
        finally { Stop(storyboard); }
    }

    private static Storyboard Create(Border target)
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }

    private static void Stop(Storyboard storyboard)
    {
        storyboard.Stop();
        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
    }
}
