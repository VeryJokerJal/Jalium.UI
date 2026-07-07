using Jalium.UI;
using System.Diagnostics;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Defines a segment of time over which output values are produced.
/// </summary>
public abstract class AnimationTimeline : Timeline, IAnimationTimeline
{
    /// <summary>
    /// Gets the type of value that this animation produces.
    /// </summary>
    public abstract Type TargetPropertyType { get; }

    /// <summary>
    /// Gets the fill behavior as the core interface type.
    /// </summary>
    AnimationFillBehavior IAnimationTimeline.AnimationFillBehavior =>
        FillBehavior == FillBehavior.HoldEnd ? AnimationFillBehavior.HoldEnd : AnimationFillBehavior.Stop;

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    public abstract object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock);

    /// <summary>
    /// Gets the current animated value using the interface clock type.
    /// </summary>
    object IAnimationTimeline.GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, IAnimationClock clock)
    {
        if (clock is AnimationClock animClock)
        {
            return GetCurrentValue(defaultOriginValue, defaultDestinationValue, animClock);
        }
        throw new ArgumentException("Clock must be an AnimationClock", nameof(clock));
    }

    /// <summary>
    /// Creates a clock for this timeline.
    /// </summary>
    public IAnimationClock CreateClock()
    {
        return new AnimationClock(this);
    }

    /// <summary>
    /// Gets whether this animation is additive.
    /// </summary>
    public virtual bool IsAdditive => false;

    /// <summary>
    /// Gets whether this animation is cumulative.
    /// </summary>
    public virtual bool IsCumulative => false;
}

/// <summary>
/// Provides a base class for animations that animate a specific type.
/// </summary>
public abstract class AnimationTimeline<T> : AnimationTimeline
{
    /// <summary>
    /// Gets the target property type.
    /// </summary>
    public override Type TargetPropertyType => typeof(T);

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var result = GetCurrentValueCore(
            (T)defaultOriginValue,
            (T)defaultDestinationValue,
            animationClock);
        return result!;
    }

    /// <summary>
    /// Gets the current animated value of type T.
    /// </summary>
    protected abstract T GetCurrentValueCore(T defaultOriginValue, T defaultDestinationValue, AnimationClock animationClock);
}

/// <summary>
/// Represents a clock that controls an animation timeline.
/// </summary>
public sealed class AnimationClock : IAnimationClock
{
    private readonly Timeline _timeline;
    // High-resolution monotonic timestamps (Stopwatch ticks). DateTime.Now has only
    // ~15.6ms resolution on Windows → animation time quantizes into 15.6ms steps →
    // visible micro-stutter/judder regardless of frame rate. Stopwatch is sub-µs and
    // monotonic (immune to wall-clock adjustments).
    private long _startTime;
    private long _firstStartTime;
    private bool _isRunning;
    private double _currentProgress;
    private bool _isReversing;
    private int _repeatCount;
    private bool _isPaused;
    private long _pauseTimestamp;
    // Completion is an independent terminal state rather than derived from
    // !_isRunning: FillBehavior.Stop must freeze progress at 0 on the completion
    // frame, and only an explicit flag lets Tick return before the tail
    // progress recomputation overwrites it.
    private bool _isCompleted;

    /// <summary>
    /// Creates a new animation clock for the specified timeline.
    /// </summary>
    public AnimationClock(Timeline timeline)
    {
        _timeline = timeline;
    }

    /// <summary>
    /// Gets the timeline associated with this clock.
    /// </summary>
    public Timeline Timeline => _timeline;

    /// <summary>
    /// Gets the timeline as the interface type.
    /// </summary>
    IAnimationTimeline? IAnimationClock.Timeline => _timeline as IAnimationTimeline;

    /// <summary>
    /// Gets the current progress of the animation (0.0 to 1.0).
    /// </summary>
    public double CurrentProgress => _currentProgress;

    /// <summary>
    /// Gets the current time of the animation.
    /// </summary>
    public TimeSpan? CurrentTime { get; private set; }

    /// <summary>
    /// Gets whether this clock is currently running. Stays <see langword="false"/>
    /// while paused (conservative compatibility with the previous implementation,
    /// where Pause simply cleared the running flag).
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets or sets the controller for this clock.
    /// </summary>
    public ClockController? Controller { get; set; }

    /// <summary>
    /// Occurs when the animation completes.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Gets whether the clock is paused.
    /// </summary>
    bool IAnimationClock.IsPaused => _isPaused;

    /// <summary>
    /// Gets whether the clock has finished its active period. A clock that was
    /// stopped externally (never to be ticked again) also reports completed so
    /// frame drivers drop it; a paused or start-pending clock does not.
    /// </summary>
    bool IAnimationClock.IsCompleted => _isCompleted || (!_isRunning && !_isPaused);

    /// <summary>
    /// Starts the animation using the unified frame timestamp when inside a
    /// frame (all animations started in the same frame share one t0).
    /// </summary>
    public void Begin()
    {
        BeginAt(Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow);
    }

    void IAnimationClock.BeginAt(long frameTimestamp) => BeginAt(frameTimestamp);

