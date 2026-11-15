using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Composition mode for CSS keyframe animations (<c>animation-composition</c>).
/// </summary>
public enum CssAnimationComposition : byte
{
    Replace,
    Add,
    Accumulate,
}

internal readonly record struct CssAnimationName(string Value, bool IsNoneKeyword = false);

internal sealed record CssAnimationDefinition(
    CssAnimationName Name,
    double Duration,
    double Delay,
    CssTimingFunction Timing,
    double Iterations,
    CssAnimationDirection Direction,
    CssAnimationFillMode Fill,
    bool Paused,
    CssAnimationComposition Composition = CssAnimationComposition.Replace)
{
    internal CssAnimationTiming Clock => new(Duration, Delay, Iterations, Direction, Fill);
}

internal sealed record CssAnimationData(CssAnimationDefinition[] Definitions)
{
    public bool Equals(CssAnimationData? other) => other is not null &&
        Definitions.SequenceEqual(other.Definitions);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var definition in Definitions) hash.Add(definition);
        return hash.ToHashCode();
    }
}

internal sealed class CssAnimationParts
{
    private object[]? _names, _durations, _delays, _timings, _counts, _directions, _fills, _plays, _compositions;
    internal bool FromState;

    internal void Set(string property, object[] values)
    {
        switch (property)
        {
            case "animation-name": _names = values; break;
            case "animation-duration": _durations = values; break;
            case "animation-delay": _delays = values; break;
            case "animation-timing-function": _timings = values; break;
            case "animation-iteration-count": _counts = values; break;
            case "animation-direction": _directions = values; break;
            case "animation-fill-mode": _fills = values; break;
            case "animation-play-state": _plays = values; break;
            case "animation-composition": _compositions = values; break;
        }
    }

    internal void Flush(ICssSetterSink sink)
    {
        var names = _names ?? [new CssAnimationName("none", true)];
        var definitions = new CssAnimationDefinition[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            definitions[index] = new CssAnimationDefinition(
                (CssAnimationName)names[index],
                At(_durations, index, 0.0),
                At(_delays, index, 0.0),
                At(_timings, index, CssTimingFunction.EaseDefault),
                At(_counts, index, 1.0),
                At(_directions, index, CssAnimationDirection.Normal),
                At(_fills, index, CssAnimationFillMode.None),
                At(_plays, index, false),
                At(_compositions, index, CssAnimationComposition.Replace));
        }

        sink.CurrentValueIsState = FromState;
        if (_compositions is { Length: > 0 })
        {
            var typedCompositions = new CssAnimationComposition[_compositions.Length];
            for (var i = 0; i < _compositions.Length; i++)
                typedCompositions[i] = (CssAnimationComposition)_compositions[i];
            sink.Set(CssAnimations.CompositionsProperty, typedCompositions);
        }

        sink.Set(CssAnimations.DataProperty, new CssAnimationData(definitions));
    }

    private static T At<T>(object[]? values, int index, T fallback)
        => values is { Length: > 0 } ? (T)values[index % values.Length] : fallback;
}

internal static class CssAnimations
{
    private static bool s_compositionRegistered;

