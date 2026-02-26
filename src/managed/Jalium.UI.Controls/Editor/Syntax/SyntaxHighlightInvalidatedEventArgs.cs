namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Event payload for syntax highlighting cache invalidation notifications.
/// </summary>
public sealed class SyntaxHighlightInvalidatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the 1-based starting line for invalidation.
    /// </summary>
    public int StartLine { get; }

    /// <summary>
    /// Gets whether the invalidation applies to the whole document.
    /// </summary>
    public bool AffectsWholeDocument { get; }

    public SyntaxHighlightInvalidatedEventArgs(int startLine, bool affectsWholeDocument)
    {
        StartLine = Math.Max(1, startLine);
        AffectsWholeDocument = affectsWholeDocument;
    }

    public static SyntaxHighlightInvalidatedEventArgs WholeDocument { get; } = new(1, true);
}
