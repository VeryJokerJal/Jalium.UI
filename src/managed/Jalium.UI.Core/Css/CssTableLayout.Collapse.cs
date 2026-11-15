using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal sealed partial class CssTableLayout
{
    private bool _collapseProbe;
    private CssCollapsedTableBorders? _activeBorders;
    private readonly Dictionary<FrameworkElement, Action<DependencyProperty, object?, object?>> _borderObservers = [];
    private Border? _collapsedBorderPainter;

    private Model TrackModel(out List<CssTableTracks.Cell> tracks, out int count)
    {
        var model = BuildModel();
        var groupEnds = new int[model.Rows.Count];
        for (var row = model.Rows.Count - 1; row >= 0; row--)
            groupEnds[row] = row + 1 < model.Rows.Count && ReferenceEquals(model.Rows[row].Group, model.Rows[row + 1].Group) ? groupEnds[row + 1] : row + 1;
        var specifications = model.Rows.Select((row, index) => (IReadOnlyList<(DependencyObject Source, int Columns, int Rows)>)row.Cells.Select(cell =>
            ((DependencyObject)cell.Element, cell.Anonymous ? 1 : Grid.GetColumnSpan(cell.Element), cell.Anonymous ? 1 : Math.Min(Grid.GetRowSpan(cell.Element), groupEnds[index] - index))).ToArray()).ToArray();
        (tracks, count) = CssTableTracks.Place(specifications);
        var fixedColumns = owner is not CssTableAnonymousBox &&
            (CssTableLayoutMode)StyleOwner.GetValue(CssTableProperties.LayoutProperty)! == CssTableLayoutMode.Fixed &&
            CssTableTracks.WidthOf(StyleOwner).Specified;
        count = CssTableTracks.MergeColumns(tracks, count, model.Columns.Sum(column => column.Count), fixedColumns);
        model.Missing = CssTableTracks.MissingCells(model.Rows.Count, count, tracks);
        var byElement = model.Cells.ToDictionary(cell => cell.Element);
        foreach (var track in tracks) byElement[(FrameworkElement)track.Source].Track = track;
        if (!CssTableProperties.Collapsed(StyleOwner)) return model;
        var borders = model.Borders = new(model.Rows.Count, count, StyleOwner.FlowDirection == FlowDirection.RightToLeft, tracks);
        if (owner is not CssTableAnonymousBox) borders.Add(owner, CssCollapsedTableBorders.Kind.Table, 0, 0, model.Rows.Count, count);
        foreach (var group in model.Columns.Where(column => column.Group is not null).GroupBy(column => column.Group!))
            borders.Add(group.Key, CssCollapsedTableBorders.Kind.ColumnGroup, 0, group.Min(column => column.First), model.Rows.Count, group.Sum(column => column.Count));
        foreach (var column in model.Columns)
            if (!ReferenceEquals(column.Source, column.Group)) borders.Add(column.Source, CssCollapsedTableBorders.Kind.Column, 0, column.First, model.Rows.Count, column.Count);
        foreach (var group in model.Rows.Select((row, index) => (row, index)).Where(item => item.row.Group is not null).GroupBy(item => item.row.Group!))
            borders.Add(group.Key, CssCollapsedTableBorders.Kind.RowGroup, group.Min(item => item.index), 0, group.Count(), count);
        for (var row = 0; row < model.Rows.Count; row++)
            if (model.Rows[row].Source is { } source) borders.Add(source, CssCollapsedTableBorders.Kind.Row, row, 0, 1, count);
        foreach (var cell in model.Cells)
            if (!cell.Anonymous) borders.Add(cell.Element, CssCollapsedTableBorders.Kind.Cell, cell.Track.Row, cell.Track.Column, cell.Track.RowSpan, cell.Track.ColumnSpan);
        return model;
    }

    internal static bool TryCollapsedBorder(FrameworkElement element, out Thickness border)
    {
        if (Assigned(element) is { Cell: true, CollapsedBorder: true } cell) { border = cell.Border; return true; }
        if (element.CssDisplayMode == CssDisplayMode.Table && CssTableProperties.Collapsed(element))
        {
            var table = CssDisplayLayout.TableFor(element);
            if (table._computing > 0) { border = table._activeBorders?.TableInsets ?? default; return true; }
            if (!table._collapseProbe)
            {
                table._collapseProbe = true;
                try { border = table.TrackModel(out _, out _).Borders?.TableInsets ?? default; return true; }
                finally { table._collapseProbe = false; }
            }
        }
        border = default; return false;
    }

    private static void SetCollapsedBorder(FrameworkElement element, Allocation state, Thickness? border)
    {
        if (state.CollapsedBorder == border.HasValue && state.Border == border.GetValueOrDefault()) return;
        state.CollapsedBorder = border.HasValue; state.Border = border.GetValueOrDefault();
        // Winning borders are intrinsic box inputs, unlike a new allocation of the same box.
        // Ancestor flow caches must not reuse widths measured with the old border model.
        element.InvalidateMeasure();
    }

    internal static bool CollapsedDecoration(DependencyObject source)
    {
        if (source is not FrameworkElement element) return CssCollapsedTableBorders.DocumentPart(source);
        if (Assigned(element) is { Caption: false } allocation && allocation.Layout.TryGetTarget(out var layout))
            return layout._model?.Borders is not null;
        if (element.CssDisplayMode == CssDisplayMode.Table) return CssTableProperties.Collapsed(element);
        if (element.CssDisplayMode is not (CssDisplayMode.TableColumn or CssDisplayMode.TableColumnGroup)) return false;
        for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
        {
            if (parent.CssDisplayMode == CssDisplayMode.Table) return CssTableProperties.Collapsed(parent);
            foreach (var table in EnumerateFlowTables(parent))
                if (CssDisplayLayout.TableFor(table)._model is { Borders: not null } model && model.Columns.Any(column => ReferenceEquals(column.Source, element) || ReferenceEquals(column.Group, element))) return true;
        }
        return false;
    }

    internal static bool CollapsedStructure(DependencyObject source) => CollapsedDecoration(source) &&
        (source is FrameworkElement element ? element.CssDisplayMode != CssDisplayMode.Table && Assigned(element) is not { Cell: true } : source is not (Jalium.UI.Documents.Table or Jalium.UI.Documents.TableCell));

    internal static bool CollapsedImageBox(FrameworkElement element, ref Rect bounds, ref Thickness border)
    {
        if (!CollapsedDecoration(element) || CollapsedStructure(element)) return false;
        var cell = Assigned(element) is { Cell: true };
        if (cell) bounds = new(bounds.X - border.Left, bounds.Y - border.Top, bounds.Width + border.Left + border.Right, bounds.Height + border.Top + border.Bottom);
        var scale = !cell && CssDisplayLayout.TryTableFor(element, out var table) && table?._model?.Borders is { HasGrid: false } ? 1 : 2;
        border = new(border.Left * scale, border.Top * scale, border.Right * scale, border.Bottom * scale); return true;
    }

    internal static bool SuppressBorder(Visual visual) => SuppressFrame(visual) || CollapsedDecoration(visual);

    internal static void CollapseChanged(FrameworkElement element)
    {
        if (Assigned(element)?.Layout.TryGetTarget(out var layout) == true) layout.InvalidateOwner();
        if (element.CssDisplayMode == CssDisplayMode.Table) { element.InvalidateMeasure(); element.InvalidateVisual(); }
        foreach (var child in element.SnapshotAttachedVisualChildren().OfType<FrameworkElement>())
            if (!child.HasValueAboveInherited(CssTableProperties.CollapseProperty)) CollapseChanged(child);
    }

    private void ObserveBorders(Model? model)
    {
        var current = model?.Borders is null ? [] : model.Cells.Select(cell => cell.Element).Concat(model.Rows.SelectMany(row => new[] { row.Source, row.Group }).OfType<FrameworkElement>()).Append(owner).ToHashSet();
        foreach (var old in _borderObservers.Keys.Where(element => !current.Contains(element)).ToArray())
        { old.PropertyChangedInternal -= _borderObservers[old]; _borderObservers.Remove(old); }
        foreach (var element in current.Where(element => !_borderObservers.ContainsKey(element)))
        {
            var weak = new WeakReference<CssTableLayout>(this);
            Action<DependencyProperty, object?, object?> changed = (property, _, _) =>
            {
                if (property.ReadOnly || !weak.TryGetTarget(out var table)) return;
                if (table._computing > 0 && property.Name == "Padding" && !element.HasLocalOrAnimatedValue(property)) return;
                table.InvalidateOwner();
            };
            _borderObservers.Add(element, changed); element.PropertyChangedInternal += changed;
        }
    }

    private bool PaintCollapsedBorders(DrawingContext context, Point offset)
    {
        if (_model is not { Borders: { } borders } model) return false;
        _collapsedBorderPainter ??= new Border(); _collapsedBorderPainter.SetRootDpi(owner.DpiScale);
        if (owner is not CssTableAnonymousBox)
            CssBorderImagePainter.CollapsedImage(owner, owner, context, new(model.GridBounds.X + offset.X, model.GridBounds.Y + offset.Y, model.GridBounds.Width, model.GridBounds.Height), model.Insets, cell: false, center: false, empty: !borders.HasGrid);
        var projected = borders.Project(model.Visibility);
        projected.DrawEmpty(context, new(model.GridBounds.X + offset.X, model.GridBounds.Y + offset.Y, model.GridBounds.Width, model.GridBounds.Height), _collapsedBorderPainter);
        projected.Draw(context, CssTableVisibility.Compact(model.Horizontal, model.Visibility.Columns), CssTableVisibility.Compact(model.Vertical, model.Visibility.Rows),
            new(model.Insets.Left + offset.X, model.GridBounds.Top + model.Insets.Top + offset.Y), _collapsedBorderPainter);
        foreach (var cell in model.Cells.Where(cell => !model.Visibility.Removed(cell.Track) && CssTableVisibility.Visible(cell.Element)))
            CssBorderImagePainter.CollapsedImage(cell.Element, cell.Element, context, new(cell.Bounds.X + offset.X, cell.Bounds.Y + offset.Y, cell.Bounds.Width, cell.Bounds.Height), borders.CellInsets(cell.Track), cell: true, center: false);
        return true;
    }

    internal static void PaintFlowBorders(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement element || element is CssTableAnonymousBox) return;
        foreach (var table in EnumerateFlowTables(element))
            CssDisplayLayout.TableFor(table).PaintCollapsedBorders(context, table.AllocationOrigin);
    }
}
