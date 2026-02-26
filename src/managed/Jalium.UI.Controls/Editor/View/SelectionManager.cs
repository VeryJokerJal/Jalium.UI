namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Manages text selection state (anchor + active offset).
/// </summary>
internal sealed class SelectionManager
{
    /// <summary>
    /// Gets or sets the anchor offset (where selection started).
    /// </summary>
    public int AnchorOffset { get; set; }

    /// <summary>
    /// Gets or sets the active offset (where selection extends to, same as caret).
    /// </summary>
    public int ActiveOffset { get; set; }

    /// <summary>
    /// Gets the start offset of the selection (minimum of anchor and active).
    /// </summary>
    public int StartOffset => Math.Min(AnchorOffset, ActiveOffset);

    /// <summary>
    /// Gets the end offset of the selection (maximum of anchor and active).
    /// </summary>
    public int EndOffset => Math.Max(AnchorOffset, ActiveOffset);

    /// <summary>
    /// Gets the length of the selection.
    /// </summary>
    public int Length => EndOffset - StartOffset;

    /// <summary>
    /// Gets whether any text is selected.
    /// </summary>
    public bool HasSelection => Length > 0;

    /// <summary>
    /// Gets the selected text from the document.
    /// </summary>
    public string GetSelectedText(TextDocument document)
    {
        if (!HasSelection) return string.Empty;
        return document.GetText(StartOffset, Length);
    }

    /// <summary>
    /// Clears the selection (sets anchor = active).
    /// </summary>
    public void ClearSelection(int caretOffset)
    {
        int offset = Math.Max(0, caretOffset);
        AnchorOffset = offset;
        ActiveOffset = offset;
    }

    /// <summary>
    /// Selects all text in the document.
    /// </summary>
    public void SelectAll(TextDocument document)
    {
        AnchorOffset = 0;
        ActiveOffset = document.TextLength;
    }

    /// <summary>
    /// Extends the selection to the specified offset (moves active, keeps anchor).
    /// </summary>
    public void ExtendTo(int offset)
    {
        ActiveOffset = Math.Max(0, offset);
    }

    /// <summary>
    /// Sets an explicit selection range.
    /// </summary>
    public void SetSelection(int start, int length)
    {
        int clampedStart = Math.Max(0, start);
        int clampedLength = Math.Max(0, length);
        AnchorOffset = clampedStart;
        ActiveOffset = clampedStart + clampedLength;
    }

    /// <summary>
    /// Gets the selection range on a specific line for rendering.
    /// Returns null if the line has no selection.
    /// </summary>
    public (int startColumn, int endColumn)? GetSelectionOnLine(DocumentLine line)
    {
        if (!HasSelection) return null;

        int selStart = StartOffset;
        int selEnd = EndOffset;
        int lineStart = line.Offset;
        int lineEnd = line.Offset + line.Length;

        // No intersection
        if (selEnd <= lineStart || selStart >= lineEnd + line.DelimiterLength)
            return null;

        int startCol = Math.Max(0, selStart - lineStart);
        int endCol = Math.Min(line.Length, selEnd - lineStart);

        // If selection extends beyond line end, extend to full line
        if (selEnd > lineEnd)
            endCol = line.Length + 1; // +1 to indicate selection continues to next line

        return (startCol, endCol);
    }
}
