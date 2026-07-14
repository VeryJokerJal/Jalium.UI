using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media;

namespace Jalium.UI.Animation;

/// <summary>
/// A per-frame animation driver registered with <see cref="AnimationManager"/>.
/// </summary>
internal interface IFrameAnimatable
{
    /// <summary>
    /// Advances the target by one frame.
    /// </summary>
    /// <param name="frameTimestamp">The unified frame timestamp in
    /// <see cref="Stopwatch"/> ticks, sampled once per frame and shared by every
    /// subscriber ticked in that frame.</param>
    /// <returns><see langword="false"/> when the target has no further work; the
    /// manager then unregisters the subscription automatically.</returns>
    bool OnAnimationFrame(long frameTimestamp);
}

/// <summary>
/// A reusable registration handle connecting one <see cref="IFrameAnimatable"/>
/// to the <see cref="AnimationManager"/>. Holders construct it once and reuse it
/// across Register/Unregister cycles. The target is referenced strongly
/// (fire-and-forget drivers such as Storyboard, which must keep running without
/// any other root) or weakly (UIElement: the manager must never become the GC
/// root that keeps a forgotten element's Forever animation alive).
/// </summary>
internal sealed class AnimationTickSubscription
{
    private readonly IFrameAnimatable? _strong;
    private readonly WeakReference<IFrameAnimatable>? _weak;

    /// <summary>
    /// Whether the subscription should receive frames. Owned by
    /// <see cref="AnimationManager"/>; flipped rather than removed so
    /// unregistering during a tick is safe.
    /// </summary>
    internal bool IsActive;

    /// <summary>
    /// Whether the subscription currently sits in one of the manager's lists.
    /// Lets a Register after a same-frame Unregister flip <see cref="IsActive"/>
    /// back instead of adding a duplicate entry (which would double-tick).
    /// </summary>
    internal bool InList;

    internal AnimationTickSubscription(IFrameAnimatable target, bool weak)
    {
        if (weak)
            _weak = new WeakReference<IFrameAnimatable>(target);
        else
            _strong = target;
    }

    internal bool TryGetTarget([MaybeNullWhen(false)] out IFrameAnimatable target)
    {
        if (_strong != null)
        {
            target = _strong;
            return true;
        }

        return _weak!.TryGetTarget(out target);
    }
}

/// <summary>
/// Central animation scheduler: the single CompositionTarget.Rendering
/// subscriber that drives every registered <see cref="IFrameAnimatable"/> with
/// one unified per-frame timestamp (Stopwatch ticks). Replaces the per-instance
/// Rendering subscriptions previously created by UIElement, Storyboard and
/// DevTools timers.
///
/// Threading invariant: subscription and frame processing are UI-thread only.
/// Deferred detach notifications can arrive from distinct dispatcher threads,
/// so that queue is protected independently by <c>_pendingDetachGate</c>.
/// </summary>
internal static class AnimationManager
{
    private static readonly List<AnimationTickSubscription> _subscriptions = new(64);
    private static readonly List<AnimationTickSubscription> _pendingAdds = new(16);

    // Deferred detach checks (NotifyDetached). Weak references so the pending
    // list never pins an element that was dropped for good between two frames.
    // Not readonly: ProcessPendingDetachChecks swaps the two lists.
    private static List<WeakReference<UIElement>> _pendingDetachChecks = new(16);
    private static List<WeakReference<UIElement>> _detachCheckScratch = new(16);
    private static readonly object _pendingDetachGate = new();

    private static bool _ticking;
    private static bool _draining;
    private static bool _subscribedToRendering;
    private static long _frameTimestamp;

    // End-of-frame callbacks (storyboard Completed events). A storyboard clock that completes
    // naturally raises Completed from inside AnimationClock.Tick — i.e. before the element that
    // owns the clock writes this frame's final/FillBehavior value (OnAnimationFrame ticks, THEN
    // writes). Deferring the Completed raise to the end of the frame lets a handler observe the
    // settled value and start follow-up animations off it, matching WPF (Completed fires after the
    // timing tick computes the frame). Drained in ProcessFrame's finally.
    private static readonly Queue<Action> _postFrameCallbacks = new();

    /// <summary>
    /// The unified timestamp (Stopwatch ticks) of the frame currently being
    /// dispatched, or 0 outside a frame.
    /// </summary>
    internal static long CurrentFrameTimestamp => _frameTimestamp;

