using System.Runtime.InteropServices;

namespace Jalium.UI;

/// <summary>
/// Provides rendering timing information and a centralized frame timer
/// for all animations. Instead of each Storyboard / UIElement creating
/// its own System.Threading.Timer, everyone subscribes to the static
/// <see cref="Rendering"/> event which fires once per frame on the UI thread.
///
/// This eliminates timer proliferation (N timers → 1) and ensures all
/// animation ticks happen in the same Dispatcher batch, so only ONE
/// render pass occurs per frame — critical for integrated GPU performance.
/// </summary>
public static partial class CompositionTarget
{
    private static volatile int _refreshRate = 60;
    private static Timer? _frameTimer;
    private static int _subscriberCount;
    private static readonly object _timerLock = new();
    private static volatile bool _inRaiseRendering;
    private static bool _highResolutionTimerRequested;

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
    /// When active, rendering is driven by the frame timer — external callers
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
    /// The animation loop is uncapped — actual FPS is determined by rendering speed.
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
        RequestHighResolutionTimer();

        // One-shot timer: fires once, then waits for re-arm after RaiseRendering
        // completes. This guarantees at most ONE RaiseRendering in-flight at any
        // time, preventing callback accumulation that causes iGPU hangs.
        _frameTimer = new Timer(OnFrameTick, null, FrameIntervalMs, Timeout.Infinite);
    }

    private static void StopTimer()
    {
        _frameTimer?.Dispose();
        _frameTimer = null;
        ReleaseHighResolutionTimer();
    }

    private static void OnFrameTick(object? state)
    {
        // One-shot: this callback fires exactly once. No guard needed.
        // Marshal to UI thread — one PostMessage per frame.
        Dispatcher.MainDispatcher?.BeginInvoke(RaiseRendering);
    }

    private static void RaiseRendering()
    {
        // Fire FrameStarting BEFORE Rendering — Window uses this to schedule
        // a render for dirty elements that accumulated between frames.
        FrameStarting?.Invoke();

        // Mark that we're inside Rendering event invocation.
        // During this phase, InvalidateWindow() is allowed to schedule a render.
        // Outside this phase (between frames), it's blocked when IsActive=true,
        // preventing mouse/interaction from triggering extra renders on iGPU.
        _inRaiseRendering = true;
        Rendering?.Invoke(null, EventArgs.Empty);
        _inRaiseRendering = false;

        // Defer re-arm via BeginInvoke so it runs AFTER ProcessRender completes.
        // ProcessQueue order: [RaiseRendering] → [ProcessRender → RenderFrame] → [RearmTimer]
        // This guarantees the next timer fires FrameIntervalMs after rendering finishes,
        // creating a gap for the message pump to process mouse/input messages.
        Dispatcher.MainDispatcher?.BeginInvoke(RearmTimer);
    }

    private static void RearmTimer()
    {
        lock (_timerLock)
        {
            if (_frameTimer != null && _subscriberCount > 0)
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
}
