using System.Collections.Concurrent;

namespace Jalium.UI.Input.StylusPlugIns;

/// <summary>
/// Coordinates real-time stylus packet flow and plug-in execution.
///
/// Two execution stages:
///   <list type="bullet">
///     <item><b>Input stage</b> — runs <c>OnStylusXxx</c> hooks.
///       Plug-ins whose <see cref="StylusPlugIn.IsRealTimeCapable"/> is
///       <see langword="true"/> run on the dedicated RTS background thread
///       so packet latency does not depend on UI-thread liveness; others
///       run on the calling (UI) thread.</item>
///     <item><b>Processed stage</b> — runs <c>OnStylusXxxProcessed</c>
///       hooks for plug-ins that called
///       <see cref="RawStylusInput.NotifyWhenProcessed"/>. Always marshalled
///       to the UI <see cref="Dispatcher"/> via
///       <see cref="QueueProcessedCallbacks"/>.</item>
///   </list>
/// </summary>
public sealed class RealTimeStylus : IDisposable
{
    private readonly UIElement _root;
    private readonly Dictionary<uint, StylusSession> _sessions = [];
    private readonly object _sessionsGate = new();
    private readonly BlockingCollection<WorkItem> _rtsQueue = new(new ConcurrentQueue<WorkItem>());
    private readonly Thread _rtsThread;
    private bool _disposed;

    /// <summary>
    /// Enables routing of <see cref="StylusPlugIn.IsRealTimeCapable"/>
    /// plug-ins onto the dedicated background thread. Default: enabled.
    /// Setting to <see langword="false"/> falls back to UI-thread execution
    /// for all plug-ins (matches the legacy synchronous behaviour and is
    /// useful in unit tests that need deterministic ordering).
    /// </summary>
    public bool UseRealTimeThread { get; set; } = true;

    public RealTimeStylus(UIElement root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _rtsThread = new Thread(RtsThreadLoop)
        {
            Name = "Jalium.RTS",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _rtsThread.Start();
    }

    public UIElement RootElement => _root;

    public RealTimeStylusProcessResult Process(
        uint pointerId,
        UIElement target,
        StylusInputAction action,
        StylusPointCollection stylusPoints,
        int timestamp,
        bool inAir,
        bool inRange,
        bool barrelButtonPressed,
        bool eraserPressed,
        bool inverted,
        bool pointerCanceled)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stylusPoints);

