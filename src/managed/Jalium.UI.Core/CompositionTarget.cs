using System.Runtime.InteropServices;
using Jalium.UI.Core.Platform;
using Jalium.UI.Threading;

namespace Jalium.UI;

/// <summary>
/// Provides rendering timing information and a centralized frame timer
/// for all animations. Instead of each Storyboard / UIElement creating
/// its own System.Threading.Timer, everyone subscribes to the static
/// <see cref="Rendering"/> event which fires once per frame on the UI thread.
///
/// This eliminates timer proliferation (N timers �?1) and ensures all
/// animation ticks happen in the same Dispatcher batch, so only ONE
/// render pass occurs per frame �?critical for integrated GPU performance.
/// </summary>
public static partial class CompositionTarget
{
    private static volatile int _refreshRate = 60;
    private static Timer? _frameTimer;
    private static int _subscriberCount;
    private static int _renderableWindowCount;
    private static bool _timerRunning;
    private static readonly object _timerLock = new();
    private static volatile bool _inRaiseRendering;
    // 0/1 latch: a RaiseRendering has been posted to the UI thread but has not
    // finished yet. The frame timer self-clocks on its own thread (re-armed in
    // OnFrameTick), so ticks keep coming at the refresh interval even while the
    // UI thread is busy rendering/presenting; this latch coalesces those ticks
    // to at most ONE in-flight RaiseRendering so a slow UI thread never piles up
    // a backlog of catch-up frames.
    private static int _framePosted;
    // The frame loop keeps ticking until at least this Environment.TickCount64 even
    // with NO animation subscriber, so a one-off dirty change (hover, IsSelected,
    // a property update, a popup fade sample) still gets rendered at the refresh
    // rate without needing an input event to pump anything. Extended by
    // RequestFrame() and Subscribe(); once it lapses AND there are no subscribers
    // the loop PARKS (blocks with ~0 CPU) instead of tearing its thread down.
    private static long _keepAliveUntilTick;
    // Grace window kept alive after the last frame request / subscriber leave. Long
    // enough to bridge a subscriber that briefly drops to zero (the popup fade /
    // spring "settle then re-arm" churn that used to stop+start the whole timer
    // thread ~1x/second), short enough that a single hover repaint costs only a
    // few idle ticks before the loop parks again.
    private const int KeepAliveMs = 250;
    private static bool _highResolutionTimerRequested;

    // High-resolution waitable timer (Windows 10 1803+).
    // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION provides sub-ms precision
    // without depending on timeBeginPeriod, which System.Threading.Timer
    // may ignore under NativeAOT (thread pool timers use their own resolution).
    private static nint _hrtHandle;
    private static Thread? _hrtThread;
    private static volatile bool _hrtRunning;

    // Cross-platform frame timer (non-Windows: timerfd on Linux, clock_nanosleep on Android)
    private static IFrameTimer? _nativeTimer;
    private static Thread? _nativeTimerThread;
    private static volatile bool _nativeTimerRunning;

    private static volatile bool _suspended;

    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private const uint HighResolutionTimerPeriodMs = 1;

    /// <summary>
    /// Occurs at the start of each frame, BEFORE <see cref="Rendering"/>.
    /// Window uses this to check for dirty elements that accumulated between frames
    /// (when InvalidateWindow was blocked by IsActive) and schedule a render.
    /// </summary>
    internal static event Action? FrameStarting;

    /// <summary>
    /// Occurs once per frame interval on the UI thread.
    /// All animation systems (Storyboard, UIElement animations, spring physics)
    /// should subscribe to this event instead of creating their own timers.
    /// The event fires via Dispatcher.BeginInvoke, so handlers run on the UI thread.
    /// </summary>
    public static event EventHandler? Rendering;

    /// <summary>
    /// Gets whether the frame timer is active (at least one subscriber).
    /// When active, rendering is driven by the frame timer �?external callers
    /// (mouse drag, property changes) should not schedule extra renders.
    /// </summary>
    public static bool IsActive => Volatile.Read(ref _subscriberCount) > 0;

