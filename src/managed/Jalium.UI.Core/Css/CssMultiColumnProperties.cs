using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssColumnFill { Balance, Auto, BalanceAll }

internal static class CssMultiColumnProperties
{
    internal static readonly DependencyProperty CountProperty = Property("CssColumnCount", typeof(int), 0);
    internal static readonly DependencyProperty WidthProperty = Property("CssColumnWidth", typeof(CssLayoutLength), CssLayoutLength.Auto);
    internal static readonly DependencyProperty FillProperty = Property("CssColumnFill", typeof(CssColumnFill), CssColumnFill.Balance);
    internal static readonly DependencyProperty SpanProperty = Property("CssColumnSpan", typeof(bool), false);
    internal static readonly DependencyProperty RuleWidthProperty = Property("CssColumnRuleWidth", typeof(double), 3d);
    internal static readonly DependencyProperty RuleStyleProperty = Property("CssColumnRuleStyle", typeof(CssBorderLineStyle), CssBorderLineStyle.None);
    internal static readonly DependencyProperty RuleColorProperty = Property("CssColumnRuleColor", typeof(Brush), null);

    private static DependencyProperty Property(string name, Type type, object? initial) => DependencyProperty.RegisterAttached(
        name, type, typeof(CssMultiColumnProperties), new PropertyMetadata(initial, static (target, _) =>
        {
            if (target is FrameworkElement element) CssMultiColumnLayout.PropertiesChanged(element);
            else if (target is TextElement text) text.NotifyTextContentChanged();
            else if (target is FlowDocument document) document.NotifyPaginationStyleChanged();
        }));

    internal static bool Specified(FrameworkElement element) => (int)element.GetValue(CountProperty)! > 0 ||
        element.GetValue(WidthProperty) is CssLayoutLength { IsAuto: false, IsSet: true };
    internal static bool Active(FrameworkElement element) => Specified(element) && element.IsCssContentHost &&
        element.CssDisplayMode is CssDisplayMode.Native or CssDisplayMode.Block or CssDisplayMode.FlowRoot or CssDisplayMode.TableCell or CssDisplayMode.TableCaption &&
        !CssReplacedContent.IsActive(element);
    internal static double Gap(FrameworkElement element, double width) => CssGapProperties.IsNormal(element, false)
        ? (double)element.GetValue(TextElement.FontSizeProperty)! : CssGapProperties.Resolve(element, false, width);

    internal static (int Count, double Width, double Gap) Columns(FrameworkElement element, double available)
    {
        var gap = Gap(element, available); var count = (int)element.GetValue(CountProperty)!;
        var hint = ((CssLayoutLength)element.GetValue(WidthProperty)!).Resolve(available, double.NaN);
        return Columns(count, hint, gap, available);
    }

    internal static (int Count, double Width, double Gap) Columns(int count, double hint, double gap, double available)
    {
        if (double.IsFinite(hint))
        {
            var fit = Math.Max(1, Math.Floor((available + gap) / (Math.Max(1, hint) + gap)));
            count = (int)Math.Min(count == 0 ? int.MaxValue : count, fit);
        }
        count = Math.Clamp(count, 1, 1000);
        return (count, Math.Max(0, (available + gap) / count - gap), gap);
    }
}

