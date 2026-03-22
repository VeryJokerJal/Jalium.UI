using System.Collections.ObjectModel;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Abstract class that, when implemented, represents a Timeline that may contain
/// a collection of child Timeline objects.
/// </summary>
public abstract class TimelineGroup : Timeline
{
    private TimelineCollection? _children;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineGroup"/> class.
    /// </summary>
    protected TimelineGroup()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified begin time.
    /// </summary>
    protected TimelineGroup(TimeSpan? beginTime)
    {
        BeginTime = beginTime;
    }

    /// <summary>
    /// Initializes a new instance with the specified begin time and duration.
    /// </summary>
    protected TimelineGroup(TimeSpan? beginTime, Duration duration)
    {
        BeginTime = beginTime;
        Duration = duration;
    }

    /// <summary>
    /// Initializes a new instance with the specified begin time, duration, and repeat behavior.
    /// </summary>
    protected TimelineGroup(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
    {
        BeginTime = beginTime;
        Duration = duration;
        RepeatBehavior = repeatBehavior;
    }

    /// <summary>
    /// Gets the collection of child Timeline objects.
    /// </summary>
    public TimelineCollection Children
    {
        get
        {
            _children ??= new TimelineCollection();
            return _children;
        }
        set => _children = value;
    }

    /// <summary>
    /// Creates a ClockGroup for this TimelineGroup.
    /// </summary>
    public ClockGroup CreateClock()
    {
        var group = new ClockGroup(this);

        if (_children != null)
        {
            foreach (var child in _children)
            {
                var childClock = new Clock(child);
                group.AddChild(childClock);
            }
        }

        return group;
    }

    /// <inheritdoc/>
    protected internal override Duration GetNaturalDuration()
    {
        if (Duration != Duration.Automatic)
            return Duration;

        // For automatic duration, use the longest child duration
        var maxDuration = TimeSpan.Zero;
        if (_children != null)
        {
            foreach (var child in _children)
            {
                var childDuration = child.GetNaturalDuration();
                if (childDuration.HasTimeSpan && childDuration.TimeSpan > maxDuration)
                {
                    var beginTime = child.BeginTime ?? TimeSpan.Zero;
                    var total = beginTime + childDuration.TimeSpan;
                    if (total > maxDuration)
                        maxDuration = total;
                }
            }
        }

        return maxDuration == TimeSpan.Zero ? Duration.Automatic : new Duration(maxDuration);
    }
}

/// <summary>
/// A collection of Timeline objects.
/// </summary>
public sealed class TimelineCollection : Collection<Timeline>
{
}
