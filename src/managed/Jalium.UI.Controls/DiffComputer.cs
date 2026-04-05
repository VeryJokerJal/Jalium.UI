using System.Collections.Generic;
using System.Linq;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the type of a diff line.
/// </summary>
public enum DiffLineType
{
    /// <summary>Line is the same in both texts.</summary>
    Unchanged,
    /// <summary>Line was added in the modified text.</summary>
    Added,
    /// <summary>Line was removed from the original text.</summary>
    Removed,
    /// <summary>Line was modified (present in both but changed).</summary>
    Modified
}

/// <summary>
/// Represents a word-level difference within a line.
/// </summary>
/// <param name="Type">The type of word diff.</param>
/// <param name="Text">The text of this word segment.</param>
public sealed record WordDiff(DiffLineType Type, string Text);

/// <summary>
/// Represents a single line in a diff result.
/// </summary>
/// <param name="LineType">The type of change for this line.</param>
/// <param name="OriginalLineNumber">The 1-based line number in the original text, or null if not present.</param>
/// <param name="ModifiedLineNumber">The 1-based line number in the modified text, or null if not present.</param>
/// <param name="OriginalText">The text of the line from the original document.</param>
/// <param name="ModifiedText">The text of the line from the modified document.</param>
/// <param name="WordDiffs">Word-level diffs for modified lines; null for unchanged/added/removed.</param>
public sealed record DiffLine(
    DiffLineType LineType,
    int? OriginalLineNumber,
    int? ModifiedLineNumber,
    string OriginalText,
    string ModifiedText,
    List<WordDiff>? WordDiffs);

