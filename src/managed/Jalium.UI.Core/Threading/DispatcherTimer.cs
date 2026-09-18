using System.Diagnostics;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Threading;

/// <summary>
/// A timer that is integrated into the <see cref="Dispatcher"/> queue which is
/// processed at a specified interval of time and at a specified priority.
///
/// Optimization: when the interval matches the frame interval (8ms or 16ms),
/// the timer piggybacks on <see cref="CompositionTarget.Rendering"/> instead
/// of creating its own System.Threading.Timer. This eliminates timer
/// proliferation — all frame-rate timers share a single backing timer.
/// Piggybacked ticks are throttled to the nominal interval, so the uncapped
/// (1ms) frame loop cannot amplify e.g. a 16ms timer to full render rate.
/// </summary>
public sealed class DispatcherTimer
{
    private sealed class TimerGeneration
    {
        internal TimerGeneration(DispatcherTimer owner, long intervalTicks)
        {
            Owner = owner;
            IntervalTicks = intervalTicks;
        }

        internal DispatcherTimer Owner { get; }
        internal long IntervalTicks { get; }
        internal long NextDueTimestamp;
        internal int TickPending;
        internal Timer? Timer;
        internal DispatcherOperation? PendingOperation;
        internal EventHandler? RenderingHandler;
        internal bool RenderingEventAttached;
        internal bool CompositionSubscribed;

        internal void OnRendering(object? sender, EventArgs e)
        {
            Owner.OnCompositionTargetRendering(this, sender, e);
        }

        internal void TrackPendingOperation(DispatcherOperation operation)
        {
            operation.Aborted += OnPendingOperationFinished;
            operation.Completed += OnPendingOperationFinished;
        }

        internal void StopTrackingPendingOperation(DispatcherOperation operation)
        {
            operation.Aborted -= OnPendingOperationFinished;
            operation.Completed -= OnPendingOperationFinished;
        }

