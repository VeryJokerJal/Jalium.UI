using System.Runtime.InteropServices;

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
    private static readonly object _timerLock = new();
    private static volatile bool _inRaiseRendering;
    private static bool _highResolutionTimerRequested;

    // High-resolution waitable timer (Windows 10 1803+).
    // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION provides sub-ms precision
    // without depending on timeBeginPeriod, which System.Threading.Timer
    // may ignore under NativeAOT (thread pool timers use their own resolution).
    private static nint _hrtHandle;
    private static Thread? _hrtThread;
    private static volatile bool _hrtRunning;

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
    /// During this phase, animation handlers' InvalidateVisual calls are allowed
    /// to schedule a render. Outside this phase (between frames), they are blocked.
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
    /// Gets the frame interval in milliseconds for the animation frame loop.
    /// Uncapped (1ms): the actual FPS is limited only by rendering speed and
    /// message pump overhead. On fast dGPU: hundreds of FPS. On iGPU: natural
    /// throttle to achievable rate. Like a game engine render loop.
    /// </summary>
    public static int FrameIntervalMs => 1;

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
            if (_subscriberCount == 1)
            {
                StartTimer();
            }
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
            if (_subscriberCount <= 0)
            {
                _subscriberCount = 0;
                StopTimer();
            }
        }
    }

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
        _frameTimer = new Timer(OnFrameTick, null, FrameIntervalMs, Timeout.Infinite);
    }

    private static void StopTimer()
    {
        if (_hrtHandle != nint.Zero)
        {
            _hrtRunning = false;
            // Signal timer immediately to unblock the wait thread.
            long immediate = -1;
            SetWaitableTimerEx(_hrtHandle, in immediate, 0, nint.Zero, nint.Zero, nint.Zero, 0);
            _hrtThread?.Join(500);
            _hrtThread = null;
            CloseHandle(_hrtHandle);
            _hrtHandle = nint.Zero;
        }
        else
        {
            _frameTimer?.Dispose();
            _frameTimer = null;
            ReleaseHighResolutionTimer();
        }
    }

    /// <summary>
    /// Background thread loop for the high-resolution waitable timer path.
    /// Waits for the timer to fire (one-shot), then dispatches OnFrameTick.
    /// The timer is re-armed by <see cref="RearmTimer"/> after rendering completes,
    /// preserving the one-shot guarantee: at most one RaiseRendering in-flight.
    /// </summary>
    private static void HighResTimerLoop()
    {
        while (_hrtRunning)
        {
            uint result = WaitForSingleObject(_hrtHandle, 1000);
            if (!_hrtRunning) break;
            if (result == 0) // WAIT_OBJECT_0
            {
                OnFrameTick(null);
            }
        }
    }

    private static void OnFrameTick(object? state)
    {
        // One-shot: this callback fires exactly once. No guard needed.
        // Marshal to UI thread with one posted message per frame.
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            try
            {
                dispatcher.BeginInvoke(RaiseRendering);
                return;
            }
            catch
            {
                // Fall through to direct re-arm. We'll retry on the next tick.
            }
        }

        RearmTimer();
    }

    private static void RaiseRendering()
    {
        // Fire FrameStarting BEFORE Rendering. Window uses this to schedule
        // a render for dirty elements that accumulated between frames.
        InvokeFrameStartingHandlers();

        // Mark that we're inside Rendering event invocation.
        // During this phase, InvalidateWindow() is allowed to schedule a render.
        // Outside this phase (between frames), it's blocked when IsActive=true,
        // preventing mouse/interaction from triggering extra renders on iGPU.
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
        var handlers = Rendering;
        if (handlers == null || handlers.GetInvocationList().Length == 0)
        {
            lock (_timerLock)
            {
                if (_subscriberCount > 0)
                {
                    _subscriberCount = 0;
                    StopTimer();
                    return; // Don't re-arm — timer is stopped.
                }
            }
        }

        // Defer re-arm via BeginInvoke so it runs AFTER ProcessRender completes.
        // ProcessQueue order: [RaiseRendering] -> [ProcessRender -> RenderFrame] -> [RearmTimer]
        // This guarantees the next timer fires after rendering finishes.
        var dispatcher = Dispatcher.MainDispatcher;
        if (dispatcher != null)
        {
            try
            {
                dispatcher.BeginInvoke(RearmTimer);
                return;
            }
            catch
            {
                // Fall through to direct re-arm.
            }
        }

        RearmTimer();
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

    private static void RearmTimer()
    {
        lock (_timerLock)
        {
            if (_subscriberCount <= 0) return;

            if (_hrtHandle != nint.Zero)
            {
                long dueTime = -10_000L * FrameIntervalMs;
                SetWaitableTimerEx(_hrtHandle, in dueTime, 0, nint.Zero, nint.Zero, nint.Zero, 0);
            }
            else if (_frameTimer != null)
            {
                _frameTimer.Change(FrameIntervalMs, Timeout.Infinite);
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