    /// <summary>
    /// Starts the animation at an explicit start timestamp (Stopwatch ticks).
    /// </summary>
    internal void BeginAt(long timestamp)
    {
        _startTime = timestamp;
        if (_timeline.BeginTime.HasValue)
        {
            // BeginTime delays the start: shift the start timestamp into the future so
            // elapsed stays negative until the delay passes (same as old DateTime.Add).
            _startTime += (long)(_timeline.BeginTime.Value.TotalSeconds * Stopwatch.Frequency);
        }
        _firstStartTime = _startTime;
        _isRunning = true;
        _currentProgress = 0;
        _isReversing = false;
        _repeatCount = 0;
        _isPaused = false;
        _isCompleted = false;
    }

    /// <summary>
    /// Stops the animation.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _isPaused = false;
    }

    /// <summary>
    /// Pauses the animation. The pause timestamp is recorded so Resume can shift
    /// the start times by the paused duration and continue from the same point.
    /// </summary>
    public void Pause()
    {
        if (_isPaused || _isCompleted) return;

        _isPaused = true;
        _isRunning = false;
        _pauseTimestamp = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
    }

    /// <summary>
    /// Resumes a paused animation, compensating for the time spent paused.
    /// </summary>
    public void Resume()
    {
        if (!_isPaused) return;

        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        long delta = now - _pauseTimestamp;
        _startTime += delta;
        _firstStartTime += delta;
        _isPaused = false;
        _isRunning = true;
    }

    /// <summary>
    /// Moves the clock to a new position in time by shifting its start timestamp,
    /// then lets the next tick recompute progress from there. Re-arms a completed
    /// or paused clock.
    /// </summary>
    /// <param name="offset">The seek offset.</param>
    /// <param name="origin">Whether <paramref name="offset"/> is measured forward
    /// from the begin time or backward from the end of the duration.</param>
    public void Seek(TimeSpan offset, TimeSeekOrigin origin)
    {
        var duration = _timeline.Duration.HasTimeSpan
            ? _timeline.Duration.TimeSpan
            : TimeSpan.FromSeconds(1);

        var target = origin == TimeSeekOrigin.Duration ? duration - offset : offset;
        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;

        // Timeline time runs at SpeedRatio × wall time, so the wall-clock offset
        // that produces `target` timeline-time is target / SpeedRatio.
        double speedRatio = Math.Max(_timeline.SpeedRatio, 1e-9);
        _startTime = now - (long)(target.TotalSeconds / speedRatio * Stopwatch.Frequency);
        _isReversing = false;
        _repeatCount = 0;
        _isCompleted = false;
        _isRunning = true;
        _isPaused = false;
    }

    void IAnimationClock.Seek(TimeSpan offset, AnimationSeekOrigin origin)
    {
        Seek(offset, origin == AnimationSeekOrigin.Duration
            ? TimeSeekOrigin.Duration
            : TimeSeekOrigin.BeginTime);
    }

    /// <summary>
    /// Updates the animation progress from the current time. Prefer
    /// <see cref="Tick(long)"/> with the unified frame timestamp.
    /// </summary>
    public void Tick()
    {
        Tick(Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Updates the animation progress using the unified frame timestamp
    /// (Stopwatch ticks) shared by every clock ticked in the same frame.
    /// </summary>
    public void Tick(long frameTimestamp)
    {
        if (!_isRunning || _isPaused || _isCompleted) return;

        var elapsed = Stopwatch.GetElapsedTime(_startTime, frameTimestamp);
        var duration = _timeline.Duration.HasTimeSpan
            ? _timeline.Duration.TimeSpan
            : TimeSpan.FromSeconds(1);

        // Apply speed ratio
        elapsed = TimeSpan.FromTicks((long)(elapsed.Ticks * _timeline.SpeedRatio));

        CurrentTime = elapsed;

        // Calculate progress
        var rawProgress = elapsed.TotalMilliseconds / duration.TotalMilliseconds;

        // Handle repeating
        if (rawProgress >= 1.0)
        {
            if (_timeline.AutoReverse && !_isReversing)
            {
                _isReversing = true;
                _startTime = frameTimestamp;
                rawProgress = 1.0;
            }
            else
            {
                var rb = _timeline.RepeatBehavior;
                _repeatCount++;

                bool shouldRepeat = false;
                if (rb == RepeatBehavior.Forever)
                {
                    shouldRepeat = true;
                }
                else if (rb.HasCount)
                {
                    shouldRepeat = _repeatCount < rb.Count;
                }
                else if (rb.HasDuration)
                {
                    // Calculate total elapsed since first start
                    shouldRepeat = Stopwatch.GetElapsedTime(_firstStartTime, frameTimestamp) < rb.Duration;
                }

                if (shouldRepeat)
                {
                    _startTime = frameTimestamp;
                    _isReversing = false;
                    rawProgress = 0;
                }
                else
                {
                    // Natural completion: set the terminal state and return before
                    // the tail progress recomputation below — FillBehavior.Stop
                    // must expose progress 0 on the completion frame (previously
                    // dead code: the tail overwrote it back to 1.0).
                    _isRunning = false;
                    _isCompleted = true;
                    _currentProgress = _timeline.FillBehavior == FillBehavior.Stop ? 0 : 1.0;
                    Completed?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }

        // Handle auto-reverse
        if (_isReversing)
        {
            _currentProgress = 1.0 - Math.Min(1.0, rawProgress);
        }
        else
        {
            _currentProgress = Math.Min(1.0, rawProgress);
        }
    }
}

// ClockController and TimeSeekOrigin are defined in Clock.cs