    internal static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "AnimationData", typeof(CssAnimationData), typeof(CssAnimations), new PropertyMetadata(null));

    internal static readonly DependencyProperty CompositionsProperty = DependencyProperty.RegisterAttached(
        "Compositions",
        typeof(CssAnimationComposition[]),
        typeof(CssAnimations),
        new PropertyMetadata(null, OnCompositionsChanged));

    static CssAnimations()
    {
        CssPropertyRegistry.RegisterExtension(RegisterComposition);
    }

    internal static void RegisterComposition()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "animation-composition",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = CompositionsProperty,
            Parse = ParseAnimationComposition,
        });
        s_compositionRegistered = true;
    }

    private static CssCompiledValue? ParseAnimationComposition(ref CssTokenReader reader, CssCompileContext context)
    {
        var list = new List<object>();
        while (true)
        {
            if (!reader.TryReadIdent(out var ident) || !TryParseCompositionKeyword(ident, out var comp))
                return null;
            list.Add(comp);
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return null;
        }

        return new CssAnimationCompositionValue(list.ToArray());
    }

    internal static bool TryParseCompositionKeyword(ReadOnlySpan<char> ident, out CssAnimationComposition composition)
    {
        if (ident.Equals("replace", StringComparison.OrdinalIgnoreCase))
        {
            composition = CssAnimationComposition.Replace;
            return true;
        }
        if (ident.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            composition = CssAnimationComposition.Add;
            return true;
        }
        if (ident.Equals("accumulate", StringComparison.OrdinalIgnoreCase))
        {
            composition = CssAnimationComposition.Accumulate;
            return true;
        }
        composition = CssAnimationComposition.Replace;
        return false;
    }

    private sealed class CssAnimationCompositionValue(object[] values) : CssCompiledValue
    {
        private readonly CssAnimationComposition[] _typed = values.Select(static v => (CssAnimationComposition)v).ToArray();

        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            context.Slots.Animations ??= new CssAnimationParts();
            context.Slots.Animations.FromState |= sink.CurrentValueIsState;
            context.Slots.Animations.Set("animation-composition", values);
            sink.Set(CompositionsProperty, _typed);
            return true;
        }
    }

    private static void OnCompositionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (CssNode.Existing(d)?.CssRuntimeState?.AnimationRunner is { } runner &&
            d.GetValue(DataProperty) is CssAnimationData data)
        {
            runner.Update(data);
        }
    }

    internal static object[]? InheritedPart(CssNode parent, string property)
    {
        if (parent.GetValue(DataProperty) is not CssAnimationData data) return null;
        return property switch
        {
            "animation-name" => data.Definitions.Select(static item => (object)item.Name).ToArray(),
            "animation-duration" => data.Definitions.Select(static item => (object)item.Duration).ToArray(),
            "animation-delay" => data.Definitions.Select(static item => (object)item.Delay).ToArray(),
            "animation-timing-function" => data.Definitions.Select(static item => (object)item.Timing).ToArray(),
            "animation-iteration-count" => data.Definitions.Select(static item => (object)item.Iterations).ToArray(),
            "animation-direction" => data.Definitions.Select(static item => (object)item.Direction).ToArray(),
            "animation-fill-mode" => data.Definitions.Select(static item => (object)item.Fill).ToArray(),
            "animation-play-state" => data.Definitions.Select(static item => (object)item.Paused).ToArray(),
            "animation-composition" => data.Definitions.Select(static item => (object)item.Composition).ToArray(),
            _ => null,
        };
    }

    internal static void Update(CssNode element, CssAnimationData? data)
    {
        var state = element.CssRuntimeState;
        if (state is null) return;
        if (data is null || data.Definitions.All(static definition => definition.Name.IsNoneKeyword))
        {
            state.AnimationRunner?.Stop();
            state.AnimationRunner = null;
            return;
        }
        (state.AnimationRunner ??= new CssAnimationRunner(element)).Update(data);
    }

    internal static void StopForRecycle(UIElement element)
    {
        if (element is not FrameworkElement { CssRuntimeState: { } state }) return;
        state.AnimationRunner?.Stop();
        state.AnimationRunner = null;
    }

    /// <summary>
    /// Combines an animated value with an underlying value according to <paramref name="composition"/>.
    /// </summary>
    public static object? ComposeValue(
        DependencyProperty property,
        object? underlyingValue,
        object? animatedValue,
        CssAnimationComposition composition,
        int completedIterations = 0,
        object? iterationEndValue = null)
    {
        if (composition == CssAnimationComposition.Replace || underlyingValue is null || animatedValue is null)
            return animatedValue;

        var iterScale = composition == CssAnimationComposition.Accumulate && completedIterations > 0
            ? completedIterations
            : 0;

        if (underlyingValue is double uDouble && animatedValue is double vDouble)
        {
            var iterAdd = iterScale > 0 && iterationEndValue is double endDouble ? iterScale * endDouble : 0.0;
            return uDouble + vDouble + iterAdd;
        }

        if (underlyingValue is float uFloat && animatedValue is float vFloat)
        {
            var iterAdd = iterScale > 0 && iterationEndValue is float endFloat ? iterScale * endFloat : 0f;
            return uFloat + vFloat + iterAdd;
        }

        if (underlyingValue is int uInt && animatedValue is int vInt)
        {
            var iterAdd = iterScale > 0 && iterationEndValue is int endInt ? iterScale * endInt : 0;
            return uInt + vInt + iterAdd;
        }

        if (underlyingValue is CssLength uLen && animatedValue is CssLength vLen)
        {
            var iterAdd = iterScale > 0 && iterationEndValue is CssLength endLen ? iterScale * endLen.Value : 0.0;
            if (uLen.Unit == vLen.Unit)
                return new CssLength(uLen.Value + vLen.Value + iterAdd, uLen.Unit);
            var ctx = new CssLengthContext(16, 16, 16, 100, 100);
            var px = CssMotionPath.ResolveLengthOrPercent(uLen, in ctx, 100) +
                     CssMotionPath.ResolveLengthOrPercent(vLen, in ctx, 100);
            if (iterScale > 0 && iterationEndValue is CssLength endLenPx)
                px += iterScale * CssMotionPath.ResolveLengthOrPercent(endLenPx, in ctx, 100);
            return new CssLength(px, CssUnit.Px);
        }

        if (underlyingValue is Thickness uThick && animatedValue is Thickness vThick)
        {
            var endT = iterScale > 0 && iterationEndValue is Thickness et ? et : default;
            return new Thickness(
                uThick.Left + vThick.Left + iterScale * endT.Left,
                uThick.Top + vThick.Top + iterScale * endT.Top,
                uThick.Right + vThick.Right + iterScale * endT.Right,
                uThick.Bottom + vThick.Bottom + iterScale * endT.Bottom);
        }

        if (underlyingValue is Point uPt && animatedValue is Point vPt)
        {
            var endP = iterScale > 0 && iterationEndValue is Point ep ? ep : default;
            return new Point(
                uPt.X + vPt.X + iterScale * endP.X,
                uPt.Y + vPt.Y + iterScale * endP.Y);
        }

        if (underlyingValue is SolidColorBrush uBrush && animatedValue is SolidColorBrush vBrush)
        {
            var c1 = uBrush.Color;
            var c2 = vBrush.Color;
            var composed = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp(c1.A + c2.A, 0, 255),
                (byte)Math.Clamp(c1.R + c2.R, 0, 255),
                (byte)Math.Clamp(c1.G + c2.G, 0, 255),
                (byte)Math.Clamp(c1.B + c2.B, 0, 255)));
            if (composed.CanFreeze) composed.Freeze();
            return composed;
        }

        if (underlyingValue is Transform uTransform && animatedValue is Transform vTransform)
        {
            return ComposeTransform(uTransform, vTransform, composition, iterScale, iterationEndValue as Transform);
        }

        return animatedValue;
    }

    private static Transform ComposeTransform(
        Transform underlying,
        Transform animated,
        CssAnimationComposition composition,
        int iterScale,
        Transform? iterationEnd)
    {
        var mu = underlying.Value;
        var mv = animated.Value;
        var mEnd = iterationEnd?.Value ?? Matrix.Identity;

        if (composition == CssAnimationComposition.Add)
        {
            // "add" post-multiplies the animated transform after the underlying transform.
            var combined = Matrix.Multiply(mu, mv);
            var mt = new MatrixTransform(combined);
            if (mt.CanFreeze) mt.Freeze();
            return mt;
        }

        // "accumulate" accumulates translation offsets and scale deltas from identity (1.0).
        var isPureScaleAndTranslate =
            Math.Abs(mu.M12) < 1e-6 && Math.Abs(mu.M21) < 1e-6 &&
            Math.Abs(mv.M12) < 1e-6 && Math.Abs(mv.M21) < 1e-6;

        Matrix accumulated;
        if (isPureScaleAndTranslate)
        {
            var sx = mu.M11 + (mv.M11 - 1.0) + iterScale * (mEnd.M11 - 1.0);
            var sy = mu.M22 + (mv.M22 - 1.0) + iterScale * (mEnd.M22 - 1.0);
            var ox = mu.OffsetX + mv.OffsetX + iterScale * mEnd.OffsetX;
            var oy = mu.OffsetY + mv.OffsetY + iterScale * mEnd.OffsetY;
            accumulated = new Matrix(sx, 0.0, 0.0, sy, ox, oy);
        }
        else
        {
            accumulated = Matrix.Multiply(mu, mv);
            if (iterScale > 0)
            {
                accumulated = new Matrix(
                    accumulated.M11,
                    accumulated.M12,
                    accumulated.M21,
                    accumulated.M22,
                    accumulated.OffsetX + iterScale * mEnd.OffsetX,
                    accumulated.OffsetY + iterScale * mEnd.OffsetY);
            }
        }

        var result = new MatrixTransform(accumulated);
        if (result.CanFreeze) result.Freeze();
        return result;
    }
}

