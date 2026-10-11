using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    internal IReadOnlyList<AutomationTextStyleSpan> GetAutomationTextStyles()
    {
        var spans = new List<AutomationTextStyleSpan>(); int offset = 0;
        var basic = Style(null, null);
        Blocks(_document.Blocks);
        if (spans.Count == 0) spans.Add(new(0, 0, basic));
        return spans;

        AutomationTextStyle Style(Run? run, Paragraph? paragraph)
        {
            var font = ResolveNativeRunFont(run); var color = ResolveNativeRunColor(run);
            var style = new AutomationTextStyle(font.Family, font.Size, font.Weight, font.Style,
                AutomationTextStyleFactory.Color(color.Color, color.Opacity),
                Alignment: (int)(paragraph?.TextAlignment ?? Jalium.UI.TextAlignment.Left),
                Direction: paragraph == null ? -1 : GetNativeParagraphDirection(paragraph), Language: Language.IetfLanguageTag);
            return AutomationTextStyleFactory.Decorations(style, run ?? (TextElement?)paragraph);
        }
        void Add(int length, AutomationTextStyle style)
        { if (length <= 0) return; AutomationTextStyles.Add(spans, offset, length, style); offset += length; }
        void Inlines(IEnumerable<Inline> inlines, Paragraph paragraph)
        {
            foreach (var inline in inlines)
                switch (inline)
                {
                    case Run run: Add(run.Text.Length, Style(run, paragraph)); break;
                    case Span span: Inlines(span.Inlines, paragraph); break;
                    case LineBreak: Add(Environment.NewLine.Length, Style(null, paragraph)); break;
                }
        }
        void Blocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
                switch (block)
                {
                    case Paragraph paragraph: Inlines(paragraph.Inlines, paragraph); Add(Environment.NewLine.Length, Style(null, paragraph)); break;
                    case Section section: Blocks(section.Blocks); break;
                    case Documents.List list: foreach (var item in list.ListItems) Blocks(item.Blocks); break;
                    case Table table:
                        foreach (var group in table.RowGroups)
                            foreach (var row in group.Rows)
                            {
                                foreach (var cell in row.Cells) { Blocks(cell.Blocks); Add(1, basic); }
                                Add(Environment.NewLine.Length, basic);
                            }
                        break;
                }
        }
    }

    internal Rect AutomationTextViewport => GetContentBounds();

    internal IReadOnlyList<AutomationTextLine> GetAutomationTextLines()
    {
        Rect bounds = GetContentBounds();
        var layout = EnsureLayout(bounds.Width);
        var result = new List<AutomationTextLine>();
        if (layout == null) return result;
        double y = bounds.Top - _verticalOffset;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        CollectAllLines(layout.Blocks, bounds.Left - _horizontalOffset, ref y, lines);
        string text = _document.GetText();
        for (int index = 0; index < lines.Count; index++)
        {
            var entry = lines[index];
            int start = Math.Clamp(entry.line.StartOffset, 0, text.Length);
            int end = index + 1 < lines.Count ? Math.Clamp(lines[index + 1].line.StartOffset, start, text.Length) : text.Length;
            result.Add(new(start, end - start, new Rect(entry.x, entry.y,
                Math.Max(1, entry.line.Width), Math.Max(1, entry.line.Height))));
        }
        if (result.Count == 0 && text.Length == 0)
            result.Add(new(0, 0, new Rect(bounds.X, bounds.Y, 1, GetDefaultLineHeight())));
        return result;
    }

    internal void ScrollToAutomationOffset(int offset)
    {
        Rect bounds = GetContentBounds();
        var layout = EnsureLayout(bounds.Width);
        if (layout == null) return;
        double y = bounds.Top - _verticalOffset;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        CollectAllLines(layout.Blocks, bounds.Left - _horizontalOffset, ref y, lines);
        int lineIndex = AutomationTextNavigation.LineFromIndex(GetAutomationTextLines(), offset, _document.GetText().Length);
        if ((uint)lineIndex >= lines.Count) return;
        var entry = lines[lineIndex];
        double x = entry.x + GetXOffsetInLine(entry.line, Math.Min(offset, entry.line.EndOffset));
        double vertical = _verticalOffset, horizontal = _horizontalOffset;
        if (entry.y < bounds.Top) vertical += entry.y - bounds.Top;
        else if (entry.y + entry.line.Height > bounds.Bottom) vertical += entry.y + entry.line.Height - bounds.Bottom;
        if (x < bounds.Left) horizontal += x - bounds.Left;
        else if (x + 1 > bounds.Right) horizontal += x + 1 - bounds.Right;
        VerticalOffsetCore = Math.Clamp(vertical, 0, Math.Max(0, layout.TotalHeight - bounds.Height));
        HorizontalOffsetCore = Math.Max(0, horizontal);
    }

    internal bool TryGetAutomationInsertionIndex(Point point, out int index)
    {
        var position = GetTextPositionFromPoint(point);
        index = position == null ? -1 : ImeTextEncoding.SnapToGraphemeBoundary(_document.GetText(), position.DocumentOffset, false);
        return index >= 0;
    }

    internal bool ReplaceAutomationSelection(string text) => TryReplaceImeText(
        Selection.Start.DocumentOffset, Selection.End.DocumentOffset - Selection.Start.DocumentOffset, text);
}