    /// <summary>
    /// Gets whether we are currently inside the Rendering event invocation.
    /// Purely informational: the old behavior of blocking InvalidateWindow
    /// between frames was removed, so scheduling no longer consults this flag.
    /// </summary>
    internal static bool IsInRenderingPhase => _inRaiseRendering;

    /// <summary>
    /// Gets the detected monitor refresh rate in Hz (e.g., 60, 120, 144).
    /// </summary>
    public static int RefreshRate => _refreshRate;

    /// <summary>
    /// Gets the detected monitor refresh rate as the nominal target frame rate.
    /// The animation loop is uncapped �?actual FPS is determined by rendering speed.
    /// </summary>
    public static int TargetFrameRate => _refreshRate;

    /// <summary>
    /// Gets the frame interval in milliseconds for the animation frame loop, capped
    /// to the display refresh rate.
    /// <para>
    /// This USED to be a hardcoded 1 ms ("uncapped, naturally throttled by rendering
    /// speed, like a game engine render loop"). That self-throttling relied on
    /// RenderFrame BLOCKING on vsync/present. Once external present pacing made
    /// RenderFrame non-blocking (present offloaded to a thread-pool credit wait), the
    /// 1 ms one-shot loop lost its only brake and free-spun at ~1000 Hz — even a single
    /// active animation then fired RaiseRendering ~590×/s, flooding the dispatcher
    /// (~2300 ProcessQueue/s) and starving input (measured: mouse-hover feedback delayed
    /// ~1 s) while the display only presented a handful of frames per second.
    /// </para>
    /// <para>
    /// Animating faster than the display can show is pure waste, so cap the loop to the
    /// refresh interval (e.g. 60 Hz → 16 ms, 165 Hz → 6 ms; 60 Hz fallback when unknown).
    /// </para>
    /// </summary>
    public static int FrameIntervalMs => _refreshRate > 0 ? Math.Max(1, 1000 / _refreshRate) : 16;

    /// <summary>
    /// Gets the frame interval as a TimeSpan.
    /// </summary>
    public static TimeSpan FrameInterval => TimeSpan.FromMilliseconds(FrameIntervalMs);

    /// <summary>
    /// Subscribes to the frame timer. The backing System.Threading.Timer is
    /// created on the first subscriber and disposed when the last one leaves.
    /// Call <see cref="Unsubscribe"/> to balance.
    /// </summary>
    public static void Subscribe()
    {
        lock (_timerLock)
        {
            _subscriberCount++;
            UpdateTimerState();
            // Un-park the loop so this subscriber's first tick lands immediately
            // rather than after the parked wait's timeout.
            WakeLoopLocked();
        }
    }

    /// <summary>
    /// Unsubscribes from the frame timer. When the last subscriber leaves,
    /// the backing timer is disposed so there is zero overhead when idle.
    /// </summary>
    public static void Unsubscribe()
    {
        lock (_timerLock)
        {
            _subscriberCount--;
            if (_subscriberCount < 0)
            {
                _subscriberCount = 0;
            }
            UpdateTimerState();
        }
    }

    /// <summary>
    /// Notifies the frame timer that a window has become renderable (visible
    /// and not minimized). Each call must be paired with
    /// <see cref="NotifyRenderableWindowRemoved"/>. When no renderable window
    /// remains, the frame timer thread is fully stopped — including the 1ms
    /// kernel waitable timer — even if subscribers are still registered.
    /// This is what makes a minimized app drop to ~0% CPU instead of paying
    /// the cost of dispatching frames that no surface can present.
    /// </summary>
    internal static void NotifyRenderableWindowAdded()
    {
        lock (_timerLock)
        {
            _renderableWindowCount++;
            UpdateTimerState();
        }
    }

    /// <summary>
    /// Notifies the frame timer that a previously renderable window has gone
    /// non-renderable (minimized, hidden, or destroyed). When the count
    /// reaches zero, the frame timer thread is stopped.
    /// </summary>
    internal static void NotifyRenderableWindowRemoved()
    {
        lock (_timerLock)
        {
            _renderableWindowCount--;
            if (_renderableWindowCount < 0)
            {
                _renderableWindowCount = 0;
            }
            UpdateTimerState();
        }
    }

