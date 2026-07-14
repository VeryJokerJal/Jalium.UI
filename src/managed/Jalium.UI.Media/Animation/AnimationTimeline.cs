using System.Diagnostics;

namespace Jalium.UI.Media.Animation;

/// <summary>Defines a timeline that produces animated values.</summary>
public abstract class AnimationTimeline : Timeline, IAnimationTimeline
{
    public static readonly DependencyProperty IsAdditiveProperty =
        DependencyProperty.Register(
            "IsAdditive",
            typeof(bool),
            typeof(AnimationTimeline),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsCumulativeProperty =
        DependencyProperty.Register(
            "IsCumulative",
            typeof(bool),
            typeof(AnimationTimeline),
            new PropertyMetadata(false));

    public abstract Type TargetPropertyType { get; }

    public virtual bool IsDestinationDefault => false;

    AnimationFillBehavior IAnimationTimeline.AnimationFillBehavior =>
        FillBehavior == FillBehavior.HoldEnd
            ? AnimationFillBehavior.HoldEnd
            : AnimationFillBehavior.Stop;

    public virtual object GetCurrentValue(
        object defaultOriginValue,
        object defaultDestinationValue,
        AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(defaultOriginValue);
        ArgumentNullException.ThrowIfNull(defaultDestinationValue);
        ArgumentNullException.ThrowIfNull(animationClock);
        throw new NotSupportedException($"{GetType().Name} must override GetCurrentValue.");
    }

    object IAnimationTimeline.GetCurrentValue(
        object defaultOriginValue,
        object defaultDestinationValue,
        IAnimationClock clock)
    {
        if (clock is not AnimationClock animationClock)
        {
            throw new ArgumentException("Clock must be an AnimationClock.", nameof(clock));
        }

        return GetCurrentValue(defaultOriginValue, defaultDestinationValue, animationClock);
    }

    public new AnimationClock CreateClock() =>
        (AnimationClock)base.CreateClock(hasControllableRoot: false);

    IAnimationClock IAnimationTimeline.CreateClock() => CreateClock();

    public new AnimationTimeline Clone() => (AnimationTimeline)base.Clone();

    /// <summary>
    /// Replaces a child Freezable owned by a concrete animation while preserving
    /// the inheritance-context, change-notification, and frozen-object rules.
    /// </summary>
    protected void ReplaceAnimationChild<TFreezable>(ref TFreezable storage, TFreezable value)
        where TFreezable : Freezable
    {
        ArgumentNullException.ThrowIfNull(value);
        WritePreamble();
        if (ReferenceEquals(storage, value))
        {
            return;
        }

        OnFreezablePropertyChanged(storage, value);
        storage = value;
        WritePostscript();
    }

    protected internal override Clock AllocateClock() => new AnimationClock(this);

    protected override Duration GetNaturalDurationCore(Clock clock) =>
        new(TimeSpan.FromSeconds(1));
}
/// <summary>Base class for strongly typed animation timelines.</summary>
public abstract class TypedAnimationTimeline<T> : AnimationTimeline
{
    public override Type TargetPropertyType => typeof(T);

    public override object GetCurrentValue(
        object defaultOriginValue,
        object defaultDestinationValue,
        AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        return GetCurrentValueCore(
            (T)defaultOriginValue,
            (T)defaultDestinationValue,
            animationClock)!;
    }

    protected abstract T GetCurrentValueCore(
        T defaultOriginValue,
        T defaultDestinationValue,
        AnimationClock animationClock);
}

/// <summary>Represents the runtime clock for an <see cref="AnimationTimeline"/>.</summary>
public sealed class AnimationClock : Clock, IAnimationClock
{
    private readonly AnimationTimeline _animation;
    private long _startTime;
    private long _firstStartTime;
    private bool _isRunning;
    private double _currentProgress;
    private bool _isPaused;
    private long _pauseTimestamp;
    private bool _isCompleted;

#pragma warning disable CS0628 // WPF exposes this protected-internal constructor on the sealed AnimationClock type.
    protected internal AnimationClock(AnimationTimeline animation)
        : base(animation ?? throw new ArgumentNullException(nameof(animation)))
    {
        _animation = animation;
    }
#pragma warning restore CS0628

    /// <summary>
    /// Compatibility constructor for existing Jalium call sites whose static
    /// type is <see cref="Timeline"/>. WPF's contract constructor above remains
    /// the exact AnimationTimeline overload.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public AnimationClock(Timeline timeline)
        : this(timeline as AnimationTimeline
            ?? throw new ArgumentException("An AnimationClock requires an AnimationTimeline.", nameof(timeline)))
    {
    }

