using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

/// <summary>One keyframe offset after combining selector lists and repeated blocks.</summary>
internal sealed record CssResolvedKeyframe(double Offset, CssDeclaration[] Declarations);

/// <summary>Compiled property values at an offset; timing declarations apply to the following segment.</summary>
internal sealed record CssCompiledKeyframe(double Offset, CssCompiledDeclaration[] Declarations,
    CssDeclaration[] TimingDeclarations);

/// <summary>One frame's values after resolving them against an element's computed style.</summary>
internal sealed record CssEvaluatedKeyframe(double Offset,
    IReadOnlyDictionary<DependencyProperty, object?> Values, CssLayoutState? LayoutState,
    CssTimingFunction? TimingFunction);

/// <summary>A computed value for one property; timing controls the segment after this offset.</summary>
internal sealed record CssPropertyKeyframe(double Offset, object? Value, CssTimingFunction? TimingFunction);

/// <summary>Document-level keyframe lookup, computed frame values and property tracks.</summary>
internal static class CssKeyframes
{
    internal static CssKeyframesRule? Find(CssNode element, string name)
    {
        var root = CssMatcher.Root(element);
        CssKeyframesRule? winner = null;
        void Add(IEnumerable<CssStyleSheet> sheets)
        {
            foreach (var sheet in sheets)
                foreach (var rule in sheet.Keyframes)
                    if (string.Equals(rule.Name, name, StringComparison.Ordinal) &&
                        (rule.Condition is null || rule.Condition.Evaluate(root)))
                        winner = rule;
        }

        // Media preferences and pointing capabilities can change without a cascade-version
        // or viewport change. Resolve conditions against current document state each time.
        if (CssEngine.ApplicationStyleSheetsProvider?.Invoke() is { } application)
            Add(application);
        var pending = new Stack<CssNode>();
        var seen = new HashSet<CssNode>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (current.CssRuntimeState?.ScopedStyleSheets is { } sheets) Add(sheets);
            foreach (var child in current.EnumerateChildren().Reverse()) pending.Push(child);
        }
        return winner;
    }

    internal static CssResolvedKeyframe[] ResolveFrames(CssKeyframesRule rule)
    {
        var offsets = new SortedDictionary<double, List<CssDeclaration>>();
        foreach (var block in rule.Blocks)
        {
            foreach (var offset in block.Offsets)
            {
                if (!offsets.TryGetValue(offset, out var declarations))
                    offsets[offset] = declarations = [];
                declarations.AddRange(block.Declarations);
            }
        }
        return offsets.Select(pair => new CssResolvedKeyframe(pair.Key, pair.Value.ToArray())).ToArray();
    }

    internal static CssCompiledKeyframe[] CompileFrames(CssKeyframesRule rule)
    {
        var context = new CssCompileContext
        {
            BaseUri = rule.BaseUri,
            ResourceResolver = rule.ResourceResolver,
            Namespaces = rule.Namespaces,
        };
        return ResolveFrames(rule).Select(frame =>
        {
            var properties = new List<CssDeclaration>();
            var timings = new List<CssDeclaration>();
            foreach (var declaration in frame.Declarations)
            {
                if (declaration.PropertyName == "animation-timing-function") timings.Add(declaration);
                else properties.Add(declaration);
            }
            return new CssCompiledKeyframe(frame.Offset,
                CssEngine.CompileDeclarations(properties, context), timings.ToArray());
        }).ToArray();
    }

    internal static CssEvaluatedKeyframe[] EvaluateFrames(CssNode element, CssKeyframesRule rule)
    {
        // Lengths and custom properties come from the element's current computed style.
        // A scratch state keeps observers and contextual values created while evaluating
        // a frame from replacing the normal cascade's state.
        var frames = CompileFrames(rule);
        var lengths = CssEngine.BuildLengthContext(element);
        var state = CssEngine.EnsureState(element);
        var evaluated = new CssEvaluatedKeyframe[frames.Length];
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            var scratch = new CssElementState
            {
                CustomProperties = state.CustomProperties,
                RegisteredProperties = state.RegisteredProperties,
                RegisteredValues = state.RegisteredValues,
                ObservesOwnSize = state.ObservesOwnSize,
            };
            element.CssRuntimeState = scratch;
            try
            {
                var slots = new CssSlotAccumulator
                {
                    LogicalRightToLeft = element.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft,
                };
                var frameLengths = lengths;
                // As in a normal declaration block, the frame's own font-size and
                // color establish the basis for its other computed values.
                var fontSizeProperty = CssDependencyPropertyLookup.Find(element.GetType(), "FontSize");
                if (fontSizeProperty is null || !element.HasLocalOrAnimatedValue(fontSizeProperty))
                {
                    foreach (var declaration in frame.Declarations)
                    {
                        if (declaration.Name != "font-size") continue;
                        var probe = new CssEngine.CssSetterCollector();
                        var probeContext = new CssApplyContext(element, frameLengths, slots);
                        if (TryApplyKeyframeValue(declaration.Value, in probeContext, probe))
                            foreach (var entry in probe.Values.Values)
                                if (entry.Value is double size && size > 0)
                                    frameLengths = frameLengths.WithElementFontSize(size);
                        break;
                    }
                }
                var foregroundProperty = CssDependencyPropertyLookup.Find(element.GetType(), "Foreground");
                if (foregroundProperty is null || !element.HasLocalOrAnimatedValue(foregroundProperty))
                {
                    foreach (var declaration in frame.Declarations)
                    {
                        if (declaration.Name != "color") continue;
                        var probe = new CssEngine.CssSetterCollector();
                        var probeContext = new CssApplyContext(element, frameLengths, slots);
                        if (TryApplyKeyframeValue(declaration.Value, in probeContext, probe))
                            foreach (var entry in probe.Values)
                                if (entry.Key.Name == "Foreground" && entry.Value.Value is Jalium.UI.Media.Brush brush)
                                    slots.ForegroundBrush = brush;
                        break;
                    }
                }
                var context = new CssApplyContext(element, frameLengths, slots);
                var sink = new CssEngine.CssSetterCollector();
                foreach (var declaration in frame.Declarations)
                {
                    // Unregistered custom properties supply var() tokens through the
                    // element's computed style; they do not produce a DP animation value.
                    if (declaration.Name.StartsWith("--", StringComparison.Ordinal)) continue;
                    if (declaration.Name is "transform" or "transform-origin") continue;
                    TryApplyKeyframeValue(declaration.Value, in context, sink);
                }
                slots.Flush(in context, sink);
                slots.TransformReferenceBox = CssTransformReferenceBox.Resolve(element, sink, useCurrentStyle: true);
                foreach (var declaration in frame.Declarations)
                    if (declaration.Name is "transform" or "transform-origin")
                        TryApplyKeyframeValue(declaration.Value, in context, sink);
                var values = sink.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Value);
                evaluated[index] = new CssEvaluatedKeyframe(frame.Offset, values, sink.LayoutState,
                    ResolveTimingFunction(element, frame.TimingDeclarations));
            }
            finally
            {
                if (scratch.InheritedColorObservers is { } colorObservers)
                    foreach (var observer in colorObservers.Values) observer.Dispose();
                if (scratch.InheritedBoxObservers is { } boxObservers)
                    foreach (var observer in boxObservers.Values) observer.Dispose();
                if (scratch.ColorBrushObservers is { } brushObservers)
                    foreach (var observer in brushObservers) observer.Dispose();
                state.ObservesOwnSize |= scratch.ObservesOwnSize;
                element.CssRuntimeState = state;
            }
        }
        return evaluated;
    }

    internal static IReadOnlyDictionary<DependencyProperty, CssPropertyKeyframe[]> BuildPropertyTracks(
        CssNode element, CssKeyframesRule rule)
    {
        var byProperty = new Dictionary<DependencyProperty, List<CssPropertyKeyframe>>();
        foreach (var frame in EvaluateFrames(element, rule))
            foreach (var (property, value) in frame.Values)
            {
                if (!byProperty.TryGetValue(property, out var track))
                    byProperty[property] = track = [];
                track.Add(new CssPropertyKeyframe(frame.Offset, value, frame.TimingFunction));
            }

        var tracks = new Dictionary<DependencyProperty, CssPropertyKeyframe[]>(byProperty.Count);
        foreach (var (property, track) in byProperty)
        {
            var underlying = element.Target.GetEffectiveBaseValue(property);
            if (track[0].Offset > 0)
                track.Insert(0, new CssPropertyKeyframe(0, underlying, null));
            if (track[^1].Offset < 1)
                track.Add(new CssPropertyKeyframe(1, underlying, null));
            tracks[property] = track.ToArray();
        }
        return tracks;
    }

    internal static IReadOnlyDictionary<DependencyProperty, CssPropertySampler> BuildSamplers(
        CssNode element, CssKeyframesRule rule)
        => BuildPropertyTracks(element, rule).ToDictionary(pair => pair.Key,
            pair => new CssPropertySampler(pair.Key, pair.Value));

    private static bool TryApplyKeyframeValue(CssCompiledValue value,
        in CssApplyContext context, ICssSetterSink sink) => value switch
    {
        CssPendingSubstitution pending => pending.TryApplyKeyframe(in context, sink),
        CssNumericDeclarationValue numeric => numeric.TryApplyKeyframe(in context, sink),
        _ => value.TryApply(in context, sink),
    };

    private static CssTimingFunction? ResolveTimingFunction(CssNode element, CssDeclaration[] declarations)
    {
        CssTimingFunction? winner = null;
        foreach (var declaration in declarations)
        {
            var raw = declaration.RawValue;
            if (CssCustomProperties.ContainsSubstitution(raw))
            {
                if (!CssCustomProperties.TrySubstitute(raw, element, out raw, declaration.PropertyName)) continue;
            }
            var reader = new CssTokenReader(raw);
            if (CssTimingFunction.TryParse(ref reader, out var timing) && reader.AtEnd)
                winner = timing;
        }
        return winner;
    }
}