    /// <summary>
    /// The single time source for animation start/pause/resume/seek points:
    /// the unified frame timestamp while inside a frame (so every animation
    /// started in the same frame shares the same t0), or
    /// <see cref="Stopwatch.GetTimestamp"/> between frames.
    /// </summary>
    internal static long CurrentFrameTimestampOrNow
        => _frameTimestamp != 0 ? _frameTimestamp : Stopwatch.GetTimestamp();

    /// <summary>
    /// Number of tracked entries (active or awaiting compaction). Test observability.
    /// </summary>
    internal static int TrackedSubscriptionCount => _subscriptions.Count + _pendingAdds.Count;

    /// <summary>
    /// Whether the manager currently holds its CompositionTarget subscription.
    /// Test observability.
    /// </summary>
    internal static bool IsSubscribedToRendering => _subscribedToRendering;

    /// <summary>
    /// Queues <paramref name="callback"/> to run at the end of the current animation frame, after
    /// every subscriber's value writes for the frame have landed. Outside a frame it runs
    /// immediately. Used by Storyboard so its Completed event is raised only once the final
    /// animated values are in place, rather than mid-tick before the owning element wrote them.
    /// </summary>
    internal static void QueuePostFrame(Action callback)
    {
        if (_ticking)
            _postFrameCallbacks.Enqueue(callback);
        else
            callback();
    }

    internal static void Register(AnimationTickSubscription subscription)
    {
        if (subscription.IsActive)
            return;

        subscription.IsActive = true;

        // Same-frame Unregister→Register: the entry is still listed awaiting
        // compaction — flipping IsActive back is enough. Adding again would
        // create a duplicate entry and double-tick the target.
        if (!subscription.InList)
        {
            subscription.InList = true;
            (_ticking ? _pendingAdds : _subscriptions).Add(subscription);
        }

        EnsureRenderingSubscription();
    }

    /// <summary>
    /// Deactivates a subscription. The entry itself is removed by end-of-frame
    /// compaction, which makes unregistering from inside a tick safe.
    /// </summary>
    internal static void Unregister(AnimationTickSubscription subscription)
    {
        subscription.IsActive = false;
    }

    /// <summary>
    /// Records an element whose visual parent just became null. The next frame
    /// re-checks: still detached → the subtree's animations are stopped via
    /// <see cref="UIElement.StopAnimationsForRecycleRecursive"/>; re-attached in
    /// the meantime (Popup/ComboBox moving content between trees within one
    /// dispatcher batch) → the pending check is dropped and nothing stops.
    /// </summary>
    internal static void NotifyDetached(UIElement element)
    {
        lock (_pendingDetachGate)
        {
            _pendingDetachChecks.Add(new WeakReference<UIElement>(element));
        }

        // The re-check must run even if the detached subtree holds the only
        // active animations (or none at all): keep a frame source alive until
        // the pending list drains.
        EnsureRenderingSubscription();
    }

