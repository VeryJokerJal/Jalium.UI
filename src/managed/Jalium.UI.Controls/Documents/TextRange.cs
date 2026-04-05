using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a selection of content between two TextPointer positions.
/// </summary>
public sealed class TextRange
{
    #region Fields

    private TextPointer _start;
    private TextPointer _end;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new TextRange spanning the specified positions.
    /// </summary>
    /// <param name="position1">The first position.</param>
    /// <param name="position2">The second position.</param>
    public TextRange(TextPointer position1, TextPointer position2)
    {
        ArgumentNullException.ThrowIfNull(position1);
        ArgumentNullException.ThrowIfNull(position2);

        if (!ReferenceEquals(position1.Document, position2.Document))
            throw new ArgumentException("Both positions must be from the same document.");

        // Ensure start is before end
        if (position1.CompareTo(position2) <= 0)
        {
            _start = position1;
            _end = position2;
        }
        else
        {
            _start = position2;
            _end = position1;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the position that marks the beginning of the selection.
    /// </summary>
    public TextPointer Start => _start;

    /// <summary>
    /// Gets the position that marks the end of the selection.
    /// </summary>
    public TextPointer End => _end;

    /// <summary>
    /// Gets a value indicating whether the range is empty.
    /// </summary>
    public bool IsEmpty => _start.CompareTo(_end) == 0;

    /// <summary>
    /// Gets or sets the plain text content of the range.
    /// </summary>
    public string Text
    {
        get => GetText();
        set => SetText(value);
    }

    #endregion

    #region Text Methods

    private string GetText()
    {
        if (IsEmpty)
            return string.Empty;

        var sb = new StringBuilder();
        var document = _start.Document;

        CollectTextFromDocument(document, sb);

        return sb.ToString();
    }

    private void CollectTextFromDocument(FlowDocument document, StringBuilder sb)
    {
        int currentOffset = 0;
        int startOffset = _start.DocumentOffset;
        int endOffset = _end.DocumentOffset;

        foreach (var block in document.Blocks)
        {
            var blockLength = GetBlockLength(block);

            if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
            {
                CollectTextFromBlock(block, sb, currentOffset, startOffset, endOffset);
            }

            currentOffset += blockLength;
            if (currentOffset >= endOffset)
                break;
        }
    }

    private void CollectTextFromBlock(Block block, StringBuilder sb, int baseOffset, int startOffset, int endOffset)
    {
        if (block is Paragraph p)
        {
            int currentOffset = baseOffset;

            foreach (var inline in p.Inlines)
            {
                var inlineLength = GetInlineLength(inline);

                if (currentOffset + inlineLength > startOffset && currentOffset < endOffset)
                {
                    CollectTextFromInline(inline, sb, currentOffset, startOffset, endOffset);
                }

                currentOffset += inlineLength;
                if (currentOffset >= endOffset)
                    break;
            }

            // Add paragraph break if within range and not at end
            if (currentOffset >= startOffset && currentOffset < endOffset)
            {
                sb.Append('\n');
            }
        }
        else if (block is Section section)
        {
            int currentOffset = baseOffset;
            foreach (var childBlock in section.Blocks)
            {
                var blockLength = GetBlockLength(childBlock);

                if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
                {
                    CollectTextFromBlock(childBlock, sb, currentOffset, startOffset, endOffset);
                }

                currentOffset += blockLength;
                if (currentOffset >= endOffset)
                    break;
            }
        }
        else if (block is List list)
        {
            int currentOffset = baseOffset;
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    var blockLength = GetBlockLength(itemBlock);

                    if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
                    {
                        CollectTextFromBlock(itemBlock, sb, currentOffset, startOffset, endOffset);
                    }

                    currentOffset += blockLength;
                    if (currentOffset >= endOffset)
                        break;
                }
            }
        }
    }

    private void CollectTextFromInline(Inline inline, StringBuilder sb, int baseOffset, int startOffset, int endOffset)
    {
        if (inline is Run run)
        {
            var text = run.Text;
            int textStart = Math.Max(0, startOffset - baseOffset);
            int textEnd = Math.Min(text.Length, endOffset - baseOffset);

            if (textStart < textEnd)
            {
                sb.Append(text, textStart, textEnd - textStart);
            }
        }
        else if (inline is Span span)
        {
            int currentOffset = baseOffset;
            foreach (var child in span.Inlines)
            {
                var childLength = GetInlineLength(child);

                if (currentOffset + childLength > startOffset && currentOffset < endOffset)
                {
                    CollectTextFromInline(child, sb, currentOffset, startOffset, endOffset);
                }

                currentOffset += childLength;
                if (currentOffset >= endOffset)
                    break;
            }
        }
        else if (inline is LineBreak)
        {
            if (baseOffset >= startOffset && baseOffset < endOffset)
            {
                sb.Append('\n');
            }
        }
    }

    private void SetText(string value)
    {
                    value ??= string.Empty;

        // Delete existing content
        DeleteContent();

        // Insert new text at start position
        InsertTextAtPosition(_start, value);
    }

