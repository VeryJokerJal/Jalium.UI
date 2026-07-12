using Jalium.UI.Controls;

namespace Jalium.UI.Documents;

/// <summary>
/// Specifies the direction of movement or retrieval for a TextPointer.
/// </summary>
public enum LogicalDirection
{
    /// <summary>
    /// Backward, or toward the beginning of the document.
    /// </summary>
    Backward,

    /// <summary>
    /// Forward, or toward the end of the document.
    /// </summary>
    Forward
}

/// <summary>
/// Specifies the category of content adjacent to a TextPointer position.
/// </summary>
public enum TextPointerContext
{
    /// <summary>
    /// No content. Position is at the beginning or end of the document.
    /// </summary>
    None = 0,

    /// <summary>
    /// Text content.
    /// </summary>
    Text = 1,

    /// <summary>
    /// An embedded object.
    /// </summary>
    EmbeddedElement = 2,

    /// <summary>
    /// An element opening tag.
    /// </summary>
    ElementStart = 3,

    /// <summary>
    /// An element closing tag.
    /// </summary>
    ElementEnd = 4
}

/// <summary>
/// Represents an immutable position in a FlowDocument.
/// </summary>
public sealed class TextPointer : ContentPosition, IComparable<TextPointer>
{
    #region Fields

    private readonly FlowDocument _document;
    private readonly TextElement? _parent;
    private readonly int _offset;
    private readonly LogicalDirection _logicalDirection;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a TextPointer at the specified position.
    /// </summary>
    internal TextPointer(FlowDocument document, TextElement? parent, int offset, LogicalDirection direction)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _parent = parent;
        _offset = offset;
        _logicalDirection = direction;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the FlowDocument that contains this TextPointer.
    /// </summary>
    public FlowDocument Document => _document;

    /// <summary>
    /// Gets the parent element at this position.
    /// </summary>
    public DependencyObject? Parent => _parent;

    /// <summary>
    /// Gets the logical direction of this TextPointer.
    /// </summary>
    public LogicalDirection LogicalDirection => _logicalDirection;

    /// <summary>
    /// Gets the offset from the beginning of the parent element.
    /// </summary>
    public int Offset => _offset;

    /// <summary>Gets the first content position in this document.</summary>
    public TextPointer DocumentStart => _document.ContentStart;

    /// <summary>Gets the final content position in this document.</summary>
    public TextPointer DocumentEnd => _document.ContentEnd;

    /// <summary>Gets whether layout information for this position is current.</summary>
    public bool HasValidLayout => false;

    /// <summary>Gets whether this position is at the beginning of a logical line.</summary>
    public bool IsAtLineStartPosition =>
        _parent is Paragraph && _offset == 0 || _parent is Run && _offset == 0 && _parent.Parent is Paragraph;

    /// <summary>
    /// Gets a value indicating whether this TextPointer is at the start of its containing paragraph.
    /// </summary>
    public bool IsAtInsertionPosition
    {
        get
        {
            // At an insertion position if we're in a text element or at an element boundary
            return _parent is Run || _parent is Paragraph || _parent == null;
        }
    }

