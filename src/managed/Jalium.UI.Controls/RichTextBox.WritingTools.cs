using Jalium.UI.Media;
using Jalium.UI.Documents;
using System.Text.Json;
namespace Jalium.UI.Controls;
public partial class RichTextBox
{
    internal void InvalidateCssWritingToolsText()
    {
        if (_cssDocumentHost is { } host) Invalidate(host);
        static void Invalidate(DependencyObject visual)
        {
            if (visual is UIElement element) element.InvalidateVisual();
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
                if (VisualTreeHelper.GetChild(visual, index) is { } child) Invalidate(child);
        }
    }
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
        if (!IsEnabled || IsReadOnly || !current()) return false;
        PrepareCssEditingLayout();
        var view = _cssDocumentView;
        string before = view?.EditorExposedText ?? _document.GetText();
        if (!ImeTextEncoding.TryNormalizeUtf16Range(before, start, length,
            out int normalizedStart, out int normalizedLength) || start != normalizedStart || length != normalizedLength) return false;
        using var json = JsonDocument.Parse(payload);
        var fragment = WritingToolsDocumentFragment.Parse(json.RootElement.GetProperty("proposal"));
        if (!fragment.TryPrepareSource(original, before, start, length, json.RootElement,
            view is null ? null : (at, count) => view.EditorSourceRanges(at, count),
            view is null ? null : at => view.EditorInsertionPosition(view.EditorSourceOffset(at)))) return false;
        string rawBefore = original.GetText();
        var retainedHidden = view is null ? Array.Empty<(TextElement Element, string Text)>()
            : HiddenRoots(Jalium.UI.Styling.CssNode.Get(original)).OfType<TextElement>()
                .Select(element => (Element: element, Text: rawBefore.Substring(original.GetDocumentOffset(element, 0), TextElement.GetContentLength(element)))).ToArray();
        if (!current()) return false;
        using var change = DeclareChangeBlock();
        var state = SaveDocumentState();
        bool committed = false;
        try
        {
            var caret = fragment.HasPreparedSource ? fragment.Insert(original, start, length, current)
                : view is null ? fragment.Insert(original, start, length, current)
                : InsertCssWritingToolsFragment(fragment, view, start, length, current);
            if (caret is null || !current() || !ReferenceEquals(original, _document)) return false;
            PrepareCssEditingLayout();
            string after = _cssDocumentView?.EditorExposedText ?? original.GetText();
            int suffix = before.Length - start - length;
            if (after.Length < start + suffix || !after.AsSpan(0, start).SequenceEqual(before.AsSpan(0, start)) ||
                !after.AsSpan(after.Length - suffix).SequenceEqual(before.AsSpan(before.Length - suffix))) return false;
            if (!current() || retainedHidden.Any(item => !ReferenceEquals(item.Element.GetFlowDocument(), original) ||
                !Jalium.UI.Styling.CssDisplayProperties.SuppressesBoxTree(item.Element) ||
                original.GetText().Substring(original.GetDocumentOffset(item.Element, 0), TextElement.GetContentLength(item.Element)) != item.Text)) return false;
            if (fragment.SourceRetired) return false;
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
            // A retired native request must not leave a half-inserted fragment
            // or an undo record. Never reclaim nodes moved to another owner.
            if (!committed && state.Snapshot.CanRestore(original, this))
            {
                if (ReferenceEquals(original, _document)) RestoreDocumentState(state);
                else state.Snapshot.Restore(original);
            }
        }
    }
}

