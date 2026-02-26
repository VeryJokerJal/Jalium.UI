namespace Jalium.UI.Media.Animation;

/// <summary>
/// Maintains run-time timing state for a Timeline.
/// </summary>
public class Clock
{
    private readonly Timeline _timeline;
    private ClockState _currentState = ClockState.Stopped;
    private TimeSpan _currentTime;
    private double _currentProgress;

    /// <summary>
    /// Initializes a new instance of the Clock class.
    /// </summary>
    public Clock(Timeline timeline)
    {
        _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
        Controller = new ClockController(this);
    }

    /// <summary>
    /// Gets the Timeline from which this Clock was created.
    /// </summary>
    public Timeline Timeline => _timeline;

    /// <summary>
    /// Gets the ClockController for this Clock.
    /// </summary>
    public ClockController Controller { get; }

    /// <summary>
    /// Gets the current state of this Clock.
    /// </summary>
    public ClockState CurrentState
    {
        get => _currentState;
        internal set => _currentState = value;
    }

    /// <summary>
    /// Gets the current time within this Clock's current iteration.
    /// </summary>
    public TimeSpan? CurrentTime
    {
        get => _currentState == ClockState.Stopped ? null : _currentTime;
        internal set => _currentTime = value ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the current progress of this Clock within its current iteration (0.0 to 1.0).
    /// </summary>
    public double? CurrentProgress
    {
        get => _currentState == ClockState.Stopped ? null : _currentProgress;
        internal set => _currentProgress = value ?? 0.0;
    }

    /// <summary>
    /// Gets the current iteration of this Clock.
    /// </summary>
    public int? CurrentIteration { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the Clock is currently active, filling, or stopped.
    /// </summary>
    public bool IsPaused { get; internal set; }

    /// <summary>
    /// Gets the natural duration of this Clock's Timeline.
    /// </summary>
    public Duration NaturalDuration => _timeline.Duration;

    /// <summary>
    /// Gets or sets the parent Clock.
    /// </summary>
    public ClockGroup? Parent { get; internal set; }

    /// <summary>
    /// Occurs when this Clock completely finishes playing.
    /// </summary>
    public event EventHandler? Completed;

    /// <summary>
    /// Occurs when the CurrentState property changes.
    /// </summary>
    public event EventHandler? CurrentStateInvalidated;

    /// <summary>
    /// Occurs when the CurrentTime property changes.
    /// </summary>
    public event EventHandler? CurrentTimeInvalidated;

    /// <summary>
    /// Occurs when the CurrentGlobalSpeed property changes.
    /// </summary>
    public event EventHandler? CurrentGlobalSpeedInvalidated;

    /// <summary>
    /// Raises the Completed event.
    /// </summary>
    internal void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises the CurrentStateInvalidated event.
    /// </summary>
    internal void RaiseCurrentStateInvalidated() => CurrentStateInvalidated?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises the CurrentTimeInvalidated event.
    /// </summary>
    internal void RaiseCurrentTimeInvalidated() => CurrentTimeInvalidated?.Invoke(this, EventArgs.Empty);

    internal void RaiseCurrentGlobalSpeedInvalidated() => CurrentGlobalSpeedInvalidated?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents a group of Clocks.
/// </summary>
public sealed class ClockGroup : Clock
{
    private readonly List<Clock> _children = new();

    /// <summary>
    /// Initializes a new instance of the ClockGroup class.
    /// </summary>
    public ClockGroup(Timeline timeline) : base(timeline)
    {
    }

    /// <summary>
    /// Gets the children of this ClockGroup.
    /// </summary>
    public IReadOnlyList<Clock> Children => _children;

    /// <summary>
    /// Adds a child clock to this group.
    /// </summary>
    internal void AddChild(Clock clock)
    {
        clock.Parent = this;
        _children.Add(clock);
    }
}

/// <summary>
/// Interactively controls a Clock.
/// </summary>
public sealed class ClockController
{
    private readonly Clock _clock;

    internal ClockController(Clock clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// Gets the Clock that this ClockController controls.
    /// </summary>
    public Clock Clock => _clock;

    /// <summary>
    /// Starts the Clock.
    /// </summary>
    public void Begin()
    {
        _clock.CurrentState = ClockState.Active;
        _clock.CurrentTime = TimeSpan.Zero;
        _clock.CurrentProgress = 0.0;
        _clock.CurrentIteration = 1;
        _clock.IsPaused = false;
        _clock.RaiseCurrentStateInvalidated();
    }

    /// <summary>
    /// Pauses the Clock.
    /// </summary>
    public void Pause()
    {
        _clock.IsPaused = true;
    }

    /// <summary>
    /// Resumes the Clock.
    /// </summary>
    public void Resume()
    {
        _clock.IsPaused = false;
    }

    /// <summary>
    /// Seeks the Clock to the specified time.
    /// </summary>
    public void Seek(TimeSpan offset, TimeSeekOrigin origin)
    {
        _clock.CurrentTime = offset;
        _clock.RaiseCurrentTimeInvalidated();
    }

    /// <summary>
    /// Seeks the Clock to the specified time, aligned to the last tick.
    /// </summary>
    public void SeekAlignedToLastTick(TimeSpan offset, TimeSeekOrigin origin)
    {
        Seek(offset, origin);
    }

    /// <summary>
    /// Stops the Clock.
    /// </summary>
    public void Stop()
    {
        _clock.CurrentState = ClockState.Stopped;
        _clock.CurrentTime = null;
        _clock.CurrentProgress = null;
        _clock.IsPaused = false;
        _clock.RaiseCurrentStateInvalidated();
        _clock.RaiseCompleted();
    }

    /// <summary>
    /// Advances the Clock to the Fill period.
    /// </summary>
    public void SkipToFill()
    {
        _clock.CurrentState = ClockState.Filling;
        _clock.CurrentProgress = 1.0;
        _clock.RaiseCurrentStateInvalidated();
    }

    /// <summary>
    /// Removes the Clock from the timing tree.
    /// </summary>
    public void Remove()
    {
        Stop();
    }

    /// <summary>
    /// Sets the speed ratio for this Clock.
    /// </summary>
    public void SpeedRatio(double ratio)
    {
        // Speed ratio adjustment
    }
}

/// <summary>
/// Describes the potential states of a Clock.
/// </summary>
public enum ClockState
{
    /// <summary>
    /// The Clock is active and progressing.
    /// </summary>
    Active,

    /// <summary>
    /// The Clock is in its fill period.
    /// </summary>
    Filling,

    /// <summary>
    /// The Clock is stopped.
    /// </summary>
    Stopped
}

/// <summary>
/// Specifies the origin of a seek operation.
/// </summary>
public enum TimeSeekOrigin
{
    /// <summary>
    /// The offset is relative to the beginning of the timeline.
    /// </summary>
    BeginTime,

    /// <summary>
    /// The offset is relative to the end of the timeline's active period.
    /// </summary>
    Duration
}

/// <summary>
/// Groups child Timeline objects that should become active at the same time.
/// </summary>
public sealed class ParallelTimeline : TimelineGroup
{
    /// <summary>
    /// Initializes a new instance of the ParallelTimeline class.
    /// </summary>
    public ParallelTimeline()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified begin time.
    /// </summary>
    public ParallelTimeline(TimeSpan? beginTime) : base(beginTime)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified begin time and duration.
    /// </summary>
    public ParallelTimeline(TimeSpan? beginTime, Duration duration) : base(beginTime, duration)
    {
    }

    /// <summary>
    /// Gets or sets the slip behavior for this ParallelTimeline.
    /// </summary>
    public SlipBehavior SlipBehavior { get; set; } = SlipBehavior.Grow;
}

/// <summary>
/// Describes the behavior of a timeline when one of its children slips.
/// </summary>
public enum SlipBehavior
{
    /// <summary>
    /// The parent timeline grows to accommodate the slipping child.
    /// </summary>
    Grow,

    /// <summary>
    /// The parent timeline does not adjust.
    /// </summary>
    Slip
}

/// <summary>
/// A read-only collection of Clock objects associated with a ClockGroup.
/// This is the WPF-compatible TimelineClockCollection equivalent.
/// </summary>
public sealed class ClockCollection : ICollection<Clock>, IReadOnlyList<Clock>
{
    private readonly Clock _owner;

    /// <summary>
    /// Initializes a new instance of the ClockCollection class for the specified owner clock.
    /// </summary>
    internal ClockCollection(Clock owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public int Count
    {
        get
        {
            if (_owner is ClockGroup clockGroup)
            {
                return clockGroup.Children.Count;
            }
            return 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => true;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    public Clock this[int index]
    {
        get
        {
            if (_owner is ClockGroup clockGroup)
            {
                return clockGroup.Children[index];
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Determines whether the collection contains a specific clock.
    /// </summary>
    public bool Contains(Clock item)
    {
        if (_owner is ClockGroup clockGroup)
        {
            return clockGroup.Children.Contains(item);
        }
        return false;
    }

    /// <summary>
    /// Copies the elements of the collection to an array.
    /// </summary>
    public void CopyTo(Clock[] array, int arrayIndex)
    {
        if (_owner is ClockGroup clockGroup)
        {
            for (var i = 0; i < clockGroup.Children.Count; i++)
            {
                array[arrayIndex + i] = clockGroup.Children[i];
            }
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<Clock> GetEnumerator()
    {
        if (_owner is ClockGroup clockGroup)
        {
            return clockGroup.Children.GetEnumerator();
        }
        return Enumerable.Empty<Clock>().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<Clock>.Add(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    bool ICollection<Clock>.Remove(Clock item) => throw new NotSupportedException("ClockCollection is read-only.");
    void ICollection<Clock>.Clear() => throw new NotSupportedException("ClockCollection is read-only.");

    public override bool Equals(object? obj) =>
        obj is ClockCollection other && _owner == other._owner;

    public override int GetHashCode() => _owner.GetHashCode();

    public static bool operator ==(ClockCollection? left, ClockCollection? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left._owner == right._owner;
    }

    public static bool operator !=(ClockCollection? left, ClockCollection? right) => !(left == right);
}

/// <summary>
/// Specifies a timeline for media playback.
/// </summary>
public sealed class MediaTimeline : Timeline
{
    /// <summary>
    /// Gets or sets the media source URI.
    /// </summary>
    public Uri? Source { get; set; }

    /// <summary>
    /// Initializes a new instance of the MediaTimeline class.
    /// </summary>
    public MediaTimeline()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified source.
    /// </summary>
    public MediaTimeline(Uri source)
    {
        Source = source;
    }
}
