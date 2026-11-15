using System.Text;
using Jalium.UI.Automation;
using Jalium.UI.Controls.TerminalEmulator;

namespace Jalium.UI.Controls;

public partial class Terminal
{
    private sealed record AutomationBufferSnapshot(
        string Text,
        string[] Lines,
        int[] Starts,
        int[][] ColToOffset,
        int[][] OffsetToCol,
        int Columns);
    private AutomationBufferSnapshot? _automationBuffer;

    private AutomationBufferSnapshot GetAutomationBuffer()
    {
        if (_automationBuffer is { } cached && cached.Columns == _buffer.Columns
            && cached.Lines.Length == _buffer.ScrollbackCount + _buffer.Rows) return cached;
        int count = _buffer.ScrollbackCount + _buffer.Rows;
        var lines = new string[count];
        var starts = new int[count];
        var colToOffset = new int[count][];
        var offsetToCol = new int[count][];
        var text = new StringBuilder();
        var lineBuilder = new StringBuilder(_buffer.Columns);
        var offsetList = new List<int>(_buffer.Columns + 1);
        for (int row = 0; row < count; row++)
        {
            starts[row] = text.Length;
            lineBuilder.Clear();
            offsetList.Clear();

            var rowColToOffset = new int[_buffer.Columns + 1];
            if (row < _buffer.ScrollbackCount)
            {
                var scrollLine = _buffer.GetScrollbackLine(row);
                for (int col = 0; col < _buffer.Columns; col++)
                {
                    rowColToOffset[col] = lineBuilder.Length;
                    var cell = col < scrollLine.Length ? scrollLine[col] : TerminalChar.Blank;
                    if (cell.Character == '\0')
                        continue;
                    offsetList.Add(col);
                    lineBuilder.Append(AutomationCharacter(cell));
                }
            }
            else
            {
                int screenRow = row - _buffer.ScrollbackCount;
                for (int col = 0; col < _buffer.Columns; col++)
                {
                    rowColToOffset[col] = lineBuilder.Length;
                    var cell = _buffer.GetCell(screenRow, col);
                    if (cell.Character == '\0')
                        continue;
                    offsetList.Add(col);
                    lineBuilder.Append(AutomationCharacter(cell));
                }
            }

            rowColToOffset[_buffer.Columns] = lineBuilder.Length;
            offsetList.Add(_buffer.Columns);

            lines[row] = lineBuilder.ToString();
            colToOffset[row] = rowColToOffset;
            offsetToCol[row] = offsetList.ToArray();
            text.Append(lines[row]);
            if (row + 1 < count) text.Append(Environment.NewLine);
        }
        return _automationBuffer = new(text.ToString(), lines, starts, colToOffset, offsetToCol, _buffer.Columns);
    }

    private static char AutomationCharacter(TerminalChar cell)
        => cell.Attributes.HasFlag(CharAttributes.Hidden) ? ' ' : cell.Character;

    private void AutomationBufferChanged()
    {
        _automationBuffer = null;
        if (GetExistingAutomationPeer() is Jalium.UI.Automation.Peers.TerminalAutomationPeer peer)
            peer.NotifyTextChanged();
    }

    internal string AutomationText => GetAutomationBuffer().Text;

    private int AutomationOffset(int row, int column)
    {
        var buffer = GetAutomationBuffer();
        row = Math.Clamp(row, 0, buffer.Lines.Length - 1);
        var map = buffer.ColToOffset[row];
        column = Math.Clamp(column, 0, map.Length - 1);
        return buffer.Starts[row] + map[column];
    }

    private (int Row, int Column) AutomationCell(int offset)
    {
        var buffer = GetAutomationBuffer();
        offset = Math.Clamp(offset, 0, buffer.Text.Length);
        int row = Array.BinarySearch(buffer.Starts, offset);
        if (row < 0) row = Math.Max(0, ~row - 1);
        int localOffset = Math.Clamp(offset - buffer.Starts[row], 0, buffer.Lines[row].Length);
        var map = buffer.OffsetToCol[row];
        int col = map[Math.Clamp(localOffset, 0, map.Length - 1)];
        return (row, col);
    }

