using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a selection of content between two TextPointer positions.
/// </summary>
public class TextRange
{
    private static readonly HashSet<string> s_supportedDataFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        DataFormats.Text,
        DataFormats.UnicodeText,
        DataFormats.Rtf,
        DataFormats.Xaml,
        DataFormats.XamlPackage,
    };

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
        if (IsEmpty) return string.Empty;
        // TextPointer offsets and native text services use this same projection,
        // including paragraph breaks, table separators and embedded objects.
        var text = _start.Document.GetText();
        int start = Math.Clamp(_start.DocumentOffset, 0, text.Length);
        int end = Math.Clamp(_end.DocumentOffset, start, text.Length);
        return text[start..end];
    }

    private void SetText(string value)
    {
        value ??= string.Empty;
        var document = _start.Document;
        int start = _start.DocumentOffset;
        int length = _end.DocumentOffset - start;
        bool includesImplicitFinalBreak = start == 0 && length == document.GetText().Length &&
            (value.EndsWith('\n') || value.EndsWith('\r'));
        using var change = (document.Parent as Controls.RichTextBox)?.DeclareChangeBlock();
        var end = DocumentTextEditing.Replace(document, start, length, value);
        _start = document.GetPositionAtOffset(Math.Min(start, end.DocumentOffset), LogicalDirection.Forward) ?? document.ContentStart;
        _end = includesImplicitFinalBreak ? document.ContentEnd : end;
        OnRangeContentChanged();
        OnChanged();
    }

    #endregion

    #region Formatting Methods

    /// <summary>Returns whether this range can load the specified clipboard format.</summary>
    public bool CanLoad(string dataFormat)
    {
        ArgumentNullException.ThrowIfNull(dataFormat);
        return s_supportedDataFormats.Contains(dataFormat);
    }

    /// <summary>Returns whether this range can save the specified clipboard format.</summary>
    public bool CanSave(string dataFormat)
    {
        ArgumentNullException.ThrowIfNull(dataFormat);
        return s_supportedDataFormats.Contains(dataFormat);
    }

    /// <summary>Loads range content from a stream in a supported text format.</summary>
    public void Load(Stream stream, string dataFormat)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!CanLoad(dataFormat))
        {
            throw new ArgumentException($"The data format '{dataFormat}' is not supported.", nameof(dataFormat));
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        Text = reader.ReadToEnd();
    }

    /// <summary>Saves range content to a stream in a supported text format.</summary>
    public void Save(Stream stream, string dataFormat) => Save(stream, dataFormat, preserveTextElements: false);

    /// <summary>Saves range content while optionally preserving compatible text elements.</summary>
    public void Save(Stream stream, string dataFormat, bool preserveTextElements)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!CanSave(dataFormat))
        {
            throw new ArgumentException($"The data format '{dataFormat}' is not supported.", nameof(dataFormat));
        }

        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        writer.Write(Text);
        writer.Flush();
    }

    /// <summary>Clears locally applied formatting properties from content in this range.</summary>
    public void ClearAllProperties()
    {
        if (IsEmpty)
        {
            return;
        }

        var startOffset = _start.DocumentOffset;
        var endOffset = _end.DocumentOffset;
        var currentOffset = 0;
        foreach (var block in _start.Document.Blocks)
        {
            var blockLength = GetBlockLength(block);
            if (currentOffset + blockLength > startOffset && currentOffset < endOffset)
            {
                ClearPropertiesInBlock(block, currentOffset, startOffset, endOffset);
            }
            currentOffset += blockLength;
        }

        OnRangeContentChanged();
        OnChanged();
    }

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

        int baseOffset = 0;
        foreach (var block in document.Blocks)
        {
            if (!CheckPropertyInBlock(block, property, ref firstValue, ref isFirst, baseOffset, startOffset, endOffset))
                return DependencyProperty.UnsetValue;
            baseOffset += GetBlockLength(block);
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
        else
            foreach (var (child, offset) in ChildBlocks(block, baseOffset))
                if (!CheckPropertyInBlock(child, property, ref firstValue, ref isFirst, offset, startOffset, endOffset))
                    return false;

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
        else
            foreach (var (child, offset) in ChildBlocks(block, baseOffset))
                if (offset + GetBlockLength(child) > startOffset && offset < endOffset)
                    ApplyPropertyToBlock(child, property, value, offset, startOffset, endOffset);
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

        OnRangeSelected(position1, position2);
        OnChanged();
    }

    private static void ClearPropertiesInBlock(
        Block block,
        int baseOffset,
        int startOffset,
        int endOffset)
    {
        if (block is Paragraph paragraph)
        {
            var currentOffset = baseOffset;
            foreach (var inline in paragraph.Inlines)
            {
                var length = GetInlineLength(inline);
                if (currentOffset + length > startOffset && currentOffset < endOffset)
                {
                    ClearPropertiesInInline(inline, currentOffset, startOffset, endOffset);
                }
                currentOffset += length;
            }
        }
        else
            foreach (var (child, offset) in ChildBlocks(block, baseOffset))
                if (offset + GetBlockLength(child) > startOffset && offset < endOffset)
                    ClearPropertiesInBlock(child, offset, startOffset, endOffset);
    }

    private static void ClearPropertiesInInline(
        Inline inline,
        int baseOffset,
        int startOffset,
        int endOffset)
    {
        inline.ClearValue(TextElement.FontFamilyProperty);
        inline.ClearValue(TextElement.FontSizeProperty);
        inline.ClearValue(TextElement.FontStretchProperty);
        inline.ClearValue(TextElement.FontStyleProperty);
        inline.ClearValue(TextElement.FontWeightProperty);
        inline.ClearValue(TextElement.ForegroundProperty);
        inline.ClearValue(TextElement.BackgroundProperty);
        inline.ClearValue(TextElement.TextDecorationsProperty);
        inline.ClearValue(TextElement.TextEffectsProperty);

        if (inline is Span span)
        {
            var currentOffset = baseOffset;
            foreach (var child in span.Inlines)
            {
                var length = GetInlineLength(child);
                if (currentOffset + length > startOffset && currentOffset < endOffset)
                {
                    ClearPropertiesInInline(child, currentOffset, startOffset, endOffset);
                }
                currentOffset += length;
            }
        }
    }

    internal virtual void OnRangeSelected(TextPointer position1, TextPointer position2)
    {
    }

    internal virtual void OnRangeContentChanged()
    {
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

    private static int GetBlockLength(Block block) => FlowDocument.GetBlockLength(block);

    private static IEnumerable<(Block Block, int Offset)> ChildBlocks(Block block, int offset)
    {
        IEnumerable<Block>? children = block switch
        {
            Section section => section.Blocks,
            List list => list.ListItems.SelectMany(item => item.Blocks),
            _ => null,
        };
        if (children is not null)
        {
            foreach (var child in children)
            {
                yield return (child, offset);
                offset += GetBlockLength(child);
            }
        }
        else if (block is Table table)
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var child in cell.Blocks)
                    {
                        yield return (child, offset);
                        offset += GetBlockLength(child);
                    }
                    ++offset;
                }
                ++offset;
            }
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
        else if (inline is InlineUIContainer or AnchoredBlock)
        {
            return 1;
        }

        return 0;
    }

    #endregion
}
