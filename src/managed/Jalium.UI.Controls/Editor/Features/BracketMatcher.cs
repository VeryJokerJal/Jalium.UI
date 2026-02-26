namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Finds matching bracket pairs in a document.
/// </summary>
public static class BracketMatcher
{
    private static readonly (char open, char close)[] BracketPairs =
    [
        ('{', '}'),
        ('(', ')'),
        ('[', ']'),
        ('<', '>'),
    ];

    /// <summary>
    /// Finds the matching bracket for the character at the specified offset.
    /// Returns -1 if no matching bracket is found.
    /// </summary>
    public static int FindMatchingBracket(TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength)
            return -1;

        char ch = document.GetCharAt(offset);

        foreach (var (open, close) in BracketPairs)
        {
            if (ch == open)
                return FindClosing(document, offset + 1, open, close);
            if (ch == close)
                return FindOpening(document, offset - 1, open, close);
        }

        return -1;
    }

    /// <summary>
    /// Finds the matching bracket, also checking the character before the caret.
    /// Returns the pair (bracketOffset, matchOffset) or null.
    /// </summary>
    public static (int bracketOffset, int matchOffset)? FindMatchingBracketPair(TextDocument document, int caretOffset)
    {
        // Check at caret position
        if (caretOffset < document.TextLength)
        {
            int match = FindMatchingBracket(document, caretOffset);
            if (match >= 0)
                return (caretOffset, match);
        }

        // Check before caret
        if (caretOffset > 0)
        {
            int match = FindMatchingBracket(document, caretOffset - 1);
            if (match >= 0)
                return (caretOffset - 1, match);
        }

        return null;
    }

    private static int FindClosing(TextDocument document, int start, char open, char close)
    {
        int depth = 1;
        for (int i = start; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == open) depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static int FindOpening(TextDocument document, int start, char open, char close)
    {
        int depth = 1;
        for (int i = start; i >= 0; i--)
        {
            char c = document.GetCharAt(i);
            if (c == close) depth++;
            else if (c == open)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