    private int AutomationSelectionOffset(bool first)
    {
        var (row, column) = first ? NormalizeSelectionStart() : NormalizeSelectionEnd();
        return AutomationOffset(row, column);
    }

    internal int AutomationCaretIndex => AutomationOffset(_buffer.ScrollbackCount + _buffer.CursorRow, _buffer.CursorCol);
    internal int AutomationSelectionStart => _hasSelection ? AutomationSelectionOffset(true) : AutomationCaretIndex;
    internal int AutomationSelectionLength => _hasSelection
        ? Math.Max(0, AutomationSelectionOffset(false) - AutomationSelectionStart) : 0;

    internal void SelectAutomationText(int start, int length)
    {
        if (!IsEnabled) throw new InvalidOperationException("Cannot select a disabled terminal.");
        var text = AutomationText;
        if (start < 0 || length < 0 || start > text.Length || length > text.Length - start)
            throw new ArgumentOutOfRangeException(nameof(start));
        _selectionStart = AutomationCell(start);
        _selectionEnd = AutomationCell(start + length);
        _hasSelection = length > 0;
        InvalidateView();
    }

    internal Rect AutomationTextViewport
    {
        get
        {
            if (_view is null || _view.RenderSize.Width <= 0 || _view.RenderSize.Height <= 0) return Rect.Empty;
            var origin = _view.TransformToAncestor(this);
            return new Rect(origin.X, origin.Y, _view.RenderSize.Width, _view.RenderSize.Height);
        }
    }

    internal IReadOnlyList<AutomationTextLine> GetAutomationTextLines()
    {
        var buffer = GetAutomationBuffer();
        var viewport = AutomationTextViewport;
        var lines = new AutomationTextLine[buffer.Lines.Length];
        for (int row = 0; row < lines.Length; row++)
        {
            var bounds = viewport.IsEmpty || _cellWidth <= 0 || _cellHeight <= 0 ? Rect.Empty
                : new Rect(viewport.X, viewport.Y + (row - TopLine) * _cellHeight - FractionalY,
                    buffer.Lines[row].Length * _cellWidth, _cellHeight);
            lines[row] = new(buffer.Starts[row], buffer.Lines[row].Length, bounds);
        }
        return lines;
    }

    internal IReadOnlyList<Rect> GetAutomationTextBounds(int start, int length)
    {
        var viewport = AutomationTextViewport;
        if (viewport.IsEmpty || _cellWidth <= 0 || _cellHeight <= 0) return [];
        var buffer = GetAutomationBuffer();
        var first = AutomationCell(start);
        var last = AutomationCell(Math.Min(buffer.Text.Length, start + length));
        var rectangles = new List<Rect>();
        int visibleEnd = Math.Min(buffer.Lines.Length - 1,
            TopLine + (int)Math.Ceiling((viewport.Height + FractionalY) / _cellHeight));
        for (int row = Math.Max(first.Row, TopLine); row <= Math.Min(last.Row, visibleEnd); row++)
        {
            int left = row == first.Row ? first.Column : 0;
            int right = row == last.Row ? last.Column : buffer.Lines[row].Length;
            if (right <= left && length > 0) continue;
            var rect = new Rect(viewport.X + left * _cellWidth,
                viewport.Y + (row - TopLine) * _cellHeight - FractionalY,
                Math.Max(1, (right - left) * _cellWidth), _cellHeight);
            rect = Rect.Intersect(rect, viewport);
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0) rectangles.Add(rect);
        }
        return rectangles;
    }

    internal bool TryGetAutomationInsertionIndex(Point point, out int index)
    {
        index = 0;
        var viewport = AutomationTextViewport;
        if (viewport.IsEmpty || !viewport.Contains(point)) return false;
        var (row, column, side) = ScreenPosToCell(new Point(point.X - viewport.X, point.Y - viewport.Y));
        index = AutomationOffset(row, column + (side == CellSide.Right ? 1 : 0));
        return true;
    }

    internal void ScrollAutomationTextIntoView(int start, int length)
    {
        if (_view is null || _cellHeight <= 0) return;
        var (row, _) = AutomationCell(start);
        _view.SetVerticalOffset(row * _cellHeight);
    }
}