    /// <summary>
    /// Asks the central frame loop to run for at least the next keep-alive window,
    /// waking it immediately if it was parked. This is the "render whenever content
    /// is dirty, with NO input needed" entry point: <c>Window.InvalidateWindow</c>
    /// calls it so a hover / property change / popup-fade sample repaints on the
    /// self-driven vsync loop instead of waiting for a mouse event to pump the
    /// dispatcher. Cheap and lock-free on the hot path (only pokes the loop on the
    /// parked→active edge); safe to call from any thread.
    /// </summary>
    public static void RequestFrame()
    {
        long now = Environment.TickCount64;
        long previousDeadline = Interlocked.Exchange(ref _keepAliveUntilTick, now + KeepAliveMs);
        // Un-park the loop only on the idle→active edge (keep-alive had lapsed and no
        // animation subscriber is already driving ticks). During a continuous hover the
        // loop is already ticking (ShouldTick honours the keep-alive window), so we just
        // extended the deadline lock-free.
        if (now >= previousDeadline && Volatile.Read(ref _subscriberCount) == 0)
        {
            lock (_timerLock) { WakeLoopLocked(); }
        }
    }

    /// <summary>
    /// True when the frame loop should actively tick this instant: an animation
    /// subscriber is registered, OR a frame was requested within the keep-alive
    /// window. When false the loop parks (blocks with ~0 CPU) until woken.
    /// </summary>
    private static bool ShouldTick() =>
        Volatile.Read(ref _subscriberCount) > 0 ||
        Environment.TickCount64 < Interlocked.Read(ref _keepAliveUntilTick);

    /// <summary>
    /// Signals the timer object so a parked loop iteration returns at once and
    /// re-evaluates <see cref="ShouldTick"/>. Caller must hold <see cref="_timerLock"/>
    /// (so it cannot race the handle close in <see cref="StopTimer"/>).
    /// </summary>
    private static void WakeLoopLocked()
    {
        if (_hrtHandle != nint.Zero)
        {
            long immediate = -1;
            SetWaitableTimerEx(_hrtHandle, in immediate, 0, nint.Zero, nint.Zero, nint.Zero, 0);
        }
        else
        {
            _nativeTimer?.Arm(1);
        }
        // The pre-1803 fallback System.Threading.Timer fires periodically on its own
        // (OnFrameTick early-outs via ShouldTick when parked), so it needs no poke.
    }

    /// <summary>
    /// Decides whether the timer THREAD should exist and starts/stops it to match.
    /// The thread lives whenever a renderable window exists; whether it actively
    /// ticks or parks is decided per-iteration by <see cref="ShouldTick"/>. Tying
    /// the thread's lifetime to the window (not to the subscriber count) is what
    /// stops the ~1x/second stop+start thrash when a sole animation subscriber
    /// briefly drops to zero. Caller must hold <see cref="_timerLock"/>.
    /// </summary>
    private static void UpdateTimerState()
    {
        bool shouldRun = _renderableWindowCount > 0;
        if (shouldRun == _timerRunning) return;

        if (shouldRun)
        {
            // Clear any stale in-flight latch from a previous run so the first
            // tick of this run is guaranteed to post a RaiseRendering.
            Volatile.Write(ref _framePosted, 0);
            StartTimer();
            _timerRunning = true;
        }
        else
        {
            StopTimer();
            _timerRunning = false;
        }
    }

    /// <summary>
    /// Suspends rendering. Frame tick callbacks are skipped while suspended.
    /// Used when the Android app is paused to save battery.
    /// </summary>
    internal static void SuspendRendering() => _suspended = true;

    /// <summary>
    /// Resumes rendering after a suspend. The next frame tick will dispatch normally.
    /// </summary>
    internal static void ResumeRendering() => _suspended = false;

    /// <summary>
    /// Updates the detected refresh rate. Called by Window when the monitor changes.
    /// </summary>
    /// <param name="rate">The detected refresh rate in Hz.</param>
    internal static void UpdateRefreshRate(int rate)
    {
        if (rate > 0)
        {
            _refreshRate = rate;
        }
    }

    private static void StartTimer()
    {
        if (s_isWindows)
        {
            StartTimerWindows();
        }
        else
        {
            StartTimerNative();
        }
    }

