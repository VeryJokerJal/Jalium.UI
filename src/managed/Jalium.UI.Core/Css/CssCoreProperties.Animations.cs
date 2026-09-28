namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterAnimations()
    {
        RegisterLonghand("animation-name", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-name", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (item.TryReadString(out var quoted)) { value = new CssAnimationName(quoted); return true; }
                if (!item.TryReadIdent(out var ident)) return false;
                value = new CssAnimationName(ident.ToString(),
                    ident.Equals("none", StringComparison.OrdinalIgnoreCase));
                return true;
            }));
        RegisterLonghand("animation-duration", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-duration", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!ReadAnimationTime(ref item, false, out var time)) return false;
                value = time;
                return true;
            }));
        RegisterLonghand("animation-delay", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-delay", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!ReadAnimationTime(ref item, true, out var time)) return false;
                value = time;
                return true;
            }));
        RegisterLonghand("animation-timing-function", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-timing-function", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!CssTimingFunction.TryParse(ref item, out var timing)) return false;
                value = timing;
                return true;
            }));
        RegisterLonghand("animation-iteration-count", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-iteration-count", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!ReadIterationCount(ref item, out var count)) return false;
                value = count;
                return true;
            }));
        RegisterLonghand("animation-direction", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-direction", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!item.TryReadIdent(out var ident) || !TryReadDirection(ident, out var direction)) return false;
                value = direction;
                return true;
            }));
        RegisterLonghand("animation-fill-mode", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-fill-mode", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!item.TryReadIdent(out var ident) || !TryReadFillMode(ident, out var fill)) return false;
                value = fill;
                return true;
            }));
        RegisterLonghand("animation-play-state", (ref CssTokenReader reader, CssCompileContext _) =>
            ParseAnimationList(ref reader, "animation-play-state", static (ref CssTokenReader item, out object? value) =>
            {
                value = null;
                if (!item.TryReadIdent(out var ident) || !TryReadPlayState(ident, out var paused)) return false;
                value = paused;
                return true;
            }));
        RegisterShorthand("animation", ExpandAnimation);
    }

    private delegate bool ReadAnimationComponent(ref CssTokenReader reader, out object? value);

    private static CssCompiledValue? ParseAnimationList(ref CssTokenReader reader, string property,
        ReadAnimationComponent parse)
    {
        var values = new List<object>();
        do
        {
            if (!parse(ref reader, out var value) || value is null) return null;
            values.Add(value);
        } while (reader.TryReadComma());
        return reader.AtEnd ? new CssAnimationPartValue(property, values.ToArray()) : null;
    }

    private static bool ReadAnimationTime(ref CssTokenReader reader, bool delay, out double milliseconds)
    {
        milliseconds = 0;
        var probe = reader;
        if (!probe.TryReadNumber(out var number, out var unit) ||
            !CssUnitConversion.TryToMilliseconds(number, unit, out milliseconds)) return false;
        if (probe.NumberWasCalculated)
        {
            var maximum = TimeSpan.MaxValue.TotalMilliseconds - 1;
            milliseconds = Math.Clamp(milliseconds, delay ? -maximum : 0, maximum);
        }
        if (!double.IsFinite(milliseconds) || (!delay && milliseconds < 0) ||
            Math.Abs(milliseconds) >= TimeSpan.MaxValue.TotalMilliseconds) return false;
        reader = probe;
        return true;
    }

    private static bool ReadIterationCount(ref CssTokenReader reader, out double count)
    {
        count = 0;
        var probe = reader;
        if (probe.TryReadIdent(out var word) && word.Equals("infinite", StringComparison.OrdinalIgnoreCase))
        {
            count = double.PositiveInfinity;
            reader = probe;
            return true;
        }
        probe = reader;
        if (!probe.TryReadNumber(out count, out var unit) || unit != CssUnit.None) return false;
        if (probe.NumberWasCalculated) count = Math.Max(0, count);
        if (!double.IsFinite(count) || count < 0) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadDirection(ReadOnlySpan<char> word, out CssAnimationDirection direction)
    {
        direction = word.ToString().ToLowerInvariant() switch
        {
            "normal" => CssAnimationDirection.Normal,
            "reverse" => CssAnimationDirection.Reverse,
            "alternate" => CssAnimationDirection.Alternate,
            "alternate-reverse" => CssAnimationDirection.AlternateReverse,
            _ => (CssAnimationDirection)255,
        };
        return (byte)direction != 255;
    }

    private static bool TryReadFillMode(ReadOnlySpan<char> word, out CssAnimationFillMode fill)
    {
        fill = word.ToString().ToLowerInvariant() switch
        {
            "none" => CssAnimationFillMode.None,
            "forwards" => CssAnimationFillMode.Forwards,
            "backwards" => CssAnimationFillMode.Backwards,
            "both" => CssAnimationFillMode.Both,
            _ => (CssAnimationFillMode)255,
        };
        return (byte)fill != 255;
    }

    private static bool TryReadPlayState(ReadOnlySpan<char> word, out bool paused)
    {
        paused = word.Equals("paused", StringComparison.OrdinalIgnoreCase);
        return paused || word.Equals("running", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExpandAnimation(ref CssTokenReader reader, CssCompileContext _,
        List<CssCompiledDeclaration> output)
    {
        var names = new List<object>(); var durations = new List<object>(); var delays = new List<object>();
        var timings = new List<object>(); var counts = new List<object>(); var directions = new List<object>();
        var fills = new List<object>(); var plays = new List<object>();
        while (true)
        {
            string? name = null; double? duration = null, delay = null, count = null;
            var nameWasQuoted = false;
            CssTimingFunction? timing = null; CssAnimationDirection? direction = null;
            CssAnimationFillMode? fill = null; bool? paused = null;
            var consumed = false;
            while (!reader.AtEnd && (!reader.TryPeekChar(out var next) || next != ','))
            {
                if (ReadAnimationTime(ref reader, duration is not null, out var time))
                {
                    if (duration is null) duration = time;
                    else if (delay is null) delay = time;
                    else return false;
                }
                else if (CssTimingFunction.TryParse(ref reader, out var parsedTiming))
                {
                    if (timing is not null) return false;
                    timing = parsedTiming;
                }
                else if (ReadIterationCount(ref reader, out var parsedCount))
                {
                    if (count is not null) return false;
                    count = parsedCount;
                }
                else if (reader.TryReadString(out var quoted))
                {
                    if (name is not null) return false;
                    name = quoted;
                    nameWasQuoted = true;
                }
                else if (reader.TryReadIdent(out var ident))
                {
                    if (TryReadDirection(ident, out var parsedDirection) && direction is null)
                        direction = parsedDirection;
                    else if (TryReadFillMode(ident, out var parsedFill) && fill is null &&
                             !ident.Equals("none", StringComparison.OrdinalIgnoreCase))
                        fill = parsedFill;
                    else if (TryReadPlayState(ident, out var parsedPaused) && paused is null)
                        paused = parsedPaused;
                    else if (name is null) name = ident.ToString();
                    else if (TryReadFillMode(ident, out parsedFill) && fill is null) fill = parsedFill;
                    else return false;
                }
                else return false;
                consumed = true;
            }
            if (!consumed) return false;
            names.Add(name is null ? new CssAnimationName("none", true) :
                new CssAnimationName(name, !nameWasQuoted &&
                    name.Equals("none", StringComparison.OrdinalIgnoreCase)));
            durations.Add(duration ?? 0); delays.Add(delay ?? 0);
            timings.Add(timing ?? CssTimingFunction.EaseDefault); counts.Add(count ?? 1);
            directions.Add(direction ?? CssAnimationDirection.Normal);
            fills.Add(fill ?? CssAnimationFillMode.None); plays.Add(paused ?? false);
            if (!reader.TryReadComma()) break;
        }
        if (!reader.AtEnd) return false;
        output.Add(new("animation-name", new CssAnimationPartValue("animation-name", names.ToArray()), false));
        output.Add(new("animation-duration", new CssAnimationPartValue("animation-duration", durations.ToArray()), false));
        output.Add(new("animation-delay", new CssAnimationPartValue("animation-delay", delays.ToArray()), false));
        output.Add(new("animation-timing-function", new CssAnimationPartValue("animation-timing-function", timings.ToArray()), false));
        output.Add(new("animation-iteration-count", new CssAnimationPartValue("animation-iteration-count", counts.ToArray()), false));
        output.Add(new("animation-direction", new CssAnimationPartValue("animation-direction", directions.ToArray()), false));
        output.Add(new("animation-fill-mode", new CssAnimationPartValue("animation-fill-mode", fills.ToArray()), false));
        output.Add(new("animation-play-state", new CssAnimationPartValue("animation-play-state", plays.ToArray()), false));
        return true;
    }
}

internal sealed class CssAnimationPartValue(string property, object[] values) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var parts = context.Slots.Animations ??= new CssAnimationParts();
        parts.FromState |= context.Slots.CurrentContributionIsState;
        parts.Set(property, values);
        return true;
    }
}
