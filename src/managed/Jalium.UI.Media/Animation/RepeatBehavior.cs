namespace Jalium.UI.Media.Animation;

/// <summary>
/// Describes how a <see cref="Timeline"/> repeats its simple duration.
/// </summary>
public readonly struct RepeatBehavior : IEquatable<RepeatBehavior>, IFormattable
{
    private readonly double _count;
    private readonly TimeSpan _duration;
    private readonly RepeatBehaviorType _type;

    /// <summary>
    /// Initializes a new instance with the specified iteration count.
    /// </summary>
    public RepeatBehavior(double count)
    {
        if (count < 0 || double.IsInfinity(count) || double.IsNaN(count))
            throw new ArgumentOutOfRangeException(nameof(count));

        _count = count;
        _duration = TimeSpan.Zero;
        _type = RepeatBehaviorType.Count;
    }

    /// <summary>
    /// Initializes a new instance with the specified repeat duration.
    /// </summary>
    public RepeatBehavior(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        _count = 0;
        _duration = duration;
        _type = RepeatBehaviorType.Duration;
    }

    /// <summary>
    /// Gets a RepeatBehavior that specifies an infinite number of repetitions.
    /// </summary>
    public static RepeatBehavior Forever => new(RepeatBehaviorType.Forever);

    private RepeatBehavior(RepeatBehaviorType type)
    {
        _type = type;
        _count = 0;
        _duration = TimeSpan.Zero;
    }

    /// <summary>
    /// Gets a value indicating whether the repeat behavior has a count.
    /// </summary>
    public bool HasCount => _type == RepeatBehaviorType.Count;

    /// <summary>
    /// Gets a value indicating whether the repeat behavior has a duration.
    /// </summary>
    public bool HasDuration => _type == RepeatBehaviorType.Duration;

    /// <summary>
    /// Gets the number of times a Timeline should repeat.
    /// </summary>
    public double Count
    {
        get
        {
            if (_type != RepeatBehaviorType.Count)
                throw new InvalidOperationException("This RepeatBehavior does not represent an iteration count.");
            return _count;
        }
    }

    /// <summary>
    /// Gets the total length of time a Timeline should play.
    /// </summary>
    public TimeSpan Duration
    {
        get
        {
            if (_type != RepeatBehaviorType.Duration)
                throw new InvalidOperationException("This RepeatBehavior does not represent a repeat duration.");
            return _duration;
        }
    }

    /// <inheritdoc />
    public bool Equals(RepeatBehavior other) =>
        _type == other._type && _count == other._count && _duration == other._duration;

    /// <summary>Determines whether two repeat behaviors represent the same behavior.</summary>
    public static bool Equals(RepeatBehavior repeatBehavior1, RepeatBehavior repeatBehavior2) =>
        repeatBehavior1.Equals(repeatBehavior2);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RepeatBehavior other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _type switch
    {
        RepeatBehaviorType.Count => _count.GetHashCode(),
        RepeatBehaviorType.Duration => _duration.GetHashCode(),
        _ => int.MaxValue - 42
    };

    /// <summary>Equality operator.</summary>
    public static bool operator ==(RepeatBehavior left, RepeatBehavior right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(RepeatBehavior left, RepeatBehavior right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => ToString(null, null);

    /// <summary>Formats this repeat behavior using the supplied culture.</summary>
    public string ToString(IFormatProvider? formatProvider) => ToString(null, formatProvider);

    /// <inheritdoc />
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return _type switch
        {
            RepeatBehaviorType.Forever => "Forever",
            RepeatBehaviorType.Count => string.Format(formatProvider, "{0:" + format + "}x", _count),
            RepeatBehaviorType.Duration => _duration.ToString(),
            _ => "Forever"
        };
    }
}

/// <summary>
/// Specifies the type of a RepeatBehavior.
/// </summary>
public enum RepeatBehaviorType
{
    /// <summary>Repeat a specified number of times.</summary>
    Count,
    /// <summary>Repeat for a specified duration.</summary>
    Duration,
    /// <summary>Repeat forever.</summary>
    Forever
}
