using Jalium.UI.Media;
using Jalium.UI.Documents;
using System.Text.Json;
namespace Jalium.UI.Controls;
public partial class RichTextBox
{
    internal void RenderWritingToolsText(DrawingContext context)
    {
        Rect rect = GetContentBounds();
        context.PushClip(new RectangleGeometry(rect));
        try { RenderDocument(context, rect); }
        finally { context.Pop(); }
    }
    private bool TryReplaceWritingToolsText(int start, int length, string payload, Func<bool> current)
    {
        if (!IsEnabled || IsReadOnly || !current() || !ImeTextEncoding.TryNormalizeUtf16Range(_document.GetText(), start, length,
            out int normalizedStart, out int normalizedLength) || start != normalizedStart || length != normalizedLength) return false;
        using var json = JsonDocument.Parse(payload);
        var fragment = WritingToolsDocumentFragment.Parse(json.RootElement.GetProperty("proposal"));
        if (!current()) return false;
        using var change = DeclareChangeBlock();
        _caretPosition = fragment.Insert(_document, start, length, current);
        if (!current()) return false;
        _selection = CreateOwnedSelection(_caretPosition, _caretPosition);
        _selectionAnchor = _caretPosition;
        OnSelectionChanged();
        if (!current()) return false;
        ResetCaretBlink(); EnsureCaretVisible(); InvalidateLayout(); InvalidateVisual();
        return true;
    }
}