/// <summary>Drives CSS keyframes independently of property-scoped native animation clocks.</summary>
internal sealed class CssAnimationRunner : IFrameAnimatable
{
    private readonly CssNode _element;
    private readonly AnimationTickSubscription _subscription;
    private CssAnimationEntry[] _entries = [];
    private CssAnimationDefinition[] _specifications = [];
    private CssKeyframesRule?[] _rules = [];
    private HashSet<DependencyProperty> _owned = [];
    private int _generation;

    internal CssAnimationRunner(CssNode element)
    {
        _element = element;
        _subscription = new AnimationTickSubscription(this, weak: true);
    }

    internal long StartTimestamp => _entries.Length == 0 ? 0 : _entries[0].StartTimestamp;

    internal void Update(CssAnimationData data)
    {
        _generation++;
        var now = AnimationManager.CurrentFrameTimestampOrNow;
        var rules = data.Definitions.Select(definition => definition.Name.IsNoneKeyword
            ? null : CssKeyframes.Find(_element, definition.Name.Value)).ToArray();
        if (_specifications.Length == data.Definitions.Length &&
            _specifications.Select((definition, index) => definition with { Paused = false } ==
                data.Definitions[index] with { Paused = false } && ReferenceEquals(_rules[index], rules[index]))
                .All(static same => same))
        {
            foreach (var entry in _entries)
            {
                entry.SetPaused(data.Definitions[entry.Index].Paused, now);
                // Underlying values, var() tokens and relative transform lengths can
                // change without changing the animation declaration or keyframes rule.
                // Recompute tracks on style evaluation while retaining the start time.
                entry.Tracks = CssKeyframes.BuildPropertyTracks(_element, entry.Rule);
                entry.Samplers = CssKeyframes.BuildSamplers(_element, entry.Rule);
            }
        }
        else
        {
            var entries = new List<CssAnimationEntry>();
            for (var index = 0; index < data.Definitions.Length; index++)
            {
                if (rules[index] is not { } rule) continue;
                var tracks = CssKeyframes.BuildPropertyTracks(_element, rule);
                var samplers = CssKeyframes.BuildSamplers(_element, rule);
                if (samplers.Count > 0)
                    entries.Add(new CssAnimationEntry(index, data.Definitions[index], rule, tracks, samplers, now));
            }
            _entries = entries.ToArray();
        }
        _specifications = data.Definitions;
        _rules = rules;
        if (CssScrollAndViewTimelines.GetAnimationTimelines(_element.Target) is { Length: > 0 })
        {
            CssScrollAndViewTimelines.TrackTarget(_element.Target);
        }
        Render(now);
    }

