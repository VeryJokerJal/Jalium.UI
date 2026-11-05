using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    internal readonly record struct TableCellLayout(TableCell Source, Rect Bounds, int Row, int Column, int RowSpan, int ColumnSpan);
    internal readonly record struct RepeatableTableInfo(
        Table Table,
        Rect TableBounds,
        TableRowGroup? Header,
        Rect HeaderBounds,
        TableRowGroup? Footer,
        Rect FooterBounds,
        double BodyTop,
        double BodyBottom);
    private readonly List<TableCellLayout> _tableCells = [];
    private readonly List<RepeatableTableInfo> _repeatableTables = [];
    private readonly Dictionary<DependencyObject, Rect> _tableGeometry = [];
    private readonly List<DependencyObject> _tableGeometryKeys = [];
    private readonly List<Line> _positionLines = [];
    internal IReadOnlyList<TableCellLayout> TableCells => _tableCells;
    internal IReadOnlyList<RepeatableTableInfo> RepeatableTables => _repeatableTables;
    internal bool HasTables => _tableGeometry.Count != 0;

    private sealed record CellContent(CssTableTracks.Cell Track, int FirstParagraph, int EndParagraph,
        int FirstLine, int EndLine, int FirstPaint, int EndPaint, int FirstFragment, int EndFragment, double Height,
        int FirstNestedCell, int EndNestedCell, DependencyObject[] NestedGeometry, int FirstCollapsedFrame, int EndCollapsedFrame);

    private static double DistanceToBox(Point point, Rect box)
    {
        var dx = point.X < box.Left ? box.Left - point.X : Math.Max(0, point.X - box.Right);
        var dy = point.Y < box.Top ? box.Top - point.Y : Math.Max(0, point.Y - box.Bottom);
        return dx * dx + dy * dy;
    }
    private static Brush? TableBackground(DependencyObject source) => source is TableColumn column ? column.Background : (Brush?)source.GetValue(TextElement.BackgroundProperty);
    private static Thickness TableBorder(DependencyObject source) => source switch { Block block => block.BorderThickness, TableCell cell => cell.BorderThickness, ListItem item => item.BorderThickness,
        FlowDocument document => (Thickness)document.GetValue(document.HasLocalOrAnimatedValue(Control.BorderThicknessProperty) ? Control.BorderThicknessProperty : CssBorderUsedThicknessProperties.ValueProperty)!, _ => default };
    private static Brush? TableBorderBrush(DependencyObject source) => source switch { Block block => block.BorderBrush, TableCell cell => cell.BorderBrush, ListItem item => item.BorderBrush,
        FlowDocument document => document.HasLocalOrAnimatedValue(Control.BorderBrushProperty) ? (Brush?)document.GetValue(Control.BorderBrushProperty)
            : (document.GetValue(CssBorderPaintProperties.ValueProperty) as CssBorderPaint)?.Top, _ => null };
    private static Thickness CellPadding(TableCell cell, double width)
        => DocumentEdges(cell, false, width);

    private void LayoutTable(Table table, double available, Thickness tablePadding, ref double y, ref int index,
        Action<IEnumerable<DependencyObject>, double, double, double> layout, Func<double, double> constrainAutoWidth, Func<double, double, double> placeWidth,
        DocumentHeight heightConstraint)
    {
        var startY = y;
        var collapsedPlan = CollapsedPlan(table);
        var spacing = CssTableProperties.Spacing(table);
        var (rows, cells, count) = DocumentTableTracks(table);
        var rowless = rows.Length == 0;
        var groups = DocumentTableGroups(table);
        var rowIndices = rows.Select((row, index) => (row, index)).ToDictionary(item => item.row, item => item.index);
        var visibility = TableVisibility(table, rows, count);
        var views = _paragraphs.ToDictionary(view => view.Source);
        foreach (var view in views.Values)
            for (DependencyObject? ancestor = view.Source.Parent; ancestor is FrameworkContentElement content; ancestor = content.Parent)
                if (content is TableCell cell && !DocumentContents(cell))
                {
                    if (!double.IsFinite(view.Source.LineHeight) && double.IsFinite(cell.LineHeight)) view.Text.LineHeight = cell.LineHeight;
                    if (!view.Source.HasValueAboveInherited(Block.TextAlignmentProperty) && cell.HasValueAboveInherited(TableCell.TextAlignmentProperty)) view.Text.TextAlignment = cell.TextAlignment;
                    break;
                }
        (double Min, double Max) Widths(IEnumerable<DependencyObject> blocks) => DocumentBlockWidths(blocks);
        var cssAuto = DocumentTableAutoWidth(table, available);
        var intrinsicBasis = cssAuto ? double.NaN : available;
        MeasureDocumentTableCells(cells, intrinsicBasis, Widths);
        var columns = rowless ? [] : DocumentTableColumns(table, intrinsicBasis);
        var fixedMode = !cssAuto && (CssTableLayoutMode)table.GetValue(CssTableProperties.LayoutProperty)! == CssTableLayoutMode.Fixed;
        if (cssAuto)
        {
            var (minimum, maximum) = CssTableTracks.IntrinsicColumns(rowless ? 0 : count, spacing.Width, columns, cells, double.IsFinite(available));
            available = Math.Max(minimum, constrainAutoWidth(Math.Min(maximum, available)));
        }
        foreach (var track in cells)
        {
            var cell = (TableCell)track.Source; var pad = CellPadding(cell, available); var border = UsedCellBorder(cell);
            track.SpecifiedWidth = CssTableTracks.WidthOf(cell, pad.Left + pad.Right + border.Left + border.Right);
        }
        var fullHorizontal = rowless ? new CssTableTracks.Axis(new double[count], new double[count], 0)
            : CssTableTracks.Columns(count, available, spacing.Width, fixedMode, columns, cells);
        var horizontal = rowless ? fullHorizontal : CssTableVisibility.Project(fullHorizontal, visibility.Columns, spacing.Width);
        var fullGridWidth = rowless || count == 0 ? available : fullHorizontal.Extent;
        var visibleWidth = rowless || count == 0 ? fullGridWidth : horizontal.Extent;
        var left = placeWidth(fullGridWidth, visibleWidth);
        // Percent padding belongs to the final, uncontracted table grid; hiding a
        // column must not reflow content in the remaining source cells.
        available = fullGridWidth;
        var reflow = cells.Any(track => NeedsCellHeightLayout(table, (TableCell)track.Source)) ? new TableCellLayoutPass(this) : null;
        var firstParagraph = index;
        var cellHeights = new Dictionary<TableCell, double>();
        var contents = new List<CellContent>();
        var groupFragments = new List<(TableRowGroup Group, int Fragment)>();
        var rowFragments = new Dictionary<TableRow, int>();
        CssTableTracks.Axis fullVertical = default, vertical = default;
        var minimumHeight = double.NaN;
        var cellsByRow = cells.ToLookup(cell => cell.Row);
        var missing = CssTableTracks.MissingCells(rows.Length, count, cells);
        var missingRows = missing.Select(slot => slot.Row).ToHashSet();
        bool ShowMissing(int row) => collapsedPlan is not null || (bool)rows[row].GetValue(CssTableProperties.EmptyCellsProperty)!;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass != 0)
            {
                reflow!.Restore(firstParagraph, index); index = firstParagraph;
                contents.Clear(); groupFragments.Clear(); rowFragments.Clear();
            }
            // Consume source paragraphs in their original order. Track indices
            // and the final cell placement use the promoted visual row order.
            foreach (var group in SourceTableGroups(table))
            {
                if(SkipDisplayNone(group,ref index,y))continue;
                var groupAt = _fragmentBoxes.Count; _fragmentBoxes.Add(new(group, _lines.Count, _lines.Count, 0, 0, 0)); groupFragments.Add((group, groupAt));
                foreach (var row in BoxRows(group))
                {
                    if(SkipDisplayNone(row,ref index,y))continue;
                    var rowAt = _fragmentBoxes.Count; _fragmentBoxes.Add(new(row, _lines.Count, _lines.Count, 0, 0, 0)); rowFragments.Add(row, rowAt);
                    foreach (var cell in BoxCells(row))
                    {
                        if(SkipDisplayNone(cell,ref index,y))continue;
                        ConsumeSkippedTableParagraphs(ref index, DocumentBoxSourceRange(cell).Start, y);
                        var track=cellsByRow[rowIndices[row]].Single(track=>ReferenceEquals(track.Source,cell)); var pad = CellPadding(cell, available); var border = UsedCellBorder(cell);
                        var width = fullHorizontal.Span(track.Column, track.ColumnSpan);
                        var firstP = index; var firstL = _lines.Count; var firstB = _blocks.Count; var firstF = _fragmentBoxes.Count;
                        _fragmentBoxes.Add(default);
                        // Reserve the cell's background layers before its descendants.
                        for (var i = 0; i < 3 + track.ColumnSpan; i++) _blocks.Add(new(cell, default, false));
                        var firstNested = _tableCells.Count; var firstGeometry = _tableGeometryKeys.Count; var firstCollapsedFrame = _collapsedTableFrames.Count;
                        y = pad.Top + border.Top;
                        var innerWidth = Math.Max(1, width - pad.Left - pad.Right - border.Left - border.Right);
                        var columnsForCell = HasOwnDocumentColumns(cell) ? CssDocumentColumns.Resolve(cell, innerWidth) : null;
                        var basis = cellHeights.GetValueOrDefault(cell, double.NaN);
                        var insets = pad.Top + pad.Bottom + border.Top + border.Bottom;
                        var cellHeight = double.IsFinite(basis) ? new DocumentHeight(basis + insets, insets, double.PositiveInfinity, insets) : (DocumentHeight?)null;
                        var cellScope = columnsForCell is { } cellConfig ? BeginColumnScope(cell, cellConfig, 0, y, index, height: cellHeight) : null;
                        layout(CellNodes(cell), 0, columnsForCell?.Width ?? innerWidth, basis);
                        if (cellScope is not null) FinishColumnScope(cellScope, ref y, index);
                        var height = y + pad.Bottom + border.Bottom;
                        var clampBox = new DocumentBox(default, pad, border, width);
                        CaptureDocumentClampBox(cell, clampBox, ResolveDocumentHeight(cell, clampBox, heightConstraint.ContentBasis), 0, height);
                        if (pass == 0)
                        {
                            var specified = DocumentLimit(cell, FrameworkElement.HeightProperty, CssNode.Get(cell).CssLayout?.Height ?? default,
                                double.NaN, double.NaN, insets, CssNode.Get(cell).CssLayout is { BoxSizing: CssBoxSizing.ContentBox },
                                (Math.Max(0, height - insets), Math.Max(0, height - insets)), height);
                            track.Height = HiddenEmptyCell(cell) ? 0 : Math.Max(height, double.IsFinite(specified) ? specified : 0);
                            track.SpecifiedHeight = HiddenEmptyCell(cell) ? default : CssTableTracks.HeightOf(cell, insets);
                            track.HasBaseline = !HiddenEmptyCell(cell) && index > firstP && CellAlignment(cell) == CssInlineAlignment.Baseline;
                            track.Baseline = HiddenEmptyCell(cell) ? 0 : index > firstP ? _paragraphs[firstP].Bounds.Top + _paragraphs[firstP].Text.GetFirstBaselineOffset(_paragraphs[firstP].Bounds.Height) : height - pad.Bottom - border.Bottom;
                            if (!double.IsFinite(track.Baseline)) track.Baseline = height - pad.Bottom - border.Bottom;
                        }
                        contents.Add(new(track, firstP, index, firstL, _lines.Count, firstB, _blocks.Count, firstF, _fragmentBoxes.Count, height,
                            firstNested, _tableCells.Count, _tableGeometryKeys.GetRange(firstGeometry, _tableGeometryKeys.Count - firstGeometry).ToArray(), firstCollapsedFrame, _collapsedTableFrames.Count));
                    }
                    _fragmentBoxes[rowAt] = _fragmentBoxes[rowAt] with { EndLine = _lines.Count };
                }
                _fragmentBoxes[groupAt] = _fragmentBoxes[groupAt] with { EndLine = _lines.Count };
            }
            ConsumeSkippedTableParagraphs(ref index, DocumentBoxSourceRange(table).End, y);
            var baselines = new double[rows.Length];
            foreach (var track in cells)
                if (!HiddenEmptyCell((TableCell)track.Source) && CellAlignment(track.Source) == CssInlineAlignment.Baseline) baselines[track.Row] = Math.Max(baselines[track.Row], track.Baseline);
            foreach (var track in cells)
                if (!HiddenEmptyCell((TableCell)track.Source) && CellAlignment(track.Source) == CssInlineAlignment.Baseline) track.OffsetY = Math.Max(0, baselines[track.Row] - track.Baseline);
            var presentChanges = new int[rows.Length + 1]; var visibleChanges = new int[rows.Length + 1];
            foreach (var cell in cells)
            {
                presentChanges[cell.Row]++; presentChanges[cell.Row + cell.RowSpan]--;
                if (!HiddenEmptyCell((TableCell)cell.Source)) { visibleChanges[cell.Row]++; visibleChanges[cell.Row + cell.RowSpan]--; }
            }
            var collapsed = new bool[rows.Length]; var present = 0; var visible = 0;
            for (var row = 0; row < rows.Length; row++)
            {
                present += presentChanges[row]; visible += visibleChanges[row];
                collapsed[row] = visible == 0 && (present > 0 || !(bool)rows[row].GetValue(CssTableProperties.EmptyCellsProperty)!) &&
                    !(missingRows.Contains(row) && ShowMissing(row));
            }
            minimumHeight = heightConstraint.ContentBasis;
            if (heightConstraint.ContentMinimum > 0)
                minimumHeight = Math.Max(double.IsFinite(minimumHeight) ? minimumHeight : 0, heightConstraint.ContentMinimum);
            var rowGroups = new List<CssTableTracks.RowGroup>(); var firstRow = 0;
            foreach (var group in groups.Ordered)
            {
                rowGroups.Add(new(firstRow, BoxRows(group).Length, CssTableTracks.HeightOf(group), groups.IsBody(group))); firstRow += BoxRows(group).Length;
            }
            fullVertical = CssTableTracks.Rows(rows.Length, spacing.Height, rows.Select(row => CssTableProperties.SizeOf(row, false, double.PositiveInfinity)).ToArray(), cells, collapsed, minimumHeight,
                rows.Select(row => CssTableTracks.HeightOf(row)).ToArray(), rowGroups);
            vertical = CssTableVisibility.Project(fullVertical, visibility.Rows, spacing.Height);
            if (pass != 0 || reflow is null) break;
            foreach (var track in cells)
            {
                var cell = (TableCell)track.Source;
                if (!ExplicitCellHeight(cell, false) && !ExplicitCellHeight(table, true)) continue;
                var pad = CellPadding(cell, available); var border = UsedCellBorder(cell);
                cellHeights[cell] = Math.Max(0, fullVertical.Span(track.Row, track.RowSpan) - pad.Top - pad.Bottom - border.Top - border.Bottom);
            }
        }
        _tableWidthReduction[table] = Math.Max(0, fullHorizontal.Extent - horizontal.Extent);
        var rtl = table.FlowDirection == FlowDirection.RightToLeft;
        var removedHeight = Math.Max(0, fullVertical.Extent - vertical.Extent);
        var gridHeight = Math.Max(vertical.Extent, double.IsFinite(minimumHeight) ? Math.Max(0, minimumHeight - removedHeight) : 0);
        var displayColumns = DisplayColumns(table);
        var columnTrackBounds = new Rect[count];
        for (var c = 0; c < count; c++)
        {
            var columnX = left + (rtl ? horizontal.Extent - horizontal.Starts[c] - horizontal.Sizes[c] : horizontal.Starts[c]);
            columnTrackBounds[c] = new(columnX, startY + spacing.Height, visibility.Columns[c] ? 0 : horizontal.Sizes[c], Math.Max(0, gridHeight - 2 * spacing.Height));
        }
        var rowTrackBounds = new Rect[rows.Length];
        for (var row = 0; row < rows.Length; row++)
            rowTrackBounds[row] = new(left + spacing.Width, startY + vertical.Starts[row], Math.Max(0, visibleWidth - 2 * spacing.Width), vertical.Sizes[row]);
        var groupStarts = new Dictionary<TableRowGroup, int>(); var firstGroupRow = 0;
        foreach (var group in groups.Ordered) { groupStarts[group] = firstGroupRow; firstGroupRow += BoxRows(group).Length; }
        var groupTrackBounds = new Dictionary<TableRowGroup, Rect>();
        foreach (var (group, _) in groupFragments)
        {
            var groupRows = BoxRows(group).Select(row => rowTrackBounds[rowIndices[row]]).ToArray();
            var first = groupStarts[group];
            var emptyTop = rowless ? spacing.Height : first < rows.Length ? vertical.Starts[first] : ReferenceEquals(group, groups.Header) ? spacing.Height : Math.Max(0, gridHeight - spacing.Height);
            var top = groupRows.Length == 0 ? startY + emptyTop : groupRows[0].Top; var bottom = groupRows.Length == 0 ? top : groupRows[^1].Bottom;
            groupTrackBounds[group] = new(left + spacing.Width, top, Math.Max(0, visibleWidth - 2 * spacing.Width), bottom - top);
        }
        foreach (var content in contents)
        {
            var track = content.Track; var cell = (TableCell)track.Source; var pad = CellPadding(cell, available); var border = UsedCellBorder(cell);
            var width = horizontal.Span(track.Column, track.ColumnSpan); var height = vertical.Span(track.Row, track.RowSpan);
            var x = left + (rtl ? horizontal.Extent - horizontal.Starts[track.Column] - width : horizontal.Starts[track.Column]);
            var top = startY + vertical.Starts[track.Row]; var bounds = new Rect(x, top, width, height);
            var fullWidth = fullHorizontal.Span(track.Column, track.ColumnSpan);
            var spare = Math.Max(0, fullVertical.Span(track.Row, track.RowSpan) - content.Height);
            var offset = CellAlignment(cell) switch { CssInlineAlignment.Bottom => spare, CssInlineAlignment.Middle or CssInlineAlignment.Center => spare / 2, _ => track.OffsetY };
            var dx = x + pad.Left + border.Left + (rtl ? width - fullWidth : 0); var dy = top + offset;
            var removed = visibility.Removed(track);
            Rect? contentClip = visibility.Intersects(track) ? bounds : null;
            for (var i = content.FirstNestedCell; i < content.EndNestedCell; i++) _tableCells[i] = _tableCells[i] with { Bounds = Shift(_tableCells[i].Bounds, dx, dy) };
            foreach (var key in content.NestedGeometry) _tableGeometry[key] = Shift(_tableGeometry[key], dx, dy);
            MoveCellContent(content, dx, dy, contentClip, removed);
            _tableCells.Add(new(cell, bounds, track.Row, track.Column, track.RowSpan, track.ColumnSpan)); SetTableGeometry(cell, bounds);
            for (var i = content.FirstFragment + 1; i < content.EndFragment; i++)
                _fragmentBoxes[i] = _fragmentBoxes[i] with { MarginTop = _fragmentBoxes[i].MarginTop + dy, BorderTop = _fragmentBoxes[i].BorderTop + dy, Bottom = _fragmentBoxes[i].Bottom + dy };
            _fragmentBoxes[content.FirstFragment] = new(cell, content.FirstLine, content.EndLine, top, top, top + height);
            var show = !HiddenEmptyCell(cell) && !removed;
            if (show && height > 0)
            {
                // Row allocation adds padding/blank space, not a taller glyph.
                // Preserve it as fragmentable space before preparing page bands.
                _heightSpaces.Add(new(cell, top, top + height)); _hasDocumentHeights = true;
            }
            var paint = content.FirstPaint;
            for (var c = track.Column; c < track.Column + track.ColumnSpan; c++)
            {
                var columnX = left + (rtl ? horizontal.Extent - horizontal.Starts[c] - horizontal.Sizes[c] : horizontal.Starts[c]);
                _blocks[paint++] = new(c < displayColumns.Length ? displayColumns[c] : cell,
                    show && c < displayColumns.Length ? new(columnX, top, horizontal.Sizes[c], height) : default, false,
                    PaintBounds: show && c < displayColumns.Length ? columnTrackBounds[c] : null);
            }
            var group = BoxRowGroup(rows[track.Row])!;
            var rowCoverage = Rect.Union(rowTrackBounds[track.Row], bounds);
            var groupCoverage = groupTrackBounds.TryGetValue(group, out var gb) ? Rect.Union(gb, bounds) : bounds;
            _blocks[paint++] = new(group, show ? bounds : default, false, PaintBounds: show ? groupCoverage : null);
            _blocks[paint++] = new(rows[track.Row], show ? bounds : default, false, PaintBounds: show ? rowCoverage : null);
            _blocks[paint] = new(cell, show ? bounds : default);
            if (content.FirstLine == content.EndLine && !removed) _lines.Add(new(DocumentBoxSourceRange(cell).Start, DocumentBoxSourceRange(cell).Start, top, height, false, IsSpace: true));
        }
        foreach (var slot in missing)
        {
            var row = rows[slot.Row];
            if (!ShowMissing(slot.Row) || visibility.Rows[slot.Row] || !CssTableVisibility.Visible(row)) continue;
            var top = startY + vertical.Starts[slot.Row]; var height = vertical.Sizes[slot.Row];
            if (height > 0) { _heightSpaces.Add(new(row, top, top + height)); _hasDocumentHeights = true; }
            var group = BoxRowGroup(row)!;
            var groupCoverage = groupTrackBounds.TryGetValue(group, out var gb) ? gb : rowTrackBounds[slot.Row];
            for (var column = slot.Column; column < slot.Column + slot.ColumnSpan; column++)
            {
                if (visibility.Columns[column]) continue;
                var x = left + (rtl ? horizontal.Extent - horizontal.Starts[column] - horizontal.Sizes[column] : horizontal.Starts[column]);
                var bounds = new Rect(x, top, horizontal.Sizes[column], height);
                if (column < displayColumns.Length) _blocks.Add(new(displayColumns[column], bounds, false, PaintBounds: columnTrackBounds[column]));
                _blocks.Add(new(group, bounds, false, PaintBounds: groupCoverage));
                _blocks.Add(new(row, bounds, false, PaintBounds: rowTrackBounds[slot.Row]));
            }
        }
        for (var c = 0; c < Math.Min(count, displayColumns.Length); c++) SetTableGeometry(displayColumns[c], columnTrackBounds[c]);
        for (var row = 0; row < rows.Length; row++)
        {
            var bounds = rowTrackBounds[row]; var top = bounds.Top;
            SetTableGeometry(rows[row], bounds);
            if (!visibility.Rows[row] && !cellsByRow[row].Any() && BoxCells(rows[row]).Length == 0)
                _lines.Add(new(DocumentBoxSourceRange(rows[row]).Start, DocumentBoxSourceRange(rows[row]).Start, top, bounds.Height, false, IsSpace: true));
            var fragment = rowFragments[rows[row]]; _fragmentBoxes[fragment] = _fragmentBoxes[fragment] with { MarginTop = top, BorderTop = top, Bottom = bounds.Bottom };
        }
        if (rowless && gridHeight > 0)
        {
            _heightSpaces.Add(new(table, startY, startY + gridHeight)); _hasDocumentHeights = true;
        }
        foreach (var (group, fragment) in groupFragments)
        {
            var bounds = groupTrackBounds[group];
            SetTableGeometry(group, bounds);
            _fragmentBoxes[fragment] = _fragmentBoxes[fragment] with { MarginTop = bounds.Top, BorderTop = bounds.Top, Bottom = bounds.Bottom };
        }
        var bodyRows = groups.Ordered.Where(groups.IsBody)
            .SelectMany(BoxRows)
            .Where(row => !visibility.Rows[rowIndices[row]] && CssTableVisibility.Visible(row) && rowTrackBounds[rowIndices[row]].Height > 0)
            .ToArray();
        if (bodyRows.Length >= 2 && (groups.Header is not null || groups.Footer is not null))
        {
            var headerBounds = groups.Header is { } hg && groupTrackBounds.TryGetValue(hg, out var hb) && !CssTableVisibility.Collapsed(hg) ? hb : default;
            var footerBounds = groups.Footer is { } fg && groupTrackBounds.TryGetValue(fg, out var fb) && !CssTableVisibility.Collapsed(fg) ? fb : default;
            if (headerBounds.Height > 0 || footerBounds.Height > 0)
            {
                var bodyTop = bodyRows.Min(r => rowTrackBounds[rowIndices[r]].Top);
                var bodyBottom = bodyRows.Max(r => rowTrackBounds[rowIndices[r]].Bottom);
                _repeatableTables.Add(new(
                    table,
                    new Rect(left, startY, visibleWidth, gridHeight),
                    headerBounds.Height > 0 ? groups.Header : null,
                    headerBounds,
                    footerBounds.Height > 0 ? groups.Footer : null,
                    footerBounds,
                    bodyTop,
                    bodyBottom));
            }
        }
        y = startY + gridHeight;
        if (collapsedPlan is not null) _collapsedTableFrames.Add(new(collapsedPlan, horizontal, vertical, new(left, startY)));
    }

    private static CssInlineAlignment CellAlignment(DependencyObject cell)
    {
        var value = ((CssInlineVerticalAlign)cell.GetValue(CssFlowProperties.VerticalAlignProperty)!).Kind;
        return value is CssInlineAlignment.Top or CssInlineAlignment.Bottom or CssInlineAlignment.Middle or CssInlineAlignment.Center ? value : CssInlineAlignment.Baseline;
    }
    private bool HiddenEmptyCell(TableCell cell) => !DocumentCollapsedPart(cell) && !CellNodes(cell).Any() && !(bool)cell.GetValue(CssTableProperties.EmptyCellsProperty)!;
    private static Rect Shift(Rect rect, double x, double y) => new(rect.X + x, rect.Y + y, rect.Width, rect.Height);
    private void SetTableGeometry(DependencyObject source, Rect bounds)
    { if (!_tableGeometry.ContainsKey(source)) _tableGeometryKeys.Add(source); _tableGeometry[source] = bounds; }

    private void PrepareTablePagination()
    {
        RemoveCollapsedLines();
        _positionLines.Clear(); _positionLines.AddRange(_lines.OrderBy(line => line.Start).ThenBy(line => line.Top));
        if (!HasTables && !_hasNestedColumns && !_hasDocumentHeights) return;
        var physical = _lines.Select((line, index) => (Line: line, Index: index))
            .Concat(DocumentHeightLines(_lines).Select(line => (Line: line, Index: -1)))
            .OrderBy(item => item.Line.Top).ThenBy(item => item.Index).ToArray();
        var bands = new List<Line>(); var mapped = new int[_lines.Count];
        foreach (var item in physical)
        {
            var line = item.Line;
            if (bands.Count > 0 && line.Top < bands[^1].Top + bands[^1].Height - .01)
            {
                var prior = bands[^1]; bands[^1] = new(Math.Min(prior.Start, line.Start), Math.Max(prior.End, line.End), prior.Top,
                    Math.Max(prior.Height, line.Top + line.Height - prior.Top), prior.BreakBefore || line.BreakBefore);
            }
            else bands.Add(line);
            if (item.Index >= 0) mapped[item.Index] = bands.Count - 1;
        }
        int FirstBand(double y, bool byBottom)
        {
            var lo = 0; var hi = bands.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2; var line = bands[mid];
                if (byBottom ? line.Top + line.Height <= y + .01 : line.Top < y - .01) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
        foreach (var box in _tableGeometry.Where(pair => pair.Key is TableRow ||
            pair.Key is Table && (_documentCssRoot is null || !_documentCssBoxes.TryGetValue(pair.Key, out var tableBox) || CssTableLayout.DecorationBox(tableBox) is null)).Select(pair => pair.Value))
        {
            var first = FirstBand(box.Top, true); var last = FirstBand(box.Bottom, false) - 1;
            if (first >= bands.Count || last < first || box.Height <= 0) continue;
            var start = bands[first]; var top = Math.Min(start.Top, box.Top); bands[first] = start with { Top = top, Height = start.Top + start.Height - top };
            var end = bands[last]; bands[last] = end with { Height = Math.Max(end.Height, box.Bottom - end.Top) };
        }
        for (var i = 0; i < _fragmentBoxes.Count; i++)
        {
            var box = _fragmentBoxes[i]; int first, end;
            if (_tableGeometry.TryGetValue(box.Source, out var geometry))
            {
                first = FirstBand(geometry.Top, true);
                end = FirstBand(geometry.Bottom, false);
            }
            else if (box.EndLine > box.FirstLine)
            {
                first = int.MaxValue; end = 0;
                for (var line = box.FirstLine; line < box.EndLine; line++) { first = Math.Min(first, mapped[line]); end = Math.Max(end, mapped[line] + 1); }
            }
            else { first = FirstBand(box.BorderTop, false); end = first; }
            if (_hasDocumentHeights && box.Source is not Table and not TableRow and not TableCell)
            {
                first = Math.Min(first, FirstBand(box.BorderTop, true));
                end = Math.Max(end, FirstBand(box.Bottom, false));
            }
            var paragraphBands = box.Source is Paragraph ? Enumerable.Range(box.FirstLine, box.EndLine - box.FirstLine).Select(line => mapped[line]).Order().ToArray() : null;
            _fragmentBoxes[i] = box with { FirstLine = first, EndLine = Math.Max(first, end), LineBands = paragraphBands };
        }
        _lines.Clear(); _lines.AddRange(bands);
    }

    internal int FirstOffsetInPage(double top, double bottom)
        => _positionLines.Where(line => line.Top < bottom && line.Top + line.Height > top).Select(line => line.Start).DefaultIfEmpty(
            _positionLines.FirstOrDefault(line => line.Top >= top, _positionLines.LastOrDefault()).Start).Min();
}
