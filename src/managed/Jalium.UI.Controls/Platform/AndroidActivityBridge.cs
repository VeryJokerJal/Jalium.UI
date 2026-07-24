using System.Runtime.InteropServices;
using Jalium.UI.Input.TextInput;
using Jalium.UI.Interop;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.Platform;

// Force-load each backend .so and explicitly register it.
// Calling these P/Invokes causes the dynamic linker to load the library,
// which also fires __attribute__((constructor)), but we call jalium_*_init
// explicitly as a belt-and-suspenders measure in case the constructor ran
// before jalium_register_backend_ex was ready in the core library.
internal static partial class BackendPreloader
{
    private const string SoftwareLib = "jalium.native.software";
    private const string VulkanLib   = "jalium.native.vulkan";

    [LibraryImport(SoftwareLib, EntryPoint = "jalium_software_init")]
    internal static partial void SoftwareInit();

    [LibraryImport(VulkanLib, EntryPoint = "jalium_vulkan_init")]
    internal static partial void VulkanInit();
}

/// <summary>
/// Immutable Android pointer packet captured on the Activity thread. Event time
/// is Android's monotonic uptime in milliseconds (<c>MotionEvent.EventTime</c>).
/// </summary>
internal readonly record struct AndroidTouchInput(
    int PointerId,
    float X,
    float Y,
    float Pressure,
    int Action,
    int PointerType,
    int Modifiers,
    long EventTimeMillis);

/// <summary>
/// Ordered pointer queue with latest-value coalescing for MOVE packets. A
/// non-MOVE packet is an ordering barrier: MOVE nodes before it can no longer
/// be updated by packets that arrived after it.
/// </summary>
internal sealed class AndroidTouchInputQueue
{
    private const int MoveAction = 2;
    private readonly LinkedList<AndroidTouchInput> _events = new();
    private readonly Dictionary<int, LinkedListNode<AndroidTouchInput>> _latestMoves = new();

    internal int Count => _events.Count;

    internal void Enqueue(AndroidTouchInput input)
    {
        if (input.Action == MoveAction)
        {
            if (_latestMoves.TryGetValue(input.PointerId, out var existing))
            {
                existing.Value = input;
                return;
            }

            var node = _events.AddLast(input);
            _latestMoves.Add(input.PointerId, node);
            return;
        }

        // DOWN/UP/CANCEL packets are never coalesced. They also split MOVE
        // coalescing into ordered epochs so a later MOVE cannot jump across a
        // pointer-state transition belonging to this or another pointer.
        _latestMoves.Clear();
        _events.AddLast(input);
    }

    internal bool TryDequeue(out AndroidTouchInput input)
    {
        var node = _events.First;
        if (node == null)
        {
            input = default;
            return false;
        }

        _events.RemoveFirst();
        input = node.Value;
        if (input.Action == MoveAction &&
            _latestMoves.TryGetValue(input.PointerId, out var latest) &&
            ReferenceEquals(latest, node))
        {
            _latestMoves.Remove(input.PointerId);
        }

        return true;
    }

    internal void Clear()
    {
        _events.Clear();
        _latestMoves.Clear();
    }
}

/// <summary>
/// Bridges Android Activity lifecycle to Jalium.UI Application + Window model.
///
/// On Android, there is typically a single full-screen window backed by an
/// ANativeWindow. This bridge:
/// - Maps onNativeWindowCreated → Window resize + surface creation
/// - Maps onPause/onResume → suspend/resume rendering
/// - Maps onDestroy → Application.Shutdown
/// - Maps touch input → Jalium.UI pointer events
///
/// Usage: Call AndroidActivityBridge.Initialize() from the native activity's
/// onCreate callback before running the Jalium.UI Application.
/// </summary>
public static class AndroidActivityBridge
{
    private enum UiDispatchState
    {
        NotStarted,
        Running,
        Stopping,
        Stopped,
        Failed,
    }

    private enum StageSurfaceResult
    {
        Rejected,
        Staged,
        RetryRunning,
    }

    private static readonly object s_lifecycleGate = new();
    private static readonly object s_stagedTransferGate = new();
    private static readonly object s_touchInputGate = new();
    private static readonly ManualResetEventSlim s_stoppingDetachCompleted = new(initialState: true);
    private static readonly AndroidTouchInputQueue s_touchInputQueue = new();
    private static bool s_touchDrainScheduled;
    private static bool s_initialized;
    private static UiDispatchState s_uiDispatchState;
    private static bool s_stopTransactionCompleted;
    private static nint s_nativeWindow;
    private static long s_nextActivityGeneration;
    private static long s_activeActivityGeneration;
    private static long s_surfaceActivityGeneration;
    private static long s_surfaceDestroyPendingGeneration;
    // A UI thread may start only after its active Activity has staged a Surface.
    // Reservation is separate from eligibility so the Activity/thread gate can
    // prevent duplicate starts without consuming the Surface before MarkReady.
    private static long s_startEligibleActivityGeneration;
    private static long s_startReservedActivityGeneration;
    private static long s_uiThreadActivityGeneration;
    // While the old dispatcher is stopping, a replacement Activity may deliver
    // its only SurfaceChanged callback. The bridge owns one ANativeWindow ref
    // until the next Jalium dispatcher can transfer it to native platform state.
    private static nint s_stagedNativeWindow;
    private static int s_stagedSurfaceWidth;
    private static int s_stagedSurfaceHeight;
    private static long s_stagedSurfaceActivityGeneration;
    private static float s_density = 1.0f;
    private static int s_refreshRate = 60;
    private static IAndroidSoftKeyboardController? s_softKeyboardController;
    // A Resume delivery raced the old dispatcher's stop/replacement window and
    // was dropped (DispatchToUi returned false) while its Activity was still
    // current. MarkUiThreadReady consumes it: the replacement UI thread replays
    // the resume so the pause-side native/managed suspend state cannot leak
    // into a session that is necessarily foreground. Guarded by s_lifecycleGate.
    private static bool s_pendingResumeReplay;
    // Bounded replay budget for a Surface attach that failed mid-transaction
    // while the dispatcher stayed Running (see the attach-failure cleanup in
    // OnNativeWindowCreatedCore). The Surface callback is consumed by then and
    // the render gate is closed, so without a replay the app stays black until
    // the next surfaceChanged — which may never come. Guarded by
    // s_lifecycleGate; a successful attach voids the budget.
    private const int MaxAttachRetryAttempts = 3;
    private const int AttachRetryDelayMs = 500;
    private static long s_attachRetryActivityGeneration;
    private static int s_attachRetryCount;

    /// <summary>
    /// Initializes the Android bridge. Should be called from native activity startup.
    /// </summary>
    public static void Initialize(float density = 1.0f, int refreshRate = 60)
    {
        bool initializePlatform;
        lock (s_lifecycleGate)
        {
            s_density = density;
            s_refreshRate = refreshRate;
            initializePlatform = !s_initialized;
        }

        if (initializePlatform)
        {
            // Pre-load rendering backend libraries before triggering the NativeMethods static
            // constructor. This ensures their __attribute__((constructor)) functions run first,
            // registering backends into jalium.native.core's registry before ContextCreate is called.
            PreloadNativeBackends();
            PlatformFactory.InitializePlatform();
            lock (s_lifecycleGate)
            {
                s_initialized = true;
            }
        }

        // A replacement Activity can have different display metrics (foldables,
        // external displays).  Always refresh them, and keep native callbacks on
        // the Jalium UI dispatcher once it exists.
        _ = DispatchToUi(() =>
        {
            NativeMethods.AndroidSetDensity(density);
            NativeMethods.AndroidSetRefreshRate(refreshRate);
        }, synchronous: true);
    }