    /// <summary>
    /// Gets the paragraph that contains this TextPointer.
    /// </summary>
    public Paragraph? Paragraph
    {
        get
        {
            var element = _parent;
            while (element != null)
            {
                if (element is Paragraph p)
                    return p;
                element = element.Parent;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the character offset from the start of the document.
    /// </summary>
    public int DocumentOffset
    {
        get => _document.GetDocumentOffset(_parent, _offset);
    }

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Returns a TextPointer at the specified offset from this position.
    /// </summary>
    /// <param name="offset">The number of positions to move (positive = forward, negative = backward).</param>
    /// <returns>A new TextPointer at the specified offset, or null if the offset is invalid.</returns>
    public TextPointer? GetPositionAtOffset(int offset)
    {
        return GetPositionAtOffset(offset, _logicalDirection);
    }

    /// <summary>
    /// Returns a TextPointer at the specified offset from this position.
    /// </summary>
    /// <param name="offset">The number of positions to move.</param>
    /// <param name="direction">The logical direction for the new TextPointer.</param>
    /// <returns>A new TextPointer at the specified offset, or null if the offset is invalid.</returns>
    public TextPointer? GetPositionAtOffset(int offset, LogicalDirection direction)
    {
        var docOffset = DocumentOffset + offset;
        if (docOffset < 0)
            return null;

        return _document.GetPositionAtOffset(docOffset, direction);
    }

    /// <summary>
    /// Returns the next insertion position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to search.</param>
    /// <returns>The next insertion position, or null if none exists.</returns>
    public TextPointer? GetNextInsertionPosition(LogicalDirection direction)
    {
        // Step over a whole grapheme cluster when this position sits inside a
        // Run's text, so caret motion and Backspace/Delete treat an emoji — a
        // ZWJ sequence, skin-tone modifier or flag included — as one unit. At a
        // Run edge or a non-Run element the step is one structural position.
        int step = 1;
        if (_parent is Run run && run.Text.Length > 0)
        {
            if (direction == LogicalDirection.Forward)
            {
                if (_offset < run.Text.Length)
                    step = GraphemeClusters.NextBoundary(run.Text, _offset) - _offset;
            }
            else if (_offset > 0)
            {
                step = _offset - GraphemeClusters.PreviousBoundary(run.Text, _offset);
            }
        }

        return GetPositionAtOffset(direction == LogicalDirection.Forward ? step : -step, direction);
    }

    /// <summary>
    /// Returns the TextPointer at the start of the current line.
    /// </summary>
    /// <returns>A TextPointer at the start of the current line.</returns>
    public TextPointer? GetLineStartPosition(int count)
    {
        return GetLineStartPosition(count, out _);
    }

    /// <summary>Returns the start of a nearby logical line and reports the number moved.</summary>
    public TextPointer? GetLineStartPosition(int count, out int actualCount)
    {
        actualCount = 0;
        var paragraphs = _document.EnumerateParagraphs().ToList();
        var paragraph = Paragraph;
        if (paragraph is null)
        {
            return null;
        }

        var index = paragraphs.IndexOf(paragraph);
        if (index < 0)
        {
            return null;
        }

        var target = Math.Clamp(index + count, 0, paragraphs.Count - 1);
        actualCount = target - index;
        return new TextPointer(_document, paragraphs[target], 0, LogicalDirection.Forward);
    }

    /// <summary>Returns the nearest valid insertion position in the requested direction.</summary>
    public TextPointer GetInsertionPosition(LogicalDirection direction) =>
        _document.GetPositionAtOffset(DocumentOffset, direction) ?? this;

    /// <summary>Returns the next position whose adjacent content context can be queried.</summary>
    public TextPointer? GetNextContextPosition(LogicalDirection direction) =>
        GetPositionAtOffset(direction == LogicalDirection.Forward ? 1 : -1, direction);

    /// <summary>Returns the signed offset from this position to another position.</summary>
    public int GetOffsetToPosition(TextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(_document, position._document))
        {
            throw new ArgumentException("The TextPointer belongs to a different document.", nameof(position));
        }

        return position.DocumentOffset - DocumentOffset;
    }

    /// <summary>Returns whether two positions belong to the same document.</summary>
    public bool IsInSameDocument(TextPointer textPosition)
    {
        ArgumentNullException.ThrowIfNull(textPosition);
        return ReferenceEquals(_document, textPosition._document);
    }

    /// <summary>Copies adjacent run text into a caller-provided buffer.</summary>
    public int GetTextInRun(LogicalDirection direction, char[] textBuffer, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(textBuffer);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (startIndex > textBuffer.Length - count)
        {
            throw new ArgumentException("The requested buffer range is outside the array.");
        }

        var text = GetTextInRun(direction);
        var copied = Math.Min(count, text.Length);
        text.CopyTo(0, textBuffer, startIndex, copied);
        return copied;
    }

    /// <summary>Inserts text at this position in a run.</summary>
    public void InsertTextInRun(string textData)
    {
        ArgumentNullException.ThrowIfNull(textData);
        if (_parent is not Run run || _offset < 0 || _offset > run.Text.Length)
        {
            throw new InvalidOperationException("Text can only be inserted at a position inside a Run.");
        }

        run.Text = run.Text.Insert(_offset, textData);
    }

    /// <summary>Deletes up to the requested number of characters from the adjacent run.</summary>
    public int DeleteTextInRun(int count)
    {
        if (_parent is not Run run || count == 0)
        {
            return 0;
        }

        if (count > 0)
        {
            var removed = Math.Min(count, run.Text.Length - _offset);
            run.Text = run.Text.Remove(_offset, removed);
            return removed;
        }

        var backward = Math.Min(-count, _offset);
        run.Text = run.Text.Remove(_offset - backward, backward);
        return -backward;
    }

    /// <summary>Inserts a line break and returns the position following it.</summary>
    public TextPointer InsertLineBreak()
    {
        var offset = DocumentOffset;
        _ = new LineBreak(this);
        return _document.GetPositionAtOffset(offset + 1, LogicalDirection.Forward) ?? _document.ContentEnd;
    }

    /// <summary>Splits the containing paragraph and returns the start of the new paragraph.</summary>
    public TextPointer InsertParagraphBreak() => _document.InsertParagraphBreak(this);

    /// <summary>Returns an approximate layout rectangle for the adjacent character.</summary>
    public Rect GetCharacterRect(LogicalDirection direction)
    {
        var paragraph = Paragraph;
        if (paragraph is null)
        {
            return Rect.Empty;
        }

        var fontSize = Math.Max(1.0, paragraph.GetEffectiveFontSize());
        var lineHeight = double.IsFinite(paragraph.LineHeight) && paragraph.LineHeight > 0
            ? paragraph.LineHeight
            : fontSize * 1.35;
        var x = Math.Max(0, DocumentOffset) * fontSize * 0.55;
        return new Rect(x, 0, Math.Max(1.0, fontSize * 0.55), lineHeight);
    }

    /// <summary>
    /// Returns the content at this position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to check.</param>
    /// <returns>The type of content.</returns>
    public TextPointerContext GetPointerContext(LogicalDirection direction)
    {
        if (_parent == null)
            return TextPointerContext.None;

        if (_parent is Run run)
        {
            if (direction == LogicalDirection.Forward)
            {
                if (_offset < run.Text.Length)
                    return TextPointerContext.Text;
                return TextPointerContext.ElementEnd;
            }
            else
            {
                if (_offset > 0)
                    return TextPointerContext.Text;
                return TextPointerContext.ElementStart;
            }
        }

        if (_parent is InlineUIContainer || _parent is BlockUIContainer)
        {
            return TextPointerContext.EmbeddedElement;
        }

        if (direction == LogicalDirection.Forward)
        {
            return TextPointerContext.ElementEnd;
        }
        else
        {
            return TextPointerContext.ElementStart;
        }
    }

    /// <summary>
    /// Gets the text immediately following this position.
    /// </summary>
    /// <param name="direction">The direction to read.</param>
    /// <returns>The text, or an empty string if no text is adjacent.</returns>
    public string GetTextInRun(LogicalDirection direction)
    {
        if (_parent is not Run run)
            return string.Empty;

        if (direction == LogicalDirection.Forward)
        {
            return _offset < run.Text.Length ? run.Text.Substring(_offset) : string.Empty;
        }
        else
        {
            return _offset > 0 ? run.Text.Substring(0, _offset) : string.Empty;
        }
    }

    /// <summary>
    /// Gets the length of the text run in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to measure.</param>
    /// <returns>The length of the text run.</returns>
    public int GetTextRunLength(LogicalDirection direction)
    {
        if (_parent is not Run run)
            return 0;

        if (direction == LogicalDirection.Forward)
        {
            return run.Text.Length - _offset;
        }
        else
        {
            return _offset;
        }
    }

    /// <summary>
    /// Gets the adjacent element in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to look.</param>
    /// <returns>The adjacent element, or null.</returns>
    public DependencyObject? GetAdjacentElement(LogicalDirection direction)
    {
        var context = GetPointerContext(direction);

        if (context == TextPointerContext.ElementStart || context == TextPointerContext.ElementEnd)
        {
            return _parent;
        }

        if (context == TextPointerContext.EmbeddedElement)
        {
            if (_parent is InlineUIContainer inlineContainer)
                return inlineContainer.Child;
            if (_parent is BlockUIContainer blockContainer)
                return blockContainer.Child;
        }

        return null;
    }

    #endregion

    #region Comparison

    /// <summary>
    /// Compares this TextPointer with another.
    /// </summary>
    /// <param name="other">The other TextPointer.</param>
    /// <returns>-1 if this is before other, 0 if equal, 1 if this is after other.</returns>
    public int CompareTo(TextPointer? other)
    {
        if (other == null)
            return 1;

        if (!ReferenceEquals(_document, other._document))
            throw new InvalidOperationException("Cannot compare TextPointers from different documents.");

        var thisOffset = DocumentOffset;
        var otherOffset = other.DocumentOffset;

        return thisOffset.CompareTo(otherOffset);
    }

    /// <summary>
    /// Determines whether this TextPointer is in the same position as another.
    /// </summary>
    public bool IsAtSamePosition(TextPointer other)
    {
        return CompareTo(other) == 0;
    }

    #endregion

    #region Helper Methods

    private static int GetBlockLength(Block block)
    {
        if (block is Paragraph p)
        {
            int length = 0;
            foreach (var inline in p.Inlines)
            {
                length += GetInlineLength(inline);
            }
            return length + 1; // +1 for paragraph break
        }
        else if (block is Section section)
        {
            int length = 0;
            foreach (var childBlock in section.Blocks)
            {
                length += GetBlockLength(childBlock);
            }
            return length;
        }
        else if (block is List list)
        {
            int length = 0;
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    length += GetBlockLength(itemBlock);
                }
            }
            return length;
        }
        else if (block is BlockUIContainer)
        {
            return 1; // Embedded object counts as 1
        }

        return 0;
    }

