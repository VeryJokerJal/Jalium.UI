namespace Jalium.UI.Documents;

/// <summary>
/// Implements the text-tree operations shared by the document constructors that accept
/// <see cref="TextPointer"/> values.
/// </summary>
internal static class DocumentInsertion
{
    internal static void InsertInline(Inline inline, TextPointer? insertionPosition)
    {
        ArgumentNullException.ThrowIfNull(inline);
        if (insertionPosition == null)
        {
            return;
        }

        if (inline.OwnerCollection != null)
        {
            throw new InvalidOperationException("The Inline already belongs to an InlineCollection.");
        }

        var paragraph = GetValidatedParagraph(insertionPosition);
        var boundary = CreateBoundary(insertionPosition, paragraph);
        boundary.Collection.Insert(boundary.Index, inline);
    }

    internal static void WrapRange(Span span, TextPointer start, TextPointer end)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);

        if (!ReferenceEquals(start.Document, end.Document))
        {
            throw new ArgumentException("The start and end positions belong to different text containers.");
        }

        if (start.CompareTo(end) > 0)
        {
            throw new ArgumentException("The start position must not follow the end position.");
        }

        var startParagraph = GetValidatedParagraph(start);
        var endParagraph = GetValidatedParagraph(end);
        if (!ReferenceEquals(startParagraph, endParagraph))
        {
            throw new ArgumentException("The start and end positions must belong to the same paragraph.");
        }

        if (HasHyperlinkAncestor(start.Parent) || HasHyperlinkAncestor(end.Parent))
        {
            throw new InvalidOperationException("A Hyperlink cannot be split by a range constructor.");
        }

        // Split the end first. If both pointers are in the same Run, this keeps the
        // original Run alive for the earlier start pointer.
        var endBoundary = CreateBoundary(end, endParagraph);
        var startBoundary = CreateBoundary(start, startParagraph);

        var guard = 0;
        while (!ReferenceEquals(startBoundary.Collection, endBoundary.Collection))
        {
            if (++guard > 64)
            {
                throw new InvalidOperationException("The text element ancestry is invalid.");
            }

            var startDepth = GetCollectionDepth(startBoundary.Collection);
            var endDepth = GetCollectionDepth(endBoundary.Collection);
            if (startDepth >= endDepth)
            {
                startBoundary = LiftBoundary(startBoundary);
            }
            else
            {
                endBoundary = LiftBoundary(endBoundary);
            }
        }

        var collection = startBoundary.Collection;
        var startIndex = startBoundary.Index;
        var endIndex = endBoundary.Index;
        if (startIndex > endIndex)
        {
            throw new ArgumentException("The start position must not follow the end position.");
        }

        var selectedCount = endIndex - startIndex;
        for (var index = 0; index < selectedCount; index++)
        {
            var child = collection[startIndex];
            collection.RemoveAt(startIndex);
            span.Inlines.Add(child);
        }

        collection.Insert(startIndex, span);
    }

    private static Paragraph GetValidatedParagraph(TextPointer position)
    {
        var paragraph = position.Paragraph;
        if (paragraph == null || !IsDescendantOrSelf(position.Parent, paragraph) ||
            !ContainsParagraph(position.Document.Blocks, paragraph))
        {
            throw new InvalidOperationException("The TextPointer is not a valid inline insertion position.");
        }

        return paragraph;
    }

    private static bool IsDescendantOrSelf(DependencyObject? element, TextElement ancestor)
    {
        while (element != null)
        {
            if (ReferenceEquals(element, ancestor))
            {
                return true;
            }

            element = (element as TextElement)?.Parent;
        }

        return false;
    }

    private static bool ContainsParagraph(IEnumerable<Block> blocks, Paragraph paragraph)
    {
        foreach (var block in blocks)
        {
            if (ReferenceEquals(block, paragraph))
            {
                return true;
            }

            switch (block)
            {
                case Paragraph candidate when ContainsParagraph(candidate.Inlines, paragraph):
                    return true;
                case Section section when ContainsParagraph(section.Blocks, paragraph):
                    return true;
                case List list:
                    foreach (var item in list.ListItems)
                    {
                        if (ContainsParagraph(item.Blocks, paragraph))
                        {
                            return true;
                        }
                    }
                    break;
                case Table table:
                    foreach (var rowGroup in table.RowGroups)
                    {
                        foreach (var row in rowGroup.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                if (ContainsParagraph(cell.Blocks, paragraph))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                    break;
            }
        }

        return false;
    }

    private static bool ContainsParagraph(IEnumerable<Inline> inlines, Paragraph paragraph)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Span span when ContainsParagraph(span.Inlines, paragraph):
                    return true;
                case AnchoredBlock anchoredBlock when ContainsParagraph(anchoredBlock.Blocks, paragraph):
                    return true;
            }
        }

        return false;
    }

    private static InlineBoundary CreateBoundary(TextPointer position, Paragraph paragraph)
    {
        switch (position.Parent)
        {
            case Run run:
                EnsureInlineBelongsToParagraph(run, paragraph);
                return SplitRun(run, position.Offset);

            case Span span:
                EnsureInlineBelongsToParagraph(span, paragraph);
                return FindBoundaryAtOffset(span.Inlines, position.Offset);

            case Inline inline:
                EnsureInlineBelongsToParagraph(inline, paragraph);
                return CreateLeafBoundary(inline, position.Offset);

            case Paragraph pointerParagraph when ReferenceEquals(pointerParagraph, paragraph):
                return FindBoundaryAtOffset(paragraph.Inlines, position.Offset);

            default:
                throw new InvalidOperationException("The TextPointer is not a valid inline insertion position.");
        }
    }

    private static void EnsureInlineBelongsToParagraph(Inline inline, Paragraph paragraph)
    {
        if (inline.OwnerCollection == null || !IsDescendantOrSelf(inline, paragraph))
        {
            throw new InvalidOperationException("The TextPointer is not a valid inline insertion position.");
        }
    }

    private static InlineBoundary SplitRun(Run run, int offset)
    {
        var collection = run.OwnerCollection
            ?? throw new InvalidOperationException("The Run is not attached to an InlineCollection.");
        if (offset < 0 || offset > run.Text.Length)
        {
            throw new InvalidOperationException("The TextPointer offset is outside its Run.");
        }

        var index = collection.IndexOf(run);
        if (index < 0)
        {
            throw new InvalidOperationException("The Run ownership is inconsistent.");
        }

        if (offset == 0)
        {
            return InlineBoundary.Before(collection, run);
        }

        if (offset == run.Text.Length)
        {
            return InlineBoundary.After(collection, run);
        }

        var originalText = run.Text;
        var trailingRun = new Run();
        CopyLocalValues(run, trailingRun);
        trailingRun.Text = originalText[offset..];
        run.Text = originalText[..offset];
        collection.Insert(index + 1, trailingRun);
        return new InlineBoundary(collection, run, trailingRun);
    }

    private static InlineBoundary FindBoundaryAtOffset(InlineCollection collection, int offset)
    {
        if (offset < 0)
        {
            throw new InvalidOperationException("The TextPointer offset is outside its parent.");
        }

        if (offset == 0)
        {
            return collection.Count == 0
                ? new InlineBoundary(collection, null, null)
                : InlineBoundary.Before(collection, collection[0]);
        }

        for (var index = 0; index < collection.Count; index++)
        {
            var inline = collection[index];
            var length = GetInlineLength(inline);
            if (offset < length)
            {
                return inline switch
                {
                    Run run => SplitRun(run, offset),
                    Span span => FindBoundaryAtOffset(span.Inlines, offset),
                    _ => throw new InvalidOperationException("The TextPointer offset is inside an indivisible inline element."),
                };
            }

            if (offset == length)
            {
                return InlineBoundary.After(collection, inline);
            }

            offset -= length;
        }

        throw new InvalidOperationException("The TextPointer offset is outside its parent.");
    }

    private static InlineBoundary CreateLeafBoundary(Inline inline, int offset)
    {
        var collection = inline.OwnerCollection
            ?? throw new InvalidOperationException("The Inline is not attached to an InlineCollection.");
        var length = GetInlineLength(inline);
        return offset switch
        {
            0 => InlineBoundary.Before(collection, inline),
            _ when offset == length => InlineBoundary.After(collection, inline),
            _ => throw new InvalidOperationException("The TextPointer offset is inside an indivisible inline element."),
        };
    }

    private static int GetInlineLength(Inline inline)
    {
        return inline switch
        {
            Run run => run.Text.Length,
            Span span => span.Inlines.Sum(GetInlineLength),
            LineBreak or InlineUIContainer or AnchoredBlock => 1,
            _ => 0,
        };
    }

    private static int GetCollectionDepth(InlineCollection collection)
    {
        var depth = 0;
        TextElement? owner = collection.Parent;
        while (owner is Span span)
        {
            depth++;
            owner = span.OwnerCollection?.Parent;
        }

        if (owner is not Paragraph)
        {
            throw new InvalidOperationException("The InlineCollection is not rooted in a Paragraph.");
        }

        return depth;
    }

    private static InlineBoundary LiftBoundary(InlineBoundary boundary)
    {
        if (boundary.Collection.Parent is not Span ownerSpan || ownerSpan.OwnerCollection == null)
        {
            throw new InvalidOperationException("The inline boundary cannot be lifted to a common parent.");
        }

        if (ownerSpan is Hyperlink)
        {
            throw new InvalidOperationException("A Hyperlink cannot be split by a range constructor.");
        }

        var parentCollection = ownerSpan.OwnerCollection;
        var ownerIndex = parentCollection.IndexOf(ownerSpan);
        if (ownerIndex < 0)
        {
            throw new InvalidOperationException("The Span ownership is inconsistent.");
        }

        var index = boundary.Index;
        if (index == 0)
        {
            return InlineBoundary.Before(parentCollection, ownerSpan);
        }

        if (index == boundary.Collection.Count)
        {
            return InlineBoundary.After(parentCollection, ownerSpan);
        }

        var trailingSpan = CreateSpanShell(ownerSpan);
        while (boundary.Collection.Count > index)
        {
            var child = boundary.Collection[index];
            boundary.Collection.RemoveAt(index);
            trailingSpan.Inlines.Add(child);
        }

        parentCollection.Insert(ownerIndex + 1, trailingSpan);
        return new InlineBoundary(parentCollection, ownerSpan, trailingSpan);
    }

    private static Span CreateSpanShell(Span source)
    {
        Span result = source switch
        {
            Bold => new Bold(),
            Italic => new Italic(),
            Underline => new Underline(),
            _ when source.GetType() == typeof(Span) => new Span(),
            _ => throw new InvalidOperationException($"Cannot split a {source.GetType().Name} element."),
        };

        CopyLocalValues(source, result);
        return result;
    }

    private static void CopyLocalValues(DependencyObject source, DependencyObject destination)
    {
        var values = source.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            var entry = values.Current;
            destination.SetValue(entry.Property, entry.Value);
        }
    }

    private static bool HasHyperlinkAncestor(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is Hyperlink)
            {
                return true;
            }

            element = (element as TextElement)?.Parent;
        }

        return false;
    }

    private sealed class InlineBoundary
    {
        private readonly Inline? _before;
        private readonly Inline? _after;

        internal InlineBoundary(InlineCollection collection, Inline? before, Inline? after)
        {
            Collection = collection;
            _before = before;
            _after = after;
        }

        internal InlineCollection Collection { get; }

        internal int Index
        {
            get
            {
                if (_after != null && ReferenceEquals(_after.OwnerCollection, Collection))
                {
                    return Collection.IndexOf(_after);
                }

                if (_before != null && ReferenceEquals(_before.OwnerCollection, Collection))
                {
                    return Collection.IndexOf(_before) + 1;
                }

                if (_before == null && _after == null && Collection.Count == 0)
                {
                    return 0;
                }

                throw new InvalidOperationException("The inline boundary is no longer valid.");
            }
        }

        internal static InlineBoundary Before(InlineCollection collection, Inline inline)
        {
            var index = collection.IndexOf(inline);
            if (index < 0)
            {
                throw new InvalidOperationException("The Inline ownership is inconsistent.");
            }

            return new InlineBoundary(collection, index == 0 ? null : collection[index - 1], inline);
        }

        internal static InlineBoundary After(InlineCollection collection, Inline inline)
        {
            var index = collection.IndexOf(inline);
            if (index < 0)
            {
                throw new InvalidOperationException("The Inline ownership is inconsistent.");
            }

            return new InlineBoundary(
                collection,
                inline,
                index + 1 < collection.Count ? collection[index + 1] : null);
        }
    }
}
