using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Maintains run-time timing state for a <see cref="Timeline"/>.
/// </summary>
public class Clock
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

    public bool IsPaused { get; internal set; }

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

            if (IsPaused)
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
            return parentSpeed * _timeline.SpeedRatio * _controller.SpeedRatio;
        }
    }

    protected TimeSpan CurrentGlobalTime => _currentGlobalTime;

    public bool HasControllableRoot => _hasControllableRoot || Parent?.HasControllableRoot == true;

    internal double AppliedSpeedRatio => _controller.SpeedRatio;

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
            target = duration.TimeSpan - offset;
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

    protected internal ClockGroup(TimelineGroup timelineGroup)
        : base(timelineGroup)
    {
        _childrenView = new ClockCollection(this);
    }

    public new TimelineGroup Timeline => (TimelineGroup)base.Timeline;

    public ClockCollection Children => _childrenView;

    internal IReadOnlyList<Clock> ChildClocks => _children;

    internal void AddChild(Clock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        clock.Parent = this;
        _children.Add(clock);
    }

    internal override void ControllerBegin()
    {
        base.ControllerBegin();
        foreach (Clock child in _children)
        {
            child.ControllerBegin();
        }
    }

    internal override void ControllerPause()
    {
        base.ControllerPause();
        foreach (Clock child in _children)
        {
            child.ControllerPause();
        }
    }

    internal override void ControllerResume()
    {
        base.ControllerResume();
        foreach (Clock child in _children)
        {
            child.ControllerResume();
        }
    }

    internal override void ControllerSeek(TimeSpan offset, TimeSeekOrigin origin, bool alignedToLastTick)
    {
        base.ControllerSeek(offset, origin, alignedToLastTick);
        foreach (Clock child in _children)
        {
            child.ControllerSeek(offset, origin, alignedToLastTick);
        }
    }

    internal override void ControllerStop()
    {
        base.ControllerStop();
        foreach (Clock child in _children)
        {
            child.ControllerStop();
        }
    }

    internal override void ControllerSkipToFill()
    {
        base.ControllerSkipToFill();
        foreach (Clock child in _children)
        {
            child.ControllerSkipToFill();
        }
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
public sealed class ClockController
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
public sealed class ClockCollection : ICollection<Clock>, IReadOnlyList<Clock>
{
    private readonly Clock _owner;

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

    public IEnumerator<Clock> GetEnumerator() => _owner is ClockGroup group
        ? group.ChildClocks.GetEnumerator()
        : Enumerable.Empty<Clock>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    public bool Remove(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    public void Clear() => throw new NotSupportedException("ClockCollection is read-only.");

    void ICollection<Clock>.Add(Clock item) => Add(item);
    bool ICollection<Clock>.Remove(Clock item) => Remove(item);
    void ICollection<Clock>.Clear() => Clear();

    public override bool Equals(object? obj) => obj is ClockCollection other && ReferenceEquals(_owner, other._owner);
    public override int GetHashCode() => _owner.GetHashCode();
    public static bool operator ==(ClockCollection? left, ClockCollection? right) => Equals(left, right);
    public static bool operator !=(ClockCollection? left, ClockCollection? right) => !Equals(left, right);
}

/// <summary>Specifies a timeline for media playback in the animation namespace.</summary>
public sealed class MediaTimeline : Timeline
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
