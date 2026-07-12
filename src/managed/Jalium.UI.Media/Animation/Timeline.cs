using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies how a timeline behaves when it is outside its active period.
/// </summary>
public enum FillBehavior
{
    HoldEnd,
    Stop,
}

/// <summary>
/// Defines a segment of time and creates the clock that evaluates that segment.
/// </summary>
public abstract class Timeline : Animatable
{
    public static readonly DependencyProperty AccelerationRatioProperty =
        DependencyProperty.Register(
            nameof(AccelerationRatio),
            typeof(double),
            typeof(Timeline),
            new PropertyMetadata(0d),
            IsValidRatio);

    public static readonly DependencyProperty AutoReverseProperty =
        DependencyProperty.Register(
            nameof(AutoReverse),
            typeof(bool),
            typeof(Timeline),
            new PropertyMetadata(false));

    public static readonly DependencyProperty BeginTimeProperty =
        DependencyProperty.Register(
            nameof(BeginTime),
            typeof(TimeSpan?),
            typeof(Timeline),
            new PropertyMetadata(TimeSpan.Zero));

    public static readonly DependencyProperty DecelerationRatioProperty =
        DependencyProperty.Register(
            nameof(DecelerationRatio),
            typeof(double),
            typeof(Timeline),
            new PropertyMetadata(0d),
            IsValidRatio);

    public static readonly DependencyProperty DesiredFrameRateProperty =
        DependencyProperty.RegisterAttached(
            "DesiredFrameRate",
            typeof(int?),
            typeof(Timeline),
            new PropertyMetadata(null),
            IsValidDesiredFrameRate);

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(
            nameof(Duration),
            typeof(Duration),
            typeof(Timeline),
            new PropertyMetadata(Duration.Automatic));

