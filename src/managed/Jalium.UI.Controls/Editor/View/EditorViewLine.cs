namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Cached rendering data for a single visible line in the editor viewport.
/// </summary>
internal sealed class EditorViewLine
{
    /// <summary>
    /// Gets the 1-based document line number.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the document offset of this line's start.
    /// </summary>
    public int DocumentOffset { get; set; }

    /// <summary>
    /// Gets the length of this line's content.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the Y position of this line in the viewport.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets whether this line is collapsed due to folding.
    /// </summary>
    public bool IsCollapsed { get; set; }

    /// <summary>
    /// Gets or sets the syntax highlighting data for this line.
    /// </summary>
    public HighlightedLine? Highlighting { get; set; }

    /// <summary>
    /// Gets or sets whether highlighting needs to be recalculated.
    /// </summary>
    public bool IsHighlightingDirty { get; set; } = true;

    public EditorViewLine(int lineNumber)
    {
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Invalidates the highlighting data.
    /// </summary>
    public void InvalidateHighlighting()
    {
        Highlighting = null;
        IsHighlightingDirty = true;
    }
}
