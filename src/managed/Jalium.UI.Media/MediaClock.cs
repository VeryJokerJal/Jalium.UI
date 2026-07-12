using Jalium.UI.Markup;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media;

/// <summary>
/// Describes media playback over a timeline and creates a matching <see cref="MediaClock"/>.
/// </summary>
public class MediaTimeline : Timeline, IUriContext
{
    private Uri? _baseUri;

    /// <summary>
    /// Identifies the <see cref="Source"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(Uri),
            typeof(MediaTimeline),
            new PropertyMetadata(null));

    /// <summary>
    /// Initializes an empty media timeline.
    /// </summary>
    public MediaTimeline()
    {
    }

    /// <summary>
    /// Initializes a media timeline for the supplied source.
    /// </summary>
    public MediaTimeline(Uri source)
    {
        if (source == null)
        {
            throw new InvalidOperationException("Must specify URI.");
        }

        Source = source;
    }

    /// <summary>
    /// Initializes a media timeline with a begin time.
    /// </summary>
    public MediaTimeline(TimeSpan? beginTime)
    {
        BeginTime = beginTime;
    }

    /// <summary>
    /// Initializes a media timeline with a begin time and duration.
    /// </summary>
    public MediaTimeline(TimeSpan? beginTime, Duration duration)
        : this(beginTime)
    {
        Duration = duration;
    }

    /// <summary>
    /// Initializes a media timeline with timing and repeat behavior.
    /// </summary>
    public MediaTimeline(TimeSpan? beginTime, Duration duration, RepeatBehavior repeatBehavior)
        : this(beginTime, duration)
    {
        RepeatBehavior = repeatBehavior;
    }

    /// <summary>
    /// Gets or sets the media source URI.
    /// </summary>
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Creates a controllable clock using a snapshot of this timeline.
    /// </summary>
    public new MediaClock CreateClock() => new(CloneForClock());

    Uri? IUriContext.BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    internal Uri? BaseUri => _baseUri;

    public new MediaTimeline Clone() => (MediaTimeline)base.Clone();

    public new MediaTimeline CloneCurrentValue() => (MediaTimeline)base.CloneCurrentValue();

    protected internal override Clock AllocateClock() => new MediaClock(this);

    protected override Duration GetNaturalDurationCore(Clock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Duration.Automatic;
    }

    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        _baseUri = ((MediaTimeline)sourceFreezable)._baseUri;
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        _baseUri = ((MediaTimeline)sourceFreezable)._baseUri;
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        _baseUri = ((MediaTimeline)source)._baseUri;
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        _baseUri = ((MediaTimeline)source)._baseUri;
    }

    protected override Freezable CreateInstanceCore() => new MediaTimeline();

    private MediaTimeline CloneForClock()
    {
        var clone = new MediaTimeline
        {
            Source = Source,
            BeginTime = BeginTime,
            Duration = Duration,
            RepeatBehavior = RepeatBehavior,
            AutoReverse = AutoReverse,
            FillBehavior = FillBehavior,
            SpeedRatio = SpeedRatio,
        };
        ((IUriContext)clone).BaseUri = _baseUri;
        return clone;
    }

    /// <inheritdoc />
    public override string ToString() =>
        Source?.ToString() ?? throw new InvalidOperationException("Must specify URI.");
}

/// <summary>
/// Maintains run-time timing state for a <see cref="MediaTimeline"/>.
/// </summary>
public class MediaClock : Clock
{
    /// <summary>
    /// Initializes a clock for a media timeline.
    /// </summary>
    protected internal MediaClock(MediaTimeline media)
        : base(media ?? throw new ArgumentNullException(nameof(media)))
    {
    }

    /// <summary>
    /// Gets the media timeline that created this clock.
    /// </summary>
    public new MediaTimeline Timeline => (MediaTimeline)base.Timeline;
}
