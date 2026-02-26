namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Interface for language-specific syntax highlighting.
/// Implementations produce per-line token arrays with state propagation for multi-line constructs.
/// </summary>
public interface ISyntaxHighlighter
{
    /// <summary>
    /// Highlights a single line, producing an array of classified tokens.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="lineText">The text of the line.</param>
    /// <param name="stateAtLineStart">Opaque state from the end of the previous line (for multi-line constructs).</param>
    /// <returns>The highlighted tokens and the state at the end of this line.</returns>
    (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(
        int lineNumber, string lineText, object? stateAtLineStart);

    /// <summary>
    /// Gets the initial state for the first line of a document.
    /// </summary>
    object? GetInitialState();
}
