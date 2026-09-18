namespace Jalium.UI.Styling;

internal enum CssLineHeightKind { Normal, Number, Pixels }
internal readonly record struct CssLineHeight(CssLineHeightKind Kind, double Value = 0);

internal sealed class CssComputedLineHeightValue(CssLineHeight value) : CssCompiledValue
{
    internal static readonly DependencyProperty Property = DependencyProperty.RegisterAttached(
        "CssLineHeight", typeof(CssLineHeight), typeof(CssComputedLineHeightValue), new PropertyMetadata(default(CssLineHeight)));

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(Property, value);
        var pixels = value.Kind switch
        {
            CssLineHeightKind.Number => value.Value * context.Lengths.ElementFontSize,
            CssLineHeightKind.Pixels => value.Value, _ => double.NaN,
        };
        return new CssNamedValue("line-height", "LineHeight", pixels).TryApply(context, sink);
    }

    internal static CssLineHeight Inherited(CssNode parent)
    {
        var native = CssDependencyPropertyLookup.Find(parent.GetType(), "LineHeight");
        if (native is not null && (parent.HasLocalOrAnimatedValue(native) ||
            parent.Target.GetValueSourceInternal(Property).BaseValueSource == BaseValueSource.Default &&
            parent.Target.GetValueSourceInternal(native).BaseValueSource != BaseValueSource.Default))
        {
            var pixels = (double)parent.GetValue(native)!;
            return double.IsFinite(pixels) ? new(CssLineHeightKind.Pixels, pixels) : default;
        }
        return (CssLineHeight)parent.GetValue(Property)!;
    }
}

internal sealed class CssLengthLineHeightValue(CssLength length) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (!length.TryResolve(context.Lengths.ForLineHeight(), CssPercentBasis.ElementFontSize, out var pixels) || !double.IsFinite(pixels)) return false;
        return new CssComputedLineHeightValue(new(CssLineHeightKind.Pixels, Math.Max(0, pixels))).TryApply(context, sink);
    }
}