    /// <summary>Registers an Activity instance and returns its monotonic generation.</summary>
    public static long RegisterActivity()
    {
        long generation = Interlocked.Increment(ref s_nextActivityGeneration);
        lock (s_lifecycleGate)
        {
            s_activeActivityGeneration = generation;
        }
        return generation;
    }

    /// <summary>
    /// Marks the dedicated Jalium thread as ready to serialize Android callbacks.
    /// The caller must first install that thread as Dispatcher.MainDispatcher.
    /// </summary>
    public static bool MarkUiThreadReady(long activityGeneration)
    {
        lock (s_stagedTransferGate)
        {
            nint stagedWindow;
            int stagedWidth;
            int stagedHeight;
            long stagedGeneration;
            float density;
            int refreshRate;
            bool replayDroppedResume;
            UiDispatchState previousState;
            long previousUiThreadGeneration;
            lock (s_lifecycleGate)
            {
                if (s_uiDispatchState is not (UiDispatchState.NotStarted or UiDispatchState.Stopped) ||
                    activityGeneration == 0 ||
                    activityGeneration != s_activeActivityGeneration ||
                    activityGeneration != s_startEligibleActivityGeneration ||
                    activityGeneration != s_startReservedActivityGeneration ||
                    activityGeneration != s_stagedSurfaceActivityGeneration ||
                    s_stagedNativeWindow == nint.Zero)
                {
                    return false;
                }

                previousState = s_uiDispatchState;
                previousUiThreadGeneration = s_uiThreadActivityGeneration;
                s_uiDispatchState = UiDispatchState.Running;
                s_stopTransactionCompleted = false;
                s_startEligibleActivityGeneration = 0;
                s_startReservedActivityGeneration = 0;
                s_uiThreadActivityGeneration = activityGeneration;
                stagedWindow = s_stagedNativeWindow;
                stagedWidth = s_stagedSurfaceWidth;
                stagedHeight = s_stagedSurfaceHeight;
                stagedGeneration = s_stagedSurfaceActivityGeneration;
                // Move (not snapshot) the staged ownership into this UI-thread
                // transaction. SurfaceDestroyed takes the same transfer gate,
                // so it cannot release the ref during attach.
                s_stagedNativeWindow = nint.Zero;
                s_stagedSurfaceWidth = 0;
                s_stagedSurfaceHeight = 0;
                s_stagedSurfaceActivityGeneration = 0;
                if (stagedWindow != nint.Zero &&
                    stagedGeneration == s_activeActivityGeneration)
                {
                    s_nativeWindow = stagedWindow;
                    s_surfaceActivityGeneration = stagedGeneration;
                    s_surfaceDestroyPendingGeneration = 0;
                }
                density = s_density;
                refreshRate = s_refreshRate;
                // Consume-on-read: a failed session start re-parks the flag via
                // the next dropped OnResume, so nothing is lost by clearing here.
                replayDroppedResume = s_pendingResumeReplay;
                s_pendingResumeReplay = false;
            }

            // A fresh UI-thread session is necessarily foreground (its Surface
            // exists). Clear a suspend flag that leaked from a Resume dispatch
            // dropped during the previous dispatcher's stop window — otherwise
            // every frame tick of this new session would be skipped forever.
            Jalium.UI.Media.CompositionTarget.ResetSuspendedForNewSession();

            bool startupCallsCompleted = false;
            bool attached = false;
            try
            {
                // We are already on the new Jalium UI thread. Apply state accumulated
                // while the old dispatcher was stopping before user startup can create
                // a Window/RT.
                NativeMethods.AndroidSetDensity(density);
                NativeMethods.AndroidSetRefreshRate(refreshRate);

                if (replayDroppedResume)
                {
                    // Replay the dropped Resume so the NATIVE pause state and any
                    // Resumed subscribers observe the same recovery the managed
                    // suspend flag just got. Failure here must not sink the whole
                    // session start.
                    try
                    {
                        NativeMethods.AndroidOnResume();
                        RaiseSafely(Resumed, nameof(Resumed));
                    }
                    catch (Exception ex)
                    {
                        LogCallbackFailure("dropped resume replay", ex);
                    }
                }

                if (stagedWindow != nint.Zero)
                {
                    attached = OnNativeWindowCreated(
                        stagedWindow, stagedWidth, stagedHeight, stagedGeneration);
                }

                startupCallsCompleted = true;
            }
            catch (Exception ex)
            {
                LogCallbackFailure("UI thread ready replay", ex);
            }
            finally
            {
                if (stagedWindow != nint.Zero)
                {
                    if (!attached)
                    {
                        lock (s_lifecycleGate)
                        {
                            if (s_nativeWindow == stagedWindow &&
                                s_surfaceActivityGeneration == stagedGeneration)
                            {
                                s_nativeWindow = nint.Zero;
                                s_surfaceActivityGeneration = 0;
                                s_surfaceDestroyPendingGeneration = 0;
                            }
                        }
                    }
                    ANativeWindowReleaseSafely(stagedWindow, "consumed staged Surface");
                }

                if (!startupCallsCompleted)
                {
                    lock (s_lifecycleGate)
                    {
                        if (s_uiDispatchState == UiDispatchState.Running &&
                            s_uiThreadActivityGeneration == activityGeneration)
                        {
                            s_uiDispatchState = previousState;
                            s_uiThreadActivityGeneration = previousUiThreadGeneration;
                            s_nativeWindow = nint.Zero;
                            s_surfaceActivityGeneration = 0;
                            s_surfaceDestroyPendingGeneration = 0;
                        }
                    }
                }
            }

            return startupCallsCompleted;
        }
    }

    /// <summary>
    /// Transitions away from a dispatcher whose native loop is about to exit.
    /// This method runs on that dispatcher thread and never enqueues back to it.
    /// </summary>
    public static bool MarkUiThreadStopping(bool terminalOnFailure = false)
    {
        long surfaceGeneration;
        lock (s_lifecycleGate)
        {
            if (s_uiDispatchState != UiDispatchState.Running)
            {
                return s_uiDispatchState == UiDispatchState.Stopping &&
                       s_stopTransactionCompleted;
            }

            s_stoppingDetachCompleted.Reset();
            s_uiDispatchState = UiDispatchState.Stopping;
            s_stopTransactionCompleted = false;
            surfaceGeneration = s_surfaceActivityGeneration;
            if (surfaceGeneration != 0)
                s_surfaceDestroyPendingGeneration = surfaceGeneration;
        }

        // No packet from the retiring Activity/dispatcher may be replayed into
        // a replacement application generation.
        ResetTouchInputQueue();

        try
        {
            var window = Application.Current?.MainWindow;

            if (surfaceGeneration != 0)
            {
                bool detached;
                try
                {
                    detached = window?.DetachAndroidSurface() ?? true;
                }
                catch (Exception ex)
                {
                    LogCallbackFailure("stopping surface detach", ex);
                    detached = false;
                }

                if (!detached)
                {
                    Console.Error.WriteLine(
                        "[AndroidActivityBridge] Stopping retained the native Surface because detach was incomplete.");
                    return CompleteFailedStopAttempt(terminalOnFailure);
                }

                try
                {
                    NativeMethods.AndroidSetNativeWindow(nint.Zero, 0, 0);
                }
                catch (Exception ex)
                {
                    LogCallbackFailure("stopping native surface release", ex);
                    return CompleteFailedStopAttempt(terminalOnFailure);
                }

                lock (s_lifecycleGate)
                {
                    if (s_surfaceActivityGeneration == surfaceGeneration)
                    {
                        s_nativeWindow = nint.Zero;
                        s_surfaceActivityGeneration = 0;
                    }
                    if (s_surfaceDestroyPendingGeneration == surfaceGeneration)
                        s_surfaceDestroyPendingGeneration = 0;
                }
            }

            bool windowClosed;
            try
            {
                // Force native WindowDestroy and free NativePlatformWindow callback
                // GCHandles before the dispatcher loop exits. Application.Cleanup
                // does not close MainWindow on its own.
                windowClosed = window?.CloseForAndroidShutdown() ?? true;
            }
            catch (Exception ex)
            {
                LogCallbackFailure("stopping platform window close", ex);
                windowClosed = false;
            }

            if (!windowClosed)
                return CompleteFailedStopAttempt(terminalOnFailure);

            lock (s_lifecycleGate)
            {
                if (s_uiDispatchState == UiDispatchState.Stopping)
                    s_stopTransactionCompleted = true;
            }
            return true;
        }
        finally
        {
            s_stoppingDetachCompleted.Set();
        }
    }

