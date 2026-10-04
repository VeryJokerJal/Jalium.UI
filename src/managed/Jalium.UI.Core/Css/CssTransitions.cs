using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

internal sealed record CssTransition(TimeSpan Duration, TimeSpan Delay,
    CssTimingFunction Timing, bool AllowDiscrete);

internal sealed record CssTransitionData(string[] Properties, double[] Durations,
    double[] Delays, CssTimingFunction[] Timings, bool[] Behaviors)
{
    public bool Equals(CssTransitionData? other) => other is not null && Properties.SequenceEqual(other.Properties) &&
        Durations.SequenceEqual(other.Durations) && Delays.SequenceEqual(other.Delays) &&
        Timings.SequenceEqual(other.Timings) && Behaviors.SequenceEqual(other.Behaviors);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Properties) hash.Add(value);
        foreach (var value in Durations) hash.Add(value);
        foreach (var value in Delays) hash.Add(value);
        foreach (var value in Timings) hash.Add(value);
        foreach (var value in Behaviors) hash.Add(value);
        return hash.ToHashCode();
    }

    public CssTransition? Find(UIElement element, DependencyProperty property)
    {
        var properties = Properties;
        if (element.HasLocalValue(UIElement.TransitionPropertyProperty))
        {
            var selection = element.GetValue(UIElement.TransitionPropertyProperty) is TransitionPropertyCollection collection
                ? collection : TransitionPropertyCollection.Parse(element.GetValue(UIElement.TransitionPropertyProperty)?.ToString());
            properties = selection.IsAll ? [TransitionPropertyCollection.AllKeyword] : selection.ToArray();
        }
        var index = -1;
        for (var i = 0; i < properties.Length; i++)
            if (properties[i] == TransitionPropertyCollection.AllKeyword || properties[i].Equals(property.Name, StringComparison.OrdinalIgnoreCase)) index = i;
        if (index < 0) return null;
        var duration = element.HasLocalValue(UIElement.TransitionDurationProperty)
            ? element.TransitionDuration.HasTimeSpan ? element.TransitionDuration.TimeSpan.TotalMilliseconds : 0
            : Durations[index % Durations.Length];
        return new(TimeSpan.FromMilliseconds(duration),
            TimeSpan.FromMilliseconds(Delays[index % Delays.Length]),
            Timings[index % Timings.Length], Behaviors[index % Behaviors.Length]);
    }
}

internal static class CssTransitions
{
    internal static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "TransitionData", typeof(CssTransitionData), typeof(CssTransitions), new PropertyMetadata(null));

    internal static bool IsConfiguration(DependencyProperty property) => property == DataProperty ||
        property == UIElement.TransitionPropertyProperty || property == UIElement.TransitionDurationProperty || property == UIElement.TransitionTimingFunctionProperty;

    internal static object? InheritedPart(CssNode parent, string property)
    {
        if (parent.GetValue(DataProperty) is not CssTransitionData data) return null;
        return property switch
        {
            "transition-property" => data.Properties,
            "transition-duration" => parent.HasLocalValue(UIElement.TransitionDurationProperty) &&
                parent.GetValue(UIElement.TransitionDurationProperty) is Duration { HasTimeSpan: true } duration
                    ? new[] { duration.TimeSpan.TotalMilliseconds } : data.Durations,
            "transition-delay" => data.Delays,
            "transition-timing-function" => data.Timings,
            "transition-behavior" => data.Behaviors,
            _ => null,
        };
    }

    internal static IAnimationTimeline? Create(UIElement element, DependencyProperty property, object? from, object? to, CssTransition transition)
    {
        IAnimationTimeline? animation = property == CssDisplayProperties.SpecificationProperty &&
            transition.AllowDiscrete && from is CssDisplaySpecification before &&
            to is CssDisplaySpecification after
            ? new CssDisplayAnimation
            {
                From = before, To = after, Duration = new Duration(transition.Duration),
            }
            : property == UIElement.RenderTransformProperty &&
                (from is null or Jalium.UI.Media.Transform) &&
                (to is null or Jalium.UI.Media.Transform)
            ? new CssTransformAnimation
            {
                From = from as Jalium.UI.Media.Transform,
                To = to as Jalium.UI.Media.Transform,
                Duration = new Duration(transition.Duration),
            }
            : AnimationFactory.CreateTransitionAnimation(property, from, to, transition.Duration,
            element.HasLocalValue(UIElement.TransitionTimingFunctionProperty) ? element.TransitionTimingFunction : TransitionTimingFunction.Linear);
        if (animation is null && transition.AllowDiscrete &&
            IsSupportedDiscreteProperty(property, from, to))
            animation = new CssDiscreteAnimation
            {
                PropertyType = property.PropertyType,
                From = from,
                To = to,
                Duration = new Duration(transition.Duration),
            };
        if (animation is Timeline timeline)
        {
            timeline.BeginTime = transition.Delay;
            timeline.FillBehavior = FillBehavior.Stop;
        }
        if (!element.HasLocalValue(UIElement.TransitionTimingFunctionProperty) && animation is DependencyObject target &&
            DependencyProperty.FromName(target.GetType(), "EasingFunction") is { } easing)
            target.SetValue(easing, transition.Timing);
        return animation;
    }

    internal static object? InitialValue(DependencyProperty property, object? from, object? to) =>
        property == CssDisplayProperties.SpecificationProperty &&
        from is CssDisplaySpecification { None: true } &&
        to is CssDisplaySpecification { None: false } ? to : from;

    private static bool IsSupportedDiscreteProperty(DependencyProperty property,
        object? from, object? to) =>
        property == Jalium.UI.Controls.ScrollViewer.CssScrollBarWidthProperty ||
        property == Jalium.UI.Controls.ScrollViewer.CssScrollBarGutterProperty ||
        property == CssDisplayProperties.VisibilityProperty ||
        property == CssPointerEventsProperties.ValueProperty ||
        property == CssUserSelectProperties.ValueProperty ||
        property == FrameworkElement.CursorProperty ||
        property == CssImageRenderingProperties.ValueProperty ||
        property == CssImageObjectProperties.FitProperty ||
        property == CssTransformReferenceBox.ValueProperty ||
        property == Jalium.UI.Controls.ScrollViewer.CssScrollBarColorsProperty &&
            (from is null || to is null);
}

