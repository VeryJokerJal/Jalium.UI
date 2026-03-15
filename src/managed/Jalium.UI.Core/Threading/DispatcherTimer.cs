using Jalium.UI;

namespace Jalium.UI.Threading;

/// <summary>
/// A timer that is integrated into the <see cref="Dispatcher"/> queue which is
/// processed at a specified interval of time and at a specified priority.
///
/// Optimization: when the interval matches the frame interval (8ms or 16ms),
/// the timer piggybacks on <see cref="CompositionTarget.Rendering"/> instead
/// of creating its own System.Threading.Timer. This eliminates timer
/// proliferation — all frame-rate timers share a single backing timer.
/// </summary>
public sealed class DispatcherTimer
{
    private readonly Dispatcher _dispatcher;
    private Timer? _timer;
    private TimeSpan _interval;
    private bool _isEnabled;
    private object? _tag;
    private bool _useCompositionTarget; // True when piggybacking on frame timer

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
        : this(priority, Dispatcher.CurrentDispatcher ?? Dispatcher.GetForCurrentThread())
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
        _interval = interval;

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
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;

                if (_isEnabled)
                {
                    StartTimer();
                }
                else
                {
                    StopTimer();
                }
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
        get => _interval;
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

            bool wasRunning = _isEnabled;

            if (wasRunning)
            {
                StopTimer();
            }

            _interval = value;

            if (wasRunning)
            {
                StartTimer();
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
        if (_timer != null || _useCompositionTarget)
        {
            return;
        }

        if (ShouldUseCompositionTarget())
        {
            // Piggyback on the centralized frame timer.
            // All frame-rate DispatcherTimers share a single System.Threading.Timer.
            _useCompositionTarget = true;
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            CompositionTarget.Subscribe();
            return;
        }

        // Non-frame-rate interval: use a dedicated timer (e.g., caret blink at 500ms)
        int intervalMs = Math.Max(1, (int)_interval.TotalMilliseconds);
        _timer = new Timer(
            OnTimerCallback,
            null,
            intervalMs,
            intervalMs);
    }

    private void StopTimer()
    {
        if (_useCompositionTarget)
        {
            _useCompositionTarget = false;
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            CompositionTarget.Unsubscribe();
            return;
        }

        if (_timer == null)
        {
            return;
        }

        _timer.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Called by CompositionTarget.Rendering on the UI thread.
    /// Already on UI thread — raise tick directly.
    /// </summary>
    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (!_isEnabled) return;
        RaiseTick();
    }

    private void OnTimerCallback(object? state)
    {
        if (!_isEnabled)
        {
            return;
        }

        // Dispatch the tick event to the associated dispatcher's thread
        try
        {
            if (_dispatcher.CheckAccess())
            {
                // Already on the dispatcher thread
                RaiseTick();
            }
            else
            {
                // Marshal to the dispatcher thread
                _dispatcher.BeginInvoke(RaiseTick);
            }
        }
        catch
        {
            // Ignore exceptions if the dispatcher is shutting down
        }
    }

    private void RaiseTick()
    {
        if (!_isEnabled)
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