    internal void RefreshTimelineProgress()
    {
        Render(AnimationManager.CurrentFrameTimestampOrNow);
    }

    internal void Stop()
    {
        _generation++;
        AnimationManager.Unregister(_subscription);
        _entries = [];
        _specifications = [];
        _rules = [];
        var owned = _owned.ToArray();
        _owned.Clear();
        foreach (var property in owned) _element.Target.ClearCssAnimatedValue(property);
        NotifyTargetInvalidated();
    }

    bool IFrameAnimatable.OnAnimationFrame(long frameTimestamp) => Render(frameTimestamp);

    internal bool Render(long frameTimestamp)
    {
        var generation = _generation;
        var values = new Dictionary<DependencyProperty, object?>();
        var running = false;
        var explicitCompositions = _element.Target.GetValue(CssAnimations.CompositionsProperty) as CssAnimationComposition[];

        // Evaluate animations in stack order so replace/add/accumulate compose properly.
        // Note: if all animations use Replace, the first animation in _entries has priority
        // per existing Jalium.UI semantics, whereas when add/accumulate is active across
        // animations or on top of the underlying value, they compose onto the base value.
        var hasAdditiveEntry = false;
        for (var i = 0; i < _entries.Length; i++)
        {
            var comp = ResolveEntryComposition(_entries[i], explicitCompositions);
            if (comp != CssAnimationComposition.Replace)
            {
                hasAdditiveEntry = true;
                break;
            }
        }

        if (!hasAdditiveEntry)
        {
            // Preserve exact original priority order when all animations use Replace.
            for (var index = _entries.Length - 1; index >= 0; index--)
            {
                var entry = _entries[index];
                var sample = ResolveEntrySample(entry, frameTimestamp, out var isTimelineDriven, out _);
                if (!sample.Finished && !entry.IsPaused && !isTimelineDriven && entry.Samplers.Count > 0)
                    running = true;
                if (!sample.Applies) continue;
                foreach (var (property, sampler) in entry.Samplers)
                {
                    values[property] = SampleEntryProperty(entry, property, sampler, in sample);
                }
            }
        }
        else
        {
            for (var index = 0; index < _entries.Length; index++)
            {
                var entry = _entries[index];
                var composition = ResolveEntryComposition(entry, explicitCompositions);
                var sample = ResolveEntrySample(entry, frameTimestamp, out var isTimelineDriven, out var completedIterations);
                if (!sample.Finished && !entry.IsPaused && !isTimelineDriven && entry.Samplers.Count > 0)
                    running = true;
                if (!sample.Applies) continue;

                foreach (var (property, sampler) in entry.Samplers)
                {
                    var sampled = SampleEntryProperty(entry, property, sampler, in sample);
                    if (composition == CssAnimationComposition.Replace)
                    {
                        values[property] = sampled;
                    }
                    else
                    {
                        var baseVal = values.TryGetValue(property, out var existing)
                            ? existing
                            : GetUnderlyingValue(property);
                        object? endVal = null;
                        if (entry.Tracks.TryGetValue(property, out var track) && track.Length > 0)
                            endVal = track[^1].Value;
                        values[property] = CssAnimations.ComposeValue(
                            property, baseVal, sampled, composition, completedIterations, endVal);
                    }
                }
            }
        }

        var oldOwned = _owned;
        _owned = values.Keys.ToHashSet();
        var changed = false;
        foreach (var property in oldOwned)
        {
            if (!values.ContainsKey(property))
            {
                _element.Target.ClearCssAnimatedValue(property);
                changed = true;
            }
            if (generation != _generation) return false;
        }
        foreach (var (property, value) in values)
        {
            _element.Target.SetCssAnimatedValue(property, value);
            changed = true;
            if (generation != _generation) return false;
        }

        if (changed)
            NotifyTargetInvalidated();

        if (running) AnimationManager.Register(_subscription);
        else AnimationManager.Unregister(_subscription);
        return running;
    }

