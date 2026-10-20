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
        var original = _document;
        var nativeCurrent = current;
        current = () => ReferenceEquals(original, _document) && IsEnabled && !IsReadOnly && nativeCurrent() &&
            ReferenceEquals(original, _document) && IsEnabled && !IsReadOnly;
        string before = original.GetText();
        if (!IsEnabled || IsReadOnly || !current() || !ImeTextEncoding.TryNormalizeUtf16Range(before, start, length,
            out int normalizedStart, out int normalizedLength) || start != normalizedStart || length != normalizedLength) return false;
        using var json = JsonDocument.Parse(payload);
        var fragment = WritingToolsDocumentFragment.Parse(json.RootElement.GetProperty("proposal"));
        if (!fragment.TryPrepareSource(_document, before, start, length, json.RootElement)) return false;
        if (!current()) return false;
        using var change = DeclareChangeBlock();
        var state = SaveDocumentState();
        bool committed = false;
        try
        {
            var caret = fragment.Insert(original, start, length, current);
            if (!current()) return false;
            _caretPosition = caret;
            _selection = CreateOwnedSelection(_caretPosition, _caretPosition);
            _selectionAnchor = _caretPosition;
            OnSelectionChanged();
            if (!current()) return false;
            ResetCaretBlink(); EnsureCaretVisible(); InvalidateLayout(); InvalidateVisual();
            committed = true;
            return true;
        }
        finally
        {
            if (!committed && state.Snapshot.CanRestore(original, this))
            {
                if (ReferenceEquals(original, _document)) RestoreDocumentState(state);
                else state.Snapshot.Restore(original);
            }
        }
    }
}

