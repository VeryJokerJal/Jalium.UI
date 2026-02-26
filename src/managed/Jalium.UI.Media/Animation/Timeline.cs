namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies how a timeline behaves when it is outside its active period.
/// </summary>
public enum FillBehavior
{
    /// <summary>
    /// The timeline holds its final value after its active period ends.
    /// </summary>
    HoldEnd,

    /// <summary>
    /// The timeline stops when its active period ends.
    /// </summary>
    Stop
}

/// <summary>
/// Defines a segment of time.
/// </summary>
public abstract class Timeline : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the BeginTime dependency property.
    /// </summary>
    public static readonly DependencyProperty BeginTimeProperty =
        DependencyProperty.Register(nameof(BeginTime), typeof(TimeSpan?), typeof(Timeline),
            new PropertyMetadata(TimeSpan.Zero));

    /// <summary>
    /// Identifies the Duration dependency property.
    /// </summary>
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(Duration), typeof(Timeline),
            new PropertyMetadata(Duration.Automatic));

    /// <summary>
    /// Identifies the AutoReverse dependency property.
    /// </summary>
    public static readonly DependencyProperty AutoReverseProperty =
        DependencyProperty.Register(nameof(AutoReverse), typeof(bool), typeof(Timeline),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the FillBehavior dependency property.
    /// </summary>
    public static readonly DependencyProperty FillBehaviorProperty =
        DependencyProperty.Register(nameof(FillBehavior), typeof(FillBehavior), typeof(Timeline),
            new PropertyMetadata(FillBehavior.HoldEnd));

    /// <summary>
    /// Identifies the SpeedRatio dependency property.
    /// </summary>
    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(Timeline),
            new PropertyMetadata(1.0));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the time at which this timeline should begin.
    /// </summary>
    public TimeSpan? BeginTime
    {
        get => (TimeSpan?)GetValue(BeginTimeProperty);
        set => SetValue(BeginTimeProperty, value);
    }

    /// <summary>
    /// Gets or sets the length of time for which this timeline plays.
    /// </summary>
    public Duration Duration
    {
        get => (Duration)(GetValue(DurationProperty) ?? Duration.Automatic);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the timeline plays in reverse after completing a forward iteration.
    /// </summary>
    public bool AutoReverse
    {
        get => (bool)GetValue(AutoReverseProperty)!;
        set => SetValue(AutoReverseProperty, value);
    }

    /// <summary>
    /// Gets or sets the behavior of this timeline when it is outside its active period.
    /// </summary>
    public FillBehavior FillBehavior
    {
        get => (FillBehavior)(GetValue(FillBehaviorProperty) ?? FillBehavior.HoldEnd);
        set => SetValue(FillBehaviorProperty, value);
    }

    /// <summary>
    /// Gets or sets the rate at which time progresses for this timeline.
    /// </summary>
    public double SpeedRatio
    {
        get => (double)GetValue(SpeedRatioProperty)!;
        set => SetValue(SpeedRatioProperty, value);
    }

    /// <summary>
    /// Gets or sets how many times this timeline should repeat.
    /// </summary>
    public RepeatBehavior RepeatBehavior { get; set; } = new RepeatBehavior(1.0);

    #endregion

    #region Events

    /// <summary>
    /// Occurs when this timeline has completely finished playing.
    /// </summary>
    public event EventHandler? Completed;

    #endregion

    /// <summary>
    /// Raises the Completed event.
    /// </summary>
    protected virtual void OnCompleted()
    {
        Completed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the natural duration of this timeline.
    /// </summary>
    protected internal virtual Duration GetNaturalDuration()
    {
        return Duration;
    }
}

/// <summary>
/// Represents a duration of time.
/// </summary>
public readonly struct Duration : IEquatable<Duration>
{
    private readonly TimeSpan _timeSpan;
    private readonly DurationType _durationType;

    /// <summary>
    /// Creates a duration from a TimeSpan.
    /// </summary>
    public Duration(TimeSpan timeSpan)
    {
        _timeSpan = timeSpan;
        _durationType = DurationType.TimeSpan;
    }

    /// <summary>
    /// Gets the TimeSpan value of this duration.
    /// </summary>
    public TimeSpan TimeSpan => _timeSpan;

    /// <summary>
    /// Gets whether this duration has a TimeSpan value.
    /// </summary>
    public bool HasTimeSpan => _durationType == DurationType.TimeSpan;

    /// <summary>
    /// Gets an automatic duration.
    /// </summary>
    public static Duration Automatic { get; } = new Duration(DurationType.Automatic);

    /// <summary>
    /// Gets a forever duration.
    /// </summary>
    public static Duration Forever { get; } = new Duration(DurationType.Forever);

    private Duration(DurationType durationType)
    {
        _timeSpan = TimeSpan.Zero;
        _durationType = durationType;
    }

    /// <summary>
    /// Implicitly converts a TimeSpan to a Duration.
    /// </summary>
    public static implicit operator Duration(TimeSpan timeSpan) => new(timeSpan);

    public bool Equals(Duration other) =>
        _durationType == other._durationType && _timeSpan == other._timeSpan;

    public override bool Equals(object? obj) =>
        obj is Duration other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(_durationType, _timeSpan);

    public static bool operator ==(Duration left, Duration right) => left.Equals(right);
    public static bool operator !=(Duration left, Duration right) => !left.Equals(right);

    private enum DurationType
    {
        Automatic,
        TimeSpan,
        Forever
    }
}