    public static readonly DependencyProperty FillBehaviorProperty =
        DependencyProperty.Register(
            nameof(FillBehavior),
            typeof(FillBehavior),
            typeof(Timeline),
            new PropertyMetadata(FillBehavior.HoldEnd),
            static value => value is FillBehavior behavior && Enum.IsDefined(behavior));

    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(
            nameof(Name),
            typeof(string),
            typeof(Timeline),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RepeatBehaviorProperty =
        DependencyProperty.Register(
            nameof(RepeatBehavior),
            typeof(RepeatBehavior),
            typeof(Timeline),
            new PropertyMetadata(new RepeatBehavior(1d)));

    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(
            nameof(SpeedRatio),
            typeof(double),
            typeof(Timeline),
            new PropertyMetadata(1d),
            static value => value is double ratio && double.IsFinite(ratio) && ratio > 0d);

    private EventHandler? _completed;
    private EventHandler? _currentGlobalSpeedInvalidated;
    private EventHandler? _currentStateInvalidated;
    private EventHandler? _currentTimeInvalidated;
    private EventHandler? _removeRequested;

    protected Timeline()
    {
    }

    protected Timeline(TimeSpan? beginTime)
    {
        BeginTime = beginTime;
    }

    protected Timeline(TimeSpan? beginTime, Duration duration)
        : this(beginTime)
    {
        Duration = duration;
    }

    protected Timeline(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
        : this(beginTime, duration)
    {
        RepeatBehavior = repeatBehavior;
    }

    public double AccelerationRatio
    {
        get => (double)(GetValue(AccelerationRatioProperty) ?? 0d);
        set
        {
            ValidateCombinedRatios(value, DecelerationRatio, nameof(value));
            SetValue(AccelerationRatioProperty, value);
        }
    }

    public bool AutoReverse
    {
        get => (bool)(GetValue(AutoReverseProperty) ?? false);
        set => SetValue(AutoReverseProperty, value);
    }

    public TimeSpan? BeginTime
    {
        get => (TimeSpan?)GetValue(BeginTimeProperty);
        set => SetValue(BeginTimeProperty, value);
    }

    public double DecelerationRatio
    {
        get => (double)(GetValue(DecelerationRatioProperty) ?? 0d);
        set
        {
            ValidateCombinedRatios(AccelerationRatio, value, nameof(value));
            SetValue(DecelerationRatioProperty, value);
        }
    }

    public Duration Duration
    {
        get => (Duration)(GetValue(DurationProperty) ?? Duration.Automatic);
        set => SetValue(DurationProperty, value);
    }

    public FillBehavior FillBehavior
    {
        get => (FillBehavior)(GetValue(FillBehaviorProperty) ?? FillBehavior.HoldEnd);
        set => SetValue(FillBehaviorProperty, value);
    }

    [DefaultValue(null)]
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public RepeatBehavior RepeatBehavior
    {
        get => (RepeatBehavior)(GetValue(RepeatBehaviorProperty) ?? new RepeatBehavior(1d));
        set => SetValue(RepeatBehaviorProperty, value);
    }

    public double SpeedRatio
    {
        get => (double)(GetValue(SpeedRatioProperty) ?? 1d);
        set => SetValue(SpeedRatioProperty, value);
    }

    public event EventHandler? Completed
    {
        add => _completed += value;
        remove => _completed -= value;
    }

    public event EventHandler? CurrentGlobalSpeedInvalidated
    {
        add => _currentGlobalSpeedInvalidated += value;
        remove => _currentGlobalSpeedInvalidated -= value;
    }

    public event EventHandler? CurrentStateInvalidated
    {
        add => _currentStateInvalidated += value;
        remove => _currentStateInvalidated -= value;
    }

    public event EventHandler? CurrentTimeInvalidated
    {
        add => _currentTimeInvalidated += value;
        remove => _currentTimeInvalidated -= value;
    }

    public event EventHandler? RemoveRequested
    {
        add => _removeRequested += value;
        remove => _removeRequested -= value;
    }

    public static int? GetDesiredFrameRate(Timeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return (int?)timeline.GetValue(DesiredFrameRateProperty);
    }

    public static void SetDesiredFrameRate(Timeline timeline, int? desiredFrameRate)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        timeline.SetValue(DesiredFrameRateProperty, desiredFrameRate);
    }

    /// <summary>Creates an uncontrollable root clock for this timeline.</summary>
    public Clock CreateClock() => CreateClock(hasControllableRoot: false);

    /// <summary>Creates a root clock and optionally exposes its controller.</summary>
    public Clock CreateClock(bool hasControllableRoot)
    {
        ValidateTimingProperties();

        Clock clock = AllocateClock();
        clock.SetHasControllableRoot(hasControllableRoot);
        AttachEventHandlers(clock);
        return clock;
    }

    /// <summary>Allocates the runtime clock used by this timeline.</summary>
    protected internal virtual Clock AllocateClock() => new(this);

    public new Timeline Clone() => (Timeline)base.Clone();

    public new Timeline CloneCurrentValue() => (Timeline)base.CloneCurrentValue();

    protected internal Duration GetNaturalDuration(Clock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Duration == Duration.Automatic ? GetNaturalDurationCore(clock) : Duration;
    }

    // Compatibility overload retained for existing Jalium callers.
    protected internal virtual Duration GetNaturalDuration() =>
        Duration == Duration.Automatic ? GetNaturalDurationCore(new Clock(this)) : Duration;

    protected virtual Duration GetNaturalDurationCore(Clock clock) => Duration.Automatic;

    protected override bool FreezeCore(bool isChecking)
    {
        ValidateTimingProperties();
        return base.FreezeCore(isChecking);
    }

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyEventHandlers((Timeline)sourceFreezable);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyEventHandlers((Timeline)sourceFreezable);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyEventHandlers((Timeline)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyEventHandlers((Timeline)sourceFreezable);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Freezable cloning requires the runtime Timeline subtype; public Timeline subtypes retain parameterless constructors as part of the WPF-compatible contract.")]
    protected override Freezable CreateInstanceCore()
    {
        Type runtimeType = GetType();
        return (Freezable?)Activator.CreateInstance(runtimeType, nonPublic: true)
            ?? throw new InvalidOperationException($"Timeline type '{runtimeType.FullName}' must have a parameterless constructor.");
    }

    /// <summary>
    /// Raises completion directly for the Jalium frame-driven storyboard path.
    /// Clock-created timelines normally raise the corresponding clock event.
    /// </summary>
    protected virtual void OnCompleted() => _completed?.Invoke(this, EventArgs.Empty);

    protected virtual void OnCurrentGlobalSpeedInvalidated() =>
        _currentGlobalSpeedInvalidated?.Invoke(this, EventArgs.Empty);

    protected virtual void OnCurrentStateInvalidated() =>
        _currentStateInvalidated?.Invoke(this, EventArgs.Empty);

    protected virtual void OnCurrentTimeInvalidated() =>
        _currentTimeInvalidated?.Invoke(this, EventArgs.Empty);

    protected virtual void OnRemoveRequested() =>
        _removeRequested?.Invoke(this, EventArgs.Empty);

    private void AttachEventHandlers(Clock clock)
    {
        if (_completed is not null) clock.Completed += _completed;
        if (_currentGlobalSpeedInvalidated is not null) clock.CurrentGlobalSpeedInvalidated += _currentGlobalSpeedInvalidated;
        if (_currentStateInvalidated is not null) clock.CurrentStateInvalidated += _currentStateInvalidated;
        if (_currentTimeInvalidated is not null) clock.CurrentTimeInvalidated += _currentTimeInvalidated;
        if (_removeRequested is not null) clock.RemoveRequested += _removeRequested;
    }

    private void CopyEventHandlers(Timeline source)
    {
        _completed = source._completed;
        _currentGlobalSpeedInvalidated = source._currentGlobalSpeedInvalidated;
        _currentStateInvalidated = source._currentStateInvalidated;
        _currentTimeInvalidated = source._currentTimeInvalidated;
        _removeRequested = source._removeRequested;
    }

    private void ValidateTimingProperties()
    {
        ValidateCombinedRatios(AccelerationRatio, DecelerationRatio, null);
    }

    private static bool IsValidRatio(object? value) =>
        value is double ratio && double.IsFinite(ratio) && ratio >= 0d && ratio <= 1d;

    private static bool IsValidDesiredFrameRate(object? value) =>
        value is null || value is int frameRate && frameRate > 0;

    private static void ValidateCombinedRatios(double acceleration, double deceleration, string? parameterName)
    {
        if (!double.IsFinite(acceleration) || !double.IsFinite(deceleration) ||
            acceleration < 0d || acceleration > 1d ||
            deceleration < 0d || deceleration > 1d ||
            acceleration + deceleration > 1d)
        {
            throw new ArgumentException(
                "AccelerationRatio and DecelerationRatio must each be between zero and one and their sum cannot exceed one.",
                parameterName);
        }
    }
}
