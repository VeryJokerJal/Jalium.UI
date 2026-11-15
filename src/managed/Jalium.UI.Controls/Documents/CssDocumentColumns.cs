using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Documents;

/// <summary>CSS column values and local native document overrides share one used-value calculation.</summary>
internal readonly record struct CssDocumentColumns(DependencyObject Source, int Count, double Width, double Gap,
    double Available, CssColumnFill Fill, FlowDirection Direction, double RuleWidth, CssBorderLineStyle RuleStyle, Brush? RuleBrush)
{
    internal static CssDocumentColumns? Resolve(DependencyObject source, double available)
    {
        var document = source as FlowDocument;
        var count = (int)source.GetValue(CssMultiColumnProperties.CountProperty)!;
        var authored = (CssLayoutLength)source.GetValue(CssMultiColumnProperties.WidthProperty)!;
        var nativeWidth = document is not null && double.IsFinite(document.ColumnWidth) && document.ColumnWidth >= 0;
        var preferNativeWidth = nativeWidth && document!.HasLocalOrAnimatedValue(FlowDocument.ColumnWidthProperty);
        var hint = preferNativeWidth || !authored.IsSet || authored.IsAuto ? nativeWidth ? document!.ColumnWidth : double.NaN : authored.Resolve(available, double.NaN);
        if (count == 0 && !double.IsFinite(hint)) return null;
        var cssGap = (CssLayoutLength)source.GetValue(CssGapProperties.ColumnProperty)!;
        var nativeGap = document is not null && document.HasLocalOrAnimatedValue(FlowDocument.ColumnGapProperty);
        var gap = nativeGap ? document!.ColumnGap : cssGap.IsSet && !cssGap.IsNormal ? cssGap.Resolve(available, 0)
            : nativeWidth && count == 0 && (!authored.IsSet || authored.IsAuto) ? document!.ColumnGap
            : (double)source.GetValue(TextElement.FontSizeProperty)!;
        gap = double.IsFinite(gap) ? Math.Max(0, gap) : 0;
        var sizing = CssMultiColumnProperties.Columns(count, hint, gap, Math.Max(0, available));
        var width = sizing.Width;
        if (nativeWidth && document!.HasLocalOrAnimatedValue(FlowDocument.IsColumnWidthFlexibleProperty) && !document.IsColumnWidthFlexible)
            width = Math.Min(width, Math.Max(1, hint));
        var ruleWidth = (double)source.GetValue(CssMultiColumnProperties.RuleWidthProperty)!;
        var style = (CssBorderLineStyle)source.GetValue(CssMultiColumnProperties.RuleStyleProperty)!;
        var brush = source.GetValue(CssMultiColumnProperties.RuleColorProperty) as Brush ?? source.GetValue(TextElement.ForegroundProperty) as Brush;
        if (document is not null)
        {
            var nativeRule = document.HasLocalOrAnimatedValue(FlowDocument.ColumnRuleWidthProperty) || document.HasLocalOrAnimatedValue(FlowDocument.ColumnRuleBrushProperty);
            if (document.HasLocalOrAnimatedValue(FlowDocument.ColumnRuleWidthProperty)) ruleWidth = document.ColumnRuleWidth;
            if (document.HasLocalOrAnimatedValue(FlowDocument.ColumnRuleBrushProperty)) brush = document.ColumnRuleBrush;
            if (nativeRule && source.GetValueSourceInternal(CssMultiColumnProperties.RuleStyleProperty).BaseValueSource == BaseValueSource.Default) style = CssBorderLineStyle.Solid;
        }
        return new(source, sizing.Count, width, gap, available, (CssColumnFill)source.GetValue(CssMultiColumnProperties.FillProperty)!,
            (FlowDirection)source.GetValue(FrameworkElement.FlowDirectionProperty)!, ruleWidth, style, brush);
    }

    internal double Left(int column) => Direction == FlowDirection.RightToLeft ? Available - Width - column * (Width + Gap) : column * (Width + Gap);
}
