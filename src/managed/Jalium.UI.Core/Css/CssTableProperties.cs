using Jalium.UI.Documents;

namespace Jalium.UI.Styling;

internal enum CssTableLayoutMode { Auto, Fixed }
internal enum CssCaptionSide { Top, Bottom }

internal static class CssTableProperties
{
    internal static readonly DependencyProperty LayoutProperty = DependencyProperty.RegisterAttached(
        "CssTableLayout", typeof(CssTableLayoutMode), typeof(CssTableProperties), new PropertyMetadata(CssTableLayoutMode.Auto, Changed));
    internal static readonly DependencyProperty SpacingProperty = DependencyProperty.RegisterAttached(
        "CssBorderSpacing", typeof(Size), typeof(CssTableProperties), new PropertyMetadata(default(Size), Changed, null, inherits: true));
    internal static readonly DependencyProperty EmptyCellsProperty = DependencyProperty.RegisterAttached(
        "CssEmptyCells", typeof(bool), typeof(CssTableProperties), new PropertyMetadata(true, Changed, null, inherits: true));
    internal static readonly DependencyProperty CaptionSideProperty = DependencyProperty.RegisterAttached(
        "CssCaptionSide", typeof(CssCaptionSide), typeof(CssTableProperties), new PropertyMetadata(CssCaptionSide.Top, Changed, null, inherits: true));
    internal static readonly DependencyProperty CollapseProperty = DependencyProperty.RegisterAttached(
        "CssBorderCollapse", typeof(bool), typeof(CssTableProperties), new PropertyMetadata(false, Changed, null, inherits: true));

    internal static bool Collapsed(DependencyObject source) => (bool)source.GetValue(CollapseProperty)!;

    private static void Changed(DependencyObject source, DependencyPropertyChangedEventArgs change)
    {
        if (source is TextElement text) text.NotifyTextContentChanged();
        else if (source is FlowDocument document) document.NotifyPaginationStyleChanged();
        else if (source is UIElement visual) { visual.InvalidateMeasure(); visual.InvalidateVisual(); }
        if (change.Property == CaptionSideProperty && source is FrameworkElement element) CssTableLayout.CaptionSideChanged(element);
        if (change.Property == CollapseProperty && source is FrameworkElement table) CssTableLayout.CollapseChanged(table);
    }

    internal static Size Spacing(Table table)
    {
        if (Collapsed(table)) return default;
        for (var node = CssNode.Get(table); node is not null; node = node.FrameworkParent)
            if (node.Target.HasValueAboveInherited(SpacingProperty)) return (Size)table.GetValue(SpacingProperty)!;
        var native = double.IsFinite(table.CellSpacing) ? Math.Max(0, table.CellSpacing) : 0;
        return new(native, native);
    }

    internal static double SizeOf(DependencyObject source, bool horizontal, double basis)
    {
        var state = CssNode.Get(source).CssLayout;
        var length = horizontal ? state?.Width : state?.Height;
        var property = horizontal ? FrameworkElement.WidthProperty : FrameworkElement.HeightProperty;
        var value = !source.HasLocalOrAnimatedValue(property) && length is { IsSet: true } size ? size.Resolve(basis, double.NaN) :
            (double)source.GetValue(property)!;
        return double.IsFinite(value) ? Math.Max(0, value) : double.NaN;
    }

    internal static void Register()
    {
        Keyword("table-layout", LayoutProperty, "auto", CssTableLayoutMode.Auto, "fixed", CssTableLayoutMode.Fixed);
        Keyword("empty-cells", EmptyCellsProperty, "show", true, "hide", false);
        Keyword("caption-side", CaptionSideProperty, "top", CssCaptionSide.Top, "bottom", CssCaptionSide.Bottom);
        Keyword("border-collapse", CollapseProperty, "separate", false, "collapse", true);
        CssPropertyRegistry.Register(new()
        {
            Name = "border-spacing", Kind = CssPropertyKind.Longhand, StorageProperty = SpacingProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!ReadLength(ref reader, out var x)) return null;
                var y = x;
                if (!reader.AtEnd && !ReadLength(ref reader, out y) || !reader.AtEnd) return null;
                if (x.IsAbsolute && y.IsAbsolute)
                    return new CssImmediateValue(SpacingProperty, new Size(Math.Max(0, x.ToPxAbsolute()), Math.Max(0, y.ToPxAbsolute())));
                return new CssDeferredValue("border-spacing", SpacingProperty, (in CssApplyContext context, out object? value) =>
                {
                    var vertical = 0d;
                    var valid = x.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out var horizontal) &&
                        y.TryResolve(context.Lengths, CssPercentBasis.NotSupported, out vertical);
                    value = valid ? new Size(Math.Max(0, horizontal), Math.Max(0, vertical)) : null;
                    return valid;
                });
            },
        });
    }

    private static bool ReadLength(ref CssTokenReader reader, out CssLength length)
        => reader.TryReadLength(out length) && length.Unit != CssUnit.Percent && length.Expression?.UsesPercent != true &&
            (length.Expression is not null || length.Value >= 0);

    private static void Keyword<T>(string name, DependencyProperty property, string a, T first, string b, T second)
        => CssPropertyRegistry.Register(new()
        {
            Name = name, Kind = CssPropertyKind.Longhand, StorageProperty = property,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var token) || !reader.AtEnd) return null;
                if (token.Equals(a, StringComparison.OrdinalIgnoreCase)) return new CssImmediateValue(property, first!);
                if (token.Equals(b, StringComparison.OrdinalIgnoreCase)) return new CssImmediateValue(property, second!);
                return null;
            },
        });
}