/// <summary>Samples one property's keyframe intervals with the native value interpolators.</summary>
internal sealed class CssPropertySampler
{
    private readonly CssPropertyKeyframe[] _frames;
    private readonly (AnimationTimeline Timeline, AnimationClock Clock)[] _segments;

    internal CssPropertySampler(DependencyProperty property, CssPropertyKeyframe[] frames)
    {
        if (frames.Length < 2 || frames[0].Offset != 0 || frames[^1].Offset != 1)
            throw new ArgumentException("A property track must span 0% through 100%.", nameof(frames));
        for (var index = 1; index < frames.Length; index++)
            if (frames[index].Offset <= frames[index - 1].Offset)
                throw new ArgumentException("Property keyframe offsets must increase.", nameof(frames));
        _frames = frames;
        _segments = new (AnimationTimeline, AnimationClock)[frames.Length - 1];
        for (var index = 0; index < _segments.Length; index++)
        {
            var from = frames[index].Value;
            var to = frames[index + 1].Value;
            var timeline = property == CssDisplayProperties.SpecificationProperty &&
                from is CssDisplaySpecification before && to is CssDisplaySpecification after
                ? new CssDisplayAnimation { From = before, To = after }
                : property == UIElement.RenderTransformProperty &&
                    (from is null or Jalium.UI.Media.Transform) &&
                    (to is null or Jalium.UI.Media.Transform)
                ? new CssTransformAnimation
                {
                    From = from as Jalium.UI.Media.Transform,
                    To = to as Jalium.UI.Media.Transform,
                }
                : AnimationFactory.CreateAnimation(property.PropertyType, from, to,
                    TimeSpan.FromSeconds(1)) as AnimationTimeline ?? new CssDiscreteAnimation
                    {
                        PropertyType = property.PropertyType, From = from, To = to,
                    };
            _segments[index] = (timeline, timeline.CreateClock());
        }
    }

    internal object? Sample(double progress, CssTimingFunction defaultTiming)
    {
        if (!double.IsFinite(progress))
            throw new ArgumentOutOfRangeException(nameof(progress));
        if (progress >= 1) return _frames[^1].Value;
        var position = Math.Max(progress, 0);
        for (var index = 0; index < _segments.Length; index++)
        {
            var from = _frames[index];
            var to = _frames[index + 1];
            if (position >= to.Offset) continue;
            var fraction = (position - from.Offset) / (to.Offset - from.Offset);
            var eased = (from.TimingFunction ?? defaultTiming).Ease(fraction);
            var (timeline, clock) = _segments[index];
            clock.SetProgressForSampling(eased);
            return timeline.GetCurrentValue(from.Value!, to.Value!, clock);
        }
        return _frames[^1].Value;
    }

    internal object? Sample(in CssAnimationSample timing, CssTimingFunction defaultTiming)
    {
        if (!timing.Applies) return null;
        // CSS timing functions do not run during the delay, including step-start.
        if (timing.DuringDelay)
            return timing.Progress == 1 ? _frames[^1].Value : _frames[0].Value;
        return Sample(timing.Progress, defaultTiming);
    }
}
