using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private bool _styledSelectionPainting;
    private static bool HasCssDocumentSelection(CssNode node) => CssSelection.For(node.Target) is { HasPaint: true } ||
        node.EnumerateChildren().Any(HasCssDocumentSelection);

    private bool RenderStyledSelection(DrawingContext context, LineLayoutInfo line, double x, double y)
    {
        if (_selection is null || _layoutCache is not { } layout) return false;
        var first = Math.Max(line.StartOffset, _selection.Start.DocumentOffset);
        var last = Math.Min(line.EndOffset, _selection.End.DocumentOffset);
        if (last <= first) return false;
        var content = GetContentBounds();
        var regions = new List<CssSelectionPainter.Region>();
        void Add(int start, int end, DependencyObject source, Brush? foreground)
        {
            if (end <= start) return;
            var style = CssSelection.For(source);
            void Rectangle(Rect rect)
            {
                rect = ClipToCell(layout, line, rect, content);
                if (rect.Width > 0 && rect.Height > 0) regions.Add(new(rect, style, foreground, y + line.Baseline));
            }
            if (line.NativeLine is { } native)
            {
                var local = Math.Clamp(start - line.ParagraphOffset, 0, native.Paragraph.Text.Length);
                var count = Math.Clamp(end - start, 0, native.Paragraph.Text.Length - local);
                foreach (var rect in native.Paragraph.Selection(native.Index, local, count))
                    Rectangle(new(x + rect.X, y, rect.Width, line.Height));
            }
            else
            {
                var left = GetXOffsetInLine(line, start); var right = GetXOffsetInLine(line, end);
                Rectangle(new(x + Math.Min(left, right), y, Math.Abs(right - left), line.Height));
            }
        }
        var cursor = first;
        foreach (var run in line.Runs)
        {
            var start = Math.Max(first, run.StartOffset); var end = Math.Min(last, run.EndOffset);
            if (end <= start) continue;
            Add(cursor, start, _document, ResolveDocumentForegroundBrush());
            Add(start, end, (DependencyObject?)run.Run ?? _document, run.Run?.Foreground ?? ResolveDocumentForegroundBrush());
            cursor = end;
        }
        Add(cursor, last, _document, ResolveDocumentForegroundBrush());
        if (regions.Count == 0) return false;
        CssSelectionPainter.Paint(context, new RectangleGeometry(content), regions, ResolveSelectionBrush(), SelectionTextBrush,
            paint => RenderLineCore(paint, line, x, y),
            (paint, region) => CssSelectionPainter.Decorations(paint, region, this));
        return true;
    }
}