    private static bool CompleteFailedStopAttempt(bool terminalOnFailure)
    {
        lock (s_lifecycleGate)
        {
            if (s_uiDispatchState == UiDispatchState.Stopping)
            {
                s_uiDispatchState = terminalOnFailure
                    ? UiDispatchState.Failed
                    : UiDispatchState.Running;
            }
            s_stopTransactionCompleted = false;
            if (terminalOnFailure)
            {
                s_startEligibleActivityGeneration = 0;
                s_startReservedActivityGeneration = 0;
            }
        }
        return false;
    }

    /// <summary>Stops routing callbacks after the old application cleanup completes.</summary>
    public static void MarkUiThreadStopped()
    {
        lock (s_lifecycleGate)
        {
            s_uiDispatchState = UiDispatchState.Stopped;
            s_stopTransactionCompleted = false;
            s_startReservedActivityGeneration = 0;
            // Application cleanup destroys the old platform Window. Any owner
            // record still present here is no longer backed by native ownership;
            // discard the raw pointer while preserving a separately-acquired
            // staged replacement Surface.
            s_nativeWindow = nint.Zero;
            s_surfaceActivityGeneration = 0;
            s_surfaceDestroyPendingGeneration = 0;
        }
        ResetTouchInputQueue();
    }

    /// <summary>
    /// Permanently stops routing to a dispatcher whose loop exited before its
    /// Surface/Window teardown transaction could be proven complete. Native
    /// ownership is deliberately preserved and no replacement UI thread may
    /// overwrite it.
    /// </summary>
    public static void MarkUiThreadStopFailed()
    {
        lock (s_lifecycleGate)
        {
            s_uiDispatchState = UiDispatchState.Failed;
            s_stopTransactionCompleted = false;
            s_startEligibleActivityGeneration = 0;
            s_startReservedActivityGeneration = 0;
        }
        ResetTouchInputQueue();
        s_stoppingDetachCompleted.Set();
    }

    /// <summary>Returns whether an Activity generation is still the active host.</summary>
    public static bool IsCurrentActivity(long activityGeneration)
    {
        lock (s_lifecycleGate)
        {
            return activityGeneration != 0 &&
                   activityGeneration == s_activeActivityGeneration;
        }
    }

    /// <summary>
    /// Atomically reserves the staged Surface that authorizes creation of a UI
    /// thread. A normal Surface replacement while Running never creates this
    /// eligibility, so Activity rotation cannot accidentally restart an app
    /// after its later, intentional shutdown.
    /// </summary>
    public static bool TryReserveUiThreadStart(long activityGeneration)
    {
        lock (s_lifecycleGate)
        {
            if (s_uiDispatchState is not (UiDispatchState.NotStarted or UiDispatchState.Stopped) ||
                activityGeneration == 0 ||
                activityGeneration != s_activeActivityGeneration ||
                activityGeneration != s_startEligibleActivityGeneration ||
                activityGeneration != s_stagedSurfaceActivityGeneration ||
                s_stagedNativeWindow == nint.Zero ||
                s_startReservedActivityGeneration != 0)
            {
                return false;
            }

            s_startReservedActivityGeneration = activityGeneration;
            return true;
        }
    }

    /// <summary>
    /// Releases a start reservation when its thread loses the Activity/Surface
    /// race before MarkUiThreadReady. Eligibility for a newer generation is
    /// deliberately left intact for the final Activity gate to consume.
    /// </summary>
    public static void CancelUiThreadStart(long activityGeneration)
    {
        lock (s_lifecycleGate)
        {
            if (s_startReservedActivityGeneration == activityGeneration)
                s_startReservedActivityGeneration = 0;
        }
    }

    private static bool DispatchToUi(
        Action callback,
        bool synchronous,
        DispatcherPriority asynchronousPriority = DispatcherPriority.Normal,
        bool alwaysPost = false)
    {
        Dispatcher? dispatcher = null;
        DispatcherOperation? operation = null;
        bool invokeDirect = false;

        void GuardedCallback()
        {
            lock (s_lifecycleGate)
            {
                if (s_uiDispatchState != UiDispatchState.Running ||
                    !ReferenceEquals(Dispatcher.MainDispatcher, dispatcher))
                {
                    return;
                }
            }

            try
            {
                callback();
            }
            catch (Exception ex)
            {
                LogCallbackFailure("dispatched callback", ex);
            }
        }

        lock (s_lifecycleGate)
        {
            // NotStarted callbacks are cached/staged by their public entrypoint.
            // They must never touch native Surface globals on Android's main
            // thread while the dedicated UI thread is being constructed.
            if (s_uiDispatchState != UiDispatchState.Running)
                return false;

            dispatcher = Dispatcher.MainDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return false;

            if (dispatcher.CheckAccess() && !alwaysPost)
            {
                invokeDirect = true;
            }
            else
            {
                try
                {
                    // State validation and enqueue are one lifecycle transaction.
                    // MarkStopped cannot publish a dead dispatcher between them.
                    operation = dispatcher.BeginInvoke(
                        synchronous ? DispatcherPriority.Send : asynchronousPriority,
                        GuardedCallback);
                }
                catch (Exception ex)
                {
                    LogCallbackFailure("dispatcher enqueue", ex);
                    return false;
                }
            }
        }

        if (invokeDirect)
        {
            try
            {
                callback();
                return true;
            }
            catch (Exception ex)
            {
                LogCallbackFailure("direct UI callback", ex);
                return false;
            }
        }

        if (operation == null ||
            operation.Status == Jalium.UI.Threading.DispatcherOperationStatus.Aborted)
            return false;

        if (synchronous)
        {
            try
            {
                while (true)
                {
                    var status = operation.Wait(TimeSpan.FromMilliseconds(100));
                    if (status == Jalium.UI.Threading.DispatcherOperationStatus.Completed)
                        return true;
                    if (status == Jalium.UI.Threading.DispatcherOperationStatus.Aborted)
                        return false;

                    if (status == Jalium.UI.Threading.DispatcherOperationStatus.Executing)
                    {
                        // Dequeue won the race; the UI transaction is genuinely
                        // running. Surface callbacks must wait for it to finish.
                        return operation.Wait() ==
                            Jalium.UI.Threading.DispatcherOperationStatus.Completed;
                    }

                    UiDispatchState currentState;
                    bool dispatcherStopped;
                    lock (s_lifecycleGate)
                    {
                        currentState = s_uiDispatchState;
                        dispatcherStopped = dispatcher.HasShutdownStarted ||
                            !ReferenceEquals(Dispatcher.MainDispatcher, dispatcher);
                    }

                    // A healthy but busy UI may remain Pending for more than 100ms;
                    // keep waiting because returning SurfaceDestroyed early is UAF.
                    // Abort only once routing has definitively left Running.
                    if ((currentState != UiDispatchState.Running || dispatcherStopped) &&
                        operation.Abort())
                    {
                        return false;
                    }

                    // Abort may lose to dequeue between Status and Abort. Loop once
                    // more to observe Executing/Completed without abandoning it.
                }
            }
            catch (Exception ex)
            {
                LogCallbackFailure("dispatcher wait", ex);
                if (operation.Status == Jalium.UI.Threading.DispatcherOperationStatus.Pending)
                    operation.Abort();
                return false;
            }
        }

        return true;
    }