    private void NotifyTargetInvalidated()
    {
        if (_element.Target is UIElement ui &&
            CssMotionPath.GetPath(ui).Kind != CssOffsetPathKind.None)
        {
            CssMotionPath.SyncElementMotionTransform(ui);
        }

        if (_element.Target is TextElement textElement)
        {
            textElement.NotifyTextContentChanged();
        }

        if (_element.Target is FrameworkContentElement)
        {
            CssDocumentConsumers.Invalidate(_element);
        }
    }

    private object? GetUnderlyingValue(DependencyProperty property)
        => property == CssFontStretchValue.PercentageProperty
            ? CssFontStretchValue.Computed(_element, skipOwnAnimation: true)
            : _element.Target.GetEffectiveBaseValue(property);

    private static CssAnimationComposition ResolveEntryComposition(
        CssAnimationEntry entry,
        CssAnimationComposition[]? explicitCompositions)
    {
        if (entry.Definition.Composition != CssAnimationComposition.Replace)
            return entry.Definition.Composition;
        if (explicitCompositions is { Length: > 0 })
            return explicitCompositions[entry.Index % explicitCompositions.Length];
        return CssAnimationComposition.Replace;
    }

    private CssAnimationSample ResolveEntrySample(
        CssAnimationEntry entry,
        long frameTimestamp,
        out bool isTimelineDriven,
        out int completedIterations)
    {
        completedIterations = 0;
        if (CssScrollAndViewTimelines.TryResolveProgress(_element.Target, entry.Index, out var timelineProgress))
        {
            isTimelineDriven = true;
            if (double.IsNaN(timelineProgress))
                return new CssAnimationSample(false, false, false, 0);

            var clamped = Math.Clamp(timelineProgress, 0.0, 1.0);
            var iterations = double.IsFinite(entry.Definition.Iterations) && entry.Definition.Iterations > 0
                ? entry.Definition.Iterations
                : 1.0;
            var scaled = clamped * iterations;
            var iterIndex = scaled >= iterations && iterations >= 1.0
                ? Math.Floor(iterations - 1e-9)
                : Math.Floor(scaled);
            completedIterations = (int)Math.Max(0, iterIndex);
            var localProgress = scaled >= iterations ? 1.0 : (scaled - iterIndex);
            var reversed = entry.Definition.Direction switch
            {
                CssAnimationDirection.Reverse => true,
                CssAnimationDirection.Alternate => iterIndex % 2 != 0,
                CssAnimationDirection.AlternateReverse => iterIndex % 2 == 0,
                _ => false,
            };
            return new CssAnimationSample(true, false, false, reversed ? 1.0 - localProgress : localProgress);
        }

        isTimelineDriven = false;
        var elapsed = entry.ElapsedMilliseconds(frameTimestamp);
        var activeTime = elapsed - entry.Definition.Delay;
        if (activeTime > 0 && entry.Definition.Duration > 0)
        {
            var cycle = activeTime / entry.Definition.Duration;
            var maxIter = double.IsPositiveInfinity(entry.Definition.Iterations)
                ? cycle
                : Math.Min(cycle, Math.Max(0, entry.Definition.Iterations - 1e-9));
            completedIterations = (int)Math.Max(0, Math.Floor(maxIter));
        }

        return entry.Definition.Clock.Sample(elapsed);
    }

