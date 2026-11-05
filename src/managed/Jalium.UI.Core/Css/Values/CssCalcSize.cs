namespace Jalium.UI.Styling;

/// <summary>CSS Values 5: the sizing basis retains layout behavior independently of the calculation.</summary>
internal sealed record CssCalcSize(CssSizingValue? Basis, CssLength? Length, CssCalcSize? Nested,
    bool Any, CssMathExpression Expression)
{
    internal static CssCalcSize? Parse(ReadOnlySpan<char> text, bool allowAuto, bool allowContent, int depth = 0)
    {
        if (depth >= 64 || text.Length > 65536) return null;
        var reader = new CssTokenReader(text);
        if (!reader.TryReadUntilTopLevelComma(out var first) || !reader.TryReadComma()) return null;
        var expression = CssMathExpression.ParseSizeCalculation(reader.Remaining);
        if (expression?.Kind is not (CssNumericKind.Length or CssNumericKind.Percent or CssNumericKind.LengthPercent)) return null;
        var basis = new CssTokenReader(first);
        if (basis.TryReadIdent(out var keyword) && basis.AtEnd)
        {
            if (keyword.Equals("any", StringComparison.OrdinalIgnoreCase))
                return expression.UsesSize ? null : new(null, null, null, true, expression);
            if (allowAuto && keyword.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return new(new(CssLayoutLength.KindAuto), null, null, false, expression);
            if (allowContent && keyword.Equals("content", StringComparison.OrdinalIgnoreCase))
                return new(new(CssLayoutLength.KindContent), null, null, false, expression);
        }
        basis = new(first);
        if (basis.TryReadFunction(out var function, out var arguments) && basis.AtEnd &&
            function.Equals("calc-size", StringComparison.OrdinalIgnoreCase))
        {
            var nested = Parse(arguments.Remaining, allowAuto, allowContent, depth + 1);
            return nested is null ? null : new(null, null, nested, false, expression);
        }
        basis = new(first);
        if (CssSizingValue.TryRead(ref basis, out var sizing, allowAuto, allowContent) && basis.AtEnd)
            return new(sizing, null, null, false, expression);
        var calculation = CssMathExpression.Parse("calc", first);
        return calculation?.Kind is CssNumericKind.Length or CssNumericKind.Percent or CssNumericKind.LengthPercent
            ? new(null, new CssLength(calculation), null, false, expression) : null;
    }

    internal CssCalcSizeComputed Compute(in CssLengthContext context)
    {
        Expression.ObserveContainerDependencies(context);
        var basis = Basis?.Compute(context) ?? default;
        if (Length is { } length)
        {
            length.ObserveContainerDependencies(context);
            if (length.Expression is { } math)
                basis = math.UsesPercent ? CssLayoutLength.Math(math, context)
                    : math.TryEvaluate(context, 0, out var number) ? CssLayoutLength.Px(number) : default;
            else if (length.Unit == CssUnit.Percent) basis = CssLayoutLength.Percent(length.Value / 100);
            else if (length.TryResolve(context, CssPercentBasis.NotSupported, out var number)) basis = CssLayoutLength.Px(number);
        }
        return new(basis, Nested?.Compute(context), Expression, context, Any);
    }
}

internal sealed record CssCalcSizeComputed(CssLayoutLength Basis, CssCalcSizeComputed? Nested,
    CssMathExpression Expression, CssLengthContext Context, bool Any,
    CssCalcSizeComputed? From = null, CssCalcSizeComputed? To = null, double Progress = 0)
{
    internal double Evaluate(Func<CssLayoutLength, double> resolveBasis, double percentBasis, double fallback)
    {
        if (From is { } from && To is { } to)
        {
            var first = from.Evaluate(resolveBasis, percentBasis, fallback);
            var second = to.Evaluate(resolveBasis, percentBasis, fallback);
            return double.IsNaN(first) || double.IsNaN(second) ? fallback
                : CssMathExpression.Censor(first * (1 - Progress) + second * Progress);
        }
        var size = Nested is { } nested ? nested.Evaluate(resolveBasis, percentBasis, fallback)
            : Any ? 0 : resolveBasis(Basis);
        // Nonvisual block heights can defer their content-dependent calculation
        // until the natural layout exists. Unknown geometry is not arithmetic NaN.
        if (Expression.UsesSize && double.IsNaN(size)) return fallback;
        return Expression.TryEvaluate(Context.WithCalcSizeBasis(size),
            double.IsFinite(percentBasis) ? percentBasis : 0, out var result) ? result : fallback;
    }

    internal CssLayoutLength SizingBasis => Nested?.SizingBasis ?? (Any ? CssLayoutLength.Px(0) : Basis);
    internal bool UsesPercent => Expression.UsesPercent || Basis.IsPercent || Nested?.UsesPercent == true ||
        From?.UsesPercent == true || To?.UsesPercent == true;
    internal int Depth => 1 + Math.Max(Nested?.Depth ?? 0, Math.Max(From?.Depth ?? 0, To?.Depth ?? 0));
}
