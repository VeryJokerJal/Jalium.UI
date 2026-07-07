using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

// AnimationManager is a UI-thread-affine static (no locks by design). The
// "Application" collection disables parallelization, which is what actually
// serializes every test that registers subscriptions or calls ProcessFrame —
// running these in parallel with other animation-driving classes would race the
// manager's subscription list.
[Collection("Application")]
public class AnimationEngineTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    // ── Test doubles ────────────────────────────────────────────────────────

    private sealed class RecordingFrameTarget : IFrameAnimatable
    {
        public readonly List<long> Timestamps = new();
        public Func<long, bool>? OnFrame;

        public bool OnAnimationFrame(long frameTimestamp)
        {
            Timestamps.Add(frameTimestamp);
            return OnFrame?.Invoke(frameTimestamp) ?? true;
        }
    }

    private sealed class FrameAction : IFrameAnimatable
    {
        private readonly Action<long> _action;

        public FrameAction(Action<long> action) => _action = action;

        public bool OnAnimationFrame(long frameTimestamp)
        {
            _action(frameTimestamp);
            return false; // one-shot: auto-unregisters
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> inside a manager frame so that
    /// AnimationManager.CurrentFrameTimestampOrNow returns the synthetic frame
    /// timestamp — the deterministic way to Begin/Pause/Resume/Seek clocks in
    /// headless tests. Exceptions are rethrown (the manager's per-subscription
    /// catch would otherwise swallow them).
    /// </summary>
    internal static void RunInsideFrame(long frameTimestamp, Action<long> action)
    {
        ExceptionDispatchInfo? error = null;
        var subscription = new AnimationTickSubscription(
            new FrameAction(ts =>
            {
                try
                {
                    action(ts);
                }
                catch (Exception ex)
                {
                    error = ExceptionDispatchInfo.Capture(ex);
                }
            }),
            weak: false);

        AnimationManager.Register(subscription);
        AnimationManager.ProcessFrame(frameTimestamp);
        error?.Throw();
    }

    private sealed class ProbeElement : UIElement
    {
    }

    // ── AnimationManager: register / tick / unregister ──────────────────────

    [Fact]
    public void ProcessFrame_TicksEverySubscriber_WithTheSameTimestamp()
    {
        var a = new RecordingFrameTarget();
        var b = new RecordingFrameTarget();
        var subA = new AnimationTickSubscription(a, weak: false);
        var subB = new AnimationTickSubscription(b, weak: false);

        try
        {
            AnimationManager.Register(subA);
            AnimationManager.Register(subB);

            long frame = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(frame);

            Assert.Equal(new[] { frame }, a.Timestamps);
            Assert.Equal(new[] { frame }, b.Timestamps);
        }
        finally
        {
            AnimationManager.Unregister(subA);
            AnimationManager.Unregister(subB);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp()); // compact
        }
    }

    [Fact]
    public void Register_SameSubscriptionTwice_DoesNotDoubleTick()
    {
        var target = new RecordingFrameTarget();
        var sub = new AnimationTickSubscription(target, weak: false);

        try
        {
            AnimationManager.Register(sub);
            AnimationManager.Register(sub); // duplicate: must be a no-op

            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());

            Assert.Single(target.Timestamps);
        }
        finally
        {
            AnimationManager.Unregister(sub);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void Unregister_ThenRegister_BeforeCompaction_DoesNotDuplicateEntry()
    {
        // The recycle-and-rebind hot path: Unregister flips IsActive but leaves
        // the entry listed until end-of-frame compaction; a Register in that
        // window must flip the flag back instead of adding a second entry
        // (which would double-tick the target every frame).
        var target = new RecordingFrameTarget();
        var sub = new AnimationTickSubscription(target, weak: false);

        try
        {
            AnimationManager.Register(sub);
            AnimationManager.Unregister(sub);
            AnimationManager.Register(sub); // still listed: flag flip only

            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());

            Assert.Single(target.Timestamps);
        }
        finally
        {
            AnimationManager.Unregister(sub);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void Subscriber_ReturningFalse_IsAutoUnregistered()
    {
        var target = new RecordingFrameTarget { OnFrame = _ => false };
        var sub = new AnimationTickSubscription(target, weak: false);

        AnimationManager.Register(sub);

        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        Assert.Single(target.Timestamps);
        Assert.False(sub.IsActive);

        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        Assert.Single(target.Timestamps); // never ticked again
    }

    [Fact]
    public void Unregister_DuringTick_StopsFutureFrames()
    {
        RecordingFrameTarget? target = null;
        AnimationTickSubscription? sub = null;
        target = new RecordingFrameTarget
        {
            OnFrame = _ =>
            {
                AnimationManager.Unregister(sub!);
                return true; // even though it asked to stay, Unregister wins
            }
        };
        sub = new AnimationTickSubscription(target, weak: false);

        AnimationManager.Register(sub);

        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        Assert.Single(target.Timestamps);

        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        Assert.Single(target.Timestamps);
        Assert.False(sub.IsActive);
    }

    [Fact]
    public void Register_DuringTick_IsDeferredToTheNextFrame()
    {
        var late = new RecordingFrameTarget();
        var lateSub = new AnimationTickSubscription(late, weak: false);

        var registrar = new RecordingFrameTarget();
        registrar.OnFrame = _ =>
        {
            AnimationManager.Register(lateSub);
            return false;
        };
        var registrarSub = new AnimationTickSubscription(registrar, weak: false);

        try
        {
            AnimationManager.Register(registrarSub);

            long frame1 = Stopwatch.GetTimestamp();
            AnimationManager.ProcessFrame(frame1);
            Assert.Empty(late.Timestamps); // added mid-tick: not ticked this frame

            long frame2 = frame1 + Ticks(0.001);
            AnimationManager.ProcessFrame(frame2);
            Assert.Equal(new[] { frame2 }, late.Timestamps);
        }
        finally
        {
            AnimationManager.Unregister(lateSub);
            AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
        }
    }

    [Fact]
    public void CurrentFrameTimestampOrNow_InsideFrame_ReturnsTheFrameTimestamp()
    {
        long frame = Stopwatch.GetTimestamp() + Ticks(123);
        long observed = 0;

        RunInsideFrame(frame, _ => observed = AnimationManager.CurrentFrameTimestampOrNow);

        Assert.Equal(frame, observed);
    }

    // ── AnimationManager: weak subscriptions and GC ─────────────────────────

    private sealed class CountingFrameTarget : IFrameAnimatable
    {
        public static int LiveTicks;

        public bool OnAnimationFrame(long frameTimestamp)
        {
            LiveTicks++;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AnimationTickSubscription RegisterCollectibleWeakTarget()
    {
        var target = new CountingFrameTarget();
        var sub = new AnimationTickSubscription(target, weak: true);
        AnimationManager.Register(sub);
        return sub;
    }

    [Fact]
    public void WeakSubscription_DeadTarget_IsDroppedAndNeverTicked()
    {
        CountingFrameTarget.LiveTicks = 0;
        var sub = RegisterCollectibleWeakTarget();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());

        Assert.Equal(0, CountingFrameTarget.LiveTicks);
        Assert.False(sub.IsActive); // dead entry deactivated and compacted away
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<UIElement> CreateAnimatedElementWithNoOtherRoots()
    {
        var element = new ProbeElement();
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromHours(1)
        });
        return new WeakReference<UIElement>(element);
    }

    [Fact]
    public void WeakSubscription_DoesNotPinAnAnimatedElement()
    {
        // The manager must never become the GC root that keeps a forgotten
        // element's long-running animation alive (the pre-rewrite static
        // CompositionTarget.Rendering subscription did exactly that).
        var weak = CreateAnimatedElementWithNoOtherRoots();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.TryGetTarget(out _), "AnimationManager must not root an animated element");

        // Compact the dead entry out so it does not linger into later tests.
        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
    }

    // ── NotifyDetached: deferred, cancellable detach stop ───────────────────

    [Fact]
    public void NotifyDetached_StillDetachedAtNextFrame_StopsSubtreeAnimations()
    {
        var panel = new StackPanel();
        var child = new ProbeElement();
        panel.Children.Add(child);

        long t0 = Stopwatch.GetTimestamp();
        RunInsideFrame(t0, _ => child.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.2,
            To = 0.8,
            Duration = TimeSpan.FromSeconds(10)
        }));

        Assert.True(child.HasAnimation(UIElement.OpacityProperty));
        Assert.True(child.HasAnimatedValue(UIElement.OpacityProperty));

        panel.Children.Remove(child); // queues the deferred detach check

        AnimationManager.ProcessFrame(t0 + Ticks(0.016));

        Assert.False(child.HasAnimation(UIElement.OpacityProperty));
        Assert.False(child.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(1.0, (double)child.GetValue(UIElement.OpacityProperty)!); // hard discard, base back in effect
    }

    [Fact]
    public void NotifyDetached_ReattachedBeforeNextFrame_KeepsTheAnimationRunning()
    {
        // Popup/ComboBox move content between trees within one dispatcher
        // batch: detach immediately followed by re-attach must not kill the
        // in-flight animation.
        var panel = new StackPanel();
        var child = new ProbeElement();
        panel.Children.Add(child);

        long t0 = Stopwatch.GetTimestamp();
        RunInsideFrame(t0, _ => child.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(1)
        }));

        panel.Children.Remove(child);
        panel.Children.Add(child); // re-attached before the re-check frame

        AnimationManager.ProcessFrame(t0 + Ticks(0.5));

        Assert.True(child.HasAnimation(UIElement.OpacityProperty), "re-attach must cancel the pending detach stop");
        Assert.Equal(0.5, (double)child.GetValue(UIElement.OpacityProperty)!, 2);

        child.BeginAnimation(UIElement.OpacityProperty, null); // cleanup
        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
    }

    // ── UIElement integration: element-driven ticking ───────────────────────

    [Fact]
    public void BeginAnimation_ProcessFrame_AdvancesTheAnimatedValueDeterministically()
    {
        var element = new ProbeElement();

        long t0 = Stopwatch.GetTimestamp();
        RunInsideFrame(t0, _ => element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(1)
        }));

        AnimationManager.ProcessFrame(t0 + Ticks(0.25));
        Assert.Equal(0.25, (double)element.GetValue(UIElement.OpacityProperty)!, 3);

        AnimationManager.ProcessFrame(t0 + Ticks(0.75));
        Assert.Equal(0.75, (double)element.GetValue(UIElement.OpacityProperty)!, 3);

        element.BeginAnimation(UIElement.OpacityProperty, null); // cleanup
        AnimationManager.ProcessFrame(Stopwatch.GetTimestamp());
    }

    [Fact]
    public void FillBehaviorStop_NaturalCompletion_RestoresBaseAndUnregisters()
    {
        var element = new ProbeElement();
        element.SetValue(UIElement.OpacityProperty, 0.8);

        long t0 = Stopwatch.GetTimestamp();
        RunInsideFrame(t0, _ => element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 0.3,
            Duration = TimeSpan.FromSeconds(1),
            FillBehavior = FillBehavior.Stop
        }));

        AnimationManager.ProcessFrame(t0 + Ticks(2.0)); // past the end: completes

        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
        Assert.False(element.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(0.8, (double)element.GetValue(UIElement.OpacityProperty)!);
    }

    // ── DispatcherTimer.ShouldFireOnFrame (piggyback throttle) ──────────────

    [Fact]
    public void ShouldFireOnFrame_BeforeDueTime_SuppressesWithoutTouchingDeadline()
    {
        long nextDue = 1_000;
        Assert.False(DispatcherTimer.ShouldFireOnFrame(now: 999, intervalTicks: 100, ref nextDue));
        Assert.Equal(1_000, nextDue);
    }

    [Fact]
    public void ShouldFireOnFrame_AtDueTime_FiresAndReArmsFromNow()
    {
        long nextDue = 1_000;
        Assert.True(DispatcherTimer.ShouldFireOnFrame(now: 1_000, intervalTicks: 100, ref nextDue));
        Assert.Equal(1_100, nextDue);
    }

    [Fact]
    public void ShouldFireOnFrame_AfterAStall_DoesNotBuildUpABacklog()
    {
        // Re-arm is "now + interval", not "previous due + interval": a starved
        // frame loop fires once and moves on instead of bursting catch-up ticks.
        long nextDue = 1_000;
        Assert.True(DispatcherTimer.ShouldFireOnFrame(now: 5_000, intervalTicks: 100, ref nextDue));
        Assert.Equal(5_100, nextDue);
        Assert.False(DispatcherTimer.ShouldFireOnFrame(now: 5_050, intervalTicks: 100, ref nextDue));
    }

    [Fact]
    public void ShouldFireOnFrame_ZeroInterval_FiresEveryFrame()
    {
        long nextDue = 0;
        for (long now = 10; now < 15; now++)
        {
            Assert.True(DispatcherTimer.ShouldFireOnFrame(now, intervalTicks: 0, ref nextDue));
        }
    }
}
