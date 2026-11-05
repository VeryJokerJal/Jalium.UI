namespace Jalium.UI.Styling;

/// <summary>Content-based sizes remain computed values until the formatting context is known.</summary>
internal sealed record CssSizingValue(byte Kind, CssLength? Limit = null, CssCalcSize? Calculation = null)
{
    internal static bool TryRead(ref CssTokenReader reader, out CssSizingValue? value,
        bool allowAuto = true, bool allowContent = false)
    {
        value = null;
        var probe = reader;
        if (probe.TryReadIdent(out var ident))
        {
            var kind = ident.ToString().ToLowerInvariant() switch
            {
                "min-content" => CssLayoutLength.KindMinContent,
                "max-content" => CssLayoutLength.KindMaxContent,
                "fit-content" => CssLayoutLength.KindFitContent,
                "stretch" => CssLayoutLength.KindStretch,
                _ => (byte)0,
            };
            if (kind == 0) return false;
            value = new(kind); reader = probe; return true;
        }
        probe = reader;
        if (!probe.TryReadFunction(out var function, out var arguments)) return false;
        if (function.Equals("calc-size", StringComparison.OrdinalIgnoreCase))
        {
            if (CssCalcSize.Parse(arguments.Remaining, allowAuto, allowContent) is not { } calculation) return false;
            value = new(CssLayoutLength.KindCalcSize, Calculation: calculation); reader = probe; return true;
        }
        if (!function.Equals("fit-content", StringComparison.OrdinalIgnoreCase)) return false;
        var body = arguments;
        if (!body.TryReadLength(out var limit) || !body.AtEnd || limit.Unit == CssUnit.Auto ||
            limit.Unit == CssUnit.None && limit.Value != 0 ||
            limit.Expression is null && limit.Value < 0) return false;
        value = new(CssLayoutLength.KindFitFunction, limit); reader = probe; return true;
    }

    internal CssLayoutLength Compute(in CssLengthContext context)
    {
        if (Calculation is not null) return CssLayoutLength.Calculate(Calculation.Compute(context));
        if (Limit is not { } limit) return CssLayoutLength.Sizing(Kind);
        limit.ObserveContainerDependencies(context);
        CssLayoutLength argument;
        if (limit.Expression is { UsesPercent: true } expression) argument = CssLayoutLength.Math(expression, context);
        else if (limit.Unit == CssUnit.Percent) argument = CssLayoutLength.Percent(limit.Value / 100);
        else if (limit.TryResolve(context, CssPercentBasis.NotSupported, out var pixels)) argument = CssLayoutLength.Px(Math.Max(0, pixels));
        else return default;
        return CssLayoutLength.Fit(argument);
    }
}
