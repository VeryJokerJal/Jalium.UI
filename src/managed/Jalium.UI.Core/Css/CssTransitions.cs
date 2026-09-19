using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

internal sealed record CssTransition(TimeSpan Duration, TimeSpan Delay, CssTimingFunction Timing);

internal sealed record CssTransitionData(string[] Properties, double[] Durations, double[] Delays, CssTimingFunction[] Timings)
{
    public bool Equals(CssTransitionData? other) => other is not null && Properties.SequenceEqual(other.Properties) &&
        Durations.SequenceEqual(other.Durations) && Delays.SequenceEqual(other.Delays) && Timings.SequenceEqual(other.Timings);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Properties) hash.Add(value);
        foreach (var value in Durations) hash.Add(value);
        foreach (var value in Delays) hash.Add(value);
        foreach (var value in Timings) hash.Add(value);
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
        return new(TimeSpan.FromMilliseconds(duration), TimeSpan.FromMilliseconds(Delays[index % Delays.Length]), Timings[index % Timings.Length]);
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
            _ => null,
        };
    }

    internal static IAnimationTimeline? Create(UIElement element, DependencyProperty property, object? from, object? to, CssTransition transition)
    {
        var animation = AnimationFactory.CreateTransitionAnimation(property, from, to, transition.Duration,
            element.HasLocalValue(UIElement.TransitionTimingFunctionProperty) ? element.TransitionTimingFunction : TransitionTimingFunction.Linear);
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
}

internal sealed class CssTransitionParts
{
    public string[]? Properties;
    public double[]? Durations;
    public double[]? Delays;
    public CssTimingFunction[]? Timings;
    public bool FromState;

    public void Flush(ICssSetterSink sink)
    {
        var data = new CssTransitionData(Properties ?? [TransitionPropertyCollection.AllKeyword], Durations ?? [0], Delays ?? [0], Timings ?? [CssTimingFunction.EaseDefault]);
        sink.CurrentValueIsState = FromState;
        sink.Set(CssTransitions.DataProperty, data);
        sink.Set(UIElement.TransitionPropertyProperty, new TransitionPropertyCollection(data.Properties));
        sink.Set(UIElement.TransitionDurationProperty, new Duration(TimeSpan.FromMilliseconds(data.Durations[0])));
        sink.Set(UIElement.TransitionTimingFunctionProperty, data.Timings[0].Legacy);
    }
}
