using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;

namespace Jalium.UI.Styling;

internal enum CssLineHeightKind { Normal, Number, Pixels }
internal readonly record struct CssLineHeight(CssLineHeightKind Kind, double Value = 0);

internal sealed class CssComputedLineHeightValue(CssLineHeight value) : CssCompiledValue
{
    internal static readonly DependencyProperty Property = DependencyProperty.RegisterAttached(
        "CssLineHeight", typeof(CssLineHeight), typeof(CssComputedLineHeightValue),
        new PropertyMetadata(default(CssLineHeight), OnChanged, null, inherits: true));

    private static void OnChanged(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (target is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (target is TextElement text) text.NotifyTextContentChanged();
        else if (target is UIElement element)
        {
            CssFlowProperties.InvalidateInheritedTextLayout(element);
            if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
        }
    }

    internal static bool HasCssSource(DependencyObject target)
    {
        for (DependencyObject? node = target; node is not null;)
        {
            var source = node.GetValueSourceInternal(Property).BaseValueSource;
            if (source is not BaseValueSource.Default and not BaseValueSource.Inherited) return true;
            if (source == BaseValueSource.Default) return false;
            node = node is FrameworkElement element ? element.Parent : (node as FrameworkContentElement)?.Parent;
        }
        return false;
    }

    internal static (double Height, double Baseline) LayoutBounds(DependencyObject target, double fontSize, in TextMetrics metrics,
        bool rubyFontArea = true)
    {
        if (rubyFontArea && CssRubyBoxMetrics.IsFontArea(target)) return CssRubyBoxMetrics.FontBounds(fontSize, metrics);
        if (CssInitialLetterProperties.Metrics(target) is { } initial) return (initial.Height, initial.Baseline);
        var line = Inherited(CssNode.Get(target));
        var natural = Math.Max(metrics.LineHeight, metrics.Baseline + metrics.Descent);
        var height = line.Kind switch
        {
            CssLineHeightKind.Number => line.Value * fontSize,
            CssLineHeightKind.Pixels => line.Value,
            _ => natural,
        };
        if (target.HasLocalOrAnimatedValue(TextBlock.LineStackingStrategyProperty) &&
            TextBlock.GetLineStackingStrategy(target) == LineStackingStrategy.MaxHeight)
            height = Math.Max(height, natural);
        var ascent = float.IsFinite(metrics.Ascent) ? Math.Max(0, metrics.Ascent) : fontSize * .8;
        var descent = float.IsFinite(metrics.Descent) ? Math.Max(0, metrics.Descent) : fontSize * .2;
        return (Math.Max(0, height), ascent + (height - ascent - descent) / 2);
    }

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
