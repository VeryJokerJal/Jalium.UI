using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private bool DocumentTableAutoWidth(Table table, double basis)
    {
        if (_documentAnonymousTableBoxes.Contains(table)) return true;
        if (table.HasLocalOrAnimatedValue(FrameworkElement.WidthProperty)) return false;
        var node = CssNode.Get(table);
        var value = DocumentSizeValue(table, FrameworkElement.WidthProperty, node.CssLayout?.Width ?? default);
        if (value.IsSizing) return value.SizingBasis.IsAuto;
        if (value.IsSet && !value.IsAuto) return !double.IsFinite(value.Resolve(basis, double.NaN));
        return node.CssRuntimeState?.Applied?.ContainsKey(FrameworkElement.WidthProperty) == true &&
            !double.IsFinite(CssTableProperties.SizeOf(table, true, basis));
    }

    private (TableRow[] Rows, List<CssTableTracks.Cell> Cells, int Count) DocumentTableTracks(Table table)
    {
        var groups = DocumentTableGroups(table).Ordered;
        var rows = groups.SelectMany(BoxRows).ToArray();
        var specifications = groups.SelectMany(group => { var rendered=BoxRows(group);return rendered.Select((row,index)=>
            (IReadOnlyList<(DependencyObject Source,int Columns,int Rows)>)BoxCells(row).Select(cell=>
                ((DependencyObject)cell,cell.ColumnSpan,Math.Min(Math.Max(1,cell.RowSpan),rendered.Length-index))).ToArray()); }).ToArray();
        var (cells, count) = CssTableTracks.Place(specifications);
        var cssAuto = DocumentTableAutoWidth(table, 0);
        var fixedColumns = !cssAuto && (CssTableLayoutMode)table.GetValue(CssTableProperties.LayoutProperty)! == CssTableLayoutMode.Fixed;
        return (rows, cells, CssTableTracks.MergeColumns(cells, count, DisplayColumns(table).Length, fixedColumns));
    }

    private void MeasureDocumentTableCells(IReadOnlyList<CssTableTracks.Cell> cells, double basis,
        Func<IEnumerable<DependencyObject>, (double Min, double Max)> widths)
    {
        foreach (var track in cells)
        {
            var cell = (TableCell)track.Source; var pad = CellPadding(cell, basis); var border = UsedCellBorder(cell);
            var content = widths(CellNodes(cell)); var inset = pad.Left + pad.Right + border.Left + border.Right;
            track.Min = content.Min + inset; track.Max = content.Max + inset;
            track.SpecifiedWidth = CssTableTracks.WidthOf(cell, inset);
            var state = CssNode.Get(cell).CssLayout;
            var sizing = DocumentSizeValue(cell, FrameworkElement.WidthProperty, state?.Width ?? default);
            var preferred = sizing.IsSizing ? DocumentLimit(cell, FrameworkElement.WidthProperty, sizing, basis,
                double.NaN, inset, state is { BoxSizing: CssBoxSizing.ContentBox }, content, content.Max + inset, basis)
                : CssTableProperties.SizeOf(cell, true, basis);
            if (!track.SpecifiedWidth.Percentage && double.IsFinite(preferred)) track.Preferred = preferred +
                (!sizing.IsSizing && state is { BoxSizing: CssBoxSizing.ContentBox } ? inset : 0);
        }
    }

    private CssTableTracks.Column[] DocumentTableColumns(Table table, double basis) => DisplayColumns(table).Select(column =>
    {
        var css = CssTableProperties.SizeOf(column, true, basis);
        var state = CssNode.Get(column).CssLayout;
        var min = !column.HasLocalOrAnimatedValue(FrameworkElement.MinWidthProperty) && state?.MinWidth is { IsSet: true } minL ? minL.Resolve(basis, 0) : (double)column.GetValue(FrameworkElement.MinWidthProperty)!;
        var max = !column.HasLocalOrAnimatedValue(FrameworkElement.MaxWidthProperty) && state?.MaxWidth is { IsSet: true } maxL ? maxL.Resolve(basis, double.PositiveInfinity) : (double)column.GetValue(FrameworkElement.MaxWidthProperty)!;
        min = double.IsFinite(min) ? Math.Max(0, min) : 0;
        max = double.IsFinite(max) ? Math.Max(min, max) : double.PositiveInfinity;
        if (!column.HasLocalOrAnimatedValue(TableColumn.WidthProperty) &&
            (column.HasValueAboveInherited(FrameworkElement.WidthProperty) || min > 0 || double.IsFinite(max)))
            return new CssTableTracks.Column(css, Request: CssTableTracks.WidthOf(column), Min: min, Max: max);
        var width = column.Width;
        return new CssTableTracks.Column(width.IsAbsolute ? width.Value : double.NaN, width.IsStar ? width.Value : 0, Min: min, Max: max);
    }).ToArray();

    private (double Min, double Max) NestedTableIntrinsicWidths(Table table, Func<IEnumerable<DependencyObject>, (double Min, double Max)> widths)
    {
        var collapsed = CssTableProperties.Collapsed(table); var border = UsedTableBorder(table);
        var (rows, cells, count) = DocumentTableTracks(table); var spacing = CssTableProperties.Spacing(table);
        MeasureDocumentTableCells(cells, double.NaN, widths);
        var columns = DocumentTableColumns(table, double.NaN);
        // The containing cell depends on this contribution. Column percentages
        // must not enlarge that cyclic max-content estimate; used layout resolves
        // them again after the outer cell has a definite width.
        var intrinsic = CssTableTracks.IntrinsicColumns(rows.Length == 0 ? 0 : count, spacing.Width, columns, cells, percentages: false);
        var padding = collapsed ? default : DocumentEdges(table, false, double.NaN);
        var margin = DocumentEdges(table, true, double.NaN);
        var insets = padding.Left + padding.Right + border.Left + border.Right;
        var margins = margin.Left + margin.Right;
        var fixedLayout = double.IsFinite(CssTableProperties.SizeOf(table, true, double.NaN)) &&
            (CssTableLayoutMode)table.GetValue(CssTableProperties.LayoutProperty)! == CssTableLayoutMode.Fixed;
        var hidden = Enumerable.Range(0, count).Select(index => index < DisplayColumns(table).Length && CssTableVisibility.Collapsed(DisplayColumns(table)[index])).ToArray();
        double Contribution(double content)
        {
            var framed = DocumentIntrinsicWidths(table, content, content, border, collapsed).Min;
            if (rows.Length == 0 || count == 0) return framed;
            var axis = CssTableTracks.Columns(count, Math.Max(0, framed - insets - margins), spacing.Width, fixedLayout, columns, cells);
            return CssTableVisibility.Project(axis, hidden, spacing.Width).Extent + insets + margins;
        }
        var min = Contribution(intrinsic.Min); return (min, Math.Max(min, Contribution(intrinsic.Max)));
    }
}
