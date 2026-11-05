using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Numeric CSS rulers captured before a gradient leaves the recording thread.</summary>
internal sealed class CssRenderLengthSnapshot
{
    private static readonly CssUnit[] Units = Enum.GetValues<CssUnit>();
    private readonly double[] _factors;
    internal readonly double SiblingIndex, SiblingCount, DpiScale;
    internal readonly long ContentHash;

    internal CssRenderLengthSnapshot(in CssLengthContext context)
    {
        _factors = new double[Units.Max(unit => (int)unit) + 1];
        Array.Fill(_factors, double.NaN);
        foreach (var unit in Units)
            if (unit is not (CssUnit.Percent or CssUnit.Expression) &&
                new CssLength(1, unit).TryResolve(context, CssPercentBasis.NotSupported, out var factor))
                _factors[(int)unit] = factor;
        SiblingIndex = Sibling("sibling-index", context);
        SiblingCount = Sibling("sibling-count", context);
        DpiScale = context.Element?.Target is Visual visual
            ? VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleX : 1;
        if (!double.IsFinite(DpiScale) || DpiScale <= 0) DpiScale = 1;
        long hash = 1469598103934665603;
        foreach (var factor in _factors) Fold(factor);
        Fold(SiblingIndex); Fold(SiblingCount); Fold(DpiScale);
        ContentHash = hash;
        void Fold(double value) => hash = unchecked((hash ^ BitConverter.DoubleToInt64Bits(value)) * 1099511628211);
    }

    internal bool TryResolve(CssUnit unit, double value, out double result)
    {
        var factor = (uint)(int)unit < (uint)_factors.Length ? _factors[(int)unit] : double.NaN;
        result = value * factor;
        if (double.IsNaN(factor)) return false;
        // Absolute/em/rem arithmetic retains IEEE special values until the
        // expression's outer censor, just like the live length context.
        return unit is CssUnit.None or CssUnit.Px or CssUnit.Pt or CssUnit.In or CssUnit.Cm or
            CssUnit.Mm or CssUnit.Q or CssUnit.Pc or CssUnit.Em or CssUnit.Rem || double.IsFinite(result);
    }

    private static double Sibling(string operation, in CssLengthContext context)
        => new CssMathExpression(operation, CssNumericType.Of(CssNumericKind.Number), default, [])
            .TryEvaluate(context, 0, out var value) ? value : double.NaN;
}