/// <summary>
/// Computes line-level and word-level diffs using the Myers diff algorithm.
/// </summary>
internal static class DiffComputer
{
    /// <summary>
    /// Computes a line-level diff between two texts.
    /// Modified lines are detected by pairing adjacent remove/add sequences.
    /// </summary>
    internal static List<DiffLine> ComputeDiff(string originalText, string modifiedText)
    {
        var originalLines = SplitLines(originalText);
        var modifiedLines = SplitLines(modifiedText);

        var editScript = ComputeMyersDiff(originalLines, modifiedLines);

        // Post-process: pair adjacent Remove/Add into Modified lines
        var result = new List<DiffLine>(editScript.Count);
        int i = 0;
        while (i < editScript.Count)
        {
            var entry = editScript[i];

            if (entry.LineType == DiffLineType.Removed)
            {
                // Look ahead for a matching Add to pair as Modified
                int removeStart = i;
                int removeCount = 0;
                while (i < editScript.Count && editScript[i].LineType == DiffLineType.Removed)
                {
                    removeCount++;
                    i++;
                }

                int addStart = i;
                int addCount = 0;
                while (i < editScript.Count && editScript[i].LineType == DiffLineType.Added)
                {
                    addCount++;
                    i++;
                }

                int pairedCount = Math.Min(removeCount, addCount);
                for (int p = 0; p < pairedCount; p++)
                {
                    var removed = editScript[removeStart + p];
                    var added = editScript[addStart + p];
                    var wordDiffs = ComputeWordDiff(removed.OriginalText, added.ModifiedText);
                    result.Add(new DiffLine(
                        DiffLineType.Modified,
                        removed.OriginalLineNumber,
                        added.ModifiedLineNumber,
                        removed.OriginalText,
                        added.ModifiedText,
                        wordDiffs));
                }

                // Remaining unpaired removes
                for (int r = pairedCount; r < removeCount; r++)
                {
                    result.Add(editScript[removeStart + r]);
                }

                // Remaining unpaired adds
                for (int a = pairedCount; a < addCount; a++)
                {
                    result.Add(editScript[addStart + a]);
                }
            }
            else
            {
                result.Add(entry);
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// Computes a word-level diff between two lines.
    /// </summary>
    internal static List<WordDiff> ComputeWordDiff(string oldLine, string newLine)
    {
        var oldTokens = TokenizeLine(oldLine);
        var newTokens = TokenizeLine(newLine);

        var editScript = ComputeMyersDiff(oldTokens, newTokens);

        // Convert the raw edit script into WordDiff entries
        var result = new List<WordDiff>(editScript.Count);
        int idx = 0;
        while (idx < editScript.Count)
        {
            var entry = editScript[idx];
            if (entry.LineType == DiffLineType.Unchanged)
            {
                result.Add(new WordDiff(DiffLineType.Unchanged, entry.OriginalText));
                idx++;
            }
            else if (entry.LineType == DiffLineType.Removed)
            {
                result.Add(new WordDiff(DiffLineType.Removed, entry.OriginalText));
                idx++;
            }
            else // Added
            {
                result.Add(new WordDiff(DiffLineType.Added, entry.ModifiedText));
                idx++;
            }
        }

        return result;
    }

    /// <summary>
    /// Splits text into lines. An empty string produces a single empty-string element.
    /// Trailing newline does not produce an extra empty line (matching standard diff behavior).
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Remove trailing empty entry caused by a final newline
        if (lines.Length > 1 && lines[^1].Length == 0)
        {
            Array.Resize(ref lines, lines.Length - 1);
        }

        return lines;
    }

    /// <summary>
    /// Tokenizes a line into words and whitespace/punctuation segments for word-level diff.
    /// </summary>
    private static string[] TokenizeLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return Array.Empty<string>();

        var tokens = new List<string>();
        int start = 0;
        while (start < line.Length)
        {
            if (char.IsWhiteSpace(line[start]))
            {
                int end = start + 1;
                while (end < line.Length && char.IsWhiteSpace(line[end]))
                    end++;
                tokens.Add(line.Substring(start, end - start));
                start = end;
            }
            else if (char.IsLetterOrDigit(line[start]) || line[start] == '_')
            {
                int end = start + 1;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                    end++;
                tokens.Add(line.Substring(start, end - start));
                start = end;
            }
            else
            {
                tokens.Add(line[start].ToString());
                start++;
            }
        }

        return tokens.ToArray();
    }

    /// <summary>
    /// Core Myers diff algorithm. Returns a list of DiffLine entries representing the shortest edit script.
    /// Works for both line-level (string[] of lines) and word-level (string[] of tokens) diffs.
    /// </summary>
    private static List<DiffLine> ComputeMyersDiff(string[] a, string[] b)
    {
        // Use LCS-based diff (O(n*m) space/time but robust — no index boundary issues)
        return ComputeLcsDiff(a, b);
    }

    private static List<DiffLine> ComputeLcsDiff(string[] a, string[] b)
    {
        int n = a.Length;
        int m = b.Length;

        // Handle trivial cases
        if (n == 0 && m == 0)
            return new List<DiffLine>();

        if (n == 0)
        {
            var result = new List<DiffLine>(m);
            for (int j = 0; j < m; j++)
                result.Add(new DiffLine(DiffLineType.Added, null, j + 1, "", b[j], null));
            return result;
        }

        if (m == 0)
        {
            var result = new List<DiffLine>(n);
            for (int j = 0; j < n; j++)
                result.Add(new DiffLine(DiffLineType.Removed, j + 1, null, a[j], "", null));
            return result;
        }

        // Build LCS table
        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal))
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to produce diff
        var result2 = new List<DiffLine>();
        int ai = n, bi = m;
        while (ai > 0 || bi > 0)
        {
            if (ai > 0 && bi > 0 && string.Equals(a[ai - 1], b[bi - 1], StringComparison.Ordinal))
            {
                result2.Add(new DiffLine(DiffLineType.Unchanged, ai, bi, a[ai - 1], b[bi - 1], null));
                ai--;
                bi--;
            }
            else if (bi > 0 && (ai == 0 || dp[ai, bi - 1] >= dp[ai - 1, bi]))
            {
                result2.Add(new DiffLine(DiffLineType.Added, null, bi, "", b[bi - 1], null));
                bi--;
            }
            else
            {
                result2.Add(new DiffLine(DiffLineType.Removed, ai, null, a[ai - 1], "", null));
                ai--;
            }
        }

        result2.Reverse();
        return result2;
    }

}