    private static StageSurfaceResult StageSurfaceForNextDispatcher(
        nint nativeWindow, int width, int height, long activityGeneration)
    {
        try
        {
            ANativeWindowAcquire(nativeWindow);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("staged Surface acquire", ex);
            return StageSurfaceResult.Rejected;
        }

        nint previous = nint.Zero;
        StageSurfaceResult result = StageSurfaceResult.Rejected;
        lock (s_stagedTransferGate)
        {
            lock (s_lifecycleGate)
            {
                if (activityGeneration == s_activeActivityGeneration &&
                    s_uiDispatchState is UiDispatchState.NotStarted or
                        UiDispatchState.Stopping or UiDispatchState.Stopped or
                        UiDispatchState.Failed)
                {
                    previous = s_stagedNativeWindow;
                    s_stagedNativeWindow = nativeWindow;
                    s_stagedSurfaceWidth = width;
                    s_stagedSurfaceHeight = height;
                    s_stagedSurfaceActivityGeneration = activityGeneration;

                    bool initialStart = s_uiDispatchState == UiDispatchState.NotStarted;
                    bool replacementStart =
                        (s_uiDispatchState is UiDispatchState.Stopping or UiDispatchState.Stopped) &&
                        activityGeneration != s_uiThreadActivityGeneration;
                    s_startEligibleActivityGeneration = initialStart || replacementStart
                        ? activityGeneration
                        : 0;
                    result = StageSurfaceResult.Staged;
                }
                else if (activityGeneration == s_activeActivityGeneration &&
                         s_uiDispatchState == UiDispatchState.Running)
                {
                    // MarkUiThreadReady can move an older staged Surface and
                    // transition to Running while this callback waits for the
                    // transfer gate. The caller still owns its original Surface
                    // callback reference and can retry normal dispatch once.
                    result = StageSurfaceResult.RetryRunning;
                }
            }

            if (previous != nint.Zero)
                ANativeWindowReleaseSafely(previous, "replaced staged Surface");
        }

        if (result != StageSurfaceResult.Staged)
        {
            ANativeWindowReleaseSafely(nativeWindow, "rejected staged Surface");
        }

        return result;
    }

    private static void ANativeWindowReleaseSafely(nint nativeWindow, string stage)
    {
        try { ANativeWindowRelease(nativeWindow); }
        catch (Exception ex) { LogCallbackFailure(stage, ex); }
    }

    private static void PreloadNativeBackends()
    {
        // P/Invoke into each backend's explicit init function.
        // This forces the dynamic linker to load the .so and registers the backend
        // with jalium.native.core's registry before PlatformInit / ContextCreate is called.
        try
        {
            BackendPreloader.SoftwareInit();
            Console.Error.WriteLine("[PreloadNativeBackends] Software backend registered OK");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PreloadNativeBackends] software init failed: {ex.Message}");
        }

        try
        {
            BackendPreloader.VulkanInit();
            Console.Error.WriteLine("[PreloadNativeBackends] Vulkan backend registered OK");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PreloadNativeBackends] vulkan init failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the JNI environment for native code (needed for clipboard, etc.).
    /// Call from the Activity with JNIEnv and Activity handles.
    /// </summary>
    public static void SetJniEnv(nint javaVM, nint activity)
    {
        NativeMethods.AndroidSetJniEnv(javaVM, activity);
    }

    /// <summary>
    /// Called when the native window is created or its surface size changes
    /// (onNativeWindowCreated / SurfaceHolder.Callback.surfaceChanged). The
    /// <paramref name="width"/>/<paramref name="height"/> are the authoritative
    /// post-rotation surface pixels reported by surfaceChanged — passing them through
    /// lets the native layer dispatch a RESIZE with correct dims instead of inferring
    /// possibly-stale ones via ANativeWindow_getWidth/Height during a device rotation.
    /// </summary>
    public static void OnNativeWindowCreated(nint nativeWindow, int width, int height)
    {
        long generation;
        lock (s_lifecycleGate)
        {
            generation = s_activeActivityGeneration;
        }
        _ = OnNativeWindowCreated(nativeWindow, width, height, generation);
    }

    /// <summary>
    /// Attaches a Surface for a specific Activity generation. Returns false for
    /// a stale Activity callback. Replacement and resize are synchronously
    /// serialized onto the Jalium UI thread.
    /// </summary>
    public static bool OnNativeWindowCreated(
        nint nativeWindow, int width, int height, long activityGeneration)
        => OnNativeWindowCreatedCore(
            nativeWindow, width, height, activityGeneration, retryRunningStage: true);

