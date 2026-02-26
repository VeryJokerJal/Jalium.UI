namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Represents a collapsible region of code.
/// </summary>
public sealed class FoldingSection
{
    /// <summary>
    /// Gets the start line (1-based).
    /// </summary>
    public int StartLine { get; internal set; }

    /// <summary>
    /// Gets the end line (1-based).
    /// </summary>
    public int EndLine { get; internal set; }

    /// <summary>
    /// Gets the line (1-based) where the visual scope guide starts.
    /// Defaults to <see cref="StartLine"/>.
    /// </summary>
    public int GuideStartLine { get; internal set; }

    /// <summary>
    /// Gets the line (1-based) where the visual scope guide ends.
    /// Defaults to <see cref="EndLine"/>.
    /// </summary>
    public int GuideEndLine { get; internal set; }

    /// <summary>
    /// Gets the start column (0-based) of the folding trigger token on <see cref="StartLine"/>.
    /// </summary>
    public int StartColumn { get; internal set; } = -1;

    /// <summary>
    /// Gets or sets the title displayed when the section is collapsed.
    /// </summary>
    public string Title { get; set; } = "...";

    /// <summary>
    /// Gets or sets whether this section is collapsed.
    /// </summary>
    public bool IsFolded { get; set; }

    /// <summary>
    /// Gets the number of lines in this section.
    /// </summary>
    public int LineCount => EndLine - StartLine + 1;

    public FoldingSection(int startLine, int endLine, string title = "...", int startColumn = -1)
    {
        StartLine = startLine;
        EndLine = endLine;
        GuideStartLine = startLine;
        GuideEndLine = endLine;
        Title = title;
        StartColumn = startColumn;
    }
}