    private void DeleteContent()
    {
        if (IsEmpty)
            return;

        int startOffset = _start.DocumentOffset;
        int endOffset = _end.DocumentOffset;

        // Simple case: selection is within a single Run
        if (_start.Parent is Run startRun && _end.Parent is Run endRun && startRun == endRun)
        {
            var text = startRun.Text;
            var localStart = _start.Offset;
            var localEnd = _end.Offset;

            startRun.Text = text.Substring(0, localStart) + text.Substring(localEnd);

            // Remove empty runs
            if (startRun.Text.Length == 0 && startRun.Parent is Paragraph p && p.Inlines.Count > 1)
            {
                p.Inlines.Remove(startRun);
            }

            _end = _start;
            return;
        }

        // General case: deletion spans multiple elements within the same paragraph
        var paragraph = _start.Paragraph;
        if (paragraph == null)
            return;

        int currentOffset = 0;
        // Calculate paragraph base offset
        foreach (var block in _start.Document.Blocks)
        {
            if (block == paragraph)
                break;
            currentOffset += GetBlockLength(block);
        }

        // Process each inline in the paragraph
        var inlinesToRemove = new List<Inline>();
        int inlineOffset = currentOffset;

        foreach (var inline in paragraph.Inlines)
        {
            int inlineLength = GetInlineLength(inline);
            int inlineEnd = inlineOffset + inlineLength;

            if (inline is Run run)
            {
                if (inlineOffset >= startOffset && inlineEnd <= endOffset)
                {
                    // Entire run is within selection - mark for removal
                    inlinesToRemove.Add(run);
                }
                else if (inlineOffset < startOffset && inlineEnd > endOffset)
                {
                    // Selection is entirely within this run
                    int localStart = startOffset - inlineOffset;
                    int localEnd = endOffset - inlineOffset;
                    run.Text = run.Text.Substring(0, localStart) + run.Text.Substring(localEnd);
                }
                else if (inlineOffset < startOffset && inlineEnd > startOffset)
                {
                    // Selection starts within this run
                    int localStart = startOffset - inlineOffset;
                    run.Text = run.Text.Substring(0, localStart);
                }
                else if (inlineOffset < endOffset && inlineEnd > endOffset)
                {
                    // Selection ends within this run
                    int localEnd = endOffset - inlineOffset;
                    run.Text = run.Text.Substring(localEnd);
                }
            }

            inlineOffset = inlineEnd;
        }

        // Remove fully-deleted runs (but keep at least one inline)
        foreach (var inline in inlinesToRemove)
        {
            if (paragraph.Inlines.Count > 1)
            {
                paragraph.Inlines.Remove(inline);
            }
            else if (inline is Run emptyRun)
            {
                emptyRun.Text = string.Empty;
            }
        }

        // Update positions
        _end = _start;
    }

    private void InsertTextAtPosition(TextPointer position, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (position.Parent is Run run)
        {
            var offset = position.Offset;
            run.Text = run.Text.Insert(offset, text);
        }
        else if (position.Parent is Paragraph paragraph)
        {
            // Insert a new Run
            var newRun = new Run(text);

            if (position.Offset == 0)
            {
                paragraph.Inlines.Insert(0, newRun);
            }
            else
            {
                paragraph.Inlines.Add(newRun);
            }
        }
    }

    #endregion

    #region Formatting Methods

    /// <summary>
    /// Gets the value of a formatting property for this range.
    /// </summary>
    /// <param name="property">The property to get.</param>
    /// <returns>The property value, or DependencyProperty.UnsetValue if mixed values exist.</returns>
    public object? GetPropertyValue(DependencyProperty property)
    {
        if (IsEmpty)
        {
            // Return the value at the start position
            return _start.Parent?.GetValue(property);
        }

        // Check if all elements in the range have the same value
        object? firstValue = null;
        bool isFirst = true;

        var document = _start.Document;
        int startOffset = _start.DocumentOffset;
        int endOffset = _end.DocumentOffset;

        foreach (var block in document.Blocks)
        {
            if (!CheckPropertyInBlock(block, property, ref firstValue, ref isFirst, 0, startOffset, endOffset))
            {
                return DependencyProperty.UnsetValue;
            }
        }

        return firstValue;
    }

    private bool CheckPropertyInBlock(Block block, DependencyProperty property, ref object? firstValue, ref bool isFirst, int baseOffset, int startOffset, int endOffset)
    {
        var blockLength = GetBlockLength(block);

        if (baseOffset + blockLength <= startOffset || baseOffset >= endOffset)
            return true; // Block is outside range

        if (block is Paragraph p)
        {
            int currentOffset = baseOffset;

            foreach (var inline in p.Inlines)
            {
                var inlineLength = GetInlineLength(inline);

                if (currentOffset + inlineLength > startOffset && currentOffset < endOffset)
                {
                    if (!CheckPropertyInInline(inline, property, ref firstValue, ref isFirst, currentOffset, startOffset, endOffset))
                        return false;
                }

                currentOffset += inlineLength;
            }
        }
        else if (block is Section section)
        {
            int currentOffset = baseOffset;
            foreach (var childBlock in section.Blocks)
            {
                if (!CheckPropertyInBlock(childBlock, property, ref firstValue, ref isFirst, currentOffset, startOffset, endOffset))
                    return false;
                currentOffset += GetBlockLength(childBlock);
            }
        }

        return true;
    }

