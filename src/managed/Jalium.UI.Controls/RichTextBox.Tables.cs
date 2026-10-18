using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private sealed class TableCellLayout
    {
        internal required TableCell Cell;
        internal required TableRow Row;
        internal required TableRowGroup Group;
        internal int Column, GridRow, RowSpan, ColumnSpan, Start, End;
        internal Rect Bounds;
        internal Thickness Insets;
        internal List<BlockLayoutInfo> Blocks = new();
    }

    private sealed class TableLayout
    {
        internal readonly List<TableCellLayout> Cells = new();
        internal double Width, Height;
    }

    // Use the existing track solver for pixel/auto/star columns and spanning
    // cells. These private elements only measure; document ownership and paint
    // stay with the editor's original flow elements and text presentations.
    private sealed class TableCellMeasure(RichTextBox owner, TableCellLayout cell) : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var block in cell.Blocks) block.Dispose();
            cell.Blocks.Clear();
            var border = SafeInsets(cell.Cell.BorderThickness);
            var padding = SafeInsets(cell.Cell.Padding);
            cell.Insets = new(border.Left + padding.Left, border.Top + padding.Top,
                border.Right + padding.Right, border.Bottom + padding.Bottom);
            double width = Math.Max(1, availableSize.Width - cell.Insets.Left - cell.Insets.Right);
            if (!double.IsFinite(width)) width = 100000;
            int offset = cell.Start;
            foreach (var block in cell.Cell.Blocks)
                cell.Blocks.Add(owner.LayoutBlock(block, width, ref offset));
            if (cell.Blocks.Count == 0)
            {
                var empty = new BlockLayoutInfo { Block = new Paragraph() };
                double height = owner.GetLineHeightForFormatting(cell.Cell.FontSize);
                empty.Lines.Add(new LineLayoutInfo { StartOffset = offset, EndOffset = offset,
                    Height = height, Baseline = height * 0.8 });
                cell.Blocks.Add(empty);
            }
            var lines = new List<(LineLayoutInfo line, double y, double x)>();
            double y = 0;
            owner.CollectAllLines(cell.Blocks, 0, ref y, lines);
            double used = lines.Count == 0 ? 0 : lines.Max(line => line.x + line.line.Width);
            return new(used + cell.Insets.Left + cell.Insets.Right,
                y + cell.Insets.Top + cell.Insets.Bottom);
        }
    }

    private static Thickness SafeInsets(Thickness value)
    {
        static double Edge(double v) => double.IsFinite(v) ? Math.Max(0, v) : 0;
        return new(Edge(value.Left), Edge(value.Top), Edge(value.Right), Edge(value.Bottom));
    }

    private void LayoutTable(Table table, BlockLayoutInfo block, double maxWidth, ref int offset)
    {
        var result = new TableLayout();
        block.Table = result;
        var grid = new Grid { RowSpacing = double.IsFinite(table.CellSpacing) ? Math.Max(0, table.CellSpacing) : 0,
            ColumnSpacing = double.IsFinite(table.CellSpacing) ? Math.Max(0, table.CellSpacing) : 0 };
        int rowBase = 0, columns = table.Columns.Count;
        foreach (var group in table.RowGroups)
        {
            var occupied = new HashSet<(int Row, int Column)>();
            for (int rowIndex = 0; rowIndex < group.Rows.Count; rowIndex++)
            {
                var row = group.Rows[rowIndex];
                int column = 0;
                foreach (var cell in row.Cells)
                {
                    int columnSpan = Math.Max(1, cell.ColumnSpan);
                    int rowSpan = Math.Clamp(cell.RowSpan, 1, group.Rows.Count - rowIndex);
                    while (Enumerable.Range(column, columnSpan).Any(c => occupied.Contains((rowIndex, c)))) ++column;
                    int end = offset + cell.Blocks.Sum(FlowDocument.GetBlockLength);
                    result.Cells.Add(new TableCellLayout { Cell = cell, Row = row, Group = group,
                        GridRow = rowBase + rowIndex, Column = column, ColumnSpan = columnSpan,
                        RowSpan = rowSpan, Start = offset, End = end });
                    for (int y = rowIndex; y < rowIndex + rowSpan; y++)
                        for (int x = column; x < column + columnSpan; x++) occupied.Add((y, x));
                    columns = Math.Max(columns, column + columnSpan);
                    column += columnSpan;
                    offset = end + 1; // FlowDocument's virtual cell tab.
                }
                ++offset; // FlowDocument's virtual row break.
            }
            rowBase += group.Rows.Count;
        }
        for (int i = 0; i < Math.Max(1, columns); i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i < table.Columns.Count ? table.Columns[i].Width : new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rowBase; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var measures = new List<TableCellMeasure>();
        try
        {
            foreach (var cell in result.Cells)
            {
                var measure = new TableCellMeasure(this, cell);
                Grid.SetRow(measure, cell.GridRow); Grid.SetColumn(measure, cell.Column);
                Grid.SetRowSpan(measure, cell.RowSpan); Grid.SetColumnSpan(measure, cell.ColumnSpan);
                grid.Children.Add(measure); measures.Add(measure);
            }
            double width = Math.Max(1, maxWidth - block.Margin.Left - block.Margin.Right);
            grid.Measure(new Size(width, double.PositiveInfinity));
            if (!double.IsFinite(width))
            {
                width = Math.Max(1, grid.DesiredSize.Width);
                grid.Measure(new Size(width, double.PositiveInfinity));
            }
            grid.Arrange(new Rect(0, 0, width, grid.DesiredSize.Height));
            result.Width = width; result.Height = grid.RenderSize.Height;
            for (int i = 0; i < result.Cells.Count; i++)
            {
                var cell = result.Cells[i]; var measure = measures[i];
                var position = VisualTreeHelper.GetOffset(measure);
                double x = table.FlowDirection == FlowDirection.RightToLeft
                    ? width - position.X - measure.RenderSize.Width : position.X;
                cell.Bounds = new Rect(x, position.Y, measure.RenderSize.Width, measure.RenderSize.Height);
                result.Width = Math.Max(result.Width, cell.Bounds.Right);
                block.ChildBlocks.AddRange(cell.Blocks);
            }
        }
        catch
        {
            foreach (var cell in result.Cells) foreach (var child in cell.Blocks) child.Dispose();
            block.ChildBlocks.Clear(); throw;
        }
        finally { grid.Children.Clear(); }
    }

    private void CollectTableLines(TableLayout table, double x, double y,
        List<(LineLayoutInfo line, double y, double x)> lines,
        Dictionary<LineLayoutInfo, Rect>? clips = null, Rect? ancestorClip = null)
    {
        foreach (var cell in table.Cells)
        {
            double top = y + cell.Bounds.Top + cell.Insets.Top;
            var clip = CellPaintClip(cell, x, y);
            if (ancestorClip is { } parent) clip = Intersect(clip, parent);
            CollectAllLines(cell.Blocks, x + cell.Bounds.Left + cell.Insets.Left, ref top, lines, clips, clip);
        }
    }

    private void RenderTable(DrawingContext context, TableLayout table, double x, double y, Rect viewport)
    {
        foreach (var cell in table.Cells)
        {
            var box = new Rect(x + cell.Bounds.Left, y + cell.Bounds.Top, cell.Bounds.Width, cell.Bounds.Height);
            if (!box.IntersectsWith(viewport)) continue;
            context.PushClip(new RectangleGeometry(CellPaintClip(cell, x, y)));
            try
            {
                double top = box.Top + cell.Insets.Top;
                foreach (var child in cell.Blocks)
                    RenderBlockLayout(context, child, box.Left + cell.Insets.Left, ref top, viewport);
            }
            finally { context.Pop(); }
        }
    }

    private void DrawTableSurfaces(DrawingContext context, List<BlockLayoutInfo> blocks, double x, ref double y)
    {
        foreach (var block in blocks)
        {
            y += block.Margin.Top;
            double left = x + block.Margin.Left;
            if (block.Table is { } table)
            {
                if (block.Block.Background is { } background)
                    context.DrawRectangle(background, null, new Rect(left, y, table.Width, table.Height));
                foreach (var cell in table.Cells)
                {
                    var box = new Rect(left + cell.Bounds.Left, y + cell.Bounds.Top, cell.Bounds.Width, cell.Bounds.Height);
                    var model = (Table)block.Block;
                    if (cell.Column < model.Columns.Count && model.Columns[cell.Column].Background is { } column)
                        context.DrawRectangle(column, null, box);
                    foreach (var brush in new[] { cell.Group.Background, cell.Row.Background, cell.Cell.Background })
                        if (brush is not null) context.DrawRectangle(brush, null, box);
                    if (cell.Cell.BorderBrush is { } border)
                    {
                        var edge = SafeInsets(cell.Cell.BorderThickness);
                        context.DrawRectangle(border, null, new Rect(box.Left, box.Top, Math.Min(box.Width, edge.Left), box.Height));
                        context.DrawRectangle(border, null, new Rect(Math.Max(box.Left, box.Right - edge.Right), box.Top, Math.Min(box.Width, edge.Right), box.Height));
                        context.DrawRectangle(border, null, new Rect(box.Left, box.Top, box.Width, Math.Min(box.Height, edge.Top)));
                        context.DrawRectangle(border, null, new Rect(box.Left, Math.Max(box.Top, box.Bottom - edge.Bottom), box.Width, Math.Min(box.Height, edge.Bottom)));
                    }
                    double top = box.Top + cell.Insets.Top;
                    context.PushClip(new RectangleGeometry(box));
                    try { DrawTableSurfaces(context, cell.Blocks, box.Left + cell.Insets.Left, ref top); }
                    finally { context.Pop(); }
                }
                y += table.Height;
            }
            else
            {
                y += block.Lines.Sum(line => line.Height);
                DrawTableSurfaces(context, block.ChildBlocks,
                    left + (block.Block is Documents.List ? 20 : 0), ref y);
            }
            y += block.Margin.Bottom;
        }
    }

    private TextPointer HitLine(LineLayoutInfo line, double x)
    {
        if (line.NativeLine is { } native)
        {
            var hit = native.Paragraph.HitTest(native.Index, x);
            return _document.GetPositionAtOffset(line.ParagraphOffset + (int)hit.TextPosition,
                hit.BackwardAffinity != 0 ? LogicalDirection.Backward : LogicalDirection.Forward) ?? _document.ContentEnd;
        }
        foreach (var run in line.Runs)
            if (run.Run is not null && x >= run.X && x <= run.X + run.Width)
            {
                int offset = run.StartOffset + FindCharIndexFromX(run.Run, x - run.X,
                    Math.Max(0, run.StartOffset - run.Run.ContentStart.DocumentOffset), run.EndOffset - run.StartOffset);
                return _document.GetPositionAtOffset(Math.Clamp(offset, run.StartOffset, run.EndOffset), LogicalDirection.Forward) ?? _document.ContentEnd;
            }
        return _document.GetPositionAtOffset(x <= 0 ? line.StartOffset : line.EndOffset,
            x <= 0 ? LogicalDirection.Forward : LogicalDirection.Backward) ?? _document.ContentEnd;
    }

    private static bool ContainsTables(List<BlockLayoutInfo> blocks) =>
        blocks.Any(block => block.Table is not null || ContainsTables(block.ChildBlocks));

    private TextPointer HitTableDocument(FlowDocumentLayoutInfo layout, Point point, Rect viewport)
    {
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double y = viewport.Top - _verticalOffset;
        CollectAllLines(layout.Blocks, viewport.Left - _horizontalOffset, ref y, lines);
        if (lines.Count == 0) return _document.ContentStart;
        if (point.Y >= y) return _document.ContentEnd;
        var local = new Point(point.X - viewport.Left + _horizontalOffset,
            point.Y - viewport.Top + _verticalOffset);
        var owner = layout.Cells.Where(cell => cell.Bounds.Width > 0 && cell.Bounds.Height > 0 && cell.Bounds.Contains(local))
            .OrderByDescending(cell => cell.Depth).FirstOrDefault();
        if (owner.Cell is not null)
            lines = lines.Where(line => line.line.StartOffset >= owner.Cell.Start && line.line.EndOffset <= owner.Cell.End).ToList();
        if (lines.Count == 0) return _document.GetPositionAtOffset(owner.Cell?.Start ?? 0, LogicalDirection.Forward) ?? _document.ContentStart;
        var candidates = lines.Select(line => (line, vertical: Math.Max(0, Math.Max(line.y - point.Y, point.Y - line.y - line.line.Height)),
            horizontal: Math.Max(0, Math.Max(line.x - point.X, point.X - line.x - line.line.Width))))
            .OrderBy(item => item.vertical).ThenBy(item => item.horizontal).ToArray();
        var best = candidates[0].line;
        return HitLine(best.line, point.X - best.x);
    }
    private static Rect Intersect(Rect value, Rect clip)
    {
        double left = Math.Clamp(value.Left, clip.Left, clip.Right);
        double top = Math.Clamp(value.Top, clip.Top, clip.Bottom);
        double right = Math.Clamp(value.Right, left, clip.Right);
        double bottom = Math.Clamp(value.Bottom, top, clip.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect CellPaintClip(TableCellLayout cell, double x, double y)
    {
        var edge = SafeInsets(cell.Cell.BorderThickness);
        double left = x + cell.Bounds.Left + Math.Min(edge.Left, cell.Bounds.Width);
        double top = y + cell.Bounds.Top + Math.Min(edge.Top, cell.Bounds.Height);
        return new Rect(left, top, Math.Max(0, cell.Bounds.Width - edge.Left - edge.Right),
            Math.Max(0, cell.Bounds.Height - edge.Top - edge.Bottom));
    }

    private Rect ClipToCell(FlowDocumentLayoutInfo layout, LineLayoutInfo line, Rect value, Rect viewport)
    {
        if (!layout.CellClips.TryGetValue(line, out var local)) return value;
        var clip = new Rect(local.Left + viewport.Left - _horizontalOffset,
            local.Top + viewport.Top - _verticalOffset, local.Width, local.Height);
        return Intersect(value, clip);
    }

    private void CollectCellRegions(List<BlockLayoutInfo> blocks, double x, ref double y, int depth,
        List<(TableCellLayout Cell, Rect Bounds, int Depth)> result, Rect? ancestorClip = null)
    {
        foreach (var block in blocks)
        {
            y += block.Margin.Top;
            double left = x + block.Margin.Left;
            if (block.Table is { } table)
            {
                foreach (var cell in table.Cells)
                {
                    var box = new Rect(left + cell.Bounds.Left, y + cell.Bounds.Top, cell.Bounds.Width, cell.Bounds.Height);
                    var region = ancestorClip is { } outer ? Intersect(box, outer) : box;
                    result.Add((cell, region, depth));
                    double childY = box.Top + cell.Insets.Top;
                    var clip = CellPaintClip(cell, left, y);
                    if (ancestorClip is { } parent) clip = Intersect(clip, parent);
                    CollectCellRegions(cell.Blocks, box.Left + cell.Insets.Left, ref childY, depth + 1, result, clip);
                }
                y += table.Height;
            }
            else
            {
                y += block.Lines.Sum(line => line.Height);
                CollectCellRegions(block.ChildBlocks, left + (block.Block is Documents.List ? 20 : 0), ref y, depth, result, ancestorClip);
            }
            y += block.Margin.Bottom;
        }
    }

    private TextSelection CreateOwnedSelection(TextPointer anchor, TextPointer moving)
    {
        if (_selection is { } previous) previous.Changed -= OnOwnedSelectionChanged;
        var selection = new TextSelection(anchor, moving);
        selection.Changed += OnOwnedSelectionChanged;
        return selection;
    }

    private void OnOwnedSelectionChanged(object? sender, EventArgs e)
    {
        var selection = _selection;
        if (!ReferenceEquals(sender, selection) || selection is null) return;
        _caretPosition = selection.MovingPosition;
        _selectionAnchor = selection.AnchorPosition;
        ResetCaretBlink(); EnsureCaretVisible();
        if (!ReferenceEquals(selection, _selection)) return;
        OnSelectionChanged(); InvalidateVisual();
    }

    private void LayoutFallbackRun(Run run, BlockLayoutInfo block, ref LineLayoutInfo line,
        double width, ref double x, ref int offset)
    {
        // The normal macOS path shapes whole paragraphs. Keep its fallback
        // usable when native shaping is unavailable: preserve source slices,
        // prefer word boundaries, and never split a grapheme cluster.
        width = double.IsFinite(width) ? Math.Max(1, width) : 100000;
        foreach (System.Text.RegularExpressions.Match token in
            System.Text.RegularExpressions.Regex.Matches(run.Text, @"\r\n|[\r\n]|[^\S\r\n]+|[^\s]+"))
        {
            string remaining = token.Value;
            if (remaining is "\r" or "\n" or "\r\n")
            {
                offset += remaining.Length;
                FinishFallbackLine(block, ref line, ref x, offset);
                continue;
            }
            while (remaining.Length != 0)
            {
                double advance = MeasureText(remaining, run);
                if (advance <= width - x)
                {
                    AddFallbackSlice(run, line, remaining.Length, advance, ref x, ref offset);
                    break;
                }
                if (x > 0)
                {
                    FinishFallbackLine(block, ref line, ref x, offset);
                    continue;
                }
                var edges = new List<int> { 0 };
                for (int i = 0; i < remaining.Length;)
                {
                    i = GraphemeClusters.NextBoundary(remaining, i); edges.Add(i);
                }
                int low = 1, high = edges.Count - 1, fits = 1;
                while (low <= high)
                {
                    int middle = low + (high - low) / 2;
                    if (MeasureText(remaining[..edges[middle]], run) <= width)
                    { fits = middle; low = middle + 1; }
                    else high = middle - 1;
                }
                int take = edges[fits];
                AddFallbackSlice(run, line, take, MeasureText(remaining[..take], run), ref x, ref offset);
                remaining = remaining[take..];
                if (remaining.Length != 0) FinishFallbackLine(block, ref line, ref x, offset);
            }
        }
    }

    private void AddFallbackSlice(Run run, LineLayoutInfo line, int length, double advance, ref double x, ref int offset)
    {
        if (line.Runs.LastOrDefault() is { } last && ReferenceEquals(last.Run, run) && last.EndOffset == offset)
        {
            last.EndOffset += length;
            int start = Math.Clamp(last.StartOffset - run.ContentStart.DocumentOffset, 0, run.Text.Length);
            last.Width = MeasureText(run.Text.Substring(start, last.EndOffset - last.StartOffset), run);
            x = last.X + last.Width;
        }
        else
        {
            line.Runs.Add(new RunLayoutInfo { Run = run, X = x, Width = advance, StartOffset = offset, EndOffset = offset + length });
            x += advance;
        }
        offset += length;
        line.Height = Math.Max(line.Height, GetLineHeightForFormatting(run.FontSize));
        line.Baseline = Math.Max(line.Baseline, line.Height * 0.8);
    }

    private void FinishFallbackLine(BlockLayoutInfo block, ref LineLayoutInfo line, ref double x, int offset)
    {
        line.EndOffset = offset; line.Width = x; block.Lines.Add(line);
        double height = GetDefaultLineHeight();
        line = new LineLayoutInfo { StartOffset = offset, Height = height, Baseline = height * 0.8 };
        x = 0;
    }

}
