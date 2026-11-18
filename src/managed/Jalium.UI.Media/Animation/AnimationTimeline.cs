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
    private long _speedAnchorTimestamp;
    private double _activeTicksAtAnchor;
    private double _anchorSpeedRatio = 1d;
    private bool _isRunning;
    private double _currentProgress;
    private bool _isPaused;
    private long _pauseTimestamp;
    private bool _isCompleted;
    internal bool HasStarted { get; private set; }
    private uint _lastParentSampleVersion = uint.MaxValue;
    private bool _samplingFromParent;
    private bool _parentCompletionReported;

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

    /// <summary>Samples a reusable timeline at a chosen progress without starting a clock or raising events.</summary>
    internal void SetProgressForSampling(double progress)
    {
        // CSS easing can legitimately extrapolate beyond both endpoints.
        _currentProgress = progress;
        base.CurrentProgress = _currentProgress;
        var duration = NaturalDuration.HasTimeSpan
            ? NaturalDuration.TimeSpan : TimeSpan.FromSeconds(1);
        CurrentTime = TimeSpan.FromTicks((long)Math.Round(duration.Ticks * Math.Clamp(progress, 0d, 1d)));
        CurrentIteration = 1;
        CurrentState = ClockState.Active;
    }

    public bool IsRunning => _isRunning;

    // A stopped child can still belong to a repeating parent. Its current
    // iteration has finished even while the parent has future timing work.
    internal bool IsStoppedAfterCompletion => _isCompleted && CurrentState == ClockState.Stopped && _animation.BeginTime.HasValue;

    bool IAnimationClock.IsPaused => IsPaused;

    bool IAnimationClock.IsCompleted => Parent is ClockGroup { HasFutureTiming: true }
        ? false : _isCompleted || (!_isRunning && !_isPaused);

    public object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue) =>
        _animation.GetCurrentValue(defaultOriginValue, defaultDestinationValue, this);

    public void Begin() => BeginAt(Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow);

    void IAnimationClock.BeginAt(long frameTimestamp) => BeginAt(frameTimestamp);

    internal void BeginAt(long timestamp)
    {
        HasStarted = true;
        if (Parent != null) timestamp = 0;
        _lastParentSampleVersion = uint.MaxValue;
        _parentCompletionReported = false;
        ClockState oldState = CurrentState;
        _startTime = timestamp;
        if (_animation.BeginTime.HasValue)
        {
            _startTime += (long)(_animation.BeginTime.Value.TotalSeconds * Stopwatch.Frequency);
        }

        _firstStartTime = _startTime;
        _speedAnchorTimestamp = _firstStartTime;
        _activeTicksAtAnchor = 0d;
        _anchorSpeedRatio = _animation.SpeedRatio * AppliedSpeedRatio;
        LocalSpeedFactor = 1d;
        _isRunning = _animation.BeginTime.HasValue;
        _currentProgress = 0d;
        base.CurrentProgress = 0d;
        CurrentTime = null;
        CurrentIteration = null;
        IsPaused = false;
        _isPaused = false;
        _isCompleted = !_animation.BeginTime.HasValue;
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

    public void Seek(TimeSpan offset, TimeSeekOrigin origin) => SeekCore(offset, origin, alignedToLastTick: false);

    void IAnimationClock.Seek(TimeSpan offset, AnimationSeekOrigin origin) =>
        SeekCore(
            offset,
            origin == AnimationSeekOrigin.Duration
                ? TimeSeekOrigin.Duration
                : TimeSeekOrigin.BeginTime,
            alignedToLastTick: false);

    public void Tick() => Tick(Stopwatch.GetTimestamp());

    public void Tick(long frameTimestamp)
    {
        if (Parent is ClockGroup parent)
        {
            parent.UpdateTiming(frameTimestamp);
            if (_lastParentSampleVersion == parent.SampleVersion) return;
            _lastParentSampleVersion = parent.SampleVersion;
            SampleFromParent(parent);
        }
        else TickCore(frameTimestamp);
    }

    internal void SampleFromParent(ClockGroup parent)
    {
        if (!_animation.BeginTime.HasValue || parent.CurrentState == ClockState.Stopped)
        {
            ClockState previous = CurrentState;
            CurrentState = ClockState.Stopped;
            CurrentTime = null;
            CurrentIteration = null;
            _currentProgress = 0d;
            base.CurrentProgress = 0d;
            _isCompleted = !_animation.BeginTime.HasValue || !parent.HasFutureTiming;
            if (previous != CurrentState) RaiseCurrentStateInvalidated();
            RaiseCurrentTimeInvalidated();
            return;
        }
        long localTimestamp = (long)(parent.SampleTime.TotalSeconds * Stopwatch.Frequency);
        _isRunning = true;
        _isCompleted = false;
        _samplingFromParent = true;
        try
        {
            TickCore(localTimestamp);
            if (!_isCompleted) _parentCompletionReported = false;
            if (!parent.HasFutureTiming && !_isCompleted)
                CompleteAtProgress(_currentProgress, CurrentTime ?? TimeSpan.Zero, CurrentIteration ?? 1);
        }
        finally { _samplingFromParent = false; }
    }

    private void TickCore(long frameTimestamp)
    {
        if (!_isRunning || _isPaused || _isCompleted)
        {
            return;
        }

        if (frameTimestamp < _firstStartTime)
        {
            ClockState previous = CurrentState;
            CurrentState = ClockState.Stopped;
            CurrentTime = null;
            CurrentIteration = null;
            _currentProgress = 0d;
            base.CurrentProgress = 0d;
            if (previous != CurrentState) RaiseCurrentStateInvalidated();
            RaiseCurrentTimeInvalidated();
            return;
        }

        if (CurrentState != ClockState.Active)
        {
            CurrentState = ClockState.Active;
            RaiseCurrentStateInvalidated();
            RaiseCurrentGlobalSpeedInvalidated();
        }

        Duration naturalDuration = NaturalDuration;
        double activeTicks = GetActiveTicks(frameTimestamp);
        if (naturalDuration == Duration.Forever)
        {
            CurrentTime = TimeSpan.FromTicks((long)Math.Min(TimeSpan.MaxValue.Ticks, activeTicks));
            CurrentIteration = 1;
            _currentProgress = 0d;
            base.CurrentProgress = 0d;
            RaiseCurrentTimeInvalidated();
            return;
        }

        TimeSpan duration = naturalDuration.HasTimeSpan
            ? naturalDuration.TimeSpan
            : TimeSpan.FromSeconds(1);
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

        double simpleProgress = Math.Clamp(currentSimpleTicks / simpleTicks, 0d, 1d);
        double speedFactor = GetAccelerationRate(simpleProgress) *
            (_animation.AutoReverse && withinIteration > simpleTicks ? -1d : 1d);
        bool speedChanged = speedFactor != LocalSpeedFactor;
        LocalSpeedFactor = speedFactor;
        _currentProgress = ApplyAcceleration(simpleProgress);
        CurrentTime = TimeSpan.FromTicks((long)Math.Round(simpleTicks * _currentProgress));
        base.CurrentProgress = _currentProgress;
        if (speedChanged) RaiseCurrentGlobalSpeedInvalidated();
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
        _speedAnchorTimestamp += delta;
        _isPaused = false;
        _isRunning = true;
        IsPaused = false;
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal override void ControllerSeek(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick) =>
        SeekCore(offset, origin, alignedToLastTick);

    internal override void ControllerSpeedRatioChanged()
    {
        long now = _isPaused ? _pauseTimestamp : Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        _activeTicksAtAnchor = GetActiveTicks(now);
        _speedAnchorTimestamp = Math.Max(now, _firstStartTime);
        _anchorSpeedRatio = _animation.SpeedRatio * AppliedSpeedRatio;
        base.ControllerSpeedRatioChanged();
    }

    private double GetActiveTicks(long timestamp) => Math.Max(0d,
        _activeTicksAtAnchor + Stopwatch.GetElapsedTime(_speedAnchorTimestamp, timestamp).Ticks * _anchorSpeedRatio);

    internal override void ControllerStop()
    {
        bool alreadyStopped = CurrentState == ClockState.Stopped;
        _isRunning = false;
        _isPaused = false;
        _isCompleted = true;
        _currentProgress = 0d;
        base.ControllerStop();
        if (alreadyStopped)
        {
            // Begin/delay clocks already report Stopped, but stopping them
            // still removes their contribution and their handoff snapshot.
            RaiseCurrentTimeInvalidated();
            RaiseCurrentGlobalSpeedInvalidated();
        }
    }

    internal override void ControllerSkipToFill()
    {
        if (_isCompleted)
        {
            return;
        }

        Duration naturalDuration = NaturalDuration;
        if (naturalDuration == Duration.Forever) return;
        TimeSpan duration = naturalDuration.HasTimeSpan
            ? naturalDuration.TimeSpan
            : TimeSpan.FromSeconds(1);
        double activeDuration = GetActiveDurationTicks(duration.Ticks * (_animation.AutoReverse ? 2d : 1d));
        if (!double.IsFinite(activeDuration)) return;
        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        _firstStartTime = now;
        _speedAnchorTimestamp = now;
        _activeTicksAtAnchor = activeDuration;
        _anchorSpeedRatio = 0d;
        _isPaused = false;
        IsPaused = false;
        _isRunning = true;
        Tick(now);
    }

    internal override void ControllerRemove()
    {
        ControllerStop();
        RaiseRemoveRequested();
    }

    private void SeekCore(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        Duration naturalDuration = NaturalDuration;
        if (origin == TimeSeekOrigin.Duration && naturalDuration == Duration.Forever)
            throw new InvalidOperationException("An infinite timeline has no duration endpoint to seek from.");
        TimeSpan duration = naturalDuration.HasTimeSpan
            ? naturalDuration.TimeSpan
            : TimeSpan.FromSeconds(1);
        TimeSpan target = origin == TimeSeekOrigin.Duration ? duration + offset : offset;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        bool wasPaused = _isPaused;
        long now = Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow;
        _startTime = now;
        _firstStartTime = now;
        _speedAnchorTimestamp = now;
        _activeTicksAtAnchor = target.Ticks;
        _anchorSpeedRatio = _animation.SpeedRatio * AppliedSpeedRatio;
        CurrentTime = null;
        CurrentIteration = null;
        _currentProgress = 0d;
        base.CurrentProgress = 0d;
        _isCompleted = false;
        HasStarted = true;
        _isRunning = true;
        _isPaused = false;
        IsPaused = false;
        CurrentState = ClockState.Active;
        DiscontinuousTimeMovement();
        RaiseCurrentTimeInvalidated();
        RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
        if (alignedToLastTick || wasPaused) Tick(now);
        if (wasPaused && !_isCompleted)
        {
            _isPaused = true;
            _isRunning = false;
            _pauseTimestamp = now;
            IsPaused = true;
            RaiseCurrentGlobalSpeedInvalidated();
        }
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

    private double GetAccelerationRate(double progress)
    {
        double acceleration = _animation.AccelerationRatio;
        double deceleration = _animation.DecelerationRatio;
        double maximumRate = 2d / (2d - acceleration - deceleration);
        if (acceleration > 0d && progress < acceleration)
            return maximumRate * progress / acceleration;
        if (deceleration > 0d && progress > 1d - deceleration)
            return maximumRate * (1d - progress) / deceleration;
        return maximumRate;
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
        if (!_samplingFromParent || !_parentCompletionReported)
        {
            _parentCompletionReported = _samplingFromParent;
            RaiseCompleted();
        }
    }

    private static bool IsWholeNumber(double value) =>
        Math.Abs(value - Math.Round(value)) <= 1e-10;
}
