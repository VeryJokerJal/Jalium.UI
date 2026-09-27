using System.Diagnostics;
using Jalium.UI.Animation;

namespace Jalium.UI.Styling;

internal readonly record struct CssAnimationName(string Value, bool IsNoneKeyword = false);

internal sealed record CssAnimationDefinition(CssAnimationName Name, double Duration, double Delay,
    CssTimingFunction Timing, double Iterations, CssAnimationDirection Direction,
    CssAnimationFillMode Fill, bool Paused)
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
    private object[]? _names, _durations, _delays, _timings, _counts, _directions, _fills, _plays;
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
        }
    }

    internal void Flush(ICssSetterSink sink)
    {
        var names = _names ?? [new CssAnimationName("none", true)];
        var definitions = new CssAnimationDefinition[names.Length];
        for (var index = 0; index < names.Length; index++)
            definitions[index] = new CssAnimationDefinition((CssAnimationName)names[index],
                At(_durations, index, 0.0), At(_delays, index, 0.0),
                At(_timings, index, CssTimingFunction.EaseDefault), At(_counts, index, 1.0),
                At(_directions, index, CssAnimationDirection.Normal),
                At(_fills, index, CssAnimationFillMode.None), At(_plays, index, false));
        sink.CurrentValueIsState = FromState;
        sink.Set(CssAnimations.DataProperty, new CssAnimationData(definitions));
    }

    private static T At<T>(object[]? values, int index, T fallback)
        => values is { Length: > 0 } ? (T)values[index % values.Length] : fallback;
}

internal static class CssAnimations
{
    internal static readonly DependencyProperty DataProperty = DependencyProperty.RegisterAttached(
        "AnimationData", typeof(CssAnimationData), typeof(CssAnimations), new PropertyMetadata(null));

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
                entry.Samplers = CssKeyframes.BuildSamplers(_element, entry.Rule);
            }
        }
        else
        {
            var entries = new List<CssAnimationEntry>();
            for (var index = 0; index < data.Definitions.Length; index++)
            {
                if (rules[index] is not { } rule) continue;
                var samplers = CssKeyframes.BuildSamplers(_element, rule);
                if (samplers.Count > 0)
                    entries.Add(new CssAnimationEntry(index, data.Definitions[index], rule, samplers, now));
            }
            _entries = entries.ToArray();
        }
        _specifications = data.Definitions;
        _rules = rules;
        Render(now);
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
    }

    bool IFrameAnimatable.OnAnimationFrame(long frameTimestamp) => Render(frameTimestamp);

    internal bool Render(long frameTimestamp)
    {
        var generation = _generation;
        var values = new Dictionary<DependencyProperty, object?>();
        var running = false;
        // The first animation in the CSS list has composite priority over later ones.
        for (var index = _entries.Length - 1; index >= 0; index--)
        {
            var entry = _entries[index];
            var sample = entry.Definition.Clock.Sample(entry.ElapsedMilliseconds(frameTimestamp));
            if (!sample.Finished && !entry.IsPaused && entry.Samplers.Count > 0) running = true;
            if (!sample.Applies) continue;
            foreach (var (property, sampler) in entry.Samplers)
                values[property] = sampler.Sample(sample, entry.Definition.Timing);
        }

        var oldOwned = _owned;
        _owned = values.Keys.ToHashSet();
        foreach (var property in oldOwned)
        {
            if (!values.ContainsKey(property)) _element.Target.ClearCssAnimatedValue(property);
            if (generation != _generation) return false;
        }
        foreach (var (property, value) in values)
        {
            _element.Target.SetCssAnimatedValue(property, value);
            if (generation != _generation) return false;
        }

        if (running) AnimationManager.Register(_subscription);
        else AnimationManager.Unregister(_subscription);
        return running;
    }

    private sealed class CssAnimationEntry
    {
        internal int Index { get; }
        internal CssAnimationDefinition Definition { get; }
        internal CssKeyframesRule Rule { get; }
        internal IReadOnlyDictionary<DependencyProperty, CssPropertySampler> Samplers { get; set; }
        internal long StartTimestamp { get; }
        private long _pausedTicks;
        private long? _pauseTimestamp;
        internal bool IsPaused => _pauseTimestamp.HasValue;

        internal CssAnimationEntry(int index, CssAnimationDefinition definition, CssKeyframesRule rule,
            IReadOnlyDictionary<DependencyProperty, CssPropertySampler> samplers, long now)
        {
            Index = index;
            Definition = definition;
            Rule = rule;
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
