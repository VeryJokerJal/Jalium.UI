using System.ComponentModel;

namespace Jalium.UI;

/// <summary>
/// Represents a duration of time.
/// </summary>
[TypeConverter(typeof(DurationConverter))]
public readonly struct Duration : IEquatable<Duration>
{
    private readonly TimeSpan _timeSpan;
    private readonly DurationType _durationType;

    /// <summary>
    /// Creates a duration from a TimeSpan.
    /// </summary>
    public Duration(TimeSpan timeSpan)
    {
        if (timeSpan < TimeSpan.Zero)
        {
            throw new ArgumentException("Duration must be non-negative.", nameof(timeSpan));
        }

        _timeSpan = timeSpan;
        _durationType = DurationType.TimeSpan;
    }

    /// <summary>
    /// Gets the TimeSpan value of this duration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// This duration is <see cref="Automatic"/> or <see cref="Forever"/>.
    /// </exception>
    public TimeSpan TimeSpan => HasTimeSpan
        ? _timeSpan
        : throw new InvalidOperationException("This Duration does not represent a TimeSpan value.");

    /// <summary>
    /// Gets whether this duration has a TimeSpan value.
    /// </summary>
    public bool HasTimeSpan => _durationType == DurationType.TimeSpan;

    /// <summary>
    /// Gets an automatic duration.
    /// </summary>
    public static Duration Automatic => new(DurationType.Automatic);

    /// <summary>
    /// Gets a forever duration.
    /// </summary>
    public static Duration Forever => new(DurationType.Forever);

    private Duration(DurationType durationType)
    {
        _timeSpan = TimeSpan.Zero;
        _durationType = durationType;
    }

    /// <summary>
    /// Implicitly converts a TimeSpan to a Duration.
    /// </summary>
    public static implicit operator Duration(TimeSpan timeSpan) => new(timeSpan);

    /// <summary>
    /// Adds two durations.
    /// </summary>
    public static Duration operator +(Duration t1, Duration t2)
    {
        if (t1.HasTimeSpan && t2.HasTimeSpan)
        {
            return new Duration(t1._timeSpan + t2._timeSpan);
        }

        if (t1._durationType != DurationType.Automatic &&
            t2._durationType != DurationType.Automatic)
        {
            return Forever;
        }

        return Automatic;
    }

    /// <summary>
    /// Subtracts one duration from another.
    /// </summary>
    public static Duration operator -(Duration t1, Duration t2)
    {
        if (t1.HasTimeSpan && t2.HasTimeSpan)
        {
            return new Duration(t1._timeSpan - t2._timeSpan);
        }

        if (t1._durationType == DurationType.Forever && t2.HasTimeSpan)
        {
            return Forever;
        }

        return Automatic;
    }

    /// <summary>
    /// Returns the supplied duration unchanged.
    /// </summary>
    public static Duration operator +(Duration duration) => duration;

    /// <summary>
    /// Determines whether the first duration is greater than the second.
    /// </summary>
    public static bool operator >(Duration t1, Duration t2)
    {
        if (t1.HasTimeSpan && t2.HasTimeSpan)
        {
            return t1._timeSpan > t2._timeSpan;
        }

        return t1._durationType == DurationType.Forever && t2.HasTimeSpan;
    }

    /// <summary>
    /// Determines whether the first duration is greater than or equal to the second.
    /// </summary>
    public static bool operator >=(Duration t1, Duration t2)
    {
        if (t1._durationType == DurationType.Automatic &&
            t2._durationType == DurationType.Automatic)
        {
            return true;
        }

        if (t1._durationType == DurationType.Automatic ||
            t2._durationType == DurationType.Automatic)
        {
            return false;
        }

        return !(t1 < t2);
    }

    /// <summary>
    /// Determines whether the first duration is less than the second.
    /// </summary>
    public static bool operator <(Duration t1, Duration t2)
    {
        if (t1.HasTimeSpan && t2.HasTimeSpan)
        {
            return t1._timeSpan < t2._timeSpan;
        }

        return t1.HasTimeSpan && t2._durationType == DurationType.Forever;
    }

    /// <summary>
    /// Determines whether the first duration is less than or equal to the second.
    /// </summary>
    public static bool operator <=(Duration t1, Duration t2)
    {
        if (t1._durationType == DurationType.Automatic &&
            t2._durationType == DurationType.Automatic)
        {
            return true;
        }

        if (t1._durationType == DurationType.Automatic ||
            t2._durationType == DurationType.Automatic)
        {
            return false;
        }

        return !(t1 > t2);
    }

    /// <summary>
    /// Compares two durations using WPF's ordering for Automatic, finite, and Forever values.
    /// </summary>
    public static int Compare(Duration t1, Duration t2)
    {
        if (t1._durationType == DurationType.Automatic)
        {
            return t2._durationType == DurationType.Automatic ? 0 : -1;
        }

        if (t2._durationType == DurationType.Automatic)
        {
            return 1;
        }

        if (t1 < t2)
        {
            return -1;
        }

        return t1 > t2 ? 1 : 0;
    }

    /// <summary>
    /// Returns the supplied duration unchanged.
    /// </summary>
    public static Duration Plus(Duration duration) => duration;

    /// <summary>
    /// Adds the supplied duration to this instance.
    /// </summary>
    public Duration Add(Duration duration) => this + duration;

    /// <summary>
    /// Subtracts the supplied duration from this instance.
    /// </summary>
    public Duration Subtract(Duration duration) => this - duration;

    /// <inheritdoc />
    public bool Equals(Duration other) =>
        _durationType == other._durationType && _timeSpan == other._timeSpan;

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is Duration other && Equals(other);

    /// <summary>
    /// Determines whether two durations are equal.
    /// </summary>
    public static bool Equals(Duration t1, Duration t2) => t1.Equals(t2);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HasTimeSpan ? _timeSpan.GetHashCode() : _durationType.GetHashCode() + 17;

    public static bool operator ==(Duration left, Duration right) => left.Equals(right);
    public static bool operator !=(Duration left, Duration right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => HasTimeSpan
        ? _timeSpan.ToString()
        : ToStringInvariant();

    internal string ToStringInvariant() => _durationType switch
    {
        DurationType.Forever => "Forever",
        DurationType.Automatic => "Automatic",
        _ => _timeSpan.ToString()
    };

    private enum DurationType
    {
        Automatic,
        TimeSpan,
        Forever
    }
}