    private static bool OnNativeWindowCreatedCore(
        nint nativeWindow, int width, int height, long activityGeneration,
        bool retryRunningStage, bool abortIfSurfaceAttached = false)
    {
        if (nativeWindow == nint.Zero)
        {
            return false;
        }

        lock (s_lifecycleGate)
        {
            if (activityGeneration == 0 || activityGeneration != s_activeActivityGeneration)
            {
                return false;
            }
        }

        bool accepted = false;
        bool dispatched = DispatchToUi(() =>
        {
            try
            {
                nint previousWindow;
                long previousGeneration;
                bool destroyPending;
                lock (s_lifecycleGate)
                {
                    // Recheck after a queued dispatcher hop: a newer Activity may
                    // have registered while this callback was waiting.
                    if (activityGeneration != s_activeActivityGeneration)
                    {
                        return;
                    }

                    previousWindow = s_nativeWindow;
                    previousGeneration = s_surfaceActivityGeneration;
                    destroyPending = s_surfaceDestroyPendingGeneration != 0;
                }

                if (abortIfSurfaceAttached && previousWindow != nint.Zero)
                {
                    // Failed-attach replay only: the cleanup path had cleared
                    // the owner record, so a non-zero owner here means a newer
                    // surfaceChanged attached (or re-attached) a Surface while
                    // the replay was pending. A stale replay must never tear
                    // down that healthy Surface via the replacement path below.
                    Console.Error.WriteLine(
                        "[AndroidActivityBridge] Surface attach retry abandoned: a Surface is already attached.");
                    return;
                }

                var window = Application.Current?.MainWindow;
                bool replacingSurface = previousWindow != nint.Zero &&
                    (previousWindow != nativeWindow ||
                     previousGeneration != activityGeneration ||
                     destroyPending);

                if (replacingSurface)
                {
                    bool detached;
                    try
                    {
                        detached = window?.DetachAndroidSurface() ?? true;
                    }
                    catch (Exception ex)
                    {
                        LogCallbackFailure("surface replacement detach", ex);
                        detached = false;
                    }

                    if (!detached)
                    {
                        // The render gate is already closed, but the old RT did
                        // not prove that it released the Surface. Keep the old
                        // native reference and refuse the replacement; releasing
                        // it here would reintroduce the real-device UAF.
                        Console.Error.WriteLine(
                            "[AndroidActivityBridge] Surface replacement deferred because detach was incomplete.");
                        return;
                    }
                }

                try
                {
                    // Set the availability gate before native RESIZE re-enters
                    // managed code and attempts to create the new RenderTarget.
                    window?.PrepareAndroidSurfaceAttach();
                    NativeMethods.AndroidSetNativeWindow(nativeWindow, width, height);
                }
                catch (Exception ex)
                {
                    LogCallbackFailure("surface attach", ex);
                    bool cleanupDetached;
                    try { cleanupDetached = window?.DetachAndroidSurface() ?? true; }
                    catch (Exception cleanupEx)
                    {
                        LogCallbackFailure("failed surface attach cleanup", cleanupEx);
                        cleanupDetached = false;
                    }

                    if (cleanupDetached)
                    {
                        bool cleanupReleased = false;
                        try
                        {
                            NativeMethods.AndroidSetNativeWindow(nint.Zero, 0, 0);
                            lock (s_lifecycleGate)
                            {
                                s_nativeWindow = nint.Zero;
                                s_surfaceActivityGeneration = 0;
                                s_surfaceDestroyPendingGeneration = 0;
                            }
                            cleanupReleased = true;
                        }
                        catch (Exception cleanupEx)
                        {
                            // Native ownership did not confirm release. Preserve
                            // the managed owner record and keep rendering gated.
                            LogCallbackFailure("failed native surface attach cleanup", cleanupEx);
                        }

                        if (cleanupReleased)
                        {
                            // The Surface callback is consumed and the render
                            // gate is closed (_androidSurfaceAvailable false),
                            // which also blocks OnFrameStarting's frame-clock
                            // render-target recovery. Without a replay the app
                            // stays black until the next surfaceChanged — which
                            // may never arrive. Schedule a bounded, delayed
                            // replay of this attach.
                            ScheduleFailedAttachRetry(
                                nativeWindow, width, height, activityGeneration);
                        }
                    }
                    return;
                }

                lock (s_lifecycleGate)
                {
                    s_nativeWindow = nativeWindow;
                    s_surfaceActivityGeneration = activityGeneration;
                    s_surfaceDestroyPendingGeneration = 0;
                    s_uiThreadActivityGeneration = activityGeneration;
                    // A completed attach voids any pending failed-attach replay
                    // budget: the next failure (if any) starts a fresh run.
                    s_attachRetryActivityGeneration = 0;
                    s_attachRetryCount = 0;
                    accepted = true;
                }

                try { window?.CompleteAndroidSurfaceAttach(); }
                catch (Exception ex) { LogCallbackFailure("surface attach completion", ex); }
                RaiseSafely(NativeWindowCreated, nativeWindow, nameof(NativeWindowCreated));
            }
            catch (Exception ex)
            {
                // Nothing in a reverse/native lifecycle callback may escape to
                // Android's main Looper; an unhandled exception there terminates
                // the process instead of reaching normal dispatcher handling.
                LogCallbackFailure("surface attach callback", ex);
            }
        }, synchronous: true);

        if (!accepted)
        {
            bool shouldStage = !dispatched;
            if (!shouldStage)
            {
                lock (s_lifecycleGate)
                {
                    // A completed guarded operation can still have been dropped
                    // by a Running -> Stopping transition. A genuine attach
                    // failure while the same dispatcher remains Running must not
                    // be turned into an unbounded retry loop.
                    shouldStage = s_uiDispatchState != UiDispatchState.Running;
                }
            }

            if (shouldStage)
            {
                StageSurfaceResult stageResult = StageSurfaceForNextDispatcher(
                    nativeWindow, width, height, activityGeneration);
                if (stageResult == StageSurfaceResult.Staged)
                    return true;

                if (stageResult == StageSurfaceResult.RetryRunning &&
                    retryRunningStage)
                {
                    return OnNativeWindowCreatedCore(
                        nativeWindow, width, height, activityGeneration,
                        retryRunningStage: false,
                        abortIfSurfaceAttached: abortIfSurfaceAttached);
                }
            }
        }

        return accepted;
    }

    /// <summary>
    /// Schedules a bounded, delayed replay of a Surface attach that failed
    /// mid-transaction while the dispatcher stayed Running. The cleanup path
    /// has already consumed the Surface callback and closed the render gate,
    /// so nothing else re-attempts the attach; the frame-clock recovery in
    /// Window.OnFrameStarting is deliberately gated on the very availability
    /// flag that the cleanup lowered. At most
    /// <see cref="MaxAttachRetryAttempts"/> replays run per Activity
    /// generation; a successful attach (this replay's or any newer
    /// surfaceChanged's) voids the budget.
    /// </summary>
    private static void ScheduleFailedAttachRetry(
        nint nativeWindow, int width, int height, long activityGeneration)
    {
        int attempt;
        lock (s_lifecycleGate)
        {
            if (activityGeneration == 0 || activityGeneration != s_activeActivityGeneration)
            {
                return;
            }

            if (s_attachRetryActivityGeneration != activityGeneration)
            {
                s_attachRetryActivityGeneration = activityGeneration;
                s_attachRetryCount = 0;
            }

            if (s_attachRetryCount >= MaxAttachRetryAttempts)
            {
                Console.Error.WriteLine(
                    $"[AndroidActivityBridge] Surface attach still failing after {MaxAttachRetryAttempts} retries (activity generation {activityGeneration}); giving up, awaiting next surfaceChanged.");
                return;
            }

            attempt = ++s_attachRetryCount;
        }

        // Hold our own ANativeWindow reference across the delay. The cleanup
        // path just told native to release ITS reference and the bridge's
        // direct attach path never takes one of its own, so without an acquire
        // here the pointer could be freed (surfaceDestroyed) before the replay
        // runs. This call site is still inside the failing attach transaction —
        // either the synchronous surfaceChanged dispatch (Android's main thread
        // is blocked in DispatchToUi, pinning the Surface) or MarkUiThreadReady's
        // staged-consumption block (the staged acquire is released only in its
        // finally) — so the acquire target is provably alive.
        try
        {
            ANativeWindowAcquire(nativeWindow);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("surface attach retry acquire", ex);
            return;
        }

        Console.Error.WriteLine(
            $"[AndroidActivityBridge] Scheduling surface attach retry {attempt}/{MaxAttachRetryAttempts} in {AttachRetryDelayMs} ms (activity generation {activityGeneration}).");

        // Dedicated short-lived thread instead of a timer: every timer flavour
        // bottoms out in the thread pool, which must be assumed starvable here
        // (the same reason Window.OnFrameStarting grew a frame-clock recovery).
        // The replay re-enters OnNativeWindowCreatedCore, whose synchronous
        // DispatchToUi marshals onto the Jalium UI thread exactly like the
        // original Android-main-thread caller.
        var retryThread = new Thread(() =>
        {
            try
            {
                Thread.Sleep(AttachRetryDelayMs);

                lock (s_lifecycleGate)
                {
                    if (activityGeneration != s_activeActivityGeneration)
                    {
                        Console.Error.WriteLine(
                            $"[AndroidActivityBridge] Surface attach retry {attempt}/{MaxAttachRetryAttempts} abandoned: activity generation {activityGeneration} superseded.");
                        return;
                    }

                    if (s_nativeWindow != nint.Zero || s_stagedNativeWindow != nint.Zero)
                    {
                        // A newer surfaceChanged already delivered a healthy
                        // Surface (attached or staged for the next dispatcher);
                        // replaying the failed one would tear it down or
                        // displace it. abortIfSurfaceAttached below closes the
                        // remaining race window on the attach side.
                        Console.Error.WriteLine(
                            $"[AndroidActivityBridge] Surface attach retry {attempt}/{MaxAttachRetryAttempts} abandoned: a replacement Surface is already present.");
                        return;
                    }
                }

                bool attached = OnNativeWindowCreatedCore(
                    nativeWindow, width, height, activityGeneration,
                    retryRunningStage: true, abortIfSurfaceAttached: true);
                Console.Error.WriteLine(
                    $"[AndroidActivityBridge] Surface attach retry {attempt}/{MaxAttachRetryAttempts} {(attached ? "succeeded" : "failed")} (activity generation {activityGeneration}).");
                // On failure the replay's own attach-cleanup path has already
                // chained the next ScheduleFailedAttachRetry or logged the
                // terminal give-up; nothing more to do here.
            }
            catch (Exception ex)
            {
                LogCallbackFailure("surface attach retry", ex);
            }
            finally
            {
                ANativeWindowReleaseSafely(nativeWindow, "surface attach retry reference");
            }
        })
        {
            IsBackground = true,
            Name = "JaliumSurfaceAttachRetry",
        };
        retryThread.Start();
    }