    private static void StartTimerWindows()
    {
        // Try high-resolution waitable timer (Windows 10 1803+).
        // This provides guaranteed sub-ms one-shot timing independent of
        // timeBeginPeriod — critical for NativeAOT where System.Threading.Timer
        // may fire at the default ~15.6ms OS resolution, capping FPS to ~60.
        _hrtHandle = CreateWaitableTimerExW(nint.Zero, nint.Zero,
            CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            TIMER_MODIFY_STATE | SYNCHRONIZE);

        if (_hrtHandle != nint.Zero)
        {
            _hrtRunning = true;
            // Arm initial 1ms one-shot (negative = relative, in 100ns units).
            long dueTime = -10_000L * FrameIntervalMs;
            SetWaitableTimerEx(_hrtHandle, in dueTime, 0, nint.Zero, nint.Zero, nint.Zero, 0);
            _hrtThread = new Thread(HighResTimerLoop)
            {
                IsBackground = true,
                Name = "Jalium.FrameTimer"
            };
            _hrtThread.Start();
            return;
        }

        // Fallback: timeBeginPeriod + System.Threading.Timer (pre-1803 or failure).
        RequestHighResolutionTimer();
        _frameTimer = new Timer(OnFrameTick, null, FrameIntervalMs, FrameIntervalMs); // periodic: self-clocks
    }

    private static void StartTimerNative()
    {
        // Cross-platform path: use jalium.native.platform timer (timerfd / clock_nanosleep)
        try
        {
            _nativeTimer = new NativeFrameTimer();
            _nativeTimerRunning = true;
            _nativeTimer.Arm(FrameIntervalMs * 1000L); // microseconds
            _nativeTimerThread = new Thread(NativeTimerLoop)
            {
                IsBackground = true,
                Name = "Jalium.FrameTimer"
            };
            _nativeTimerThread.Start();
        }
        catch
        {
            // Fallback to System.Threading.Timer
            _nativeTimer?.Dispose();
            _nativeTimer = null;
            _frameTimer = new Timer(OnFrameTick, null, FrameIntervalMs, FrameIntervalMs); // periodic: self-clocks
        }
    }

    private static void StopTimer()
    {
        // Stop native cross-platform timer
        if (_nativeTimer != null)
        {
            _nativeTimerRunning = false;
            _nativeTimer.Arm(1); // Arm with tiny value to unblock wait
            // The loop RE-ARMS this timer lock-free at the top of each iteration, so
            // Dispose() must not run while the loop thread might still touch it. Only
            // dispose once the thread has actually exited; on the (practically
            // impossible) Join timeout, leak the native timer rather than free it
            // under an active writer (use-after-free). _hrtRunning/_nativeTimerRunning
            // are volatile and the wait has a 1 s backstop, so the thread exits
            // promptly and the Join normally returns at once.
            bool exited = _nativeTimerThread == null || _nativeTimerThread.Join(500);
            _nativeTimerThread = null;
            if (exited)
            {
                _nativeTimer.Dispose();
            }
            _nativeTimer = null;
        }

        // Stop Windows high-resolution timer
        if (_hrtHandle != nint.Zero)
        {
            _hrtRunning = false;
            // Signal timer immediately to unblock the wait thread.
            long immediate = -1;
            SetWaitableTimerEx(_hrtHandle, in immediate, 0, nint.Zero, nint.Zero, nint.Zero, 0);
            // Same rule as the native path: the loop re-arms _hrtHandle lock-free, so
            // CloseHandle only after the thread has exited; leak the handle on the
            // impossible timeout rather than close it under an active writer.
            bool exited = _hrtThread == null || _hrtThread.Join(500);
            _hrtThread = null;
            if (exited)
            {
                CloseHandle(_hrtHandle);
            }
            _hrtHandle = nint.Zero;
        }
        else if (_frameTimer != null)
        {
            _frameTimer.Dispose();
            _frameTimer = null;
            if (s_isWindows) ReleaseHighResolutionTimer();
        }
    }

