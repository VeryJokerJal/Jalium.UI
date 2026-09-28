using Jalium.UI.Media.Animation;

namespace Jalium.UI.Styling;

/// <summary>CSS timing functions, including exact x-inverted cubic Bézier curves.</summary>
internal sealed record CssTimingFunction(string Kind, double X1 = 0, double Y1 = 0, double X2 = 1, double Y2 = 1,
    int Steps = 1, string Jump = "jump-end") : IEasingFunction
{
    public static CssTimingFunction EaseDefault { get; } = new("ease", .25, .1, .25, 1);
    public double Ease(double normalizedTime)
    {
        var time = Math.Clamp(normalizedTime, 0, 1);
        if (Kind == "linear") return time;
        if (Kind == "steps")
        {
            var step = Math.Floor(time * Steps);
            if (Jump is "jump-start" or "jump-both") step++;
            var jumps = Steps + (Jump == "jump-both" ? 1 : Jump == "jump-none" ? -1 : 0);
            return Math.Clamp(step / jumps, 0, 1);
        }
        if (time is 0 or 1) return time;
        var low = 0.0; var high = 1.0;
        for (var i = 0; i < 48; i++)
        {
            var t = (low + high) * .5;
            if (Bezier(t, X1, X2) < time) low = t; else high = t;
        }
        return Bezier((low + high) * .5, Y1, Y2);
    }

    private static double Bezier(double t, double a, double b)
    { var inverse = 1 - t; return 3 * inverse * inverse * t * a + 3 * inverse * t * t * b + t * t * t; }

    public TransitionTimingFunction Legacy => Kind switch
    {
        "linear" => TransitionTimingFunction.Linear, "ease-in" => TransitionTimingFunction.EaseIn,
        "ease-out" => TransitionTimingFunction.EaseOut, "ease-in-out" => TransitionTimingFunction.EaseInOut,
        _ => TransitionTimingFunction.Recommended,
    };

    public static bool TryParse(ref CssTokenReader reader, out CssTimingFunction? timing)
    {
        timing = null;
        var probe = reader;
        if (probe.TryReadIdent(out var word))
        {
            timing = word.ToString().ToLowerInvariant() switch
            {
                "linear" => new("linear"), "ease" => EaseDefault,
                "ease-in" => new("ease-in", .42, 0, 1, 1), "ease-out" => new("ease-out", 0, 0, .58, 1),
                "ease-in-out" => new("ease-in-out", .42, 0, .58, 1),
                "step-start" => new("steps", Steps: 1, Jump: "jump-start"),
                "step-end" => new("steps"), _ => null,
            };
        }
        else if (probe.TryReadFunction(out var function, out var args))
        {
            if (function.Equals("cubic-bezier", StringComparison.OrdinalIgnoreCase))
            {
                Span<double> components = stackalloc double[4];
                for (var i = 0; i < 4; i++)
                {
                    if (i > 0 && !args.TryReadComma() || !args.TryReadNumber(out components[i], out var unit) ||
                        unit != CssUnit.None || !double.IsFinite(components[i])) return false;
                    if (i is 0 or 2 && args.NumberWasCalculated) components[i] = Math.Clamp(components[i], 0, 1);
                }
                if (!args.AtEnd || components[0] is < 0 or > 1 || components[2] is < 0 or > 1) return false;
                timing = new("cubic-bezier", components[0], components[1], components[2], components[3]);
            }
            else if (function.Equals("steps", StringComparison.OrdinalIgnoreCase))
            {
                if (!args.TryReadInteger(out var count, minimum: 1)) return false;
                var jump = "jump-end";
                if (args.TryReadComma())
                {
                    if (!args.TryReadIdent(out var mode)) return false;
                    jump = mode.ToString().ToLowerInvariant();
                    if (jump == "start") jump = "jump-start";
                    if (jump == "end") jump = "jump-end";
                }
                if (!args.AtEnd || jump is not ("jump-start" or "jump-end" or "jump-none" or "jump-both") || jump == "jump-none" && count < 2) return false;
                timing = new("steps", Steps: (int)count, Jump: jump);
            }
        }
        if (timing is null) return false;
        reader = probe;
        return true;
    }
}
