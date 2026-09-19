namespace Jalium.UI.Styling;

internal static partial class CssCoreProperties
{
    private static void RegisterDetailedTransitions()
    {
        RegisterLonghand("transition-property", (ref CssTokenReader reader, CssCompileContext context) =>
        {
            var properties = new List<string>();
            while (true)
            {
                if (!reader.TryReadIdent(out var name)) return null;
                var raw = name.ToString();
                properties.Add(raw.Equals("all", StringComparison.OrdinalIgnoreCase) ? TransitionPropertyCollection.AllKeyword
                    : raw.Equals("none", StringComparison.OrdinalIgnoreCase) ? TransitionPropertyCollection.NoneKeyword
                    : TryGetTransitionTargetName(raw, out var target) ? target : raw);
                if (!reader.TryReadComma()) break;
            }
            return reader.AtEnd && !(properties.Count > 1 && properties.Contains(TransitionPropertyCollection.NoneKeyword))
                ? new CssTransitionPartValue("transition-property", properties.ToArray()) : null;
        });
        RegisterLonghand("transition-duration", (ref CssTokenReader reader, CssCompileContext context) => ParseTransitionTimes(ref reader, delay: false));
        RegisterLonghand("transition-delay", (ref CssTokenReader reader, CssCompileContext context) => ParseTransitionTimes(ref reader, delay: true));
        RegisterLonghand("transition-timing-function", (ref CssTokenReader reader, CssCompileContext context) =>
        {
            var values = new List<CssTimingFunction>();
            while (true)
            {
                if (!CssTimingFunction.TryParse(ref reader, out var timing)) return null;
                values.Add(timing!);
                if (!reader.TryReadComma()) break;
            }
            return reader.AtEnd ? new CssTransitionPartValue("transition-timing-function", values.ToArray()) : null;
        });
        RegisterShorthand("transition", ExpandDetailedTransition);
    }

    private static CssCompiledValue? ParseTransitionTimes(ref CssTokenReader reader, bool delay)
    {
        var times = new List<double>();
        while (true)
        {
            if (!ReadTransitionTime(ref reader, delay, out var time)) return null;
            times.Add(time);
            if (!reader.TryReadComma()) break;
        }
        return reader.AtEnd ? new CssTransitionPartValue(delay ? "transition-delay" : "transition-duration", times.ToArray()) : null;
    }

    private static bool ReadTransitionTime(ref CssTokenReader reader, bool delay, out double milliseconds)
    {
        milliseconds = 0;
        var probe = reader;
        if (!probe.TryReadNumber(out var value, out var unit) || !CssUnitConversion.TryToMilliseconds(value, unit, out milliseconds)) return false;
        if (probe.NumberWasCalculated)
        {
            var maximum = TimeSpan.MaxValue.TotalMilliseconds - 1;
            milliseconds = Math.Clamp(milliseconds, delay ? -maximum : 0, maximum);
        }
        if (!double.IsFinite(milliseconds) || !delay && milliseconds < 0 || Math.Abs(milliseconds) >= TimeSpan.MaxValue.TotalMilliseconds) return false;
        reader = probe;
        return true;
    }

    private static bool ExpandDetailedTransition(ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        var properties = new List<string>(); var durations = new List<double>(); var delays = new List<double>();
        var timings = new List<CssTimingFunction>();
        while (true)
        {
            string? property = null; CssTimingFunction? timing = null;
            double? duration = null, delay = null;
            var consumed = false;
            while (!reader.AtEnd && (!reader.TryPeekChar(out var next) || next != ','))
            {
                if (ReadTransitionTime(ref reader, duration is not null, out var time))
                {
                    if (duration is null) duration = time;
                    else if (delay is null) delay = time;
                    else return false;
                }
                else if (CssTimingFunction.TryParse(ref reader, out var parsed))
                {
                    if (timing is not null) return false;
                    timing = parsed;
                }
                else if (reader.TryReadIdent(out var name))
                {
                    if (property is not null) return false;
                    var raw = name.ToString();
                    property = raw.Equals("all", StringComparison.OrdinalIgnoreCase) ? TransitionPropertyCollection.AllKeyword
                        : raw.Equals("none", StringComparison.OrdinalIgnoreCase) ? TransitionPropertyCollection.NoneKeyword
                        : TryGetTransitionTargetName(raw, out var target) ? target : raw;
                }
                else return false;
                consumed = true;
            }
            if (!consumed) return false;
            properties.Add(property ?? TransitionPropertyCollection.AllKeyword);
            durations.Add(duration ?? 0); delays.Add(delay ?? 0); timings.Add(timing ?? CssTimingFunction.EaseDefault);
            if (!reader.TryReadComma()) break;
        }
        if (!reader.AtEnd || properties.Count > 1 && properties.Contains(TransitionPropertyCollection.NoneKeyword)) return false;
        output.Add(new("transition-property", new CssTransitionPartValue("transition-property", properties.ToArray()), false));
        output.Add(new("transition-duration", new CssTransitionPartValue("transition-duration", durations.ToArray()), false));
        output.Add(new("transition-delay", new CssTransitionPartValue("transition-delay", delays.ToArray()), false));
        output.Add(new("transition-timing-function", new CssTransitionPartValue("transition-timing-function", timings.ToArray()), false));
        return true;
    }
}

internal sealed class CssTransitionPartValue(string property, object value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var parts = context.Slots.Transitions ??= new CssTransitionParts();
        parts.FromState |= context.Slots.CurrentContributionIsState;
        switch (property)
        {
            case "transition-property": parts.Properties = (string[])value; break;
            case "transition-duration": parts.Durations = (double[])value; break;
            case "transition-delay": parts.Delays = (double[])value; break;
            case "transition-timing-function": parts.Timings = (CssTimingFunction[])value; break;
        }
        return true;
    }
}
