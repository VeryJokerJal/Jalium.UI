namespace Jalium.UI.Documents;

/// <summary>
/// Edits plain text through the flow tree, retaining the nodes and formatting
/// outside the replaced range. Offsets use the document's UTF-16 text projection.
/// </summary>
internal static class DocumentTextEditing
{
    private static readonly DependencyProperty[] CharacterProperties =
    [
        TextElement.FontFamilyProperty, TextElement.FontSizeProperty,
        TextElement.FontStyleProperty, TextElement.FontWeightProperty,
        TextElement.FontStretchProperty, TextElement.ForegroundProperty,
        TextElement.BackgroundProperty, TextElement.TextDecorationsProperty,
        TextElement.TextEffectsProperty, Inline.BaselineAlignmentProperty,
    ];

    internal static TextPointer Replace(FlowDocument document, int start, int length, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var originalLength = document.GetText().Length;
        if (start < 0 || length < 0 || start > originalLength || length > originalLength - start)
            throw new ArgumentOutOfRangeException(nameof(start));

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var paragraphs = document.EnumerateParagraphs()
            .Select(p => (Paragraph: p, Start: document.GetDocumentOffset(p, 0), Length: TextElement.GetContentLength(p)))
            .ToArray();
        if (paragraphs.Length == 0)
        {
            if (text.Length == 0) return document.ContentStart;
            // A final newline is already represented by the implicit paragraph.
            if (text.EndsWith('\n')) text = text[..^1];
            var paragraph = new Paragraph();
            document.Blocks.Add(paragraph);
            return Insert(document, paragraph.ContentStart, text);
        }

        var first = FindParagraph(paragraphs, start);
        var last = FindParagraph(paragraphs, start + length);
        int firstOffset = Math.Clamp(start - paragraphs[first].Start, 0, paragraphs[first].Length);
        int lastOffset = Math.Clamp(start + length - paragraphs[last].Start, 0, paragraphs[last].Length);
        start = paragraphs[first].Start + firstOffset;
        var insertion = document.GetPositionAtOffset(start, LogicalDirection.Forward)!;
        var typingFormat = CaptureCharacterFormatting(insertion.Parent as TextElement ?? paragraphs[first].Paragraph);
        var keep = insertion.Parent as Run;

        if (start == 0 && length == originalLength && text.EndsWith('\n')) text = text[..^1];
        if (HasNonMergeableAncestor(insertion)) text = text.Replace('\n', ' ');
        var endPosition = document.GetPositionAtOffset(paragraphs[last].Start + lastOffset, LogicalDirection.Backward);
        if (text.Length == 0 && insertion.Offset == 0 && ReferenceEquals(endPosition?.Parent, keep) &&
            endPosition!.Offset == keep?.Text.Length && keep.OwnerCollection is { Count: > 1 })
            keep = null;
        if (first == last && keep is not null && ReferenceEquals(endPosition?.Parent, keep) && !text.Contains('\n'))
        {
            // A bound Run must publish the committed replacement to its source,
            // without exposing an intermediate delete through a two-way binding.
            keep.Text = keep.Text.Remove(insertion.Offset, endPosition!.Offset - insertion.Offset).Insert(insertion.Offset, text);
            return new TextPointer(document, keep, insertion.Offset + text.Length, LogicalDirection.Backward);
        }

        if (length != 0)
        {
            if (first == last)
            {
                DeleteInlines(paragraphs[first].Paragraph.Inlines, firstOffset, lastOffset, keep);
            }
            else
            {
                var startParagraph = paragraphs[first].Paragraph;
                var endParagraph = paragraphs[last].Paragraph;
                DeleteInlines(startParagraph.Inlines, firstOffset, paragraphs[first].Length, keep);
                DeleteInlines(endParagraph.Inlines, 0, lastOffset, null);
                // Moving the surviving suffix must not change its inherited
                // font merely because its paragraph had a different style.
                foreach (var inline in endParagraph.Inlines.ToArray())
                {
                    var formatting = CaptureCharacterFormatting(inline);
                    endParagraph.Inlines.Remove(inline);
                    startParagraph.Inlines.Add(inline);
                    ApplyCharacterFormatting(inline, formatting, preserveLocalValues: true);
                }
                for (int i = first + 1; i <= last; i++)
                    RemoveParagraphAndEmptyAncestors(paragraphs[i].Paragraph);
            }
        }

        if (keep?.GetFlowDocument() == document)
            insertion = new TextPointer(document, keep, Math.Min(insertion.Offset, keep.Text.Length), LogicalDirection.Forward);
        else
            insertion = document.GetPositionAtOffset(start, LogicalDirection.Forward) ?? paragraphs[first].Paragraph.ContentEnd;
        return Insert(document, insertion, text, typingFormat);
    }

    private static int FindParagraph((Paragraph Paragraph, int Start, int Length)[] paragraphs, int offset)
    {
        for (int i = 0; i < paragraphs.Length; i++)
            if (offset <= paragraphs[i].Start + paragraphs[i].Length) return i;
        return paragraphs.Length - 1;
    }