    /// <summary>
    /// Drops any pending detach check for <paramref name="element"/>. Called
    /// when its animations were already stopped deterministically (container
    /// recycling), making the deferred re-check redundant.
    /// </summary>
    internal static void CancelPendingDetach(UIElement element)
    {
        lock (_pendingDetachGate)
        {
            var list = _pendingDetachChecks;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].TryGetTarget(out var pending) && ReferenceEquals(pending, element))
                {
                    list[i] = list[^1];
                    list.RemoveAt(list.Count - 1);
                }
            }
        }
    }

    /// <summary>
    /// Dispatches one animation frame. Invoked by CompositionTarget.Rendering;
    /// also the direct entry point for headless tests (no real frame loop).
    /// </summary>
    internal static void ProcessFrame(long frameTimestamp)
    {
        // A nested frame (a subscriber pumping the dispatcher, or a deferred Completed handler
        // that pumps during the end-of-frame drain) would corrupt the in-place iteration and
        // compaction; skip it — the outer frame is authoritative and the nested work runs next
        // frame.
        if (_ticking || _draining)
            return;

        _frameTimestamp = frameTimestamp;
        _ticking = true;
        try
        {
            ProcessPendingDetachChecks();

            // _subscriptions is stable during the loop: Register lands in
            // _pendingAdds while _ticking and Unregister only flips a flag.
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                var subscription = _subscriptions[i];
                if (!subscription.IsActive)
                    continue;

                bool keep;
                try
                {
                    keep = subscription.TryGetTarget(out var target)
                        && target.OnAnimationFrame(frameTimestamp);
                }
                catch
                {
                    // Same isolation level as the previous per-element Rendering
                    // handlers (CompositionTarget catches per handler): one failing
                    // subscriber must not break the frame for the others, and it
                    // stays registered exactly like a throwing handler did.
                    keep = true;
                }

                if (!keep)
                    subscription.IsActive = false;
            }
        }
        finally
        {
            _ticking = false;

            // Drain end-of-frame callbacks (storyboard Completed) while _frameTimestamp still holds
            // this frame's value, so a follow-up animation a deferred handler starts shares the same
            // t0 as the rest of the frame instead of sampling real-now (CurrentFrameTimestampOrNow).
            // _draining stands in for _ticking here: a handler that pumps the dispatcher during the
            // drain must not run a nested frame (the guard at the top of ProcessFrame honors it).
            _draining = true;
            try
            {
                DrainPostFrameCallbacks();
            }
            finally
            {
                _draining = false;
                _frameTimestamp = 0;
            }

            Compact();
            TeardownIfEmpty();
        }
    }

    /// <summary>
    /// Runs the callbacks queued via <see cref="QueuePostFrame"/> during the frame that just
    /// finished ticking. <c>_ticking</c> is already false, so any completion a callback triggers
    /// synchronously runs immediately instead of re-queuing. A throwing callback (a user Completed
    /// handler) is isolated exactly like a throwing per-frame subscriber so it cannot skip the rest
    /// of the queue or the frame teardown that follows.
    /// </summary>
    private static void DrainPostFrameCallbacks()
    {
        while (_postFrameCallbacks.Count > 0)
        {
            var callback = _postFrameCallbacks.Dequeue();
            try
            {
                callback();
            }
            catch
            {
            }
        }
    }

    private static void ProcessPendingDetachChecks()
    {
        lock (_pendingDetachGate)
        {
            if (_pendingDetachChecks.Count == 0)
                return;

            // Swap-and-iterate: stopping a subtree runs user code (OnPropertyChanged,
            // storyboard bookkeeping) that may detach further elements. Those new
            // NotifyDetached entries must land in a list we are not iterating.
            (_pendingDetachChecks, _detachCheckScratch) = (_detachCheckScratch, _pendingDetachChecks);
            var due = _detachCheckScratch;

            for (int i = 0; i < due.Count; i++)
            {
                if (!due[i].TryGetTarget(out var element))
                    continue; // collected: nothing left to stop

                if (element.VisualParent != null)
                    continue; // re-attached before the re-check: cancelled

                element.StopAnimationsForRecycleRecursive();
            }

            due.Clear();
        }
    }

    /// <summary>
    /// End-of-frame compaction: swap-removes deactivated entries and merges the
    /// adds deferred during the tick. Backward iteration keeps the swap safe:
    /// every entry above the current index has already been visited, and only
    /// active entries survive above it.
    /// </summary>
    private static void Compact()
    {
        for (int i = _subscriptions.Count - 1; i >= 0; i--)
        {
            var subscription = _subscriptions[i];
            if (!subscription.IsActive)
            {
                subscription.InList = false;
                int last = _subscriptions.Count - 1;
                _subscriptions[i] = _subscriptions[last];
                _subscriptions.RemoveAt(last);
            }
        }

        for (int i = 0; i < _pendingAdds.Count; i++)
        {
            var subscription = _pendingAdds[i];
            if (subscription.IsActive)
                _subscriptions.Add(subscription);
            else
                subscription.InList = false;
        }

        _pendingAdds.Clear();
    }

    private static void EnsureRenderingSubscription()
    {
        if (_subscribedToRendering)
            return;

        _subscribedToRendering = true;
        CompositionTarget.Rendering += OnRendering;
        CompositionTarget.Subscribe();
    }

    private static void TeardownIfEmpty()
    {
        if (!_subscribedToRendering)
            return;

        if (_subscriptions.Count > 0 || _pendingAdds.Count > 0 || _pendingDetachChecks.Count > 0)
            return;

        _subscribedToRendering = false;
        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Unsubscribe();
    }

    private static void OnRendering(object? sender, EventArgs e)
        => ProcessFrame(Stopwatch.GetTimestamp());
}
