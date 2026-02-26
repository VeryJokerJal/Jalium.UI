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
    None,

    /// <summary>
    /// Text content.
    /// </summary>
    Text,

    /// <summary>
    /// An element opening tag.
    /// </summary>
    ElementStart,

    /// <summary>
    /// An element closing tag.
    /// </summary>
    ElementEnd,

    /// <summary>
    /// An embedded object.
    /// </summary>
    EmbeddedElement
}

/// <summary>
/// Represents an immutable position in a FlowDocument.
/// </summary>
public sealed class TextPointer : IComparable<TextPointer>
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
    public TextElement? Parent => _parent;

    /// <summary>
    /// Gets the logical direction of this TextPointer.
    /// </summary>
    public LogicalDirection LogicalDirection => _logicalDirection;

    /// <summary>
    /// Gets the offset from the beginning of the parent element.
    /// </summary>
    public int Offset => _offset;

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
        get
        {
            int offset = 0;

            // Calculate offset by traversing document from start to this position
            foreach (var block in _document.Blocks)
            {
                var blockOffset = GetOffsetInBlock(block, this);
                if (blockOffset >= 0)
                {
                    return offset + blockOffset;
                }
                offset += GetBlockLength(block);
            }

            return offset;
        }
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
        if (direction == LogicalDirection.Forward)
        {
            return GetPositionAtOffset(1, direction);
        }
        else
        {
            return GetPositionAtOffset(-1, direction);
        }
    }

    /// <summary>
    /// Returns the TextPointer at the start of the current line.
    /// </summary>
    /// <returns>A TextPointer at the start of the current line.</returns>
    public TextPointer? GetLineStartPosition(int count)
    {
        // For now, return the start of the paragraph
        var paragraph = Paragraph;
        if (paragraph == null)
            return null;

        return new TextPointer(_document, paragraph, 0, LogicalDirection.Forward);
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