    /// <summary>
    /// Called when the native window is destroyed (onNativeWindowDestroyed).
    /// </summary>
    public static void OnNativeWindowDestroyed()
    {
        long generation;
        lock (s_lifecycleGate)
        {
            generation = s_surfaceActivityGeneration != 0
                ? s_surfaceActivityGeneration
                : s_stagedSurfaceActivityGeneration;
        }
        _ = OnNativeWindowDestroyed(generation);
    }

    /// <summary>
    /// Synchronously drains and disposes every renderer resource using an
    /// Activity's Surface. A destroy from an older Activity is ignored after a
    /// newer Activity generation has attached its own Surface.
    /// </summary>
    public static bool OnNativeWindowDestroyed(long activityGeneration)
    {
        nint stagedWindow = nint.Zero;
        UiDispatchState state;
        lock (s_stagedTransferGate)
        {
            lock (s_lifecycleGate)
            {
                if (activityGeneration != 0 &&
                    activityGeneration == s_stagedSurfaceActivityGeneration)
                {
                    stagedWindow = s_stagedNativeWindow;
                    s_stagedNativeWindow = nint.Zero;
                    s_stagedSurfaceWidth = 0;
                    s_stagedSurfaceHeight = 0;
                    s_stagedSurfaceActivityGeneration = 0;
                    if (s_startEligibleActivityGeneration == activityGeneration)
                        s_startEligibleActivityGeneration = 0;
                    if (s_startReservedActivityGeneration == activityGeneration)
                        s_startReservedActivityGeneration = 0;
                }

                state = s_uiDispatchState;
                if (stagedWindow != nint.Zero)
                {
                    // The separately-acquired staged ref is now exclusively owned
                    // by this callback and can be released outside the state lock.
                }
                else
                {
                    if (activityGeneration == 0 || activityGeneration != s_surfaceActivityGeneration)
                    {
                        return false;
                    }

                    // Publish a logical "unavailable" state before waiting for the UI
                    // dispatcher, but retain the actual pointer/owner record until RT
                    // detach succeeds and native confirms the reference was released.
                    s_surfaceDestroyPendingGeneration = activityGeneration;
                }
            }
        }

        if (stagedWindow != nint.Zero)
        {
            ANativeWindowReleaseSafely(stagedWindow, "destroyed staged Surface");
            return true;
        }

        if (state == UiDispatchState.Stopping)
        {
            // MarkUiThreadStopping performs teardown directly on the old UI
            // thread. Wait for that transaction rather than Invoke a dispatcher
            // whose native pump is exiting.
            s_stoppingDetachCompleted.Wait();
            lock (s_lifecycleGate)
            {
                return s_surfaceActivityGeneration != activityGeneration;
            }
        }

        if (state is UiDispatchState.NotStarted or UiDispatchState.Stopped or UiDispatchState.Failed)
            return false;

        bool accepted = false;
        bool dispatched = DispatchToUi(() =>
        {
            bool detached;
            try
            {
                // This is the synchronous SurfaceHolder destruction barrier:
                // stop frames, release retained handles/drawing context, then
                // dispose the RT/VkSurface while the platform still owns the
                // old ANativeWindow reference.
                detached = Application.Current?.MainWindow?.DetachAndroidSurface() ?? true;
            }
            catch (Exception ex)
            {
                LogCallbackFailure("surface destroy detach", ex);
                detached = false;
            }

            if (!detached)
            {
                // DetachAndroidSurface already closed the render gate. Retain
                // native ownership until a later replacement/destroy can prove
                // teardown; leaking temporarily is safer than Surface UAF.
                Console.Error.WriteLine(
                    "[AndroidActivityBridge] Native Surface retained because detach was incomplete.");
                return;
            }

            try
            {
                // Only a confirmed detach authorizes native to release its owned
                // ANativeWindow reference.
                NativeMethods.AndroidSetNativeWindow(nint.Zero, 0, 0);
            }
            catch (Exception ex)
            {
                LogCallbackFailure("native surface release", ex);
                return;
            }

            lock (s_lifecycleGate)
            {
                if (s_surfaceActivityGeneration == activityGeneration)
                {
                    s_nativeWindow = nint.Zero;
                    s_surfaceActivityGeneration = 0;
                }
                if (s_surfaceDestroyPendingGeneration == activityGeneration)
                    s_surfaceDestroyPendingGeneration = 0;
                accepted = true;
            }

            RaiseSafely(NativeWindowDestroyed, nameof(NativeWindowDestroyed));
        }, synchronous: true);

        if (!dispatched)
        {
            lock (s_lifecycleGate) { state = s_uiDispatchState; }
            if (state == UiDispatchState.Stopping)
            {
                s_stoppingDetachCompleted.Wait();
                lock (s_lifecycleGate)
                {
                    return s_surfaceActivityGeneration != activityGeneration;
                }
            }
        }

        return accepted;
    }

    /// <summary>Called when the activity is paused.</summary>
    public static void OnPause()
    {
        long generation;
        lock (s_lifecycleGate) { generation = s_activeActivityGeneration; }
        OnPause(generation);
    }

    public static void OnPause(long activityGeneration)
    {
        DispatchCurrentActivity(activityGeneration, () =>
        {
            NativeMethods.AndroidOnPause();
            RaiseSafely(Paused, nameof(Paused));
        }, synchronous: false);
    }

    /// <summary>Called when the activity is resumed.</summary>
    public static void OnResume()
    {
        long generation;
        lock (s_lifecycleGate) { generation = s_activeActivityGeneration; }
        OnResume(generation);
    }

