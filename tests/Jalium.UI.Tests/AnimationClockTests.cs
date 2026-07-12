using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Animation;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

// Deterministic clock semantics driven purely by synthetic Stopwatch-tick
// timestamps (no real clock, no frame loop). Pause/Resume/Seek take their "now"
// from AnimationManager.CurrentFrameTimestampOrNow, so those calls are scripted
// inside manager frames via AnimationEngineTests.RunInsideFrame — which is also
// why this class shares the serialized "Application" collection.
[Collection("Application")]
public class AnimationClockTests
{
    private const long T0 = 1_000_000_000; // arbitrary synthetic time base

    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private static AnimationClock CreateClock(
        double durationSeconds = 1.0,
        FillBehavior fillBehavior = FillBehavior.HoldEnd,
        RepeatBehavior? repeatBehavior = null)
    {
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            FillBehavior = fillBehavior
        };
        if (repeatBehavior.HasValue)
        {
            animation.RepeatBehavior = repeatBehavior.Value;
        }

        return new AnimationClock(animation);
    }

    [Fact]
    public void Tick_WithSyntheticFrameTimestamps_AdvancesDeterministically()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);

        clock.Tick(T0 + Ticks(0.25));
        Assert.Equal(0.25, clock.CurrentProgress, 3);

        clock.Tick(T0 + Ticks(0.9));
        Assert.Equal(0.9, clock.CurrentProgress, 3);
        Assert.True(clock.IsRunning);
    }

    [Fact]
    public void Pause_FreezesProgress_AndReportsIsPaused()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);
        clock.Tick(T0 + Ticks(0.25));

        AnimationEngineTests.RunInsideFrame(T0 + Ticks(0.25), _ => clock.Pause());

        Assert.True(((IAnimationClock)clock).IsPaused);
        Assert.False(((IAnimationClock)clock).IsCompleted);

        clock.Tick(T0 + Ticks(0.6)); // ignored while paused
        Assert.Equal(0.25, clock.CurrentProgress, 3);
    }

    [Fact]
    public void Resume_CompensatesThePausedDuration()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);
        clock.Tick(T0 + Ticks(0.25));

        AnimationEngineTests.RunInsideFrame(T0 + Ticks(0.25), _ => clock.Pause());
        // Paused from 0.25s to 1.0s — none of that time may count.
        AnimationEngineTests.RunInsideFrame(T0 + Ticks(1.0), _ => clock.Resume());

        Assert.False(((IAnimationClock)clock).IsPaused);
        Assert.True(clock.IsRunning);

        clock.Tick(T0 + Ticks(1.25)); // 0.25s active before pause + 0.25s after
        Assert.Equal(0.5, clock.CurrentProgress, 3);
    }

    [Fact]
    public void Seek_FromBeginTime_LandsExactlyAtTheOffset()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);

        long seekFrame = T0 + Ticks(0.1);
        AnimationEngineTests.RunInsideFrame(seekFrame, _ => clock.Seek(TimeSpan.FromSeconds(0.5), TimeSeekOrigin.BeginTime));

        clock.Tick(seekFrame);
        Assert.Equal(0.5, clock.CurrentProgress, 3);
    }

    [Fact]
    public void Seek_FromDurationEnd_LandsAtDurationMinusOffset()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);

        long seekFrame = T0 + Ticks(0.1);
        AnimationEngineTests.RunInsideFrame(seekFrame, _ => clock.Seek(TimeSpan.FromSeconds(0.25), TimeSeekOrigin.Duration));

        clock.Tick(seekFrame);
        Assert.Equal(0.75, clock.CurrentProgress, 3);
    }

    [Fact]
    public void Seek_ReArmsACompletedClock()
    {
        var clock = CreateClock();
        clock.BeginAt(T0);
        clock.Tick(T0 + Ticks(2.0)); // past the end
        Assert.True(((IAnimationClock)clock).IsCompleted);

        long seekFrame = T0 + Ticks(3.0);
        AnimationEngineTests.RunInsideFrame(seekFrame, _ => clock.Seek(TimeSpan.FromSeconds(0.5), TimeSeekOrigin.BeginTime));

        Assert.False(((IAnimationClock)clock).IsCompleted);
        Assert.True(clock.IsRunning);

        clock.Tick(seekFrame);
        Assert.Equal(0.5, clock.CurrentProgress, 3);
    }

    [Fact]
    public void NaturalCompletion_FillBehaviorStop_FreezesProgressAtZero()
    {
        var clock = CreateClock(fillBehavior: FillBehavior.Stop);
        var completedCount = 0;
        clock.Completed += (_, _) => completedCount++;

        clock.BeginAt(T0);
        clock.Tick(T0 + Ticks(1.5));

        // Previously dead code: the tail progress recomputation overwrote the
        // Stop-fill zero back to 1.0 on the completion frame.
        Assert.Equal(0.0, clock.CurrentProgress);
        Assert.True(((IAnimationClock)clock).IsCompleted);
        Assert.False(clock.IsRunning);
        Assert.Equal(1, completedCount);

        clock.Tick(T0 + Ticks(2.0)); // completed clocks never tick again
        Assert.Equal(0.0, clock.CurrentProgress);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public void NaturalCompletion_HoldEnd_FreezesProgressAtOne()
    {
        var clock = CreateClock(fillBehavior: FillBehavior.HoldEnd);
        var completedCount = 0;
        clock.Completed += (_, _) => completedCount++;

        clock.BeginAt(T0);
        clock.Tick(T0 + Ticks(1.5));

        Assert.Equal(1.0, clock.CurrentProgress);
        Assert.True(((IAnimationClock)clock).IsCompleted);
        Assert.Equal(1, completedCount);
    }

    [Fact]
    public void RepeatBehaviorWithDuration_UsesTheFrameTimestampTimeBase()
    {
        // Regression for the second-time-source leak: the HasDuration branch
        // must measure total elapsed with the frame timestamp, not a fresh
        // Stopwatch.GetTimestamp(). With the synthetic far-past time base a
        // wall-clock measurement would see hours of "elapsed" and terminate on
        // the first boundary instead of repeating.
        var clock = CreateClock(repeatBehavior: new RepeatBehavior(TimeSpan.FromSeconds(2.5)));
        clock.BeginAt(T0);

        clock.Tick(T0 + Ticks(1.1)); // first boundary: 1.1s < 2.5s → repeat
        Assert.True(clock.IsRunning);
        Assert.False(((IAnimationClock)clock).IsCompleted);

        clock.Tick(T0 + Ticks(3.5)); // 3.5s ≥ 2.5s → done
        Assert.True(((IAnimationClock)clock).IsCompleted);
    }
}

