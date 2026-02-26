namespace Jalium.UI.Controls.Editor;

/// <summary>
/// A line of text with its syntax tokens for rendering.
/// </summary>
public sealed class HighlightedLine
{
    /// <summary>
    /// Gets the 1-based line number.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the array of syntax tokens for this line.
    /// Tokens are in document-local offsets (relative to line start).
    /// </summary>
    public SyntaxToken[] Tokens { get; }

    public HighlightedLine(int lineNumber, SyntaxToken[] tokens)
    {
        LineNumber = lineNumber;
        Tokens = tokens;
    }

    /// <summary>
    /// Creates a default (unhighlighted) line.
    /// </summary>
    public static HighlightedLine CreatePlainText(int lineNumber, int length)
    {
        return new HighlightedLine(lineNumber,
            length > 0
                ? [new SyntaxToken(0, length, TokenClassification.PlainText)]
                : []);
    }
}