    public new AnimationTimeline Timeline => _animation;

    IAnimationTimeline IAnimationClock.Timeline => _animation;

    public new double CurrentProgress => _currentProgress;

    public bool IsRunning => _isRunning;

    bool IAnimationClock.IsPaused => _isPaused;

    bool IAnimationClock.IsCompleted => _isCompleted || (!_isRunning && !_isPaused);

    public object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue) =>
        _animation.GetCurrentValue(defaultOriginValue, defaultDestinationValue, this);

    public void Begin() => BeginAt(Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow);

    void IAnimationClock.BeginAt(long frameTimestamp) => BeginAt(frameTimestamp);

    internal void BeginAt(long timestamp)
    {
        ClockState oldState = CurrentState;
        _startTime = timestamp;
        if (_animation.BeginTime.HasValue)
        {
            _startTime += (long)(_animation.BeginTime.Value.TotalSeconds * Stopwatch.Frequency);
        }

        _firstStartTime = _startTime;
        _isRunning = true;
        _currentProgress = 0d;
        base.CurrentProgress = 0d;
        CurrentTime = null;
        CurrentIteration = null;
        IsPaused = false;
        _isPaused = false;
        _isCompleted = false;
        CurrentState = ClockState.Stopped;

        if (oldState != CurrentState)
        {
            RaiseCurrentStateInvalidated();
        }
        RaiseCurrentTimeInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    public void Stop() => ControllerStop();

    public void Pause() => ControllerPause();

    public void Resume() => ControllerResume();

    public void Seek(TimeSpan offset, TimeSeekOrigin origin) => SeekCore(offset, origin);

    void IAnimationClock.Seek(TimeSpan offset, AnimationSeekOrigin origin) =>
        SeekCore(
            offset,
            origin == AnimationSeekOrigin.Duration
                ? TimeSeekOrigin.Duration
                : TimeSeekOrigin.BeginTime);

    public void Tick() => Tick(Stopwatch.GetTimestamp());

    public void Tick(long frameTimestamp)
    {
        if (!_isRunning || _isPaused || _isCompleted)
        {
            return;
        }

        if (frameTimestamp < _firstStartTime)
        {
            CurrentState = ClockState.Stopped;
            CurrentTime = null;
            CurrentIteration = null;
            _currentProgress = 0d;
            base.CurrentProgress = 0d;
            return;
        }

        if (CurrentState != ClockState.Active)
        {
            CurrentState = ClockState.Active;
            RaiseCurrentStateInvalidated();
            RaiseCurrentGlobalSpeedInvalidated();
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(_firstStartTime, frameTimestamp);
        TimeSpan duration = _animation.Duration.HasTimeSpan
            ? _animation.Duration.TimeSpan
            : TimeSpan.FromSeconds(1);

        double effectiveSpeed = Math.Max(0d, _animation.SpeedRatio * AppliedSpeedRatio);
        double activeTicks = Math.Max(0d, elapsed.Ticks * effectiveSpeed);
        double simpleTicks = duration.Ticks;
        if (simpleTicks <= 0d)
        {
            CompleteAtProgress(_animation.AutoReverse ? 0d : 1d, TimeSpan.Zero, 1);
            return;
        }

        double iterationTicks = simpleTicks * (_animation.AutoReverse ? 2d : 1d);
        double activeDurationTicks = GetActiveDurationTicks(iterationTicks);
        bool terminal = !double.IsPositiveInfinity(activeDurationTicks) && activeTicks >= activeDurationTicks;
        double sampleTicks = terminal ? activeDurationTicks : activeTicks;

        double iterationNumber = sampleTicks / iterationTicks;
        int iterationIndex;
        double withinIteration;
        if (terminal && sampleTicks > 0d && IsWholeNumber(iterationNumber))
        {
            iterationIndex = Math.Max(0, (int)Math.Ceiling(iterationNumber) - 1);
            withinIteration = iterationTicks;
        }
        else
        {
            iterationIndex = Math.Max(0, (int)Math.Floor(iterationNumber));
            withinIteration = sampleTicks - iterationIndex * iterationTicks;
        }

        CurrentIteration = iterationIndex + 1;

        double currentSimpleTicks;
        if (_animation.AutoReverse && withinIteration > simpleTicks)
        {
            currentSimpleTicks = Math.Max(0d, iterationTicks - withinIteration);
        }
        else
        {
            currentSimpleTicks = Math.Min(simpleTicks, withinIteration);
        }

        CurrentTime = TimeSpan.FromTicks((long)Math.Round(currentSimpleTicks));
        double simpleProgress = Math.Clamp(currentSimpleTicks / simpleTicks, 0d, 1d);
        _currentProgress = ApplyAcceleration(simpleProgress);
        base.CurrentProgress = _currentProgress;
        RaiseCurrentTimeInvalidated();

        if (terminal)
        {
            CompleteAtProgress(_currentProgress, CurrentTime ?? TimeSpan.Zero, CurrentIteration.Value);
        }
    }

    internal override void ControllerBegin() => Begin();

    internal override void ControllerPause()
    {
        if (_isPaused || _isCompleted || !_isRunning)
        {
            return;
        }

        _isPaused = true;
        _isRunning = false;
        _pauseTimestamp = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        IsPaused = true;
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal override void ControllerResume()
    {
        if (!_isPaused)
        {
            return;
        }

        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        long delta = now - _pauseTimestamp;
        _startTime += delta;
        _firstStartTime += delta;
        _isPaused = false;
        _isRunning = true;
        IsPaused = false;
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal override void ControllerSeek(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick) =>
        SeekCore(offset, origin);

    internal override void ControllerStop()
    {
        _isRunning = false;
        _isPaused = false;
        _isCompleted = true;
        base.ControllerStop();
    }

    internal override void ControllerSkipToFill()
    {
        if (_isCompleted)
        {
            return;
        }

        TimeSpan duration = _animation.Duration.HasTimeSpan
            ? _animation.Duration.TimeSpan
            : TimeSpan.FromSeconds(1);
        CompleteAtProgress(_animation.AutoReverse ? 0d : 1d, duration, CurrentIteration ?? 1);
    }

    internal override void ControllerRemove()
    {
        ControllerStop();
        RaiseRemoveRequested();
    }

    private void SeekCore(TimeSpan offset, TimeSeekOrigin origin)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        TimeSpan duration = _animation.Duration.HasTimeSpan
            ? _animation.Duration.TimeSpan
            : TimeSpan.FromSeconds(1);
        TimeSpan target = origin == TimeSeekOrigin.Duration ? duration - offset : offset;
        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        double speedRatio = Math.Max(_animation.SpeedRatio * AppliedSpeedRatio, 1e-9);
        _startTime = now - (long)(target.TotalSeconds / speedRatio * Stopwatch.Frequency);
        _firstStartTime = _startTime;
        CurrentTime = null;
        CurrentIteration = null;
        _currentProgress = 0d;
        base.CurrentProgress = 0d;
        _isCompleted = false;
        _isRunning = true;
        _isPaused = false;
        IsPaused = false;
        CurrentState = ClockState.Active;
        DiscontinuousTimeMovement();
        RaiseCurrentTimeInvalidated();
        RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    private double GetActiveDurationTicks(double iterationTicks)
    {
        RepeatBehavior repeatBehavior = _animation.RepeatBehavior;
        if (repeatBehavior == RepeatBehavior.Forever)
        {
            return double.PositiveInfinity;
        }

        if (repeatBehavior.HasCount)
        {
            return iterationTicks * repeatBehavior.Count;
        }

        return repeatBehavior.HasDuration
            ? repeatBehavior.Duration.Ticks
            : iterationTicks;
    }

    private double ApplyAcceleration(double progress)
    {
        double acceleration = _animation.AccelerationRatio;
        double deceleration = _animation.DecelerationRatio;
        if (acceleration == 0d && deceleration == 0d)
        {
            return progress;
        }

        double maximumRate = 2d / (2d - acceleration - deceleration);
        if (acceleration > 0d && progress < acceleration)
        {
            return maximumRate * progress * progress / (2d * acceleration);
        }

        if (deceleration > 0d && progress > 1d - deceleration)
        {
            double remaining = 1d - progress;
            return 1d - maximumRate * remaining * remaining / (2d * deceleration);
        }

        return maximumRate * (progress - acceleration / 2d);
    }

    private void CompleteAtProgress(double terminalProgress, TimeSpan terminalTime, int terminalIteration)
    {
        CurrentTime = terminalTime;
        CurrentIteration = terminalIteration;
        _isRunning = false;
        _isPaused = false;
        IsPaused = false;
        _isCompleted = true;
        CurrentState = _animation.FillBehavior == FillBehavior.HoldEnd
            ? ClockState.Filling
            : ClockState.Stopped;
        _currentProgress = _animation.FillBehavior == FillBehavior.Stop ? 0d : terminalProgress;
        base.CurrentProgress = _currentProgress;
        RaiseCurrentTimeInvalidated();
        RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
        RaiseCompleted();
    }

    private static bool IsWholeNumber(double value) =>
        Math.Abs(value - Math.Round(value)) <= 1e-10;
}