    private bool CheckPropertyInInline(Inline inline, DependencyProperty property, ref object? firstValue, ref bool isFirst, int baseOffset, int startOffset, int endOffset)
    {
        if (inline is Run || inline is LineBreak)
        {
            var value = inline.GetValue(property);

            if (isFirst)
            {
                firstValue = value;
                isFirst = false;
            }
            else if (!Equals(firstValue, value))
            {
                return false; // Mixed values
            }
        }
        else if (inline is Span span)
        {
            int currentOffset = baseOffset;
            foreach (var child in span.Inlines)
            {
                var childLength = GetInlineLength(child);

                if (currentOffset + childLength > startOffset && currentOffset < endOffset)
                {
                    if (!CheckPropertyInInline(child, property, ref firstValue, ref isFirst, currentOffset, startOffset, endOffset))
                        return false;
                }

                currentOffset += childLength;
            }
        }

        return true;
    }

    /// <summary>
    /// Applies a formatting property to this range.
    /// </summary>
    /// <param name="property">The property to set.</param>
    /// <param name="value">The value to apply.</param>
    public void ApplyPropertyValue(DependencyProperty property, object? value)
    {
        if (IsEmpty)
            return;

        var document = _start.Document;
        int startOffset = _start.DocumentOffset;
        int endOffset = _end.DocumentOffset;
        int currentOffset = 0;

        foreach (var block in document.Blocks)
        {
            var blockLength = GetBlockLength(block);

            if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
            {
                ApplyPropertyToBlock(block, property, value, currentOffset, startOffset, endOffset);
            }

            currentOffset += blockLength;
            if (currentOffset >= endOffset)
                break;
        }
    }

    private void ApplyPropertyToBlock(Block block, DependencyProperty property, object? value, int baseOffset, int startOffset, int endOffset)
    {
        if (block is Paragraph p)
        {
            int currentOffset = baseOffset;

            foreach (var inline in p.Inlines)
            {
                var inlineLength = GetInlineLength(inline);

                if (currentOffset + inlineLength > startOffset && currentOffset < endOffset)
                {
                    ApplyPropertyToInline(inline, property, value, currentOffset, startOffset, endOffset);
                }

                currentOffset += inlineLength;
            }
        }
        else if (block is Section section)
        {
            int currentOffset = baseOffset;
            foreach (var childBlock in section.Blocks)
            {
                var blockLength = GetBlockLength(childBlock);

                if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
                {
                    ApplyPropertyToBlock(childBlock, property, value, currentOffset, startOffset, endOffset);
                }

                currentOffset += blockLength;
            }
        }
    }

    private void ApplyPropertyToInline(Inline inline, DependencyProperty property, object? value, int baseOffset, int startOffset, int endOffset)
    {
        var inlineLength = GetInlineLength(inline);

        // Check if the entire inline is within the range
        if (baseOffset >= startOffset && baseOffset + inlineLength <= endOffset)
        {
            // Apply to entire inline
            inline.SetValue(property, value);
        }
        else if (inline is Run run)
        {
            // Need to split the run
            // This is a complex operation that would require splitting the run
            // into multiple runs and wrapping the selected portion in a Span
            // For now, apply to the whole run
            run.SetValue(property, value);
        }
        else if (inline is Span span)
        {
            int currentOffset = baseOffset;
            foreach (var child in span.Inlines)
            {
                var childLength = GetInlineLength(child);

                if (currentOffset + childLength > startOffset && currentOffset < endOffset)
                {
                    ApplyPropertyToInline(child, property, value, currentOffset, startOffset, endOffset);
                }

                currentOffset += childLength;
            }
        }
    }

    #endregion

    #region Selection Methods

    /// <summary>
    /// Selects the content between the specified positions.
    /// </summary>
    /// <param name="position1">The first position.</param>
    /// <param name="position2">The second position.</param>
    public void Select(TextPointer position1, TextPointer position2)
    {
        ArgumentNullException.ThrowIfNull(position1);
        ArgumentNullException.ThrowIfNull(position2);

        if (!ReferenceEquals(position1.Document, _start.Document) ||
            !ReferenceEquals(position2.Document, _start.Document))
        {
            throw new ArgumentException("Positions must be from the same document as this TextRange.");
        }

        if (position1.CompareTo(position2) <= 0)
        {
            _start = position1;
            _end = position2;
        }
        else
        {
            _start = position2;
            _end = position1;
        }

        OnChanged();
    }

    /// <summary>
    /// Determines whether the range contains the specified position.
    /// </summary>
    /// <param name="position">The position to check.</param>
    /// <returns>True if the position is within the range.</returns>
    public bool Contains(TextPointer position)
    {
        if (position == null || !ReferenceEquals(position.Document, _start.Document))
            return false;

        return position.CompareTo(_start) >= 0 && position.CompareTo(_end) <= 0;
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the range changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raises the Changed event.
    /// </summary>
    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
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
            return 1;
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

    #endregion
}