    private static void DeleteInlines(InlineCollection inlines, int start, int end, Run? keep)
    {
        int cursor = 0;
        foreach (var inline in inlines.ToArray())
        {
            int length = TextElement.GetContentLength(inline);
            int next = cursor + length;
            if (cursor < end && next > start)
            {
                if (inline is Run run)
                {
                    int localStart = Math.Clamp(start - cursor, 0, length);
                    int localEnd = Math.Clamp(end - cursor, localStart, length);
                    if (localStart == 0 && localEnd == length && !ReferenceEquals(run, keep))
                        inlines.Remove(run);
                    else
                        run.Text = run.Text.Remove(localStart, localEnd - localStart);
                }
                else if (inline is Span span)
                {
                    if (cursor >= start && next <= end && !ContainsRun(span, keep))
                        inlines.Remove(span);
                    else
                        DeleteInlines(span.Inlines, Math.Max(0, start - cursor), Math.Min(length, end - cursor), keep);
                }
                else if (cursor >= start && next <= end)
                    inlines.Remove(inline);
            }
            cursor = next;
        }
    }

    private static bool ContainsRun(Span span, Run? run)
    {
        for (TextElement? current = run; current is not null; current = current.Parent)
            if (ReferenceEquals(current, span)) return true;
        return false;
    }

    private static void RemoveParagraphAndEmptyAncestors(Paragraph paragraph)
    {
        var owner = paragraph.OwnerCollection;
        var parent = owner?.Parent;
        owner?.Remove(paragraph);
        while (parent is TextElement element)
        {
            switch (element)
            {
                case Section section when section.Blocks.Count == 0:
                    parent = section.OwnerCollection?.Parent;
                    section.OwnerCollection?.Remove(section);
                    break;
                case ListItem item when item.Blocks.Count == 0:
                    parent = item.Parent;
                    item.OwnerCollection?.Remove(item);
                    break;
                case List list when list.ListItems.Count == 0:
                    parent = list.OwnerCollection?.Parent;
                    list.OwnerCollection?.Remove(list);
                    break;
                default:
                    return;
            }
        }
    }

    private static TextPointer Insert(FlowDocument document, TextPointer position, string text,
        Dictionary<DependencyProperty, object?>? formatting = null)
    {
        if (text.Length == 0) return position;
        formatting ??= CaptureCharacterFormatting(position.Parent as TextElement ?? position.Paragraph!);
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length != 0)
            {
                if (position.Parent is Run run)
                {
                    int localOffset = Math.Clamp(position.Offset, 0, run.Text.Length);
                    run.Text = run.Text.Insert(localOffset, parts[i]);
                    position = new TextPointer(document, run, localOffset + parts[i].Length, LogicalDirection.Backward);
                }
                else
                {
                    var newRun = new Run(parts[i], position);
                    ApplyCharacterFormatting(newRun, formatting);
                    position = new TextPointer(document, newRun, newRun.Text.Length, LogicalDirection.Backward);
                }
            }
            if (i + 1 < parts.Length)
            {
                if (HasNonMergeableAncestor(position))
                {
                    var run = (Run)position.Parent!;
                    run.Text = run.Text.Insert(position.Offset, " ");
                    position = new TextPointer(document, run, position.Offset + 1, LogicalDirection.Backward);
                }
                else
                {
                    position = document.InsertParagraphBreak(position);
                    // Continuation inherits the typing format even when splitting
                    // at the end of a styled inline leaves no trailing text.
                    if (position.Paragraph is { Inlines.Count: 0 } paragraph)
                    {
                        var newRun = new Run();
                        ApplyCharacterFormatting(newRun, formatting);
                        paragraph.Inlines.Add(newRun);
                    }
                    position = document.GetPositionAtOffset(position.DocumentOffset, LogicalDirection.Forward) ?? position;
                }
            }
        }
        return position;
    }

    internal static bool HasNonMergeableAncestor(TextPointer position)
    {
        for (var current = position.Parent as TextElement; current is not null && current is not Paragraph; current = current.Parent)
            if (current is Span span && !DocumentInsertion.CanSplitSpan(span)) return true;
        return false;
    }

    internal static Dictionary<DependencyProperty, object?> CaptureCharacterFormatting(TextElement element)
    {
        var properties = new HashSet<DependencyProperty>(CharacterProperties);
        for (FrameworkContentElement? current = element; current is not null; current = current.Parent as FrameworkContentElement)
            foreach (var property in current.GetEffectiveSetPropertiesInternal())
                if (!property.ReadOnly && property.GetMetadata(typeof(Run)).Inherits &&
                    (property.OwnerType == typeof(TextElement) || property.OwnerType == typeof(Typography) ||
                     property.Name is "FlowDirection" or "Language"))
                    properties.Add(property);
        return properties.ToDictionary(property => property, element.GetValue);
    }

    internal static void ApplyCharacterFormatting(TextElement element,
        Dictionary<DependencyProperty, object?> formatting, bool preserveLocalValues = false)
    {
        foreach (var (property, value) in formatting)
            if (!Equals(element.GetValue(property), value) &&
                (!preserveLocalValues || ReferenceEquals(element.ReadLocalValue(property), DependencyProperty.UnsetValue)))
                element.SetValue(property, value);
    }
}
