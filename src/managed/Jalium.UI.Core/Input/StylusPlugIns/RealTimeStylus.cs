using System.Collections.Concurrent;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
    private readonly object _workerGate = new();
    private RtsWorker? _worker;
    private int _disposeState;
    private static int s_activeThreadCount;

    /// <summary>
    /// Number of dedicated RTS worker threads that are currently alive. Kept
    /// internal so lifecycle tests and diagnostics can detect worker leaks
    /// without adding a Jalium-specific member to the public WPF surface.
    /// </summary>
    internal static int ActiveThreadCount => Volatile.Read(ref s_activeThreadCount);

    private bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

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
        NotifyBoundaryTransitions(rawStylusInput, previousTarget, path, enteredRange, exitedRange);
        var (rtsPlugIns, uiPlugIns) = PartitionPlugIns(path);

        if (UseRealTimeThread && rtsPlugIns.Count > 0 && !IsDisposed)
        {
            // Hand the RTS-capable plug-ins to the dedicated thread and block
            // until it completes. This keeps the public API synchronous (the
            // dispatcher relies on the returned result) while still giving
            // plug-ins thread isolation and avoiding any UI-thread state
            // dependency they'd otherwise create.
            using var completed = new ManualResetEventSlim(false);
            var work = new WorkItem(rawStylusInput, rtsPlugIns, completed);
            if (TryQueue(work))
            {
                completed.Wait();
            }
            else
            {
                // Disposal may have won the race before the work was queued.
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
        NotifyBoundaryTransitions(rawStylusInput, previousTarget, path, enteredRange, exitedRange);
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

        if (rtsPlugIns.Count == 0 || !UseRealTimeThread || IsDisposed)
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
        if (!TryQueue(work))
        {
            // Disposal may have completed the queue before we could enqueue.
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
        foreach (var callback in callbacks)
        {
            var pendingCallback = callback;
            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    bool targetVerified = IsTargetWithinElement(
                        processResult.RawStylusInput.Target,
                        pendingCallback.PlugIn.Element);
                    pendingCallback.PlugIn.InvokeProcessed(
                        pendingCallback.Action,
                        pendingCallback.CallbackData,
                        targetVerified);
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
    /// Synchronously requests that the RTS thread stop and waits briefly for
    /// queued work to drain. Idempotent.
    /// After <see cref="Dispose"/> further <see cref="Process"/> calls run all
    /// plug-ins on the calling thread (graceful degradation).
    /// </summary>
    public void Dispose()
    {
        DisposeCore(waitForWorker: true);
        GC.SuppressFinalize(this);
    }

    ~RealTimeStylus()
    {
        // The worker deliberately does not reference its RealTimeStylus owner,
        // so an abandoned Window/InkCanvas pipeline remains collectible. Never
        // block the finalizer thread; CompleteAdding is enough for the worker to
        // drain and terminate in the background.
        DisposeCore(waitForWorker: false);
    }

    private void DisposeCore(bool waitForWorker)
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        RtsWorker? worker;
        lock (_workerGate)
        {
            worker = _worker;
            _worker = null;
        }

        worker?.Stop(waitForWorker);

        lock (_sessionsGate)
        {
            _sessions.Clear();
        }
    }

    private bool TryQueue(WorkItem work)
    {
        RtsWorker? worker;
        lock (_workerGate)
        {
            if (IsDisposed)
                return false;

            worker = _worker ??= new RtsWorker();
        }

        return worker.TryAdd(work);
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

    private void NotifyBoundaryTransitions(
        RawStylusInput rawStylusInput,
        UIElement? previousTarget,
        List<UIElement> currentPath,
        bool enteredRange,
        bool exitedRange)
    {
        List<UIElement> previousPath = previousTarget is null
            ? []
            : BuildPathFromRootToTarget(previousTarget);

        int commonPrefixLength = 0;
        if (!enteredRange && !exitedRange)
        {
            int commonCount = Math.Min(previousPath.Count, currentPath.Count);
            while (commonPrefixLength < commonCount &&
                   ReferenceEquals(previousPath[commonPrefixLength], currentPath[commonPrefixLength]))
            {
                commonPrefixLength++;
            }
        }

        // Leave the old branch from the former target back toward the common
        // ancestor. A plug-in on a shared ancestor remains active and must not
        // receive a synthetic leave/enter pair when only a descendant changes.
        for (int index = previousPath.Count - 1; index >= commonPrefixLength; index--)
        {
            StylusPlugInCollection? plugIns = previousPath[index].GetStylusPlugIns(createIfMissing: false);
            if (plugIns is null)
                continue;
            foreach (StylusPlugIn plugIn in plugIns.Snapshot())
            {
                if (plugIn.IsActiveForInput)
                    plugIn.InvokeStylusLeave(rawStylusInput, confirmed: true);
            }
        }

        if (exitedRange || !rawStylusInput.InRange)
            return;

        // Enter the new branch from the common ancestor toward the target.
        // On initial range entry the common prefix is intentionally zero, so
        // every active plug-in in the path receives its enter notification.
        for (int index = commonPrefixLength; index < currentPath.Count; index++)
        {
            StylusPlugInCollection? plugIns = currentPath[index].GetStylusPlugIns(createIfMissing: false);
            if (plugIns is null)
                continue;
            foreach (StylusPlugIn plugIn in plugIns.Snapshot())
            {
                if (plugIn.IsActiveForInput)
                    plugIn.InvokeStylusEnter(rawStylusInput, confirmed: true);
            }
        }
    }

    private static bool IsTargetWithinElement(UIElement target, UIElement? element)
    {
        if (element is null)
            return false;
        for (Visual? current = target; current is not null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, element))
                return true;
        }
        return false;
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

    /// <summary>
    /// Owns the queue and thread without retaining the parent
    /// <see cref="RealTimeStylus"/>. This separation is essential: an instance
    /// ThreadStart delegate would root the entire input host forever when a
    /// caller forgot to dispose it, preventing even finalization from running.
    /// </summary>
    private sealed class RtsWorker
    {
        private readonly BlockingCollection<WorkItem> _queue =
            new(new ConcurrentQueue<WorkItem>());
        private readonly Thread _thread;
        private int _stopState;

        public RtsWorker()
        {
            _thread = new Thread(static state => ((RtsWorker)state!).Run())
            {
                Name = "Jalium.RTS",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };

            Interlocked.Increment(ref s_activeThreadCount);
            try
            {
                _thread.Start(this);
            }
            catch
            {
                Interlocked.Decrement(ref s_activeThreadCount);
                _queue.Dispose();
                throw;
            }
        }

        public bool TryAdd(WorkItem work)
        {
            if (Volatile.Read(ref _stopState) != 0)
                return false;

            try
            {
                _queue.Add(work);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void Stop(bool waitForWorker)
        {
            if (Interlocked.Exchange(ref _stopState, 1) == 0)
            {
                try { _queue.CompleteAdding(); }
                catch (ObjectDisposedException) { }
            }

            if (!waitForWorker || ReferenceEquals(Thread.CurrentThread, _thread))
                return;

            try
            {
                _thread.Join(TimeSpan.FromMilliseconds(500));
            }
            catch (ThreadStateException)
            {
                // A failed Thread.Start is rethrown by the constructor, so this
                // can only be a defensive race during process teardown.
            }
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    // Clear the previous item before blocking for the next one.
                    // A raw input packet retains its target; keeping the last
                    // packet in a worker-local would otherwise recreate the
                    // Window -> RealTimeStylus ownership cycle we intentionally
                    // broke by separating this worker from its owner.
                    WorkItem? item = null;
                    if (!_queue.TryTake(out item, Timeout.Infinite))
                        break;

                    try
                    {
                        ProcessWorkItem(item);
                    }
                    finally
                    {
                        // BlockingCollection/Thread stack implementations may
                        // retain the last dequeued WorkItem while waiting. Make
                        // the item harmless even in that case by severing all
                        // packet, target and continuation references explicitly.
                        item.ReleaseReferences();
                    }

                    item = null;
                }
            }
            catch (ObjectDisposedException)
            {
                // The queue is disposed by this worker's finally block; this is
                // only a defensive guard for process-shutdown races.
            }
            finally
            {
                _queue.Dispose();
                Interlocked.Decrement(ref s_activeThreadCount);
            }
        }

        private static void ProcessWorkItem(WorkItem item)
        {
            RawStylusInput rawStylusInput = item.RawStylusInput!;
            try
            {
                ExecutePlugInList(rawStylusInput, item.PlugIns!);
            }
            catch
            {
                rawStylusInput.Cancel();
            }
            finally
            {
                if (item.CompletionSignal != null)
                {
                    try { item.CompletionSignal.Set(); }
                    catch (ObjectDisposedException) { }
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
        public RawStylusInput? RawStylusInput { get; private set; }
        public List<StylusPlugIn>? PlugIns { get; private set; }
        public ManualResetEventSlim? CompletionSignal { get; private set; }
        public Dispatcher? UiDispatcher { get; private set; }
        public Action? UiContinuation { get; private set; }

        public void ReleaseReferences()
        {
            RawStylusInput = null;
            PlugIns = null;
            CompletionSignal = null;
            UiDispatcher = null;
            UiContinuation = null;
        }
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