    private static int GetInlineLength(Inline inline)
    {
        if (inline is Run run)
        {
            return run.Text.Length;
        }
        else if (inline is Span span)
        {
            int length = 0;
            foreach (var child in span.Inlines)
            {
                length += GetInlineLength(child);
            }
            return length;
        }
        else if (inline is LineBreak)
        {
            return 1;
        }
        else if (inline is InlineUIContainer)
        {
            return 1;
        }

        return 0;
    }

    private int GetOffsetInBlock(Block block, TextPointer pointer)
    {
        if (block is Paragraph p)
        {
            if (ReferenceEquals(pointer._parent, p))
            {
                return pointer._offset;
            }

            int offset = 0;
            foreach (var inline in p.Inlines)
            {
                var inlineOffset = GetOffsetInInline(inline, pointer, offset);
                if (inlineOffset >= 0)
                {
                    return inlineOffset;
                }
                offset += GetInlineLength(inline);
            }
        }
        else if (block is Section section)
        {
            int offset = 0;
            foreach (var childBlock in section.Blocks)
            {
                var blockOffset = GetOffsetInBlock(childBlock, pointer);
                if (blockOffset >= 0)
                {
                    return offset + blockOffset;
                }
                offset += GetBlockLength(childBlock);
            }
        }

        return -1; // Not found in this block
    }