internal sealed class CssTransitionParts
{
    public string[]? Properties;
    public double[]? Durations;
    public double[]? Delays;
    public CssTimingFunction[]? Timings;
    public bool[]? Behaviors;
    public bool FromState;

    public void Flush(ICssSetterSink sink)
    {
        var data = new CssTransitionData(Properties ?? [TransitionPropertyCollection.AllKeyword],
            Durations ?? [0], Delays ?? [0], Timings ?? [CssTimingFunction.EaseDefault],
            Behaviors ?? [false]);
        sink.CurrentValueIsState = FromState;
        sink.Set(CssTransitions.DataProperty, data);
        sink.Set(UIElement.TransitionPropertyProperty, new TransitionPropertyCollection(data.Properties));
        sink.Set(UIElement.TransitionDurationProperty, new Duration(TimeSpan.FromMilliseconds(data.Durations[0])));
        sink.Set(UIElement.TransitionTimingFunctionProperty, data.Timings[0].Legacy);
    }
}

/// <summary>Flips supported CSS keyword values after the timing curve reaches 50%.</summary>
internal sealed class CssDiscreteAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(object), typeof(CssDiscreteAnimation),
            new PropertyMetadata(null));

    internal static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(object), typeof(CssDiscreteAnimation),
            new PropertyMetadata(null));

    internal static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction),
            typeof(CssDiscreteAnimation), new PropertyMetadata(null));

    internal Type PropertyType { get; set; } = typeof(object);
    internal object? From { get => GetValue(FromProperty); set => SetValue(FromProperty, value); }
    internal object? To { get => GetValue(ToProperty); set => SetValue(ToProperty, value); }
    internal IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => PropertyType;

    public override object GetCurrentValue(object defaultOriginValue,
        object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var progress = EasingFunction?.Ease(animationClock.CurrentProgress) ??
            animationClock.CurrentProgress;
        return (progress < .5 ? From : To)!;
    }

    protected override Freezable CreateInstanceCore() => new CssDiscreteAnimation
    {
        PropertyType = PropertyType,
    };
}

/// <summary>CSS Display 4 keeps a none/non-none pair rendered until the exit endpoint.</summary>
internal sealed class CssDisplayAnimation : AnimationTimeline
{
    internal static readonly DependencyProperty EasingFunctionProperty = DependencyProperty.Register(
        nameof(EasingFunction), typeof(IEasingFunction), typeof(CssDisplayAnimation), new PropertyMetadata(null));

    internal CssDisplaySpecification From { get; set; }
    internal CssDisplaySpecification To { get; set; }
    internal IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public override Type TargetPropertyType => typeof(CssDisplaySpecification);

    public override object GetCurrentValue(object defaultOriginValue,
        object defaultDestinationValue, AnimationClock animationClock)
    {
        ArgumentNullException.ThrowIfNull(animationClock);
        var progress = EasingFunction?.Ease(animationClock.CurrentProgress) ?? animationClock.CurrentProgress;
        if (From.None != To.None)
            return To.None && progress >= 1 ? To : From.None ? To : From;
        return progress < .5 ? From : To;
    }

    protected override Freezable CreateInstanceCore() => new CssDisplayAnimation
    {
        From = From, To = To,
    };
}