    private static object? SampleEntryProperty(
        CssAnimationEntry entry,
        DependencyProperty property,
        CssPropertySampler sampler,
        in CssAnimationSample sample)
    {
        if ((property == CssMotionPath.DistanceProperty || property == CssMotionPath.RotateProperty ||
             property == CssMotionPath.AnchorProperty || property == CssMotionPath.PositionProperty) &&
            entry.Tracks.TryGetValue(property, out var frames) &&
            frames.Length >= 2)
        {
            if (!sample.Applies) return null;
            if (sample.DuringDelay)
                return sample.Progress == 1 ? frames[^1].Value : frames[0].Value;
            var progress = Math.Clamp(sample.Progress, 0.0, 1.0);
            if (progress >= 1.0) return frames[^1].Value;
            for (var i = 0; i < frames.Length - 1; i++)
            {
                var from = frames[i];
                var to = frames[i + 1];
                if (progress >= to.Offset) continue;
                var fraction = (progress - from.Offset) / (to.Offset - from.Offset);
                var eased = (from.TimingFunction ?? entry.Definition.Timing).Ease(fraction);
                return CssMotionPath.SampleKeyframeValue(property, from.Value, to.Value, eased) ??
                    sampler.Sample(in sample, entry.Definition.Timing);
            }
            return frames[^1].Value;
        }

        return sampler.Sample(in sample, entry.Definition.Timing);
    }

    private sealed class CssAnimationEntry
    {
        internal int Index { get; }
        internal CssAnimationDefinition Definition { get; }
        internal CssKeyframesRule Rule { get; }
        internal IReadOnlyDictionary<DependencyProperty, CssPropertyKeyframe[]> Tracks { get; set; }
        internal IReadOnlyDictionary<DependencyProperty, CssPropertySampler> Samplers { get; set; }
        internal long StartTimestamp { get; }
        private long _pausedTicks;
        private long? _pauseTimestamp;
        internal bool IsPaused => _pauseTimestamp.HasValue;

        internal CssAnimationEntry(
            int index,
            CssAnimationDefinition definition,
            CssKeyframesRule rule,
            IReadOnlyDictionary<DependencyProperty, CssPropertyKeyframe[]> tracks,
            IReadOnlyDictionary<DependencyProperty, CssPropertySampler> samplers,
            long now)
        {
            Index = index;
            Definition = definition;
            Rule = rule;
            Tracks = tracks;
            Samplers = samplers;
            StartTimestamp = now;
            if (definition.Paused) _pauseTimestamp = now;
        }

        internal void SetPaused(bool paused, long now)
        {
            if (paused && !_pauseTimestamp.HasValue) _pauseTimestamp = now;
            else if (!paused && _pauseTimestamp is { } since)
            {
                _pausedTicks += Math.Max(0, now - since);
                _pauseTimestamp = null;
            }
        }

        internal double ElapsedMilliseconds(long now)
        {
            var tick = _pauseTimestamp ?? now;
            return Math.Max(0, (tick - StartTimestamp - _pausedTicks) * 1000.0 / Stopwatch.Frequency);
        }
    }
}
