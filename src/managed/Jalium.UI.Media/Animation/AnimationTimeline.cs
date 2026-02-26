using Jalium.UI;

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
    private DateTime _startTime;
    private DateTime _firstStartTime;
    private bool _isRunning;
    private double _currentProgress;
    private bool _isReversing;
    private int _repeatCount;

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
    /// Gets whether this clock is currently running.
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
    /// Starts the animation.
    /// </summary>
    public void Begin()
    {
        _startTime = DateTime.Now;
        if (_timeline.BeginTime.HasValue)
        {
            _startTime = _startTime.Add(_timeline.BeginTime.Value);
        }
        _firstStartTime = _startTime;
        _isRunning = true;
        _currentProgress = 0;
        _isReversing = false;
        _repeatCount = 0;
    }

    /// <summary>
    /// Stops the animation.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Pauses the animation.
    /// </summary>
    public void Pause()
    {
        _isRunning = false;
    }

    /// <summary>
    /// Resumes a paused animation.
    /// </summary>
    public void Resume()
    {
        _isRunning = true;
    }

    /// <summary>
    /// Updates the animation progress.
    /// </summary>
    public void Tick()
    {
        if (!_isRunning) return;

        var elapsed = DateTime.Now - _startTime;
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
                _startTime = DateTime.Now;
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
                    shouldRepeat = (DateTime.Now - _firstStartTime) < rb.Duration;
                }

                if (shouldRepeat)
                {
                    _startTime = DateTime.Now;
                    _isReversing = false;
                    rawProgress = 0;
                }
                else
                {
                    rawProgress = 1.0;
                    _isRunning = false;

                    if (_timeline.FillBehavior == FillBehavior.Stop)
                    {
                        _currentProgress = 0;
                    }

                    Completed?.Invoke(this, EventArgs.Empty);
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