    /// <summary>
    /// Background thread loop for the high-resolution waitable timer path (Windows).
    /// Persistent: it lives for as long as a renderable window exists and PARKS (blocks)
    /// while there is no work, instead of being torn down and recreated every time the
    /// sole animation subscriber briefly drops to zero (which used to stop+start this
    /// thread ~1x/second and starve the popup fade / hover repaint). When active it
    /// self-clocks by re-arming the one-shot each iteration — decoupled from UI-thread /
    /// present latency. This thread owns <c>_hrtHandle</c> for its whole lifetime and
    /// <see cref="StopTimer"/> Join()s it BEFORE CloseHandle, so the handle is always
    /// valid here and no lock is taken in the hot path (avoiding a deadlock against
    /// StopTimer's Join under _timerLock).
    /// </summary>
    private static void HighResTimerLoop()
    {
        while (_hrtRunning)
        {
            if (ShouldTick())
            {
                // Active: re-arm the next one-shot (negative = relative, 100ns units)
                // and wait for it to fire (~one refresh interval), then dispatch.
                long dueTime = -10_000L * FrameIntervalMs;
                SetWaitableTimerEx(_hrtHandle, in dueTime, 0, nint.Zero, nint.Zero, nint.Zero, 0);
                WaitForSingleObject(_hrtHandle, 1000);
                if (!_hrtRunning) break;
                OnFrameTick(null);
            }
            else
            {
                // Parked: nothing to render. Block on the handle — woken at once by
                // WakeLoopLocked's immediate SetWaitableTimerEx (Subscribe / RequestFrame),
                // or re-check after 1 s. An idle window costs ~0 CPU and this thread is
                // NOT destroyed, so a subscribe/dirty burst never pays thread teardown.
                WaitForSingleObject(_hrtHandle, 1000);
                if (!_hrtRunning) break;
            }
        }
    }

    /// <summary>
    /// Background thread loop for the native cross-platform timer (Linux/Android).
    /// </summary>
    private static void NativeTimerLoop()
    {
        while (_nativeTimerRunning)
        {
            if (ShouldTick())
            {
                // Active: re-arm on THIS thread before waiting, decoupled from the
                // UI-thread render round trip (see HighResTimerLoop for rationale).
                _nativeTimer?.Arm(FrameIntervalMs * 1000L); // microseconds
                bool fired = _nativeTimer?.Wait(1000) ?? false;
                if (!_nativeTimerRunning) break;
                if (fired)
                {
                    OnFrameTick(null);
                }
            }
            else
            {
                // Parked: arm a long interval; woken early by WakeLoopLocked's Arm(1).
                _nativeTimer?.Arm(1_000_000L); // ~1 s in microseconds
                _nativeTimer?.Wait(2000);
                if (!_nativeTimerRunning) break;
            }
        }
    }

