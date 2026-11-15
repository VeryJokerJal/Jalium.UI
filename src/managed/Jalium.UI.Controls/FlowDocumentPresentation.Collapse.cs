using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    private sealed record CollapsedTablePlan(Table Table, CssCollapsedTableBorders Borders, IReadOnlyList<CssTableTracks.Cell> Cells);
    private sealed record CollapsedTableFrame(CollapsedTablePlan Plan, CssTableTracks.Axis Horizontal, CssTableTracks.Axis Vertical, Point Origin,
        Rect? Clip = null, Point Projection = default, IReadOnlyDictionary<DependencyObject, Rect>? Geometry = null);
    private readonly Dictionary<Table, CollapsedTablePlan> _collapsedTablePlans = [];
    private readonly Dictionary<TableCell, Thickness> _collapsedCellBorders = [];
    private readonly List<CollapsedTableFrame> _collapsedTableFrames = [];
    private Border? _collapsedBorderPainter;

    private CollapsedTablePlan? CollapsedPlan(Table table)
    {
        if (!CssTableProperties.Collapsed(table)) return null;
        if (_collapsedTablePlans.TryGetValue(table, out var plan)) return plan;
        var (rows, cells, count) = DocumentTableTracks(table);
        var borders = new CssCollapsedTableBorders(rows.Length, count, table.FlowDirection == FlowDirection.RightToLeft, cells);
        borders.Add(table, CssCollapsedTableBorders.Kind.Table, 0, 0, rows.Length, count);
        for (var column = 0; column < DisplayColumns(table).Length; column++)
            borders.Add(DisplayColumns(table)[column], CssCollapsedTableBorders.Kind.Column, 0, column, rows.Length, 1);
        var at = 0;
        foreach (var group in DocumentTableGroups(table).Ordered)
        {
            if (!_documentAnonymousTableBoxes.Contains(group)) borders.Add(group, CssCollapsedTableBorders.Kind.RowGroup, at, 0, BoxRows(group).Length, count);
            foreach (var row in BoxRows(group)) borders.Add(row, CssCollapsedTableBorders.Kind.Row, at++, 0, 1, count);
        }
        foreach (var cell in cells) borders.Add(cell.Source, CssCollapsedTableBorders.Kind.Cell, cell.Row, cell.Column, cell.RowSpan, cell.ColumnSpan);
        foreach (var cell in cells) _collapsedCellBorders[(TableCell)cell.Source] = borders.CellInsets(cell);
        _collapsedTablePlans[table] = plan = new(table, borders, cells); return plan;
    }

    private Thickness UsedTableBorder(Block block) => block is Table table && CollapsedPlan(table) is { } plan ? plan.Borders.TableInsets : block.BorderThickness;
    private Thickness UsedCellBorder(TableCell cell) => _collapsedCellBorders.GetValueOrDefault(cell, cell.BorderThickness);

    private void ClearCollapsedTables()
    { _collapsedTablePlans.Clear(); _collapsedCellBorders.Clear(); _collapsedTableFrames.Clear(); _repeatableTables.Clear(); _tableVisibility.Clear(); _tableWidthReduction.Clear(); _clippedTableLines.Clear(); }

    private void FreezeCollapsedColumnGeometry()
    {
        for (var index = 0; index < _collapsedTableFrames.Count; index++)
        {
            var frame = _collapsedTableFrames[index]; if (frame.Geometry is not null) continue;
            _collapsedTableFrames[index] = frame with { Geometry = _tableGeometry.Where(pair => Within(pair.Key, frame.Plan.Table)).ToDictionary() };
        }
    }

    protected override void OnPostRender(DrawingContext context)
    {
        base.OnPostRender(context);
        if (_document is null || _pageBlank || _collapsedTableFrames.Count == 0) return;
        var clip = GetChildLayoutClip(); if (clip is not null) context.PushClip(clip);
        try
        {
            _collapsedBorderPainter ??= new Border(); _collapsedBorderPainter.SetRootDpi(DpiScale);
            var p = EffectivePagePadding;
            var dy = p.Top - _pageTop + _pageRepeatedHeaderHeight;
            Rect At(Rect bounds) => new(bounds.X + p.Left, bounds.Y + dy, bounds.Width, bounds.Height);
            foreach (var frame in _collapsedTableFrames)
            {
                var geometry = frame.Geometry ?? _tableGeometry;
                Rect Projected(Rect bounds) => At(Shift(bounds, frame.Projection.X, frame.Projection.Y));
                var visibility = _tableVisibility[frame.Plan.Table]; var projected = frame.Plan.Borders.Project(visibility);
                if (frame.Clip is { } frameClip) context.PushClip(new RectangleGeometry(At(frameClip)));
                try
                {
                    if (geometry.TryGetValue(frame.Plan.Table, out var table))
                    {
                        CssBorderImagePainter.CollapsedImage(this, frame.Plan.Table, context, Projected(table), frame.Plan.Borders.TableInsets, cell: false, center: false, empty: !frame.Plan.Borders.HasGrid);
                        projected.DrawEmpty(context, Projected(table), _collapsedBorderPainter);
                    }
                    projected.Draw(context, CssTableVisibility.Compact(frame.Horizontal, visibility.Columns), CssTableVisibility.Compact(frame.Vertical, visibility.Rows),
                        new(frame.Origin.X + p.Left, frame.Origin.Y + dy), _collapsedBorderPainter);
                    foreach (var cell in frame.Plan.Cells.Where(cell => !visibility.Removed(cell) && CssTableVisibility.Visible(cell.Source)))
                        if (geometry.TryGetValue(cell.Source, out var bounds))
                            CssBorderImagePainter.CollapsedImage(this, cell.Source, context, Projected(bounds), frame.Plan.Borders.CellInsets(cell), cell: true, center: false);
                }
                finally { if (frame.Clip is not null) context.Pop(); }
            }
        }
        finally { if (clip is not null) context.Pop(); }
    }

    private void PaintCollapsedCenter(DependencyObject source, DrawingContext context, Rect bounds)
    {
        if (source is Table table && CollapsedPlan(table) is { } plan)
            CssBorderImagePainter.CollapsedImage(this, source, context, bounds, plan.Borders.TableInsets, cell: false, center: true, empty: !plan.Borders.HasGrid);
        else if (source is TableCell cell)
            CssBorderImagePainter.CollapsedImage(this, source, context, bounds, UsedCellBorder(cell), cell: true, center: true);
    }
}