        StylusSession session;
        UIElement? previousTarget;
        bool enteredRange, exitedRange, targetChanged, enteredElement, leftElement, barrelButtonDown, barrelButtonUp;
        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(pointerId, out session!))
            {
                session = new StylusSession();
                _sessions[pointerId] = session;
            }

            previousTarget = session.Target;
            enteredRange = !session.InRange && inRange;
            exitedRange = session.InRange && !inRange;
            targetChanged = !ReferenceEquals(previousTarget, target);
            enteredElement = targetChanged;
            leftElement = targetChanged && previousTarget != null;
            barrelButtonDown = !session.BarrelButtonPressed && barrelButtonPressed;
            barrelButtonUp = session.BarrelButtonPressed && !barrelButtonPressed;
        }

        var rawStylusInput = new RawStylusInput(
            pointerId,
            target,
            action,
            stylusPoints,
            timestamp,
            inAir,
            inRange,
            inverted,
            barrelButtonPressed,
            eraserPressed);

        // Build the plug-in path once on the UI thread (visual tree traversal must
        // not race with layout). Split into RTS and UI buckets up front so the
        // background thread can run without touching the visual tree.
        var path = BuildPathFromRootToTarget(target);
        var (rtsPlugIns, uiPlugIns) = PartitionPlugIns(path);

        if (UseRealTimeThread && rtsPlugIns.Count > 0 && !_disposed)
        {
            // Hand the RTS-capable plug-ins to the dedicated thread and block
            // until it completes. This keeps the public API synchronous (the
            // dispatcher relies on the returned result) while still giving
            // plug-ins thread isolation and avoiding any UI-thread state
            // dependency they'd otherwise create.
            using var completed = new ManualResetEventSlim(false);
            var work = new WorkItem(rawStylusInput, rtsPlugIns, completed);
            try
            {
                _rtsQueue.Add(work);
                completed.Wait();
            }
            catch (InvalidOperationException)
            {
                // Queue was completed (disposed) between the check and Add.
                ExecutePlugInList(rawStylusInput, rtsPlugIns);
            }
        }
        else if (rtsPlugIns.Count > 0)
        {
            ExecutePlugInList(rawStylusInput, rtsPlugIns);
        }

        if (!rawStylusInput.IsCanceled)
        {
            ExecutePlugInList(rawStylusInput, uiPlugIns);
        }

        if (pointerCanceled)
        {
            rawStylusInput.Cancel();
        }

        bool sessionEnded = rawStylusInput.IsCanceled || action == StylusInputAction.Up || !inRange;
        lock (_sessionsGate)
        {
            if (sessionEnded)
            {
                _sessions.Remove(pointerId);
            }
            else
            {
                session.Target = target;
                session.InRange = inRange;
                session.InAir = inAir;
                session.BarrelButtonPressed = barrelButtonPressed;
                session.Inverted = inverted;
                session.EraserPressed = eraserPressed;
            }
        }

        return new RealTimeStylusProcessResult(
            rawStylusInput,
            previousTarget,
            enteredRange,
            exitedRange,
            enteredElement,
            leftElement,
            barrelButtonDown,
            barrelButtonUp,
            sessionEnded);
    }

    /// <summary>
    /// Truly non-blocking variant of <see cref="Process"/>: returns to the UI
    /// thread immediately and runs RTS-capable plug-ins on the background
    /// thread. When the input chain completes, <paramref name="onCompleted"/>
    /// is marshalled back to the UI <see cref="Dispatcher"/> with the final
    /// <see cref="RealTimeStylusProcessResult"/>. UI-capable plug-ins on the
    /// same path also run in that UI continuation.
    /// </summary>
    public void BeginProcess(
        uint pointerId,
        UIElement target,
        StylusInputAction action,
        StylusPointCollection stylusPoints,
        int timestamp,
        bool inAir,
        bool inRange,
        bool barrelButtonPressed,
        bool eraserPressed,
        bool inverted,
        bool pointerCanceled,
        Action<RealTimeStylusProcessResult> onCompleted)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(stylusPoints);
        ArgumentNullException.ThrowIfNull(onCompleted);

        // Session bookkeeping mirrors Process(...) but must happen on the
        // calling (UI) thread because UIElement.GetStylusPlugIns walks the
        // visual tree.
        StylusSession session;
        UIElement? previousTarget;
        bool enteredRange, exitedRange, targetChanged, enteredElement, leftElement, barrelButtonDown, barrelButtonUp;
        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(pointerId, out session!))
            {
                session = new StylusSession();
                _sessions[pointerId] = session;
            }
            previousTarget = session.Target;
            enteredRange = !session.InRange && inRange;
            exitedRange = session.InRange && !inRange;
            targetChanged = !ReferenceEquals(previousTarget, target);
            enteredElement = targetChanged;
            leftElement = targetChanged && previousTarget != null;
            barrelButtonDown = !session.BarrelButtonPressed && barrelButtonPressed;
            barrelButtonUp = session.BarrelButtonPressed && !barrelButtonPressed;
        }

        var rawStylusInput = new RawStylusInput(
            pointerId, target, action, stylusPoints, timestamp,
            inAir, inRange, inverted, barrelButtonPressed, eraserPressed);

        var path = BuildPathFromRootToTarget(target);
        var (rtsPlugIns, uiPlugIns) = PartitionPlugIns(path);

        void RunUiContinuation()
        {
            // UI-stage plug-ins (and processed callbacks) on the dispatcher.
            if (!rawStylusInput.IsCanceled)
            {
                ExecutePlugInList(rawStylusInput, uiPlugIns);
            }

            if (pointerCanceled)
            {
                rawStylusInput.Cancel();
            }

            bool sessionEnded = rawStylusInput.IsCanceled || action == StylusInputAction.Up || !inRange;
            lock (_sessionsGate)
            {
                if (sessionEnded)
                {
                    _sessions.Remove(pointerId);
                }
                else
                {
                    session.Target = target;
                    session.InRange = inRange;
                    session.InAir = inAir;
                    session.BarrelButtonPressed = barrelButtonPressed;
                    session.Inverted = inverted;
                    session.EraserPressed = eraserPressed;
                }
            }

            var result = new RealTimeStylusProcessResult(
                rawStylusInput, previousTarget,
                enteredRange, exitedRange, enteredElement, leftElement,
                barrelButtonDown, barrelButtonUp, sessionEnded);
            try { onCompleted(result); }
            catch { /* never let continuation failures escape */ }
        }

        if (rtsPlugIns.Count == 0 || !UseRealTimeThread || _disposed)
        {
            // Fast path: no background work, run UI continuation synchronously.
            if (rtsPlugIns.Count > 0)
            {
                ExecutePlugInList(rawStylusInput, rtsPlugIns);
            }
            RunUiContinuation();
            return;
        }

        // RTS thread runs the real-time plug-ins, then schedules the UI
        // continuation through the dispatcher. UI thread returns immediately.
        var work = new WorkItem(rawStylusInput, rtsPlugIns, _root.Dispatcher, RunUiContinuation);
        try
        {
            _rtsQueue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // Queue completed before we could enqueue. Fall back to sync.
            ExecutePlugInList(rawStylusInput, rtsPlugIns);
            RunUiContinuation();
        }
    }

    public void QueueProcessedCallbacks(RealTimeStylusProcessResult processResult)
    {
        ArgumentNullException.ThrowIfNull(processResult);

        var callbacks = processResult.RawStylusInput.DrainProcessedCallbacks();
        if (callbacks.Count == 0)
        {
            return;
        }

        var dispatcher = _root.Dispatcher;
        foreach (var stylusPlugIn in callbacks)
        {
            var plugIn = stylusPlugIn;
            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    plugIn.InvokeProcessed(processResult.RawStylusInput);
                }
                catch
                {
                    // Processed-stage failures must not crash the input loop.
                }
            });
        }
    }

    public void CancelSession(uint pointerId)
    {
        lock (_sessionsGate)
        {
            _sessions.Remove(pointerId);
        }
    }

    /// <summary>
    /// Synchronously stops the RTS thread and releases the queue. Idempotent.
    /// After <see cref="Dispose"/> further <see cref="Process"/> calls run all
    /// plug-ins on the calling thread (graceful degradation).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _rtsQueue.CompleteAdding();
        }
        catch (ObjectDisposedException) { }
        try
        {
            _rtsThread.Join(TimeSpan.FromMilliseconds(500));
        }
        catch (ThreadStateException) { }
        _rtsQueue.Dispose();
    }

    private void RtsThreadLoop()
    {
        try
        {
            foreach (var item in _rtsQueue.GetConsumingEnumerable())
            {
                try
                {
                    ExecutePlugInList(item.RawStylusInput, item.PlugIns);
                }
                catch
                {
                    item.RawStylusInput.Cancel();
                }
                finally
                {
                    if (item.CompletionSignal != null)
                    {
                        try { item.CompletionSignal.Set(); } catch (ObjectDisposedException) { }
                    }
                    if (item.UiContinuation != null && item.UiDispatcher != null)
                    {
                        // Marshal UI-stage execution back to the UI thread.
                        try { item.UiDispatcher.BeginInvoke(item.UiContinuation); }
                        catch { /* dispatcher may be shutting down */ }
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Queue was disposed; exit cleanly.
        }
    }

    private static void ExecutePlugInList(RawStylusInput rawStylusInput, List<StylusPlugIn> plugIns)
    {
        for (int i = 0; i < plugIns.Count; i++)
        {
            var plugIn = plugIns[i];
            if (!plugIn.ShouldProcess(rawStylusInput))
            {
                continue;
            }

            try
            {
                plugIn.InvokeInput(rawStylusInput);
            }
            catch
            {
                rawStylusInput.Cancel();
                return;
            }

            if (rawStylusInput.IsCanceled)
            {
                return;
            }
        }
    }

    private static (List<StylusPlugIn> Rts, List<StylusPlugIn> Ui) PartitionPlugIns(List<UIElement> path)
    {
        var rts = new List<StylusPlugIn>();
        var ui = new List<StylusPlugIn>();
        for (int i = 0; i < path.Count; i++)
        {
            var plugIns = path[i].GetStylusPlugIns(createIfMissing: false);
            if (plugIns == null || plugIns.Count == 0)
            {
                continue;
            }
            foreach (var stylusPlugIn in plugIns.Snapshot())
            {
                if (stylusPlugIn.IsRealTimeCapable)
                    rts.Add(stylusPlugIn);
                else
                    ui.Add(stylusPlugIn);
            }
        }
        return (rts, ui);
    }

    private List<UIElement> BuildPathFromRootToTarget(UIElement target)
    {
        var path = new List<UIElement>(8);
        UIElement? current = target;
        while (current != null)
        {
            path.Add(current);
            if (ReferenceEquals(current, _root))
            {
                break;
            }

            current = current.VisualParent as UIElement;
        }

        path.Reverse();
        return path;
    }

    private sealed class StylusSession
    {
        public UIElement? Target { get; set; }
        public bool InRange { get; set; }
        public bool InAir { get; set; }
        public bool BarrelButtonPressed { get; set; }
        public bool Inverted { get; set; }
        public bool EraserPressed { get; set; }
    }

    private sealed class WorkItem
    {
        public WorkItem(RawStylusInput rawStylusInput, List<StylusPlugIn> plugIns, ManualResetEventSlim completionSignal)
        {
            RawStylusInput = rawStylusInput;
            PlugIns = plugIns;
            CompletionSignal = completionSignal;
        }
        // Fire-and-forget overload — completion handled via UiContinuation rather than a signal.
        public WorkItem(RawStylusInput rawStylusInput, List<StylusPlugIn> plugIns, Dispatcher uiDispatcher, Action uiContinuation)
        {
            RawStylusInput = rawStylusInput;
            PlugIns = plugIns;
            UiDispatcher = uiDispatcher;
            UiContinuation = uiContinuation;
        }
        public RawStylusInput RawStylusInput { get; }
        public List<StylusPlugIn> PlugIns { get; }
        public ManualResetEventSlim? CompletionSignal { get; }
        public Dispatcher? UiDispatcher { get; }
        public Action? UiContinuation { get; }
    }
}

/// <summary>
/// Result object produced by RealTimeStylus processing.
/// </summary>
public sealed class RealTimeStylusProcessResult
{
    internal RealTimeStylusProcessResult(
        RawStylusInput rawStylusInput,
        UIElement? previousTarget,
        bool enteredRange,
        bool exitedRange,
        bool enteredElement,
        bool leftElement,
        bool barrelButtonDown,
        bool barrelButtonUp,
        bool sessionEnded)
    {
        RawStylusInput = rawStylusInput;
        PreviousTarget = previousTarget;
        EnteredRange = enteredRange;
        ExitedRange = exitedRange;
        EnteredElement = enteredElement;
        LeftElement = leftElement;
        BarrelButtonDown = barrelButtonDown;
        BarrelButtonUp = barrelButtonUp;
        SessionEnded = sessionEnded;
    }

    public RawStylusInput RawStylusInput { get; }
    public UIElement? PreviousTarget { get; }
    public bool EnteredRange { get; }
    public bool ExitedRange { get; }
    public bool EnteredElement { get; }
    public bool LeftElement { get; }
    public bool BarrelButtonDown { get; }
    public bool BarrelButtonUp { get; }
    public bool SessionEnded { get; }
    public bool Canceled => RawStylusInput.IsCanceled;
}