    public static void OnResume(long activityGeneration)
    {
        bool dispatched = DispatchCurrentActivity(activityGeneration, () =>
        {
            NativeMethods.AndroidOnResume();
            RaiseSafely(Resumed, nameof(Resumed));
        }, synchronous: false);

        if (dispatched)
        {
            return;
        }

        // The dispatcher was stopping or mid-replacement, so this resume was
        // silently dropped. A pause that DID land has no counterpart then:
        // rendering stays suspended into the next UI-thread generation and no
        // later callback resets it. Park the resume for MarkUiThreadReady —
        // but only while this Activity is still the current one (a stale
        // generation's resume belongs to an Activity that already lost).
        lock (s_lifecycleGate)
        {
            if (activityGeneration == s_activeActivityGeneration)
            {
                s_pendingResumeReplay = true;
            }
        }
    }

    /// <summary>Called when the activity is being destroyed.</summary>
    public static void OnDestroy()
    {
        long generation;
        lock (s_lifecycleGate) { generation = s_activeActivityGeneration; }
        OnDestroy(generation);
    }

    public static void OnDestroy(long activityGeneration)
    {
        DispatchCurrentActivity(activityGeneration, () =>
        {
            NativeMethods.AndroidOnDestroy();
            RaiseSafely(Destroying, nameof(Destroying));
        }, synchronous: true);
    }

    private static bool DispatchCurrentActivity(
        long activityGeneration, Action callback, bool synchronous)
    {
        lock (s_lifecycleGate)
        {
            if (activityGeneration == 0 || activityGeneration != s_activeActivityGeneration)
            {
                return false;
            }
        }

        return DispatchToUi(() =>
        {
            lock (s_lifecycleGate)
            {
                if (activityGeneration != s_activeActivityGeneration)
                {
                    return;
                }
            }

            try
            {
                callback();
            }
            catch (Exception ex)
            {
                LogCallbackFailure("activity lifecycle callback", ex);
            }
        }, synchronous);
    }

    /// <summary>
    /// Called when the display density changes (e.g. foldable device fold/unfold,
    /// display settings change). Updates the native platform layer which dispatches
    /// a DpiChanged event to the managed Window.
    /// </summary>
    public static void OnDensityChanged(float density)
    {
        lock (s_lifecycleGate) { s_density = density; }
        DispatchToUi(() =>
        {
            NativeMethods.AndroidSetDensity(density);
            RaiseSafely(DensityChanged, density, nameof(DensityChanged));
        }, synchronous: false);
    }

    /// <summary>
    /// Called when window insets change (safe area / cutout / status bar / navigation bar).
    /// Insets are in physical pixels.
    /// </summary>
    public static void OnSafeAreaInsetsChanged(float top, float bottom, float left, float right)
    {
        DispatchToUi(
            () => NativeMethods.AndroidSetSafeAreaInsets(top, bottom, left, right),
            synchronous: false);
    }

    /// <summary>
    /// Called when soft keyboard visibility or height changes.
    /// </summary>
    public static void OnKeyboardVisibilityChanged(bool visible, int heightPx)
    {
        DispatchToUi(
            () => NativeMethods.AndroidSetKeyboardVisible(visible ? 1 : 0, heightPx),
            synchronous: false);
    }

    /// <summary>
    /// Called when device orientation changes.
    /// 0=portrait, 1=landscape, 2=portrait-reverse, 3=landscape-reverse.
    /// </summary>
    public static void OnOrientationChanged(int orientation)
    {
        DispatchToUi(() => NativeMethods.AndroidSetOrientation(orientation), synchronous: false);
    }

    /// <summary>Called when the system is low on memory.</summary>
    public static void OnLowMemory()
    {
        long generation;
        lock (s_lifecycleGate) { generation = s_activeActivityGeneration; }
        OnLowMemory(generation);
    }

    public static void OnLowMemory(long activityGeneration)
    {
        DispatchCurrentActivity(activityGeneration, () =>
        {
            NativeMethods.AndroidOnLowMemory();
            RaiseSafely(LowMemory, nameof(LowMemory));
        }, synchronous: false);
    }

    /// <summary>Gets the current ANativeWindow handle.</summary>
    public static nint NativeWindow
    {
        get
        {
            lock (s_lifecycleGate)
            {
                return s_surfaceDestroyPendingGeneration == 0
                    ? s_nativeWindow
                    : nint.Zero;
            }
        }
    }

    /// <summary>Gets the display density.</summary>
    public static float Density
    {
        get
        {
            lock (s_lifecycleGate) { return s_density; }
        }
    }

    /// <summary>Gets the display refresh rate.</summary>
    public static int RefreshRate
    {
        get
        {
            lock (s_lifecycleGate) { return s_refreshRate; }
        }
    }

    /// <summary>Gets whether running on Android.</summary>
    public static bool IsAndroid => PlatformFactory.IsAndroid;