    private int GetOffsetInInline(Inline inline, TextPointer pointer, int baseOffset)
    {
        if (ReferenceEquals(pointer._parent, inline))
        {
            return baseOffset + pointer._offset;
        }

        if (inline is Span span)
        {
            int offset = baseOffset;
            foreach (var child in span.Inlines)
            {
                var childOffset = GetOffsetInInline(child, pointer, offset);
                if (childOffset >= 0)
                {
                    return childOffset;
                }
                offset += GetInlineLength(child);
            }
        }

        return -1;
    }

    #endregion

    #region Operators

    /// <summary>
    /// Determines whether two TextPointers are equal.
    /// </summary>
    public static bool operator ==(TextPointer? left, TextPointer? right)
    {
        if (left is null)
            return right is null;
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two TextPointers are not equal.
    /// </summary>
    public static bool operator !=(TextPointer? left, TextPointer? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Determines whether this TextPointer is less than another.
    /// </summary>
    public static bool operator <(TextPointer? left, TextPointer? right)
    {
        if (left is null)
            return right is not null;
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Determines whether this TextPointer is greater than another.
    /// </summary>
    public static bool operator >(TextPointer? left, TextPointer? right)
    {
        if (left is null)
            return false;
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Determines whether this TextPointer is less than or equal to another.
    /// </summary>
    public static bool operator <=(TextPointer? left, TextPointer? right)
    {
        if (left is null)
            return true;
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Determines whether this TextPointer is greater than or equal to another.
    /// </summary>
    public static bool operator >=(TextPointer? left, TextPointer? right)
    {
        if (left is null)
            return right is null;
        return left.CompareTo(right) >= 0;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is TextPointer other)
        {
            return ReferenceEquals(_document, other._document) && CompareTo(other) == 0;
        }
        return false;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(_document, DocumentOffset);
    }

    #endregion
}
