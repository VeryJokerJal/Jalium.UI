using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssInlineAlignment { Baseline, Top, Bottom, TextTop, TextBottom, Offset }
internal readonly record struct CssInlineVerticalAlign(CssInlineAlignment Kind, CssLayoutLength Offset = default);
internal enum CssFlowTextAlignment { Start, End, Left, Right, Center, Justify }

internal static class CssFlowProperties
{
    internal static readonly DependencyProperty VerticalAlignProperty = DependencyProperty.RegisterAttached(
        "CssVerticalAlign", typeof(CssInlineVerticalAlign), typeof(CssFlowProperties), new PropertyMetadata(default(CssInlineVerticalAlign), Changed));
    internal static readonly DependencyProperty TextAlignProperty = DependencyProperty.RegisterAttached(
        "CssTextAlign", typeof(CssFlowTextAlignment), typeof(CssFlowProperties), new PropertyMetadata(CssFlowTextAlignment.Start, Changed));

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is not UIElement element) return;
        element.InvalidateMeasure();
        if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
    }

    internal static void Register() => CssPropertyRegistry.Register(new()
    {
        Name = "vertical-align", Kind = CssPropertyKind.Longhand, StorageProperty = VerticalAlignProperty,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            var probe = reader;
            if (probe.TryReadIdent(out var keyword) && probe.AtEnd)
            {
                CssInlineAlignment? alignment = keyword.ToString().ToLowerInvariant() switch
                {
                    "baseline" => CssInlineAlignment.Baseline, "top" => CssInlineAlignment.Top,
                    "bottom" => CssInlineAlignment.Bottom, "text-top" => CssInlineAlignment.TextTop,
                    "text-bottom" => CssInlineAlignment.TextBottom, _ => null,
                };
                return alignment is { } value ? new CssImmediateValue(VerticalAlignProperty, new CssInlineVerticalAlign(value)) : null;
            }
            return reader.TryReadLength(out var length) && reader.AtEnd ? new OffsetValue(length) : null;
        },
    });

    private sealed class OffsetValue(CssLength length) : CssCompiledValue
    {
        public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
        {
            var value = length.Expression is { } expression ? CssLayoutLength.Math(expression, context.Lengths)
                : length.Unit == CssUnit.Percent ? CssLayoutLength.Percent(length.Value / 100)
                : length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var pixels) ? CssLayoutLength.Px(pixels) : default;
            sink.Set(VerticalAlignProperty, new CssInlineVerticalAlign(CssInlineAlignment.Offset, value));
            return true;
        }
    }

    internal static (double Height, double Baseline, double Ascent, double Descent) LineMetrics(FrameworkElement element)
    {
        var fontSize = (double)element.GetValue(TextElement.FontSizeProperty)!;
        var family = (FontFamily)element.GetValue(TextElement.FontFamilyProperty)!;
        var weight = (FontWeight)element.GetValue(TextElement.FontWeightProperty)!;
        var style = (FontStyle)element.GetValue(TextElement.FontStyleProperty)!;
        var metrics = TextMeasurement.GetFontMetrics(family.Source, fontSize, weight.ToOpenTypeWeight(), style.ToOpenTypeStyle());
        var lineHeight = (double)element.GetValue(TextBlock.LineHeightProperty)!;
        if (!double.IsFinite(lineHeight) || lineHeight < 0) lineHeight = Math.Max(metrics.LineHeight, metrics.Baseline + metrics.Descent);
        var natural = Math.Max(metrics.LineHeight, metrics.Baseline + metrics.Descent);
        return (lineHeight, metrics.Baseline + (lineHeight - natural) / 2, metrics.Ascent, metrics.Descent);
    }

    internal static CssFlowTextAlignment TextAlignment(FrameworkElement element)
    {
        var native = TextBlock.TextAlignmentProperty;
        var cssSource = element.GetValueSourceInternal(TextAlignProperty).BaseValueSource;
        if (element.HasLocalOrAnimatedValue(native) || cssSource == BaseValueSource.Default &&
            element.GetValueSourceInternal(native).BaseValueSource != BaseValueSource.Default)
            return (TextAlignment)element.GetValue(native)! switch
            {
                Jalium.UI.TextAlignment.Right => CssFlowTextAlignment.Right,
                Jalium.UI.TextAlignment.Center => CssFlowTextAlignment.Center,
                Jalium.UI.TextAlignment.Justify => CssFlowTextAlignment.Justify, _ => CssFlowTextAlignment.Left,
            };
        return (CssFlowTextAlignment)element.GetValue(TextAlignProperty)!;
    }
}

internal sealed class CssFlowTextAlignmentValue(CssFlowTextAlignment value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssFlowProperties.TextAlignProperty, value);
        var native = value switch
        {
            CssFlowTextAlignment.Right or CssFlowTextAlignment.End => TextAlignment.Right,
            CssFlowTextAlignment.Center => TextAlignment.Center, CssFlowTextAlignment.Justify => TextAlignment.Justify, _ => TextAlignment.Left,
        };
        return new CssNamedValue("text-align", "TextAlignment", native).TryApply(context, sink);
    }
}