    private static void OnFrameTick(object? state)
    {
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.TICK);
        Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_TIMER_RUNNING, _timerRunning ? 1 : 0);
        Jalium.UI.Diagnostics.HoverTrace.Gauge(Jalium.UI.Diagnostics.HoverTrace.G_SUBS, _subscriberCount);

        // Skip dispatching while suspended (e.g. Android app paused). The owning
        // timer loop keeps clocking, so rendering resumes immediately when
        // ResumeRendering clears the flag.
        if (_suspended)
        {
            return;
        }

        // Nothing to drive this frame (no subscriber and the keep-alive window has
        // lapsed). Covers the pre-1803 periodic fallback Timer, which fires every
        // interval regardless, and a keep-alive that expired during the wait.
        if (!ShouldTick())
        {
            return;
        }

        // Marshal this frame to the UI thread. The frame clock self-runs on the
        // owning timer loop (HighResTimerLoop / NativeTimerLoop / periodic Timer),
        // decoupled from how long RaiseRendering / rendering / present take on the
        // UI thread. The old code re-armed the NEXT frame only AFTER RaiseRendering
        // had round-tripped through the UI dispatcher; once present pacing made
        // RenderFrame non-blocking, that round trip stretched out and the whole
        // animation frame loop collapsed to the 1000 ms WaitForSingleObject
        // fallback (~1 tick/s) whenever the UI thread was busy or idle — so
        // on-screen animations (popup fade-in, spring physics, etc.) crawled or
        // froze until mouse input happened to pump the loop.
        //
        // Coalesce to at most ONE RaiseRendering in flight: if the UI thread has
        // not finished the previous frame, drop this tick instead of stacking a
        // backlog of catch-up frames.
        if (Interlocked.CompareExchange(ref _framePosted, 1, 0) != 0)
        {
            return;
        }

        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            try
            {
                var op = dispatcher.BeginInvoke(RaiseRendering);
                // If the op is aborted synchronously (dispatcher shutting down),
                // its action never runs, so RaiseRendering's finally never clears
                // the latch — release it here, otherwise the self-clocking loop
                // would keep ticking but never post another frame.
                if (op.Status == DispatcherOperationStatus.Aborted)
                {
                    Volatile.Write(ref _framePosted, 0);
                }
                return;
            }
            catch
            {
                // Dispatcher unavailable (shutdown race): release the latch so a
                // later tick can retry.
                Volatile.Write(ref _framePosted, 0);
                return;
            }
        }

        // No dispatcher yet: release the latch and try again next tick.
        Volatile.Write(ref _framePosted, 0);
    }

    private static void RaiseRendering()
    {
        Jalium.UI.Diagnostics.HoverTrace.Bump(Jalium.UI.Diagnostics.HoverTrace.RAISE);
        try
        {
            // Fire FrameStarting BEFORE Rendering. Window uses this to schedule
            // a render for dirty elements that accumulated between frames.
            InvokeFrameStartingHandlers();

            // Mark that we're inside Rendering event invocation.
            _inRaiseRendering = true;
            try
            {
                InvokeRenderingHandlers();
            }
            finally
            {
                _inRaiseRendering = false;
            }

            // Safety net: if all Rendering subscribers have been removed but the
            // subscriber count leaked (e.g. Subscribe without matching Unsubscribe),
            // stop the timer to prevent an empty frame loop burning CPU/GPU.
            // UpdateTimerState -> StopTimer sets _hrtRunning / _nativeTimerRunning
            // false, so the self-clocking loop exits on its next iteration.
            var handlers = Rendering;
            if (handlers == null || handlers.GetInvocationList().Length == 0)
            {
                lock (_timerLock)
                {
                    if (_subscriberCount > 0)
                    {
                        _subscriberCount = 0;
                        UpdateTimerState();
                    }
                }
            }
        }
        finally
        {
            // Release the in-flight latch so the next timer tick may post a fresh
            // frame. Re-arm is NOT done here anymore — the timer self-clocks on its
            // own thread (see OnFrameTick), decoupled from this UI-thread round trip.
            Volatile.Write(ref _framePosted, 0);
        }
    }

    private static void InvokeFrameStartingHandlers()
    {
        var handlers = FrameStarting;
        if (handlers == null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
                // Keep the frame loop alive even if one subscriber fails.
            }
        }
    }

    private static void InvokeRenderingHandlers()
    {
        var handlers = Rendering;
        if (handlers == null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(null, EventArgs.Empty);
            }
            catch
            {
                // Keep the frame loop alive even if one subscriber fails.
            }
        }
    }

    private static void RequestHighResolutionTimer()
    {
        if (_highResolutionTimerRequested)
        {
            return;
        }

        if (TimeBeginPeriod(HighResolutionTimerPeriodMs) == 0)
        {
            _highResolutionTimerRequested = true;
        }
    }

    private static void ReleaseHighResolutionTimer()
    {
        if (!_highResolutionTimerRequested)
        {
            return;
        }

        _ = TimeEndPeriod(HighResolutionTimerPeriodMs);
        _highResolutionTimerRequested = false;
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint uPeriod);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint uPeriod);

    // ── High-resolution waitable timer (kernel32) ──

    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_MODIFY_STATE = 0x0002;
    private const uint SYNCHRONIZE = 0x00100000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateWaitableTimerExW(
        nint lpTimerAttributes, nint lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimerEx(
        nint hTimer, in long lpDueTime, int lPeriod,
        nint pfnCompletionRoutine, nint lpArgToCompletionRoutine,
        nint wakeContext, uint tolerableDelay);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);
}
