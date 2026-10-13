using Jalium.UI.Interop;
using Jalium.UI.Automation;

namespace Jalium.UI.Controls;

public partial class EditControl
{
    internal IReadOnlyList<AutomationTextStyleSpan> GetAutomationTextStyles() =>
        _view.GetAutomationTextStyles(AutomationTextStyleFactory.Control(this, ResolveForegroundBrush()), ResolveForegroundBrush());

    internal Rect AutomationTextViewport
    {
        get
        {
            EnsureViewLayoutMetrics(); UpdateScrollBarLayout(RenderSize);
            double left = ShowLineNumbers ? _view.TextAreaLeft : 0;
            return new Rect(left, 0, Math.Max(0, _view.ViewportWidth - left), _view.ViewportHeight);
        }
    }

    internal IReadOnlyList<AutomationTextLine> GetAutomationTextLines()
    {
        Rect viewport = AutomationTextViewport;
        var result = new List<AutomationTextLine>();
        int number = 1;
        while (number <= _document.LineCount)
        {
            var line = _document.GetLineByNumber(number);
            if (_view.TryGetLineTop(number, out double y))
                result.Add(new(line.Offset, line.TotalLength,
                    new Rect(viewport.X, y, viewport.Width, Math.Max(1, _view.LineHeight))));
            int next = _view.MoveVisibleLine(number, 1);
            if (next <= number) break;
            number = next;
        }
        return result;
    }

    internal bool TryGetAutomationInsertionIndex(Point point, out int index)
    {
        _ = AutomationTextViewport;
        index = SnapGraphemeOffset(_view.GetOffsetFromPoint(point, ShowLineNumbers), forward: false);
        return true;
    }

    internal bool ReplaceAutomationSelection(string text)
    {
        if (IsReadOnly) return false;
        InsertText(text); return true;
    }

    internal void ReplaceAutomationText(string value)
    {
        if (_document.Text == value) return;
        // Use the regular edit transaction so AX writes participate in undo/redo.
        // Announce the final selection through InsertText, without a temporary SelectAll.
        _selection.SetSelection(0, _document.TextLength);
        _caret.Offset = _document.TextLength;
        InsertText(value);
    }

    internal void ScrollToAutomationOffset(int offset)
    {
        ScrollToOffset(offset);
        // An accessibility client may immediately query the newly revealed range.
        if (_isScrollAnimating)
            SetScrollOffsetsImmediate(_scrollAnimationTargetVerticalOffset, _scrollAnimationTargetHorizontalOffset,
                userInitiated: false, cancelAnimation: true);
    }

    internal IReadOnlyList<Rect> GetAutomationTextBounds(int start, int length)
    {
        if (start < 0 || length < 0 || start > _document.TextLength || length > _document.TextLength - start
            || RenderSize.Width <= 0 || RenderSize.Height <= 0) return [];
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        double left = ShowLineNumbers ? _view.TextAreaLeft : 0;
        var viewport = new Rect(left, 0, Math.Max(0, _view.ViewportWidth - left), _view.ViewportHeight);
        if (viewport.Width <= 0 || viewport.Height <= 0) return [];
        int end = SnapGraphemeOffset(start + length, forward: true);
        start = SnapGraphemeOffset(start, forward: false);
        var rectangles = new List<Rect>();
        if (length == 0)
        {
            var line = _document.GetLineByOffset(start);
            if (_view.TryGetLineTop(line.LineNumber, out double y))
            {
                var point = _view.GetPointFromOffset(start, ShowLineNumbers);
                AddVisible(new Rect(point.X, y, 1, Math.Max(1, _view.LineHeight)));
            }
            return rectangles;
        }

        int firstLine = Math.Max(_document.GetLineByOffset(start).LineNumber, _view.FirstVisibleLineNumber);
        int lastLine = Math.Min(_document.GetLineByOffset(end).LineNumber, _view.LastVisibleLineNumber);
        // Iterate rendered rows through the folding map rather than every document line.
        int lineNumber = _view.GetVisibleAnchorLineNumber(firstLine);
        while (lineNumber <= lastLine)
        {
            var line = _document.GetLineByNumber(lineNumber);
            if (_view.TryGetLineTop(lineNumber, out double y) && line.Offset < end && line.Offset + line.TotalLength > start)
            {
                int from = Math.Clamp(start - line.Offset, 0, line.Length);
                int to = Math.Clamp(end - line.Offset, from, line.Length);
                string lineText = _document.GetLineText(lineNumber);
                if (to > from && TextMeasurement.HitTestTextRangeWrapped(lineText,
                    FontFamily?.GetRenderingSource(this) ?? "Cascadia Code", FontSize,
                    FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle(), float.PositiveInfinity,
                    (uint)from, (uint)(to - from), out var range) && range.Length == to - from
                    && double.IsFinite(range.X) && double.IsFinite(range.Width))
                {
                    AddVisible(new Rect(left + range.X - _view.HorizontalOffset, y,
                        Math.Max(1, range.Width), Math.Max(1, _view.LineHeight)));
                }
                else
                {
                    // Reuse the grapheme caret edges from drawing when native shaping is unavailable.
                    Rect bounds = Rect.Empty;
                    int offset = line.Offset + from, limit = line.Offset + to;
                    do
                    {
                        int next = Math.Min(limit, NextGraphemeOffset(offset));
                        var leading = _view.GetPointFromOffset(offset, ShowLineNumbers);
                        var trailing = _view.GetPointFromOffset(next, ShowLineNumbers, backwardAffinity: true);
                        var cluster = new Rect(Math.Min(leading.X, trailing.X), y,
                            Math.Max(1, Math.Abs(trailing.X - leading.X)), Math.Max(1, _view.LineHeight));
                        bounds = bounds.IsEmpty ? cluster : Rect.Union(bounds, cluster);
                        if (next <= offset) break;
                        offset = next;
                    } while (offset < limit);
                    AddVisible(bounds);
                }
            }
            int nextLine = _view.MoveVisibleLine(lineNumber, 1);
            if (nextLine <= lineNumber) break;
            lineNumber = nextLine;
        }
        return rectangles;

        void AddVisible(Rect rectangle)
        {
            var clipped = Rect.Intersect(rectangle, viewport);
            if (!clipped.IsEmpty && clipped.Width > 0 && clipped.Height > 0) rectangles.Add(clipped);
        }
    }
}
