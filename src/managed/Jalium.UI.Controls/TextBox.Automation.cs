using Jalium.UI.Automation;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

public partial class TextBox
{
    internal IReadOnlyList<AutomationTextStyleSpan> GetAutomationTextStyles() =>
        [new(0, Text.Length, AutomationTextStyleFactory.Control(this, ResolveTextForegroundBrush(), (int)TextAlignment))];

    internal Rect AutomationTextViewport => GetTextViewportRect();
    internal bool AutomationCaretHasBackwardAffinity => _caretHasBackwardAffinity;

    internal IReadOnlyList<AutomationTextLine> GetAutomationTextLines()
    {
        EnsureLinesValid();
        Rect viewport = GetTextViewportRect();
        double height = Math.Max(1, Math.Round(GetLineHeight()));
        EnsureVisualLineCounts(viewport.Width, height);
        double y = viewport.Y + ComputeVerticalContentOffset(viewport.Width, viewport.Height, height) - Math.Round(_verticalOffset);
        var result = new List<AutomationTextLine>();
        for (int logical = 0; logical < _lines.Count; logical++)
        {
            var line = _lines[logical];
            int limit = logical + 1 < _lines.Count ? _lines[logical + 1].StartIndex : Text.Length;
            string text = Text.Substring(line.StartIndex, line.Length);
            int position = 0, row = 0;
            do
            {
                int rowStart = position, rowEnd = line.Length;
                if (TextWrapping != TextWrapping.NoWrap && text.Length > 0
                    && TextMeasurement.TryGetVisualLineMetrics(text,
                        FontFamily?.GetRenderingSource(this) ?? FrameworkElement.DefaultFontFamilyName,
                        FontSize, FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle(),
                        (float)Math.Max(1, viewport.Width), (uint)position, false, out var metrics)
                    && metrics.Length > 0)
                {
                    rowStart = (int)metrics.TextPosition;
                    rowEnd = Math.Min(line.Length, rowStart + (int)metrics.Length);
                }
                int end = rowEnd == line.Length ? limit : line.StartIndex + rowEnd;
                result.Add(new(line.StartIndex + rowStart, end - line.StartIndex - rowStart,
                    new Rect(viewport.X, y + row * height, viewport.Width, height)));
                if (rowEnd <= position) break;
                position = rowEnd; row++;
            } while (position < line.Length);
            y += Math.Max(1, _lineVisualCounts[logical]) * height;
        }
        return result;
    }

    internal void ScrollToAutomationOffset(int offset)
    {
        Rect viewport = GetTextViewportRect(), caret = GetRectFromCharacterIndex(Math.Clamp(offset, 0, Text.Length));
        double y = _verticalOffset, x = _horizontalOffset;
        if (caret.Top < viewport.Top) y += caret.Top - viewport.Top;
        else if (caret.Bottom > viewport.Bottom) y += caret.Bottom - viewport.Bottom;
        if (caret.Left < viewport.Left) x += caret.Left - viewport.Left;
        else if (caret.Right + 1 > viewport.Right) x += caret.Right + 1 - viewport.Right;
        ScrollToVerticalOffset(Math.Max(0, y)); ScrollToHorizontalOffset(Math.Max(0, x));
    }
}