        internal void OnPendingOperationFinished(object? sender, EventArgs e)
        {
            if (sender is not DispatcherOperation operation)
            {
                return;
            }

            StopTrackingPendingOperation(operation);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref PendingOperation, null, operation),
                    operation))
            {
                // This operation never reached RaiseQueuedTick (synchronous abort),
                // or completed before its handle could be observed there. In either
                // case it still owns this generation's single in-flight tick slot.
                ExitTickGate(ref TickPending);
            }
        }
    }

    private readonly Dispatcher _dispatcher;
    private readonly object _lifecycleLock = new();
    private TimeSpan _interval;
    private bool _isEnabled;
    private object? _tag;
    private TimerGeneration? _activeGeneration;

    /// <summary>
    /// Occurs when the timer interval has elapsed.
    /// </summary>
    public event EventHandler? Tick;

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherTimer"/> class.
    /// </summary>
    public DispatcherTimer()
        : this(DispatcherPriority.Background)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherTimer"/> class
    /// which processes timer events at the specified priority.
    /// </summary>
    /// <param name="priority">The priority at which to invoke the timer.</param>
    public DispatcherTimer(DispatcherPriority priority)
        : this(priority, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherTimer"/> class
    /// which processes timer events at the specified priority on the specified dispatcher.
    /// </summary>
    /// <param name="priority">The priority at which to invoke the timer.</param>
    /// <param name="dispatcher">The dispatcher to associate with the timer.</param>
    public DispatcherTimer(DispatcherPriority priority, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Dispatcher.ValidatePriority(priority, nameof(priority));
        Priority = priority;
        _interval = TimeSpan.Zero;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DispatcherTimer"/> class
    /// which uses the specified time interval, priority, event handler, and dispatcher.
    /// </summary>
    /// <param name="interval">The period of time between ticks.</param>
    /// <param name="priority">The priority at which to invoke the timer.</param>
    /// <param name="callback">The event handler to call when the Tick event occurs.</param>
    /// <param name="dispatcher">The dispatcher to associate with the timer.</param>
    public DispatcherTimer(TimeSpan interval, DispatcherPriority priority, EventHandler callback, Dispatcher dispatcher)
        : this(priority, dispatcher)
    {
        Interval = interval;

        if (callback != null)
        {
            Tick += callback;
        }
    }

    /// <summary>
    /// Gets the <see cref="Dispatcher"/> associated with this <see cref="DispatcherTimer"/>.
    /// </summary>
    public Dispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// Gets or sets a value that indicates whether the timer is running.
    /// </summary>
    public bool IsEnabled
    {
        get => Volatile.Read(ref _isEnabled);
        set
        {
            TimerGeneration? generationToDispose = null;
            lock (_lifecycleLock)
            {
                if (_isEnabled == value)
                {
                    return;
                }

                Volatile.Write(ref _isEnabled, value);

                if (value)
                {
                    StartTimer();
                }
                else
                {
                    generationToDispose = DetachTimer();
                }
            }

            // DispatcherOperation.Abort synchronously invokes Aborted handlers and
            // dispatcher hooks. Dispose outside the lifecycle lock so those callbacks
            // may safely restart or reconfigure this timer.
            if (generationToDispose != null)
            {
                DisposeGeneration(generationToDispose);
            }
        }
    }

    /// <summary>
    /// Gets or sets the period of time between timer ticks.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is less than 0 or greater than <see cref="int.MaxValue"/> milliseconds.
    /// </exception>
    public TimeSpan Interval
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _interval;
            }
        }
        set
        {
            if (value.TotalMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval cannot be negative.");
            }

            if (value.TotalMilliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Interval is too large.");
            }

            TimerGeneration? generationToDispose = null;
            try
            {
                lock (_lifecycleLock)
                {
                    bool wasRunning = _isEnabled;

                    if (wasRunning)
                    {
                        generationToDispose = DetachTimer();
                    }

                    _interval = value;

                    if (wasRunning)
                    {
                        // Publish the replacement generation while still holding the
                        // lifecycle lock. Old callbacks already fail identity checks;
                        // their resources are released below, outside the lock.
                        StartTimer();
                    }
                }
            }
            finally
            {
                if (generationToDispose != null)
                {
                    DisposeGeneration(generationToDispose);
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the priority at which timer events are dispatched.
    /// </summary>
    public DispatcherPriority Priority { get; set; }

    /// <summary>
    /// Gets or sets a user-defined data object.
    /// </summary>
    public object? Tag
    {
        get => _tag;
        set => _tag = value;
    }

    /// <summary>
    /// Starts the <see cref="DispatcherTimer"/>.
    /// </summary>
    public void Start()
    {
        IsEnabled = true;
    }

    /// <summary>
    /// Stops the <see cref="DispatcherTimer"/>.
    /// </summary>
    public void Stop()
    {
        IsEnabled = false;
    }

    /// <summary>
    /// Determines if this timer's interval is short enough to piggyback on
    /// CompositionTarget.Rendering instead of creating a dedicated timer.
    /// Any interval at or below one display refresh period (e.g. 16ms at 60Hz)
    /// is merged into the centralized frame timer. This eliminates timer
    /// proliferation for frame-rate timers (animations, spring physics, etc.).
    /// </summary>
    private bool ShouldUseCompositionTarget()
    {
        int intervalMs = (int)_interval.TotalMilliseconds;
        int frameMs = 1000 / Math.Max(CompositionTarget.RefreshRate, 30);
        return intervalMs <= frameMs + 2;
    }

    private void StartTimer()
    {
        if (Volatile.Read(ref _activeGeneration) != null)
        {
            return;
        }

        long intervalTicks = IntervalToStopwatchTicks(_interval);
        var generation = new TimerGeneration(this, intervalTicks);
        Volatile.Write(ref _activeGeneration, generation);

        try
        {
            if (ShouldUseCompositionTarget())
            {
                // Piggyback on the centralized frame timer.
                // All frame-rate DispatcherTimers share a single System.Threading.Timer.
                // First tick is due one interval from now, matching the dedicated
                // timer's "first interval elapses before the first tick" semantics.
                generation.NextDueTimestamp = Stopwatch.GetTimestamp() + intervalTicks;

                // CompositionTarget snapshots its invocation list before calling it. A
                // handler removed by Stop can therefore still be invoked later in that
                // same snapshot, after this DispatcherTimer has already been restarted.
                // Bind one handler directly to the generation represented by that
                // subscription so the stale snapshot cannot borrow the new generation.
                EventHandler renderingHandler = generation.OnRendering;
                generation.RenderingHandler = renderingHandler;

                CompositionTarget.Rendering += renderingHandler;
                generation.RenderingEventAttached = true;
                CompositionTarget.Subscribe();
                generation.CompositionSubscribed = true;
                return;
            }

            // Non-frame-rate interval: use a dedicated timer (e.g., caret blink at 500ms)
            int intervalMs = Math.Max(1, (int)_interval.TotalMilliseconds);
            generation.Timer = new Timer(
                OnTimerCallback,
                generation,
                intervalMs,
                intervalMs);
        }
        catch
        {
            Interlocked.CompareExchange(ref _activeGeneration, null, generation);
            // IsEnabled was set before StartTimer. Restore the public state when the
            // backing timer/subscription could not be created so a later Start can retry.
            Volatile.Write(ref _isEnabled, false);
            DisposeGeneration(generation);
            throw;
        }
    }

    private TimerGeneration? DetachTimer()
    {
        TimerGeneration? generation = Interlocked.Exchange(ref _activeGeneration, null);
        // Invalidate under the lifecycle lock, then dispose outside it. Timer.Dispose
        // cannot recall a callback that already started and Rendering may already have
        // snapshotted its handlers, so every stale path still validates this identity.
        return generation;
    }

    private static void DisposeGeneration(TimerGeneration generation)
    {
        CancelPendingOperation(generation);

        if (generation.RenderingHandler is { } renderingHandler)
        {
            generation.RenderingHandler = null;
            if (generation.RenderingEventAttached)
            {
                generation.RenderingEventAttached = false;
                CompositionTarget.Rendering -= renderingHandler;
            }

            if (generation.CompositionSubscribed)
            {
                generation.CompositionSubscribed = false;
                CompositionTarget.Unsubscribe();
            }
        }

        Timer? timer = generation.Timer;
        generation.Timer = null;
        timer?.Dispose();
    }

    /// <summary>
    /// Called by CompositionTarget.Rendering on the UI thread.
    /// Already on UI thread — raise tick directly, throttled to the nominal
    /// interval (the frame loop is uncapped and can run far above 60Hz).
    /// </summary>
    private void OnCompositionTargetRendering(
        TimerGeneration generation,
        object? sender,
        EventArgs e)
    {
        if (!ReferenceEquals(generation.Owner, this) ||
            !IsCurrentGeneration(generation))
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (!ShouldFireOnFrame(now, generation.IntervalTicks, ref generation.NextDueTimestamp)) return;

        RaiseTick(generation);
    }

    /// <summary>
    /// Piggyback throttle: fires only once <paramref name="now"/> reaches the
    /// due timestamp, then re-arms one interval from NOW rather than from the
    /// previous due time, so a starved frame loop never builds up a backlog of
    /// catch-up ticks. A zero/near-frame interval (≤ the ≥1ms frame period) is
    /// due on every frame, leaving FrameInterval-based timers unaffected.
    /// </summary>
    internal static bool ShouldFireOnFrame(long now, long intervalTicks, ref long nextDueTimestamp)
    {
        if (now < nextDueTimestamp)
        {
            return false;
        }

        nextDueTimestamp = now + intervalTicks;
        return true;
    }

    private static long IntervalToStopwatchTicks(TimeSpan interval)
        => (long)(interval.TotalSeconds * Stopwatch.Frequency);

    private void OnTimerCallback(object? state)
    {
        if (state is not TimerGeneration generation ||
            !ReferenceEquals(generation.Owner, this) ||
            !IsCurrentGeneration(generation))
        {
            return;
        }

        // Dispatch the tick event to the associated dispatcher's thread
        try
        {
            if (_dispatcher.CheckAccess())
            {
                // Already on the dispatcher thread
                RaiseTick(generation);
                return;
            }

            // At most one tick may be in flight. The backing System.Threading.Timer is
            // periodic and keeps firing on a thread-pool thread whether or not the UI
            // thread has processed anything; queueing every callback lets a starved
            // dispatcher (startup, a long layout pass, a blocking operation) accumulate
            // a backlog that then drains as a burst of ticks inside a single frame.
            //
            // That burst is observable, not theoretical: a staggered reveal driven by a
            // 55ms timer collapses into one frame, and periodic work (caret blink,
            // debounce, polling) fires several times back to back the moment the thread
            // frees up. WPF's DispatcherTimer never does this — it re-arms around the
            // dispatcher actually processing the tick, so a starved dispatcher yields
            // FEWER ticks rather than a catch-up burst.
            //
            // The piggyback path already guards this (see ShouldFireOnFrame, which re-arms
            // from "now" instead of the previous due time). This is the same rule for the
            // dedicated-timer path: drop callbacks that arrive while a tick is still
            // queued or still running, and let the next one be scheduled from a clean state.
            if (!TryEnterTickGate(ref generation.TickPending))
            {
                return;
            }

            try
            {
                DispatcherOperation operation =
                    _dispatcher.BeginInvoke(() => RaiseQueuedTick(generation));
                generation.TrackPendingOperation(operation);

                DispatcherOperation? existing = Interlocked.CompareExchange(
                    ref generation.PendingOperation,
                    operation,
                    null);
                if (existing != null)
                {
                    // The generation gate makes this unreachable in normal operation,
                    // but never leave an untracked dispatcher callback behind if its
                    // invariant is violated by a future change.
                    generation.StopTrackingPendingOperation(operation);
                    TryAbortOperation(operation);
                    ExitTickGate(ref generation.TickPending);
                    return;
                }

                // Stop can invalidate the generation while BeginInvoke is adding the
                // operation (including re-entrantly from OperationPosted hooks). Publish
                // first, then re-check so whichever side loses can remove the real queue
                // node rather than leaving a no-op that retains this timer.
                if (!IsCurrentGeneration(generation))
                {
                    CancelPendingOperation(generation, operation);
                    return;
                }

                // A stopped dispatcher aborts synchronously and returns the operation.
                // Likewise, a very fast UI thread may have completed the callback before
                // BeginInvoke returns. The terminal event covers the normal race; this
                // post-publication check covers terminal state reached before handlers
                // were attached.
                if (operation.Task.IsCompleted)
                {
                    generation.OnPendingOperationFinished(operation, EventArgs.Empty);
                }
            }
            catch
            {
                // Never leave the gate latched shut when the tick could not be queued,
                // otherwise the timer goes permanently silent after one failed dispatch.
                ExitTickGate(ref generation.TickPending);
                throw;
            }
        }
        catch
        {
            // Ignore exceptions if the dispatcher is shutting down
        }
    }

    /// <summary>
    /// Runs a queued tick and reopens the gate only after the handler returns, so a
    /// long-running handler cannot have further ticks pile up behind it either.
    /// </summary>
    private void RaiseQueuedTick(TimerGeneration generation)
    {
        DispatcherOperation? operation = Interlocked.Exchange(
            ref generation.PendingOperation,
            null);
        if (operation != null)
        {
            generation.StopTrackingPendingOperation(operation);
        }

        try
        {
            RaiseTick(generation);
        }
        finally
        {
            // When the operation was already published, this UI callback owns the
            // generation gate. If it ran before BeginInvoke returned, publication and
            // the Completed/Aborted callback release the gate instead; keeping it closed
            // until then prevents a second timer callback from overtaking publication.
            if (operation != null)
            {
                ExitTickGate(ref generation.TickPending);
            }
        }
    }

    private static void CancelPendingOperation(TimerGeneration generation)
    {
        DispatcherOperation? operation = Interlocked.Exchange(
            ref generation.PendingOperation,
            null);
        if (operation != null)
        {
            generation.StopTrackingPendingOperation(operation);
            TryAbortOperation(operation);
        }

        // Stop invalidated the whole generation. Releasing its private gate is safe
        // even when the operation was between BeginInvoke and publication; that path
        // re-checks generation identity and aborts its operation after publishing it.
        ExitTickGate(ref generation.TickPending);
    }

    private static void CancelPendingOperation(
        TimerGeneration generation,
        DispatcherOperation operation)
    {
        if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref generation.PendingOperation,
                    null,
                    operation),
                operation))
        {
            return;
        }

        generation.StopTrackingPendingOperation(operation);
        TryAbortOperation(operation);
        ExitTickGate(ref generation.TickPending);
    }

    private static void TryAbortOperation(DispatcherOperation operation)
    {
        try
        {
            operation.Abort();
        }
        catch
        {
            // Abort removes the queue node before invoking diagnostic hooks. A hook
            // failure must not resurrect stale timer work or break Stop/Interval set.
        }
    }

    /// <summary>
    /// Claims the single in-flight tick slot. Returns <see langword="false"/> when a tick
    /// is already queued or running, in which case this timer callback must be dropped
    /// rather than queued behind it — dropping keeps the cadence, queueing creates a burst.
    /// </summary>
    internal static bool TryEnterTickGate(ref int tickPending)
        => Interlocked.CompareExchange(ref tickPending, 1, 0) == 0;

    /// <summary>
    /// Releases the in-flight tick slot. Must run even when the tick handler threw,
    /// otherwise the timer goes permanently silent.
    /// </summary>
    internal static void ExitTickGate(ref int tickPending)
        => Volatile.Write(ref tickPending, 0);

    private bool IsCurrentGeneration(TimerGeneration generation)
        => Volatile.Read(ref _isEnabled) &&
           ReferenceEquals(Volatile.Read(ref _activeGeneration), generation);

    private void RaiseTick(TimerGeneration generation)
    {
        if (!IsCurrentGeneration(generation))
        {
            return;
        }

        try
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Exception silently handled to keep timer running
        }
    }
}
