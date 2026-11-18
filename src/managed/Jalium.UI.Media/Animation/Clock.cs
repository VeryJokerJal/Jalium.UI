using System.Collections;
using Jalium.UI.Threading;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Maintains run-time timing state for a <see cref="Timeline"/>.
/// </summary>
public class Clock : DispatcherObject
{
    private readonly Timeline _timeline;
    private readonly ClockController _controller;
    private ClockState _currentState = ClockState.Stopped;
    private TimeSpan _currentGlobalTime;
    private TimeSpan _currentTime;
    private double _currentProgress;
    private bool _hasControllableRoot = true;

    /// <summary>Initializes a clock for the supplied timeline.</summary>
    protected internal Clock(Timeline timeline)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        _controller = new ClockController(this);
    }

    public Timeline Timeline => _timeline;

    /// <summary>
    /// Gets the controller for a controllable root clock. Child clocks and roots
    /// created with <c>hasControllableRoot == false</c> return <see langword="null"/>.
    /// </summary>
    public ClockController Controller => Parent is null && HasControllableRoot ? _controller : null!;

    public ClockState CurrentState
    {
        get => _currentState;
        internal set => _currentState = value;
    }

    public TimeSpan? CurrentTime
    {
        get => _currentState == ClockState.Stopped ? null : GetCurrentTimeCore();
        internal set => _currentTime = value ?? TimeSpan.Zero;
    }

    public double? CurrentProgress
    {
        get => _currentState == ClockState.Stopped ? null : _currentProgress;
        internal set => _currentProgress = value ?? 0d;
    }

    public int? CurrentIteration { get; internal set; }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused || Parent?.IsPaused == true;
        internal set => _isPaused = value;
    }

    public Duration NaturalDuration => _timeline.GetNaturalDuration(this);

    public Clock? Parent { get; internal set; }

    public double? CurrentGlobalSpeed
    {
        get
        {
            if (_currentState == ClockState.Stopped)
            {
                return null;
            }

            if (IsPaused || _currentState == ClockState.Filling)
            {
                return 0d;
            }

            double parentSpeed = 1d;
            if (Parent is not null)
            {
                double? inheritedSpeed = Parent.CurrentGlobalSpeed;
                if (!inheritedSpeed.HasValue)
                {
                    return null;
                }
                parentSpeed = inheritedSpeed.Value;
            }
            return parentSpeed * _timeline.SpeedRatio * _controller.SpeedRatio * LocalSpeedFactor;
        }
    }

    protected TimeSpan CurrentGlobalTime => _currentGlobalTime;

    public bool HasControllableRoot => _hasControllableRoot || Parent?.HasControllableRoot == true;

    internal double AppliedSpeedRatio => _controller.SpeedRatio;

    internal double LocalSpeedFactor { get; set; } = 1d;

    internal bool IsTimeProgressBlocked => IsPaused || AppliedSpeedRatio == 0d || Parent?.IsTimeProgressBlocked == true;

    public event EventHandler? Completed;
    public event EventHandler? CurrentStateInvalidated;
    public event EventHandler? CurrentTimeInvalidated;
    public event EventHandler? CurrentGlobalSpeedInvalidated;
    public event EventHandler? RemoveRequested;

    protected virtual void DiscontinuousTimeMovement()
    {
    }

    protected virtual bool GetCanSlip() => false;

    protected virtual TimeSpan GetCurrentTimeCore() => _currentTime;

    protected virtual void SpeedChanged()
    {
    }

    protected virtual void Stopped()
    {
    }

    internal void SetHasControllableRoot(bool value) => _hasControllableRoot = value;

    internal void SetInteractiveSpeedRatio(double value) => _controller.SpeedRatio = value;

    internal virtual void ControllerBegin()
    {
        ClockState oldState = _currentState;
        _currentState = ClockState.Active;
        _currentTime = TimeSpan.Zero;
        _currentGlobalTime = TimeSpan.Zero;
        _currentProgress = 0d;
        CurrentIteration = 1;
        IsPaused = false;
        DiscontinuousTimeMovement();
        RaiseCurrentTimeInvalidated();
        if (oldState != _currentState)
        {
            RaiseCurrentStateInvalidated();
        }
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void ControllerPause()
    {
        if (_currentState == ClockState.Stopped || IsPaused)
        {
            return;
        }

        IsPaused = true;
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void ControllerResume()
    {
        if (_currentState == ClockState.Stopped || !IsPaused)
        {
            return;
        }

        IsPaused = false;
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void ControllerSeek(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick)
    {
        ValidateSeekOrigin(origin);

        TimeSpan target = offset;
        Duration duration = NaturalDuration;
        if (origin == TimeSeekOrigin.Duration && duration.HasTimeSpan)
        {
            target = duration.TimeSpan + offset;
        }

        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        ClockState oldState = _currentState;
        _currentState = ClockState.Active;
        _currentGlobalTime = target;
        _currentTime = target;
        CurrentIteration = 1;
        IsPaused = false;

        if (duration.HasTimeSpan && duration.TimeSpan > TimeSpan.Zero)
        {
            _currentProgress = Math.Clamp(target.TotalMilliseconds / duration.TimeSpan.TotalMilliseconds, 0d, 1d);
        }
        else
        {
            _currentProgress = 0d;
        }

        DiscontinuousTimeMovement();
        RaiseCurrentTimeInvalidated();
        if (oldState != _currentState)
        {
            RaiseCurrentStateInvalidated();
        }
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void ControllerStop()
    {
        if (_currentState == ClockState.Stopped)
        {
            return;
        }

        _currentState = ClockState.Stopped;
        _currentTime = TimeSpan.Zero;
        _currentGlobalTime = TimeSpan.Zero;
        _currentProgress = 0d;
        CurrentIteration = null;
        IsPaused = false;
        Stopped();
        RaiseCurrentTimeInvalidated();
        RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void ControllerSkipToFill()
    {
        Duration duration = NaturalDuration;
        ClockState oldState = _currentState;
        _currentState = _timeline.FillBehavior == FillBehavior.HoldEnd
            ? ClockState.Filling
            : ClockState.Stopped;
        _currentTime = duration.HasTimeSpan ? duration.TimeSpan : TimeSpan.Zero;
        _currentGlobalTime = _currentTime;
        _currentProgress = _currentState == ClockState.Stopped ? 0d : 1d;
        CurrentIteration ??= 1;
        IsPaused = false;
        DiscontinuousTimeMovement();
        RaiseCurrentTimeInvalidated();
        if (oldState != _currentState)
        {
            RaiseCurrentStateInvalidated();
        }
        RaiseCurrentGlobalSpeedInvalidated();
        RaiseCompleted();
    }

    internal virtual void ControllerRemove()
    {
        ControllerStop();
        RaiseRemoveRequested();
    }

    internal virtual void ControllerSpeedRatioChanged()
    {
        SpeedChanged();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    internal virtual void NotifyParentTimingChanged()
    {
        RaiseCurrentTimeInvalidated();
        RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
    }

    protected internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);
    protected internal void RaiseCurrentStateInvalidated() => CurrentStateInvalidated?.Invoke(this, EventArgs.Empty);
    protected internal void RaiseCurrentTimeInvalidated() => CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);
    protected internal void RaiseCurrentGlobalSpeedInvalidated() => CurrentGlobalSpeedInvalidated?.Invoke(this, EventArgs.Empty);
    protected internal void RaiseRemoveRequested() => RemoveRequested?.Invoke(this, EventArgs.Empty);

    private static void ValidateSeekOrigin(TimeSeekOrigin origin)
    {
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
    }
}

/// <summary>Represents a group of clocks.</summary>
public class ClockGroup : Clock
{
    private readonly List<Clock> _children = [];
    private readonly ClockCollection _childrenView;
    private AnimationClock? _timingClock;
    private long _lastFrameTimestamp = long.MinValue;
    private bool _completionPending;
    internal uint SampleVersion { get; private set; }

    protected internal ClockGroup(TimelineGroup timelineGroup)
        : base(timelineGroup)
    {
        _childrenView = new ClockCollection(this);
    }

    public new TimelineGroup Timeline => (TimelineGroup)base.Timeline;

    public ClockCollection Children => _childrenView;

    internal IReadOnlyList<Clock> ChildClocks => _children;

    internal bool HasFutureTiming => Timeline.BeginTime.HasValue &&
        (Parent is ClockGroup parent ? parent.HasFutureTiming :
            _timingClock != null && !((IAnimationClock)_timingClock).IsCompleted);

    internal TimeSpan SampleTime
    {
        get
        {
            Duration duration = _timingClock?.NaturalDuration ?? NaturalDuration;
            return duration.HasTimeSpan && CurrentProgress.HasValue
                ? TimeSpan.FromTicks((long)(duration.TimeSpan.Ticks * CurrentProgress.Value))
                : CurrentTime ?? TimeSpan.Zero;
        }
    }

    private AnimationClock TimingClock
    {
        get
        {
            if (_timingClock != null) return _timingClock;
            var timing = new DoubleAnimation
            {
                Duration = NaturalDuration,
                BeginTime = Timeline.BeginTime,
                RepeatBehavior = Timeline.RepeatBehavior,
                AutoReverse = Timeline.AutoReverse,
                SpeedRatio = Timeline.SpeedRatio,
                AccelerationRatio = Timeline.AccelerationRatio,
                DecelerationRatio = Timeline.DecelerationRatio,
                FillBehavior = Timeline.FillBehavior
            };
            _timingClock = new AnimationClock(timing);
            _timingClock.Completed += (_, _) => _completionPending = true;
            return _timingClock;
        }
    }

    internal void UpdateTiming(long frameTimestamp)
    {
        if (_lastFrameTimestamp == frameTimestamp) return;
        var timing = TimingClock;
        if (!timing.HasStarted)
            timing.BeginAt(Parent == null ? frameTimestamp : 0);
        if (Parent is ClockGroup parent)
        {
            parent.UpdateTiming(frameTimestamp);
            timing.SampleFromParent(parent);
        }
        else timing.Tick(frameTimestamp);
        _lastFrameTimestamp = frameTimestamp;
        unchecked { SampleVersion++; }
        SynchronizeTiming();
    }

    internal void TickChildren(long frameTimestamp)
    {
        UpdateTiming(frameTimestamp);
        foreach (var child in _children)
        {
            if (child is AnimationClock animation) animation.Tick(frameTimestamp);
            else if (child is ClockGroup group) group.TickChildren(frameTimestamp);
        }
    }

    private void SynchronizeTiming()
    {
        if (_timingClock == null) return;
        ClockState previous = CurrentState;
        CurrentState = _timingClock.CurrentState;
        CurrentTime = _timingClock.CurrentTime;
        CurrentProgress = _timingClock.CurrentProgress;
        CurrentIteration = _timingClock.CurrentIteration;
        LocalSpeedFactor = _timingClock.LocalSpeedFactor;
        IsPaused = _timingClock.IsPaused;
        RaiseCurrentTimeInvalidated();
        if (previous != CurrentState) RaiseCurrentStateInvalidated();
        RaiseCurrentGlobalSpeedInvalidated();
        if (_completionPending)
        {
            _completionPending = false;
            RaiseCompleted();
        }
    }

    internal override void NotifyParentTimingChanged()
    {
        _lastFrameTimestamp = long.MinValue;
        base.NotifyParentTimingChanged();
        foreach (var child in _children) child.NotifyParentTimingChanged();
    }

    internal void AddChild(Clock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        clock.Parent = this;
        _children.Add(clock);
    }

    internal override void ControllerBegin()
    {
        base.ControllerBegin();
        TimingClock.BeginAt(Parent == null ? Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow : 0);
        _lastFrameTimestamp = long.MinValue;
        foreach (Clock child in _children)
        {
            child.ControllerBegin();
        }
    }

    internal override void ControllerPause()
    {
        if (Parent == null) TimingClock.Pause();
        base.ControllerPause();
        SynchronizeTiming();
        foreach (Clock child in _children) child.NotifyParentTimingChanged();
    }

    internal override void ControllerResume()
    {
        if (Parent == null) TimingClock.Resume();
        base.ControllerResume();
        SynchronizeTiming();
        foreach (Clock child in _children) child.NotifyParentTimingChanged();
    }

    internal override void ControllerSeek(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick)
    {
        // Seeking to the end can clear IsPaused as the clock enters its fill period.
        bool sampleImmediately = alignedToLastTick || IsPaused;
        TimingClock.ControllerSeek(offset, origin, alignedToLastTick);
        _lastFrameTimestamp = long.MinValue;
        SynchronizeTiming();
        foreach (Clock child in _children) child.NotifyParentTimingChanged();
        if (sampleImmediately)
            TickChildren(Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow);
    }

    internal override void ControllerStop()
    {
        _timingClock?.Stop();
        base.ControllerStop();
        foreach (Clock child in _children)
        {
            child.ControllerStop();
        }
    }

    internal override void ControllerSkipToFill()
    {
        TimingClock.ControllerSkipToFill();
        _lastFrameTimestamp = long.MinValue;
        TickChildren(Jalium.UI.Animation.AnimationManager.CurrentFrameTimestampOrNow);
    }

    internal override void ControllerSpeedRatioChanged()
    {
        TimingClock.SetInteractiveSpeedRatio(AppliedSpeedRatio);
        base.ControllerSpeedRatioChanged();
        foreach (Clock child in _children) child.NotifyParentTimingChanged();
    }

    internal override void ControllerRemove()
    {
        base.ControllerRemove();
        foreach (Clock child in _children)
        {
            child.ControllerRemove();
        }
    }
}

/// <summary>Interactively controls a root clock.</summary>
public sealed class ClockController : DispatcherObject
{
    private readonly Clock _clock;
    private double _speedRatio = 1d;

    internal ClockController(Clock clock)
    {
        _clock = clock;
    }

    public Clock Clock => _clock;

    public double SpeedRatio
    {
        get => _speedRatio;
        set
        {
            if (!double.IsFinite(value) || value < 0d)
            {
                throw new ArgumentException("SpeedRatio must be a finite, non-negative value.", nameof(value));
            }

            if (_speedRatio.Equals(value))
            {
                return;
            }

            _speedRatio = value;
            _clock.ControllerSpeedRatioChanged();
        }
    }

    public void Begin() => _clock.ControllerBegin();
    public void Pause() => _clock.ControllerPause();
    public void Resume() => _clock.ControllerResume();
    public void Seek(TimeSpan offset, TimeSeekOrigin origin) => _clock.ControllerSeek(offset, origin, alignedToLastTick: false);
    public void SeekAlignedToLastTick(TimeSpan offset, TimeSeekOrigin origin) => _clock.ControllerSeek(offset, origin, alignedToLastTick: true);
    public void Stop() => _clock.ControllerStop();
    public void SkipToFill() => _clock.ControllerSkipToFill();
    public void Remove() => _clock.ControllerRemove();
}

public enum ClockState
{
    Active,
    Filling,
    Stopped,
}

public enum TimeSeekOrigin
{
    BeginTime,
    Duration,
}

/// <summary>Groups child timelines that should become active together.</summary>
public class ParallelTimeline : TimelineGroup
{
    public static readonly DependencyProperty SlipBehaviorProperty =
        DependencyProperty.Register(
            nameof(SlipBehavior),
            typeof(SlipBehavior),
            typeof(ParallelTimeline),
            new PropertyMetadata(SlipBehavior.Grow),
            static value => value is SlipBehavior behavior && Enum.IsDefined(behavior));

    public ParallelTimeline()
    {
    }

    public ParallelTimeline(TimeSpan? beginTime)
        : base(beginTime)
    {
    }

    public ParallelTimeline(TimeSpan? beginTime, Duration duration)
        : base(beginTime, duration)
    {
    }

    public ParallelTimeline(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
        : base(beginTime, duration, repeatBehavior)
    {
    }

    public SlipBehavior SlipBehavior
    {
        get => (SlipBehavior)(GetValue(SlipBehaviorProperty) ?? SlipBehavior.Grow);
        set => SetValue(SlipBehaviorProperty, value);
    }

    public new ParallelTimeline Clone() => (ParallelTimeline)base.Clone();
    public new ParallelTimeline CloneCurrentValue() => (ParallelTimeline)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new ParallelTimeline();

    protected override Duration GetNaturalDurationCore(Clock clock) =>
        base.GetNaturalDurationCore(clock);
}

public enum SlipBehavior
{
    Grow,
    Slip,
}

/// <summary>A read-only collection of child clocks associated with a clock group.</summary>
public class ClockCollection : ICollection<Clock>
{
    private readonly Clock? _owner;

    private ClockCollection()
    {
    }

    internal ClockCollection(Clock owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public static bool Equals(ClockCollection? objA, ClockCollection? objB) =>
        ReferenceEquals(objA, objB) || objA is not null && objA.Equals(objB);

    public int Count => _owner is ClockGroup group ? group.ChildClocks.Count : 0;

    public bool IsReadOnly => true;

    public Clock this[int index] => _owner is ClockGroup group
        ? group.ChildClocks[index]
        : throw new ArgumentOutOfRangeException(nameof(index));

    public bool Contains(Clock item) => _owner is ClockGroup group && group.ChildClocks.Contains(item);

    public void CopyTo(Clock[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (_owner is not ClockGroup group)
        {
            return;
        }

        for (int i = 0; i < group.ChildClocks.Count; i++)
        {
            array[arrayIndex + i] = group.ChildClocks[i];
        }
    }

    IEnumerator<Clock> IEnumerable<Clock>.GetEnumerator() => _owner is ClockGroup group
        ? group.ChildClocks.GetEnumerator()
        : Enumerable.Empty<Clock>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<Clock>)this).GetEnumerator();

    public void Add(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    public bool Remove(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    public void Clear() => throw new NotSupportedException("ClockCollection is read-only.");

    public override bool Equals(object? obj) => obj is ClockCollection other && ReferenceEquals(_owner, other._owner);
    public override int GetHashCode() => _owner?.GetHashCode() ?? 0;
    public static bool operator ==(ClockCollection? left, ClockCollection? right) => Equals(left, right);
    public static bool operator !=(ClockCollection? left, ClockCollection? right) => !Equals(left, right);
}

/// <summary>Specifies a timeline for media playback in the animation namespace.</summary>
// Jalium.UI.Media.MediaTimeline is the canonical WPF-compatible type. This
// older simplified timeline is retained as an implementation detail only.
internal sealed class MediaTimeline : Timeline
{
    public Uri? Source { get; set; }

    public MediaTimeline()
    {
    }

    public MediaTimeline(Uri source)
    {
        Source = source;
    }
}
