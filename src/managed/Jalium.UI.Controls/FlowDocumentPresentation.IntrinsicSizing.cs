using Jalium.UI.Documents;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private readonly Dictionary<DependencyObject, (double Min, double Max)> _intrinsicDocumentWidths = [];

    private (double Min, double Max) DocumentContentWidths(DependencyObject source)
    {
        if (CssDisplayProperties.SuppressesBoxTree(source)) return default;
        if (_intrinsicDocumentWidths.TryGetValue(source, out var cached)) return cached;
        (double Min, double Max) widths = source switch
        {
            Paragraph paragraph => ParagraphWidths(paragraph),
            FlowDocument document => DocumentBlockWidths(document.Blocks),
            Section section => DocumentBlockWidths(section.Blocks),
            ListItem item => DocumentBlockWidths(item.Blocks),
            TableCell cell => DocumentBlockWidths(CellNodes(cell)),
            DocList list => ListWidths(list),
            Table table => TableWidths(table),
            _ => default,
        };
        _intrinsicDocumentWidths[source] = widths;
        return widths;

        (double Min, double Max) ParagraphWidths(Paragraph paragraph)
        {
            var view = _paragraphs.First(v => ReferenceEquals(v.Source, paragraph));
            var box = new CssLayoutBox(view.Text, 0); box.MeasureIntrinsicWidth();
            return (box.MinContent.Width, box.MaxContent.Width);
        }
        (double Min, double Max) ListWidths(DocList list)
            => DocumentBlockWidths(list.ListItems);
        (double Min, double Max) TableWidths(Table table)
        {
            var (rows, cells, count) = DocumentTableTracks(table);
            MeasureDocumentTableCells(cells, double.NaN, DocumentBlockWidths);
            return CssTableTracks.IntrinsicColumns(rows.Length == 0 ? 0 : count,
                CssTableProperties.Spacing(table).Width, DocumentTableColumns(table, double.NaN), cells, percentages: false);
        }
    }

    private (double Min, double Max) DocumentBlockWidths(IEnumerable<DependencyObject> blocks)
    {
        var min = 0d; var max = 0d;
        var pending = new List<Paragraph>();
        void Include((double Min, double Max) widths)
        { min = Math.Max(min, widths.Min); max = Math.Max(max, widths.Max); }
        void Flush()
        {
            if (pending.Count == 0) return;
            Include(DocumentContentsWidths(pending)); pending.Clear();
        }
        foreach (var block in ContentsFlowNodes(blocks))
        {
            if(CssDisplayProperties.SuppressesBoxTree(block))continue;
            if (block is Paragraph paragraph && DocumentContents(block)) { pending.Add(paragraph); continue; }
            Flush();
            (double Min, double Max) framed;
            if (block is Table table) framed = NestedTableIntrinsicWidths(table, DocumentBlockWidths);
            else
            {
                var content = DocumentContentWidths(block);
                var border = block is Block normal ? UsedTableBorder(normal) : ((ListItem)block).BorderThickness;
                framed = DocumentIntrinsicWidths(block, content.Min, content.Max, border, false);
                if (block is ListItem { List: { } list }) framed = (framed.Min + NativeListIndent(list), framed.Max + NativeListIndent(list));
            }
            Include(framed);
        }
        Flush();
        return (min, max);
    }

    private static CssLayoutLength DocumentSizeValue(DependencyObject source, DependencyProperty property, CssLayoutLength declared)
    {
        var field = property == FrameworkElement.WidthProperty ? CssLayoutSlotField.Width
            : property == FrameworkElement.HeightProperty ? CssLayoutSlotField.Height
            : property == FrameworkElement.MinWidthProperty ? CssLayoutSlotField.MinWidth
            : property == FrameworkElement.MinHeightProperty ? CssLayoutSlotField.MinHeight
            : property == FrameworkElement.MaxWidthProperty ? CssLayoutSlotField.MaxWidth : CssLayoutSlotField.MaxHeight;
        return CssSizingPresentation.Presented(source, (int)field, declared);
    }

    private static double ResolveDocumentSizing(CssLayoutLength value, double basis, double fallback, double insets,
        bool contentBox, (double Min, double Max) content, double automatic, double stretch)
    {
        double Quantity(double border) => contentBox ? Math.Max(0, border - insets) : border;
        double Natural(bool minimum) => Quantity((minimum ? content.Min : content.Max) + insets);
        double ResolveBasis(CssLayoutLength sizing)
        {
            if (sizing.IsAuto) return fallback == 0 ? 0 : Quantity(automatic);
            if (sizing.Kind == CssLayoutLength.KindMinContent) return Natural(true);
            if (sizing.Kind is CssLayoutLength.KindMaxContent or CssLayoutLength.KindContent) return Natural(false);
            if (sizing.Kind == CssLayoutLength.KindStretch)
                return double.IsFinite(stretch) ? Quantity(stretch) : Quantity(automatic);
            if (sizing.Kind is CssLayoutLength.KindFitContent or CssLayoutLength.KindFitFunction)
            {
                var limit = sizing.Kind == CssLayoutLength.KindFitFunction
                    ? sizing.FitArgument.Resolve(basis, double.PositiveInfinity) : Quantity(stretch);
                return Math.Clamp(double.IsNaN(limit) ? Natural(false) : limit, Natural(true), Natural(false));
            }
            var result = sizing.Resolve(fallback == 0 ? CssIntrinsicPercentage.MinimumBasis(basis) : basis, double.NaN);
            return !double.IsNaN(result) ? result : fallback == 0 ? 0
                : double.IsPositiveInfinity(fallback) ? fallback : Quantity(automatic);
        }
        var quantity = value.Calculation is { } calculation ? calculation.Evaluate(ResolveBasis, basis, fallback) : ResolveBasis(value);
        return double.IsNaN(quantity) ? fallback : Math.Max(insets, Math.Max(0, quantity) + (contentBox ? insets : 0));
    }

    private static bool DocumentDefiniteSize(CssLayoutLength value, double basis)
    {
        var behavior = value.SizingBasis;
        return behavior.Kind == CssLayoutLength.KindStretch ? double.IsFinite(basis)
            : !behavior.IsAuto && !behavior.IsSizing && double.IsFinite(behavior.Resolve(basis, double.NaN));
    }
}
