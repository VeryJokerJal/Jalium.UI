using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    internal IReadOnlyList<AutomationTextStyleSpan> GetAutomationTextStyles()
    {
        PrepareCssEditingLayout();
        var spans = new List<AutomationTextStyleSpan>(); int offset = 0;
        var basic = Style(null, null);
        Blocks(_document.Blocks);
        if (spans.Count == 0) spans.Add(new(0, 0, basic));
        return _cssDocumentView is { } view ? spans.Select(span =>
        {
            var start = view.EditorExposedOffset(span.Start);
            return span with { Start = start, Length = view.EditorExposedOffset(span.Start + span.Length) - start };
        }).Where(span => span.Length > 0).ToArray() : spans;

        AutomationTextStyle Style(Run? run, Paragraph? paragraph, FrameworkContentElement? languageSource = null)
        {
            var font = ResolveNativeRunFont(run); var color = ResolveNativeRunColor(run);
            var style = new AutomationTextStyle(font.Family, font.Size, font.Weight, font.Style,
                AutomationTextStyleFactory.Color(color.Color, color.Opacity),
                Alignment: (int)(paragraph?.TextAlignment ?? Jalium.UI.TextAlignment.Left),
                Direction: paragraph == null ? -1 : GetNativeParagraphDirection(paragraph),
                Language: (languageSource?.Language ?? run?.Language ?? paragraph?.Language ?? _document.Language).IetfLanguageTag,
                Background: AutomationTextStyleFactory.InlineBackground(run ?? languageSource as Inline),
                InlineContext: AutomationTextStyleFactory.InlineContext(run ?? languageSource as Inline));
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
                    case LineBreak or InlineUIContainer: Add(1, Style(null, paragraph, inline)); break;
                }
        }
        void Blocks(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
                switch (block)
                {
                    case Paragraph paragraph: Inlines(paragraph.Inlines, paragraph); Add(1, Style(null, paragraph)); break;
                    case BlockUIContainer: Add(1, Style(null, null, block)); break;
                    case Section section: Blocks(section.Blocks); break;
                    case Documents.List list: foreach (var item in list.ListItems) Blocks(item.Blocks); break;
                    case Table table:
                        foreach (var group in table.RowGroups)
                            foreach (var row in group.Rows)
                            {
                                foreach (var cell in row.Cells) { Blocks(cell.Blocks); Add(1, Style(null, null, cell)); }
                                Add(1, Style(null, null, row));
                            }
                        break;
                }
        }
    }

    internal Rect AutomationTextViewport => GetContentBounds();

    internal string AutomationDocumentText
    {
        get { PrepareCssEditingLayout(); return _cssDocumentView?.EditorExposedText ?? _document.GetText(); }
    }

    internal int AutomationDocumentOffset(int source)
    {
        PrepareCssEditingLayout();
        return _cssDocumentView?.EditorExposedOffset(source) ?? source;
    }

    internal void SelectAutomationDocumentText(int start, int length)
    {
        PrepareCssEditingLayout();
        var from = _cssDocumentView?.EditorSourceOffset(start) ?? start;
        var to = length == 0 ? from : _cssDocumentView?.EditorSourceOffset(start + length, forward: false) ?? start + length;
        ((IImeSupport)this).TrySetImeSelection(from, Math.Max(0, to - from));
    }

    internal IReadOnlyList<Rect>? CssAutomationBounds(int start, int length)
    {
        PrepareCssEditingLayout();
        return _cssDocumentView?.EditorExposedBounds(start, length).Select(CssToEditor).ToArray();
    }

    internal IReadOnlyList<AutomationTextLine> GetAutomationTextLines()
    {
        Rect bounds = GetContentBounds();
        var layout = EnsureLayout(bounds.Width);
        var result = new List<AutomationTextLine>();
        if (layout == null) return result;
        if (_cssDocumentView is { } view)
        {
            foreach (var line in CssEditorLines)
            {
                var start = view.EditorExposedOffset(line.Start);
                result.Add(new(start, Math.Max(0, view.EditorExposedOffset(line.End) - start),
                    CssToEditor(new(line.Left, line.Top, Math.Max(1, line.Width), line.Height))));
            }
            if (result.Count == 0 && view.EditorExposedText.Length == 0)
                result.Add(new(0, 0, new(bounds.X, bounds.Y, 1, GetDefaultLineHeight())));
            return result;
        }
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
        if (_cssDocumentView is { } view)
        {
            var caret = view.EditorCaret(view.EditorSourceOffset(offset), false);
            if (caret is not { } raw) return;
            var rect = CssToEditor(raw);
            var cssVertical = _verticalOffset + (rect.Top < bounds.Top ? rect.Top - bounds.Top : Math.Max(0, rect.Bottom - bounds.Bottom));
            var cssHorizontal = _horizontalOffset + (rect.Left < bounds.Left ? rect.Left - bounds.Left : Math.Max(0, rect.Right + 1 - bounds.Right));
            VerticalOffsetCore = Math.Clamp(cssVertical, 0, Math.Max(0, layout.TotalHeight - bounds.Height));
            HorizontalOffsetCore = Math.Clamp(cssHorizontal, 0, Math.Max(0, layout.TotalWidth - bounds.Width));
            InvalidateArrange();
            return;
        }
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
        if (index >= 0 && _cssDocumentView is { } view) index = view.EditorExposedOffset(index);
        return index >= 0;
    }

    internal bool ReplaceAutomationSelection(string text) => TryReplaceImeText(
        Selection.Start.DocumentOffset, Selection.End.DocumentOffset - Selection.Start.DocumentOffset, text);

    internal bool ReplaceAutomationText(string text) => TryReplaceImeText(0, _document.GetText().Length, text);
}