// Storyboard lifecycle on the element-driven path: value writes go through the
// element's animated layer, settlement bookkeeping releases the static active
// set, and Completed fires only for an all-natural finish.
[Collection("Application")]
public class StoryboardLifecycleTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private sealed class ProbeElement : UIElement
    {
    }

    private static Storyboard CreateOpacityStoryboard(
        UIElement target,
        double from,
        double to,
        double durationSeconds,
        FillBehavior fillBehavior)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            FillBehavior = fillBehavior
        };

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
        return storyboard;
    }

    private static bool IsInActiveStoryboardSet(Storyboard storyboard)
    {
        var field = typeof(Storyboard).GetField("_activeStoryboards", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var set = (System.Collections.IEnumerable)field!.GetValue(null)!;
        foreach (var entry in set)
        {
            if (ReferenceEquals(entry, storyboard))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Begin_ElementTarget_DrivesTheAnimatedLayerNotTheLocalValue()
    {
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.8);
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 1.0, durationSeconds: 1.0, FillBehavior.HoldEnd);

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());

        Assert.True(element.HasAnimation(UIElement.OpacityProperty));
        Assert.True(IsInActiveStoryboardSet(storyboard));

        AnimationManager.ProcessFrame(t0 + Ticks(0.5));

        Assert.True(element.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(0.5, (double)element.GetValue(UIElement.OpacityProperty)!, 3);

        storyboard.Stop();

        // WPF controllable-storyboard semantics: Stop removes the animation
        // clock and restores the property's base value. HoldEnd applies while
        // a completed clock remains attached; it does not rewrite the base.
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.False(element.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(0.8, (double)element.GetValue(UIElement.OpacityProperty)!, 3);
    }

    [Fact]
    public void NaturalCompletion_FillBehaviorStop_ClearsTheAnimatedLayerAndRestoresBase()
    {
        // D07 regression: FillBehavior.Stop used to leave the final animated
        // value permanently shadowing the base value.
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.8);
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 0.3, durationSeconds: 1.0, FillBehavior.Stop);

        var completedCount = 0;
        storyboard.Completed += (_, _) => completedCount++;

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());

        AnimationManager.ProcessFrame(t0 + Ticks(2.0)); // completes naturally

        Assert.Equal(1, completedCount);
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.False(element.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(0.8, (double)element.GetValue(UIElement.OpacityProperty)!);
        Assert.False(IsInActiveStoryboardSet(storyboard), "a settled storyboard must leave the static active set");
    }

    [Fact]
    public void Pause_SuppressesCompletion_ResumeContinuesAndCompletes()
    {
        // The pre-rewrite completion check treated "no clock running" as done,
        // so pausing a storyboard fired Completed. Paused clocks must not
        // settle; Resume must pick up where the animation left off.
        var element = new ProbeElement();
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 1.0, durationSeconds: 1.0, FillBehavior.HoldEnd);

        var completedCount = 0;
        storyboard.Completed += (_, _) => completedCount++;

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());
        AnimationEngineTests.RunInsideFrame(t0 + Ticks(0.25), _ => storyboard.Pause());

        AnimationManager.ProcessFrame(t0 + Ticks(5.0));
        AnimationManager.ProcessFrame(t0 + Ticks(6.0));
        Assert.Equal(0, completedCount);
        Assert.True(IsInActiveStoryboardSet(storyboard));

        AnimationEngineTests.RunInsideFrame(t0 + Ticks(10.0), _ => storyboard.Resume());

        // 0.25s consumed before the pause: half-way lands at +0.5s after resume.
        AnimationManager.ProcessFrame(t0 + Ticks(10.5));
        Assert.Equal(0, completedCount);
        Assert.Equal(0.75, (double)element.GetValue(UIElement.OpacityProperty)!, 3);

        AnimationManager.ProcessFrame(t0 + Ticks(11.0));
        Assert.Equal(1, completedCount);
        Assert.False(IsInActiveStoryboardSet(storyboard));
    }

    [Fact]
    public void Stop_DoesNotFireCompleted_AndReleasesTheActiveSet()
    {
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.8);
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 1.0, durationSeconds: 1.0, FillBehavior.Stop);

        var completedCount = 0;
        storyboard.Completed += (_, _) => completedCount++;

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());
        AnimationManager.ProcessFrame(t0 + Ticks(0.3));

        storyboard.Stop();

        Assert.Equal(0, completedCount);
        Assert.False(IsInActiveStoryboardSet(storyboard));
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.Equal(0.8, (double)element.GetValue(UIElement.OpacityProperty)!);

        // No zombie ticking afterwards.
        AnimationManager.ProcessFrame(t0 + Ticks(5.0));
        Assert.Equal(0, completedCount);
    }

    [Fact]
    public void RecycleStop_SettlesTheTerminatedClock_AndReleasesTheActiveSet()
    {
        // Element-side termination bookkeeping: a storyboard whose only clock
        // is killed by container recycling must leave the static active set
        // without firing Completed — otherwise it pins the recycled subtree
        // forever (the reviewed static-leak blocker).
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.8);
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 0.2, durationSeconds: 1.0, FillBehavior.HoldEnd);

        var completedCount = 0;
        storyboard.Completed += (_, _) => completedCount++;

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());
        AnimationManager.ProcessFrame(t0 + Ticks(0.2));
        Assert.True(IsInActiveStoryboardSet(storyboard));

        element.StopAnimationsForRecycleRecursive();

        Assert.False(IsInActiveStoryboardSet(storyboard), "termination must settle the storyboard's bookkeeping");
        Assert.Equal(0, completedCount);
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.False(element.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(0.8, (double)element.GetValue(UIElement.OpacityProperty)!); // hard discard, no HoldEnd ghost

        AnimationManager.ProcessFrame(t0 + Ticks(5.0));
        Assert.Equal(0, completedCount);
    }

    [Fact]
    public void Completed_ElementDrivenHoldEnd_ObservesFinalValueNotStalePreCompletionValue()
    {
        // The storyboard subscribes to the clock's Completed before the element's own handler,
        // and an element-driven clock raises Completed from INSIDE AnimationClock.Tick — which
        // UIElement.OnAnimationFrame runs before it writes the frame's final animated value.
        // Raising Storyboard.Completed synchronously there let a handler observe the pre-completion
        // value; it is now deferred to the end of the frame, after the final value lands.
        var element = new ProbeElement();
        var storyboard = CreateOpacityStoryboard(element, from: 0.0, to: 1.0, durationSeconds: 1.0, FillBehavior.HoldEnd);

        double observedInHandler = double.NaN;
        storyboard.Completed += (_, _) => observedInHandler = (double)element.GetValue(UIElement.OpacityProperty)!;

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => storyboard.Begin());

        // Advance partway so a non-final animated value (0.5) is actually written first: without
        // the deferral the completion handler would observe THIS value instead of the final 1.0.
        AnimationManager.ProcessFrame(t0 + Ticks(0.5));
        Assert.Equal(0.5, (double)element.GetValue(UIElement.OpacityProperty)!, 3);

        AnimationManager.ProcessFrame(t0 + Ticks(2.0)); // completes naturally (HoldEnd → holds 1.0)

        Assert.Equal(1.0, observedInHandler, 3);
        Assert.Equal(1.0, (double)element.GetValue(UIElement.OpacityProperty)!, 3);
    }
}
