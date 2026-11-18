using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class AnimationRuntimeClockTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private static AnimationClock CreateClock(AnimationTimeline? animation = null) =>
        (AnimationClock)((Timeline)(animation ?? new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))))
            .CreateClock(hasControllableRoot: true);

    [Fact]
    public void GlobalSpeed_ReflectsAccelerationAndReverseDirection()
    {
        var clock = CreateClock(new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))
        {
            AccelerationRatio = 1,
            AutoReverse = true,
            SpeedRatio = 2
        });
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(.125));
        Assert.Equal(1d, clock.CurrentGlobalSpeed!.Value, 5);
        clock.Tick(start + Ticks(.875));
        Assert.Equal(-1d, clock.CurrentGlobalSpeed!.Value, 5);
    }

    [Fact]
    public void SeekingFromDuration_UsesTheSignedOffset()
    {
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        AnimationEngineTests.RunInsideFrame(start, _ =>
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(-.25), TimeSeekOrigin.Duration));
        Assert.Equal(.75, clock.CurrentProgress, 5);
    }

    [Fact]
    public void ChangingSpeedRatio_PreservesProgressAndChangesOnlyFutureTime()
    {
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(.25));
        AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => clock.Controller.SpeedRatio = 2);
        clock.Tick(start + Ticks(.25));
        Assert.Equal(.25, clock.CurrentProgress, 5);
        clock.Tick(start + Ticks(.5));
        Assert.Equal(.75, clock.CurrentProgress, 5);
    }

    [Fact]
    public void ZeroSpeed_FreezesAndResumesFromTheSamePosition()
    {
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(.25));
        AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => clock.Controller.SpeedRatio = 0);
        clock.Tick(start + Ticks(5));
        Assert.Equal(.25, clock.CurrentProgress, 5);
        AnimationEngineTests.RunInsideFrame(start + Ticks(5), _ => clock.Controller.SpeedRatio = 1);
        clock.Tick(start + Ticks(5.25));
        Assert.Equal(.5, clock.CurrentProgress, 5);
    }

    [Fact]
    public void AutomaticDuration_UsesTheFinalKeyFrameTime()
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(100, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        var clock = CreateClock(animation);
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(1));
        Assert.True(clock.IsRunning);
        Assert.Equal(.5, clock.CurrentProgress, 5);
        Assert.Equal(50d, clock.GetCurrentValue(0d, 0d));
        clock.Tick(start + Ticks(2));
        Assert.Equal(100d, clock.GetCurrentValue(0d, 0d));
    }

    [Fact]
    public void ForeverDuration_DoesNotCompleteAfterOneSecond()
    {
        var clock = CreateClock(new DoubleAnimation { From = 0, To = 100, Duration = Duration.Forever });
        int completed = 0;
        clock.Completed += (_, _) => completed++;
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(3600));
        Assert.True(clock.IsRunning);
        Assert.Equal(ClockState.Active, clock.CurrentState);
        Assert.Equal(TimeSpan.FromHours(1), clock.CurrentTime);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void NullBeginTime_NeverStartsOrCompletesAutomatically()
    {
        var clock = CreateClock(new DoubleAnimation { To = 100, Duration = TimeSpan.FromSeconds(1), BeginTime = null });
        int completed = 0;
        clock.Completed += (_, _) => completed++;
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(5));
        Assert.False(clock.IsRunning);
        Assert.Equal(ClockState.Stopped, clock.CurrentState);
        Assert.Null(clock.CurrentTime);
        Assert.Equal(0, completed);
    }

    [Fact]
    public void SeekAlignedToLastTick_UpdatesProgressImmediately()
    {
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ =>
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(.75), TimeSeekOrigin.BeginTime));
        Assert.Equal(.75, clock.CurrentProgress, 5);
        Assert.Equal(TimeSpan.FromSeconds(.75), clock.CurrentTime);
    }

    [Fact]
    public void SeekingWhilePaused_PreservesThePausedState()
    {
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        clock.BeginAt(start);
        clock.Tick(start + Ticks(.25));
        AnimationEngineTests.RunInsideFrame(start + Ticks(.25), _ => clock.Pause());
        AnimationEngineTests.RunInsideFrame(start + Ticks(1), _ =>
            clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(.75), TimeSeekOrigin.BeginTime));
        Assert.True(clock.IsPaused);
        Assert.Equal(.75, clock.CurrentProgress, 5);
        clock.Tick(start + Ticks(2));
        Assert.Equal(.75, clock.CurrentProgress, 5);
    }

    [Theory]
    [InlineData(false, 2.25, .25)]
    [InlineData(true, 1.5, 1)]
    [InlineData(true, 1.75, .5)]
    public void SkipToFill_UsesTheActualRepeatEndpoint(bool autoReverse, double repeats, double expectedProgress)
    {
        var clock = CreateClock(new DoubleAnimation(0, 100, TimeSpan.FromSeconds(1))
        {
            AutoReverse = autoReverse,
            RepeatBehavior = new RepeatBehavior(repeats)
        });
        clock.BeginAt(Stopwatch.GetTimestamp());
        clock.Controller.SkipToFill();
        Assert.Equal(expectedProgress, clock.CurrentProgress, 5);
        Assert.Equal(ClockState.Filling, clock.CurrentState);
    }

    [Fact]
    public void SharedClock_RemovingOneTargetKeepsTheOtherTargetAnimating()
    {
        var first = new RotateTransform();
        var second = new RotateTransform();
        var clock = CreateClock();
        long start = Stopwatch.GetTimestamp();
        try
        {
            AnimationEngineTests.RunInsideFrame(start, _ =>
            {
                first.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
                second.ApplyAnimationClock(RotateTransform.AngleProperty, clock);
            });
            AnimationManager.ProcessFrame(start + Ticks(.25));
            Assert.Equal(25d, first.Angle, 5);
            first.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(start + Ticks(.5));
            Assert.Equal(50d, second.Angle, 5);
            Assert.Equal(0d, first.Angle);
        }
        finally
        {
            first.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            second.ApplyAnimationClock(RotateTransform.AngleProperty, null);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }
}