    private static void RaiseSafely(Action? handlers, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception ex) { LogCallbackFailure(eventName, ex); }
        }
    }

    private static void RaiseSafely<T>(Action<T>? handlers, T value, string eventName)
    {
        if (handlers == null)
            return;

        foreach (Action<T> handler in handlers.GetInvocationList())
        {
            try { handler(value); }
            catch (Exception ex) { LogCallbackFailure(eventName, ex); }
        }
    }

    private static void LogCallbackFailure(string stage, Exception exception)
        => Console.Error.WriteLine($"[AndroidActivityBridge] {stage} failed: {exception}");

    [DllImport("android", EntryPoint = "ANativeWindow_acquire")]
    private static extern void ANativeWindowAcquire(nint nativeWindow);

    [DllImport("android", EntryPoint = "ANativeWindow_release")]
    private static extern void ANativeWindowRelease(nint nativeWindow);

    // ========================================================================
    // Input Injection (called from Activity touch/key event overrides)
    // ========================================================================

    /// <summary>
    /// Pointer type constants matching JALIUM_POINTER_* and Android MotionEvent tool types.
    /// </summary>
    public const int PointerTypeMouse = 0;
    public const int PointerTypeTouch = 1;
    public const int PointerTypePen = 2;

    /// <summary>
    /// Injects a touch/pointer event into the Jalium input pipeline.
    /// Called from Activity.DispatchTouchEvent().
    /// </summary>
    /// <param name="pointerId">The pointer/finger ID.</param>
    /// <param name="x">X coordinate in physical pixels.</param>
    /// <param name="y">Y coordinate in physical pixels.</param>
    /// <param name="pressure">Touch pressure (0.0-1.0).</param>
    /// <param name="action">0=DOWN, 1=UP, 2=MOVE, 3=CANCEL.</param>
    /// <param name="pointerType">PointerTypeTouch, PointerTypePen, or PointerTypeMouse.</param>
    /// <param name="modifiers">Modifier key flags.</param>
    public static void InjectTouch(int pointerId, float x, float y, float pressure,
        int action, int pointerType, int modifiers)
        => InjectTouch(
            pointerId, x, y, pressure, action, pointerType, modifiers,
            Environment.TickCount64);

    /// <summary>
    /// Injects a timestamped Android touch/pointer event into the Jalium input pipeline.
    /// </summary>
    /// <param name="eventTimeMillis">Android monotonic MotionEvent.EventTime in milliseconds.</param>
    public static void InjectTouch(int pointerId, float x, float y, float pressure,
        int action, int pointerType, int modifiers, long eventTimeMillis)
    {
        if (!s_initialized || (uint)action > 3u)
            return;

        bool scheduleDrain;
        lock (s_touchInputGate)
        {
            s_touchInputQueue.Enqueue(new AndroidTouchInput(
                pointerId, x, y, pressure, action, pointerType, modifiers,
                eventTimeMillis));
            scheduleDrain = !s_touchDrainScheduled;
            if (scheduleDrain)
                s_touchDrainScheduled = true;
        }

        if (scheduleDrain && !ScheduleTouchInputDrain())
            ResetTouchInputQueue();
    }

    private static bool ScheduleTouchInputDrain()
        => DispatchToUi(
            DrainTouchInputQueue,
            synchronous: false,
            asynchronousPriority: DispatcherPriority.Normal,
            alwaysPost: true);

    private static void DrainTouchInputQueue()
    {
        int budget;
        lock (s_touchInputGate)
        {
            budget = s_touchInputQueue.Count;
            if (budget == 0)
            {
                s_touchDrainScheduled = false;
                return;
            }
        }

        // Process only the snapshot that existed when this dispatcher turn
        // began. If input keeps arriving, queue one Normal-priority continuation.
        // Frame callbacks use the same priority, so FIFO ordering lets a frame
        // already in the dispatcher run before the next input batch.
        while (budget-- > 0)
        {
            AndroidTouchInput input;
            lock (s_touchInputGate)
            {
                if (!s_touchInputQueue.TryDequeue(out input))
                    break;
            }

            try
            {
                NativeMethods.AndroidInjectTouch(
                    input.PointerId,
                    input.X,
                    input.Y,
                    input.Pressure,
                    input.Action,
                    input.PointerType,
                    input.Modifiers,
                    input.EventTimeMillis);
            }
            catch (Exception ex)
            {
                LogCallbackFailure("touch input injection", ex);
            }
        }

        bool continueDrain;
        lock (s_touchInputGate)
        {
            continueDrain = s_touchInputQueue.Count != 0;
            if (!continueDrain)
                s_touchDrainScheduled = false;
        }

        // Keep the single-flight latch set while posting the continuation.
        // Producers can only update/append queue nodes; they never post a
        // second drain operation while this one is in flight.
        if (continueDrain && !ScheduleTouchInputDrain())
            ResetTouchInputQueue();
    }

    private static void ResetTouchInputQueue()
    {
        lock (s_touchInputGate)
        {
            s_touchInputQueue.Clear();
            s_touchDrainScheduled = false;
        }
    }

    /// <summary>
    /// Injects a key event into the Jalium input pipeline.
    /// Called from Activity.DispatchKeyEvent().
    /// </summary>
    /// <param name="androidKeyCode">Android KEYCODE_* value.</param>
    /// <param name="scanCode">Hardware scan code.</param>
    /// <param name="action">0=KEY_DOWN, 1=KEY_UP.</param>
    /// <param name="metaState">Android meta state flags.</param>
    /// <param name="repeatCount">Number of key repeats.</param>
    public static void InjectKey(int androidKeyCode, int scanCode,
        int action, int metaState, int repeatCount)
    {
        if (!s_initialized) return;
        _ = DispatchToUi(
            () => NativeMethods.AndroidInjectKey(
                androidKeyCode, scanCode, action, metaState, repeatCount),
            synchronous: false);
    }

    /// <summary>
    /// Injects a character input event (e.g., from IME or key press).
    /// </summary>
    /// <param name="codepoint">Unicode codepoint of the character.</param>
    public static void InjectChar(uint codepoint)
    {
        if (!s_initialized) return;
        _ = DispatchToUi(
            () => NativeMethods.AndroidInjectChar(codepoint),
            synchronous: false);
    }

    // ========================================================================
    // Soft Keyboard (IME) — Android system on-screen keyboard integration
    // ========================================================================

    /// <summary>
    /// Registers (or clears with <see langword="null"/>) the controller that
    /// drives the Android system soft keyboard. Called by the Android entry
    /// package (<c>JaliumActivity</c>) once its Activity and text-input view
    /// exist, and cleared on Activity destroy.
    /// </summary>
    public static void SetSoftKeyboardController(IAndroidSoftKeyboardController? controller)
    {
        lock (s_lifecycleGate)
        {
            s_softKeyboardController = controller;
        }
    }

    /// <summary>
    /// Pushes a new IME state toward the system soft keyboard. Invoked on the
    /// Jalium UI thread by <see cref="Window"/>; the controller is responsible
    /// for marshalling onto the Android main thread before touching any View or
    /// InputMethodManager state.
    /// </summary>
    internal static void UpdateSoftKeyboard(AndroidImeState state)
    {
        IAndroidSoftKeyboardController? controller;
        lock (s_lifecycleGate)
        {
            controller = s_softKeyboardController;
        }

        try
        {
            controller?.UpdateImeState(state);
        }
        catch (Exception ex)
        {
            LogCallbackFailure("soft keyboard update", ex);
        }
    }

    /// <summary>Gets whether a soft-keyboard controller is currently registered.</summary>
    internal static bool HasSoftKeyboardController
    {
        get { lock (s_lifecycleGate) { return s_softKeyboardController != null; } }
    }

    /// <summary>
    /// Injects IME composition (pre-edit / underlined) text from the input
    /// connection into the focused editor. Enables Chinese/CJK predictive input.
    /// </summary>
    /// <param name="compositionText">The current composition string.</param>
    /// <param name="caretOffset">Caret offset within the composition string.</param>
    public static void InjectImeComposition(string compositionText, int caretOffset)
    {
        if (!s_initialized) return;
        string text = compositionText ?? string.Empty;
        _ = DispatchToUi(
            () => (Application.Current?.MainWindow)?.InjectAndroidImeComposition(text, caretOffset),
            synchronous: false);
    }

    /// <summary>
    /// Commits final text from the input connection, ending any active
    /// composition first.
    /// </summary>
    public static void InjectImeCommit(string text)
    {
        if (!s_initialized) return;
        string value = text ?? string.Empty;
        _ = DispatchToUi(
            () => (Application.Current?.MainWindow)?.InjectAndroidImeCommit(value),
            synchronous: false);
    }

    /// <summary>Finishes composition, committing the current pre-edit text as-is.</summary>
    public static void InjectImeFinishComposing()
    {
        if (!s_initialized) return;
        _ = DispatchToUi(
            () => (Application.Current?.MainWindow)?.InjectAndroidImeFinishComposing(),
            synchronous: false);
    }

    /// <summary>
    /// Deletes text around the cursor as requested by the IME. Counts are
    /// UTF-16 code units (Android <c>InputConnection.deleteSurroundingText</c>).
    /// </summary>
    public static void InjectImeDeleteSurrounding(int beforeChars, int afterChars)
    {
        if (!s_initialized) return;
        _ = DispatchToUi(
            () => (Application.Current?.MainWindow)?.InjectAndroidImeDeleteSurrounding(beforeChars, afterChars),
            synchronous: false);
    }

    /// <summary>
    /// Runs the on-screen keyboard's action key (Next/Done/Search/Go/Send/…) on
    /// the focused editor — advancing focus or submitting as appropriate.
    /// </summary>
    public static void InjectImeEditorAction(TextInputReturnKeyType action)
    {
        if (!s_initialized) return;
        _ = DispatchToUi(
            () => (Application.Current?.MainWindow)?.InjectAndroidImeEditorAction(action),
            synchronous: false);
    }

    // Events
    public static event Action<nint>? NativeWindowCreated;
    public static event Action? NativeWindowDestroyed;
    public static event Action? Paused;
    public static event Action? Resumed;
    public static event Action? Destroying;
    public static event Action? LowMemory;
    public static event Action<float>? DensityChanged;
}
