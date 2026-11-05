using System.Diagnostics;
using System.Runtime.CompilerServices;
using Jalium.UI.Animation;
using Jalium.UI.Documents;
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

    public CssTransition? Find(DependencyObject element, DependencyProperty property)
    {
        if (property == CssUnicodeBidiProperties.ValueProperty || property == FrameworkElement.FlowDirectionProperty)
            return null;

        var properties = Properties;
        if (element is UIElement uiElement && uiElement.HasLocalValue(UIElement.TransitionPropertyProperty))
        {
            var selection = uiElement.GetValue(UIElement.TransitionPropertyProperty) is TransitionPropertyCollection collection
                ? collection
                : TransitionPropertyCollection.Parse(uiElement.GetValue(UIElement.TransitionPropertyProperty)?.ToString());
            properties = selection.IsAll ? [TransitionPropertyCollection.AllKeyword] : selection.ToArray();
        }

        var index = -1;
        var propertyName = CssSizingPresentation.Native(property)?.Name ?? property.Name;
        for (var i = 0; i < properties.Length; i++)
        {
            if (properties[i] == TransitionPropertyCollection.AllKeyword || Matches(properties[i], propertyName, property))
                index = i;
        }

        if (index < 0) return null;

        var duration = element is UIElement ui && ui.HasLocalValue(UIElement.TransitionDurationProperty)
            ? ui.TransitionDuration.HasTimeSpan ? ui.TransitionDuration.TimeSpan.TotalMilliseconds : 0
            : Durations[index % Durations.Length];

        return new(
            TimeSpan.FromMilliseconds(duration),
            TimeSpan.FromMilliseconds(Delays[index % Delays.Length]),
            Timings[index % Timings.Length],
            Behaviors[index % Behaviors.Length]);
    }

    private static bool Matches(string name, string propertyName, DependencyProperty property)
    {
        if (name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (propertyName.StartsWith("Css", StringComparison.Ordinal) &&
            name.Equals(propertyName.AsSpan(3), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var descriptor = CssPropertyRegistry.LookupForCompile(name, out _);
        if (descriptor is not null &&
            (descriptor.StorageProperty == property ||
             string.Equals(descriptor.StorageProperty?.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(descriptor.TransitionTargetDpName, propertyName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((name.Equals("offset", StringComparison.OrdinalIgnoreCase)) &&
            (property == CssMotionPath.DistanceProperty ||
             property == CssMotionPath.RotateProperty ||
             property == CssMotionPath.PathProperty ||
             property == CssMotionPath.AnchorProperty ||
             property == CssMotionPath.PositionProperty))
        {
            return true;
        }

        if (name.Equals("OffsetDistance", StringComparison.OrdinalIgnoreCase) && property == CssMotionPath.DistanceProperty ||
            name.Equals("OffsetRotate", StringComparison.OrdinalIgnoreCase) && property == CssMotionPath.RotateProperty ||
            name.Equals("OffsetPath", StringComparison.OrdinalIgnoreCase) && property == CssMotionPath.PathProperty ||
            name.Equals("OffsetAnchor", StringComparison.OrdinalIgnoreCase) && property == CssMotionPath.AnchorProperty ||
            name.Equals("OffsetPosition", StringComparison.OrdinalIgnoreCase) && property == CssMotionPath.PositionProperty)
        {
            return true;
        }

        var mask = name.Equals("mask", StringComparison.OrdinalIgnoreCase);
        var border = name.Equals("MaskBorder", StringComparison.OrdinalIgnoreCase) || name.Equals("mask-border", StringComparison.OrdinalIgnoreCase);
        return (mask || border) && CssPropertyMetadata.Longhands(mask ? "mask" : "mask-border")
            .Any(part => CssPropertyRegistry.LookupForCompile(part, out _)?.StorageProperty?.Name == propertyName);
    }
}

internal static class CssTransitions
{
    private static readonly ConditionalWeakTable<DependencyObject, CssContentTransitionRunner> s_contentRunners = new();

    internal static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "TransitionData",
        typeof(CssTransitionData),
        typeof(CssTransitions),
        new PropertyMetadata(null, OnTransitionDataChanged));

    internal static bool IsConfiguration(DependencyProperty property) => property == DataProperty ||
        property == UIElement.TransitionPropertyProperty || property == UIElement.TransitionDurationProperty || property == UIElement.TransitionTimingFunctionProperty;

    private static void OnTransitionDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement visual)
        {
            visual.CancelUnselectedCssTransitions(e.NewValue as CssTransitionData);
            return;
        }

        if (e.NewValue is CssTransitionData)
        {
            s_contentRunners.GetValue(d, static target => new CssContentTransitionRunner(target));
        }
        else if (s_contentRunners.TryGetValue(d, out var runner))
        {
            runner.StopAll(clearAnimatedValues: true);
        }
    }

    internal static bool HasActiveTransition(DependencyObject target, DependencyProperty property)
    {
        if (target is UIElement uiElement)
            return uiElement.HasAutomaticTransition(property);

        return s_contentRunners.TryGetValue(target, out var runner) && runner.HasActive(property);
    }

    internal static bool SampleActiveTransitions(DependencyObject target, double normalizedProgress)
    {
        if (s_contentRunners.TryGetValue(target, out var runner))
            return runner.SampleAll(normalizedProgress);

        return false;
    }

    internal static void StopActiveTransition(DependencyObject target, DependencyProperty property, bool clearAnimatedValue = true)
    {
        if (target is UIElement uiElement)
        {
            uiElement.StopAutomaticTransition(property, clearAnimatedValue);
            return;
        }

        if (s_contentRunners.TryGetValue(target, out var runner))
        {
            runner.Stop(property, clearAnimatedValue);
        }
    }

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

    internal static IAnimationTimeline? Create(
        DependencyObject element,
        DependencyProperty property,
        object? from,
        object? to,
        CssTransition transition)
    {
        // The numeric percentage track owns CSS font width. Animating its native
        // projection as well would override that track with discrete width classes.
        if (property.Name == "FontStretch")
            return null;

        IAnimationTimeline? animation = CssSizingPresentation.Animation(property, from, to, CssSizingPresentation.AllowKeywords(element));
        animation ??= CssMotionPath.CreateTransitionAnimation(property, from, to, transition);
        animation ??= CssFontSizeAdjust.Animation(property, from, to);
        if (animation is CssFontSizeAdjustAnimation sizing) sizing.Duration = new Duration(transition.Duration);
        animation ??= CssInitialLetterProperties.Animation(property, from, to);
        if (animation is CssInitialLetterAnimation initial) initial.Duration = new Duration(transition.Duration);
        if (animation is CssInitialLetterWrapAnimation initialWrap) initialWrap.Duration = new Duration(transition.Duration);
        animation ??= CssFontVariationSettings.Animation(property, from, to);
        if (animation is CssFontVariationAnimation variations) variations.Duration = new Duration(transition.Duration);
        animation ??= CssTextSpacing.Animation(property, from, to);
        if (animation is CssTextSpacingAnimation spacing)
            spacing.Duration = new Duration(transition.Duration);
        animation ??= CssMaskAnimation.Create(property, from, to);
        if (animation is CssMaskAnimation mask) mask.Duration = new Duration(transition.Duration);
        animation ??= property == CssDisplayProperties.SpecificationProperty &&
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
                element is UIElement ui && ui.HasLocalValue(UIElement.TransitionTimingFunctionProperty)
                    ? ui.TransitionTimingFunction
                    : TransitionTimingFunction.Linear);

        if (animation is null && transition.AllowDiscrete &&
            IsSupportedDiscreteProperty(property, from, to))
        {
            animation = new CssDiscreteAnimation
            {
                PropertyType = property.PropertyType,
                From = from,
                To = to,
                Duration = new Duration(transition.Duration),
            };
        }

        if (animation is Timeline timeline)
        {
            timeline.BeginTime = transition.Delay;
            timeline.FillBehavior = FillBehavior.Stop;
        }

        var hasLocalLegacyTiming = element is UIElement uiElem && uiElem.HasLocalValue(UIElement.TransitionTimingFunctionProperty);
        if (!hasLocalLegacyTiming && animation is DependencyObject target &&
            DependencyProperty.FromName(target.GetType(), "EasingFunction") is { } easing)
        {
            target.SetValue(easing, transition.Timing);
        }

        return animation;
    }

    internal static object? InitialValue(DependencyProperty property, object? from, object? to) =>
        property == CssDisplayProperties.SpecificationProperty &&
        from is CssDisplaySpecification { None: true } &&
        to is CssDisplaySpecification { None: false } ? to : from;

    private static bool IsSupportedDiscreteProperty(
        DependencyProperty property,
        object? from,
        object? to)
    {
        if (property.Name == "FontStretch")
            return false;

        if (CssSizingPresentation.IsPresentation(property) ||
            property == CssMaskProperties.ModesProperty || property == CssMaskProperties.RepeatsProperty ||
            property == CssMaskProperties.OriginsProperty || property == CssMaskProperties.ClipsProperty ||
            property == CssMaskProperties.CompositesProperty || property == CssMaskProperties.SizesProperty || property == CssMaskProperties.PositionsProperty ||
            property == CssMaskProperties.TypeProperty || property == CssInitialLetterProperties.ValueProperty ||
            property == CssInitialLetterProperties.AlignProperty || property == CssInitialLetterProperties.WrapProperty ||
            property == CssMaskBorderProperties.SourceProperty || property == CssMaskBorderProperties.ModeProperty ||
            property == CssMaskBorderProperties.SliceProperty || property == CssMaskBorderProperties.WidthProperty ||
            property == CssMaskBorderProperties.OutsetProperty || property == CssMaskBorderProperties.RepeatProperty ||
            property == CssFontLigatures.KerningProperty ||
            property == CssFontLigatures.VariantProperty ||
            property == CssFontNumeric.VariantProperty ||
            property == CssFontCaps.VariantProperty || property == CssFontPosition.VariantProperty || property == CssFontEmoji.VariantProperty || property == CssFontPosition.SynthesisProperty ||
            property == CssFontEastAsian.VariantProperty || property == CssFontAlternates.VariantProperty ||
            property == CssFontFeatureSettings.SettingsProperty || property == CssFontVariationSettings.SettingsProperty || property == CssFontOpticalSizing.AutoProperty || property == CssFontSizeAdjust.ValueProperty ||
            property == Jalium.UI.Controls.ScrollViewer.CssScrollBarWidthProperty ||
            property == Jalium.UI.Controls.ScrollViewer.CssScrollBarGutterProperty ||
            property == CssDisplayProperties.VisibilityProperty ||
            property == UIElement.VisibilityProperty ||
            property == CssPointerEventsProperties.ValueProperty ||
            property == CssUserSelectProperties.ValueProperty ||
            property == FrameworkElement.CursorProperty ||
            property == CssImageRenderingProperties.ValueProperty ||
            property == CssImageObjectProperties.FitProperty ||
            property == CssTransformReferenceBox.ValueProperty ||
            property == CssMotionPath.PathProperty ||
            property == CssMotionPath.RotateProperty ||
            property == CssMotionPath.AnchorProperty ||
            property == CssMotionPath.PositionProperty ||
            property == CssViewTransitions.NameProperty ||
            property == CssViewTransitions.ClassProperty ||
            property == Jalium.UI.Controls.ScrollViewer.CssScrollBarColorsProperty &&
                (from is null || to is null))
        {
            return true;
        }

        return property.PropertyType.IsEnum ||
               property.PropertyType == typeof(bool) ||
               property.PropertyType == typeof(FontWeight) ||
               property.PropertyType == typeof(FontStyle) ||
               property.PropertyType == typeof(TextAlignment) ||
               property.PropertyType == typeof(TextWrapping) ||
               property.PropertyType == typeof(TextTrimming) ||
               property.PropertyType == typeof(TextDecorationCollection);
    }

    /// <summary>
    /// Automatic CSS transition runner for non-<see cref="UIElement"/> dependency objects
    /// such as <see cref="FrameworkContentElement"/> (<c>Run</c>, <c>Span</c>, <c>Paragraph</c>,
    /// <c>Section</c>, <c>TableCell</c>).
    /// </summary>
    private sealed class CssContentTransitionRunner : IFrameAnimatable
    {
        private readonly DependencyObject _target;
        private readonly AnimationTickSubscription _subscription;
        private readonly Dictionary<DependencyProperty, ActiveEntry> _active = [];
        private readonly HashSet<DependencyProperty> _suppressedProperties = [];
        private bool _suppressHook;

        public CssContentTransitionRunner(DependencyObject target)
        {
            _target = target;
            _subscription = new AnimationTickSubscription(this, weak: true);
            _target.PropertyChangedInternal += OnTargetPropertyChanged;
        }

        public bool HasActive(DependencyProperty dp) => _active.ContainsKey(dp);

        public void Stop(DependencyProperty dp, bool clearAnimatedValue)
        {
            if (!_active.Remove(dp))
                return;

            if (clearAnimatedValue)
            {
                _suppressedProperties.Add(dp);
                try
                {
                    _target.ClearCssAnimatedValue(dp);
                }
                finally
                {
                    _suppressedProperties.Remove(dp);
                }
            }

            if (_active.Count == 0)
                AnimationManager.Unregister(_subscription);
            NotifyConsumers();
        }

        public void StopAll(bool clearAnimatedValues)
        {
            if (_active.Count == 0)
                return;

            var keys = _active.Keys.ToArray();
            _active.Clear();
            AnimationManager.Unregister(_subscription);

            if (clearAnimatedValues)
            {
                _suppressHook = true;
                try
                {
                    foreach (var dp in keys)
                        _target.ClearCssAnimatedValue(dp);
                }
                finally
                {
                    _suppressHook = false;
                }
            }

            NotifyConsumers();
        }

        public bool SampleAll(double normalizedProgress)
        {
            if (_active.Count == 0)
                return false;

            var progress = Math.Clamp(normalizedProgress, 0.0, 1.0);
            _suppressHook = true;
            try
            {
                foreach (var (dp, entry) in _active.ToArray())
                {
                    entry.Clock.SetProgressForSampling(progress);
                    var val = entry.Timeline.GetCurrentValue(entry.FromValue!, entry.ToValue!, entry.Clock);
                    entry.CurrentValue = val;
                    _target.SetCssAnimatedValue(dp, val);
                }
            }
            finally
            {
                _suppressHook = false;
            }

            NotifyConsumers();
            return true;
        }

        public bool OnAnimationFrame(long frameTimestamp)
        {
            if (_active.Count == 0)
                return false;

            var keepRunning = false;
            _suppressHook = true;
            try
            {
                foreach (var (dp, entry) in _active.ToArray())
                {
                    var elapsedMs = Math.Max(0.0, (frameTimestamp - entry.StartTimestamp) * 1000.0 / Stopwatch.Frequency);
                    var delayMs = entry.Transition.Delay.TotalMilliseconds;
                    var durationMs = entry.Transition.Duration.TotalMilliseconds;

                    if (elapsedMs < delayMs)
                    {
                        _target.SetCssAnimatedValue(dp, entry.InitialValue);
                        keepRunning = true;
                        continue;
                    }

                    var activeMs = elapsedMs - delayMs;
                    if (durationMs <= 0 || activeMs >= durationMs)
                    {
                        _active.Remove(dp);
                        _target.ClearCssAnimatedValue(dp);
                        continue;
                    }

                    var linearProgress = Math.Clamp(activeMs / durationMs, 0.0, 1.0);
                    entry.Clock.SetProgressForSampling(linearProgress);
                    var currentVal = entry.Timeline.GetCurrentValue(entry.FromValue!, entry.ToValue!, entry.Clock);
                    entry.CurrentValue = currentVal;
                    _target.SetCssAnimatedValue(dp, currentVal);
                    keepRunning = true;
                }
            }
            finally
            {
                _suppressHook = false;
            }

            NotifyConsumers();
            if (!keepRunning)
                AnimationManager.Unregister(_subscription);
            return keepRunning;
        }

        private void OnTargetPropertyChanged(DependencyProperty dp, object? oldValue, object? newValue)
        {
            if (_suppressHook || _suppressedProperties.Contains(dp) || IsConfiguration(dp) || dp == CssAnimations.DataProperty)
                return;

            var node = CssNode.Existing(_target);
            if (node?.CssRuntimeState?.HasAppliedStyle != true)
                return;

            if (_target.GetValue(DataProperty) is not CssTransitionData data)
                return;

            var transition = data.Find(_target, dp);
            if (transition is null || (transition.Duration <= TimeSpan.Zero && transition.Delay <= TimeSpan.Zero))
            {
                if (_active.ContainsKey(dp))
                    Stop(dp, clearAnimatedValue: true);
                return;
            }

            var fromValue = _active.TryGetValue(dp, out var existing) ? existing.CurrentValue : oldValue;
            var toValue = newValue;
            if (Equals(fromValue, toValue))
                return;

            if (Create(_target, dp, fromValue, toValue, transition) is not AnimationTimeline timeline)
                return;

            var clock = timeline.CreateClock();
            var initial = InitialValue(dp, fromValue, toValue);
            var now = AnimationManager.CurrentFrameTimestampOrNow;

            _active[dp] = new ActiveEntry(dp, timeline, clock, fromValue, toValue, initial, transition, now);

            _suppressedProperties.Add(dp);
            try
            {
                _target.SetCssAnimatedValue(dp, initial);
            }
            finally
            {
                _suppressedProperties.Remove(dp);
            }

            AnimationManager.Register(_subscription);
            NotifyConsumers();
        }

        private void NotifyConsumers()
        {
            if (_target is TextElement textElement)
            {
                textElement.NotifyTextContentChanged();
            }

            CssDocumentConsumers.Invalidate(CssNode.Get(_target));
        }

        private sealed class ActiveEntry(
            DependencyProperty property,
            AnimationTimeline timeline,
            AnimationClock clock,
            object? fromValue,
            object? toValue,
            object? initialValue,
            CssTransition transition,
            long startTimestamp)
        {
            public DependencyProperty Property { get; } = property;
            public AnimationTimeline Timeline { get; } = timeline;
            public AnimationClock Clock { get; } = clock;
            public object? FromValue { get; } = fromValue;
            public object? ToValue { get; } = toValue;
            public object? InitialValue { get; } = initialValue;
            public object? CurrentValue { get; set; } = initialValue;
            public CssTransitionTransitionWrapper TransitionWrapper => new(transition);
            public CssTransition Transition { get; } = transition;
            public long StartTimestamp { get; } = startTimestamp;
        }

        private readonly record struct CssTransitionTransitionWrapper(CssTransition Inner);
    }
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