internal static partial class CssCoreProperties
{
    private static void RegisterMultiColumns()
    {
        CssPropertyRegistry.Register(new() { Name = "column-count", Kind = CssPropertyKind.Longhand, StorageProperty = CssMultiColumnProperties.CountProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var name) && Eq(name, "auto") && probe.AtEnd)
                { reader = probe; return new CssImmediateValue(CssMultiColumnProperties.CountProperty, 0); }
                return reader.TryReadInteger(out var count, minimum: 1) && reader.AtEnd ? new CssImmediateValue(CssMultiColumnProperties.CountProperty, count) : null;
            } });
        CssPropertyRegistry.Register(new() { Name = "column-width", Kind = CssPropertyKind.Longhand, StorageProperty = CssMultiColumnProperties.WidthProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                var probe = reader;
                if (probe.TryReadIdent(out var name) && Eq(name, "auto") && probe.AtEnd)
                { reader = probe; return new CssImmediateValue(CssMultiColumnProperties.WidthProperty, CssLayoutLength.Auto); }
                return reader.TryReadLength(out var length) && reader.AtEnd && ColumnWidthLength(length)
                    ? ColumnLength(length) : null;
            } });
        CssPropertyRegistry.Register(new() { Name = "columns", Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                CssCompiledValue? width = null, count = null; var autos = 0; var tokens = 0;
                while (!reader.AtEnd)
                {
                    if (++tokens > 2) return false;
                    var probe = reader;
                    if (probe.TryReadIdent(out var name) && Eq(name, "auto")) { reader = probe; autos++; continue; }
                    probe = reader;
                    if (probe.TryReadInteger(out var number, minimum: 1))
                    { if (count is not null) return false; count = new CssImmediateValue(CssMultiColumnProperties.CountProperty, number); reader = probe; continue; }
                    probe = reader;
                    if (probe.TryReadLength(out var length) && ColumnWidthLength(length))
                    { if (width is not null) return false; width = ColumnLength(length); reader = probe; continue; }
                    return false;
                }
                if (tokens == 0 || autos > (width is null ? 1 : 0) + (count is null ? 1 : 0)) return false;
                output.Add(new("column-width", width ?? new CssImmediateValue(CssMultiColumnProperties.WidthProperty, CssLayoutLength.Auto), false));
                output.Add(new("column-count", count ?? new CssImmediateValue(CssMultiColumnProperties.CountProperty, 0), false)); return true;
            } });
        ColumnKeyword("column-fill", CssMultiColumnProperties.FillProperty, new Dictionary<string, object> {
            ["auto"] = CssColumnFill.Auto, ["balance"] = CssColumnFill.Balance, ["balance-all"] = CssColumnFill.BalanceAll });
        ColumnKeyword("column-span", CssMultiColumnProperties.SpanProperty, new Dictionary<string, object> { ["none"] = false, ["all"] = true });
        CssPropertyRegistry.Register(new() { Name = "column-rule-width", Kind = CssPropertyKind.Longhand, StorageProperty = CssMultiColumnProperties.RuleWidthProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => TryReadBorderWidth(ref reader, out var width) && reader.AtEnd ? ColumnRuleWidth(width) : null });
        CssPropertyRegistry.Register(new() { Name = "column-rule-style", Kind = CssPropertyKind.Longhand, StorageProperty = CssMultiColumnProperties.RuleStyleProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => TryReadBorderStyle(ref reader, out var style) && reader.AtEnd ? new CssImmediateValue(CssMultiColumnProperties.RuleStyleProperty, style) : null });
        CssPropertyRegistry.Register(new() { Name = "column-rule-color", Kind = CssPropertyKind.Longhand, StorageProperty = CssMultiColumnProperties.RuleColorProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) => TryReadBorderColor(ref reader, out var color) && reader.AtEnd ? ColumnRuleColor(color) : null });
        CssPropertyRegistry.Register(new() { Name = "column-rule", Kind = CssPropertyKind.Shorthand,
            Expand = (ref CssTokenReader reader, CssCompileContext _, List<CssCompiledDeclaration> output) =>
            {
                if (!TryReadBorderComponents(ref reader, out var parts)) return false;
                output.Add(new("column-rule-width", ColumnRuleWidth(parts.Width), false));
                output.Add(new("column-rule-style", new CssImmediateValue(CssMultiColumnProperties.RuleStyleProperty, parts.Style), false));
                output.Add(new("column-rule-color", ColumnRuleColor(parts.Color), false)); return true;
            } });
    }

    private static CssCompiledValue ColumnLength(CssLength length) => new CssDeferredValue("column-width", CssMultiColumnProperties.WidthProperty,
        (in CssApplyContext context, out object? value) =>
        {
            if (length.Expression is { } expression) { value = CssLayoutLength.Math(expression, context.Lengths); return true; }
            var valid = length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px) && px >= 0;
            value = CssLayoutLength.Px(Math.Max(0, px)); return valid;
        });
    private static bool ColumnWidthLength(CssLength length) => !length.UsesPercent &&
        (length.Expression is not null || length.Value >= 0 && (length.Unit != CssUnit.None || length.Value == 0));
    private static CssCompiledValue ColumnRuleWidth(CssLength length) => new CssDeferredValue("column-rule-width", CssMultiColumnProperties.RuleWidthProperty,
        (in CssApplyContext context, out object? value) => { var valid = length.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var px) && px >= 0; value = Math.Max(0, px); return valid; });
    private static CssCompiledValue ColumnRuleColor(CssBorderColorPart color) => color.Deferred is not null || color.CurrentColor
        ? new CssContextualColorValue("column-rule-color", color.Deferred ?? "currentColor")
        : new CssImmediateValue(CssMultiColumnProperties.RuleColorProperty, FreezeIfPossible(new SolidColorBrush(color.Color)));
    private static void ColumnKeyword(string name, DependencyProperty property, Dictionary<string, object> values) => CssPropertyRegistry.Register(new()
    { Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
      Parse = (ref CssTokenReader reader, CssCompileContext _) => reader.TryReadIdent(out var token) && reader.AtEnd && values.TryGetValue(token.ToString().ToLowerInvariant(), out var value)
        ? new CssImmediateValue(property, value) : null });
}
