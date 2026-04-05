namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Folding strategy for JALXAML documents that combines XML element folding
/// with Razor code block folding (<c>@if</c>, <c>@for</c>, <c>@{ }</c>, etc.).
/// </summary>
public sealed class JalxamlFoldingStrategy : IFoldingStrategy
{
    private static readonly HashSet<string> s_razorBlockKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "else", "for", "foreach", "while", "do", "switch",
        "try", "catch", "finally", "using", "lock", "section", "code",
    };

    private readonly XmlFoldingStrategy _xmlStrategy = new();

    public IEnumerable<FoldingSection> CreateFoldings(TextDocument document)
    {
        var foldings = new List<FoldingSection>();

        // XML element foldings
        foreach (var section in _xmlStrategy.CreateFoldings(document))
            foldings.Add(section);

        // Razor code block foldings
        AddRazorFoldings(document, foldings);

        foldings.Sort(static (a, b) =>
        {
            int byStart = a.StartLine.CompareTo(b.StartLine);
            if (byStart != 0)
                return byStart;

            int byEndDescending = b.EndLine.CompareTo(a.EndLine);
            if (byEndDescending != 0)
                return byEndDescending;

            return a.StartColumn.CompareTo(b.StartColumn);
        });

        return foldings;
    }

    private static void AddRazorFoldings(TextDocument document, List<FoldingSection> foldings)
    {
        var braceStack = new Stack<BraceInfo>();

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            string lineText = document.GetLineText(lineNum);
            ScanLineForRazorBraces(lineText, lineNum, braceStack, foldings, document);
        }
    }

    private static void ScanLineForRazorBraces(
        string lineText,
        int lineNum,
        Stack<BraceInfo> braceStack,
        List<FoldingSection> foldings,
        TextDocument document)
    {
        for (int i = 0; i < lineText.Length; i++)
        {
            char c = lineText[i];

            // Skip string literals
            if (c is '"' or '\'')
            {
                i = SkipStringLiteral(lineText, i);
                continue;
            }

            // Skip line comments
            if (c == '/' && i + 1 < lineText.Length && lineText[i + 1] == '/')
                break;

            // Skip block comments
            if (c == '/' && i + 1 < lineText.Length && lineText[i + 1] == '*')
            {
                i = SkipBlockComment(lineText, i);
                continue;
            }

            // Skip XML comments
            if (c == '<' && i + 3 < lineText.Length && lineText[i + 1] == '!' && lineText[i + 2] == '-' && lineText[i + 3] == '-')
            {
                int end = lineText.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) break;
                i = end + 2;
                continue;
            }

            // Skip Razor comments @* ... *@
            if (c == '@' && i + 1 < lineText.Length && lineText[i + 1] == '*')
            {
                int end = lineText.IndexOf("*@", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                i = end + 1;
                continue;
            }

            // Skip escaped @@ and attribute values containing @
            if (c == '@' && i + 1 < lineText.Length && lineText[i + 1] == '@')
            {
                i++;
                continue;
            }

            if (c == '{')
            {
                // Determine the Razor context: look back for @keyword or standalone @{
                string title = ResolveRazorBlockTitle(lineText, i, lineNum, document);
                int anchorLine = lineNum;
                int anchorColumn = i;

                // Allman style: if { is alone on the line, anchor to the previous line
                if (title.Length > 0 && IsStandaloneBrace(lineText, i))
                {
                    if (lineNum > 1)
                    {
                        string prevLine = document.GetLineText(lineNum - 1);
                        if (!string.IsNullOrWhiteSpace(prevLine))
                        {
                            anchorLine = lineNum - 1;
                            anchorColumn = prevLine.TrimEnd().Length;
                        }
                    }
                }

                braceStack.Push(new BraceInfo(lineNum, i, anchorLine, anchorColumn, title));
            }
            else if (c == '}' && braceStack.Count > 0)
            {
                var open = braceStack.Pop();
                if (lineNum > open.AnchorLine && open.Title.Length > 0)
                {
                    var section = new FoldingSection(open.AnchorLine, lineNum, open.Title, open.AnchorColumn)
                    {
                        GuideStartLine = open.BraceLine,
                        GuideEndLine = lineNum,
                    };
                    foldings.Add(section);
                }
            }
        }
    }

    private static string ResolveRazorBlockTitle(string lineText, int braceColumn, int lineNum, TextDocument document)
    {
        // Look backward on this line for @keyword or @{
        string before = lineText[..braceColumn].TrimEnd();

        // Check this line for Razor directive pattern
        string? title = TryExtractRazorTitle(before);
        if (title != null)
            return title;

        // Allman style: check previous line
        if (IsStandaloneBrace(lineText, braceColumn) && lineNum > 1)
        {
            string prevLine = document.GetLineText(lineNum - 1).TrimEnd();
            title = TryExtractRazorTitle(prevLine);
            if (title != null)
                return title;
        }

        return string.Empty;
    }

    private static string? TryExtractRazorTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Find the last @ that starts a Razor directive
        int atIndex = -1;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '@')
            {
                // Not escaped @@
                if (i > 0 && text[i - 1] == '@')
                    continue;
                atIndex = i;
                break;
            }
        }

        if (atIndex < 0)
            return null;

        string afterAt = text[(atIndex + 1)..].TrimStart();

        // @{ code block
        if (afterAt.Length == 0 || afterAt == "{")
            return "@{ ... }";

        // Extract keyword
        int keyEnd = 0;
        while (keyEnd < afterAt.Length && char.IsLetter(afterAt[keyEnd]))
            keyEnd++;

        if (keyEnd == 0)
            return null;

        string keyword = afterAt[..keyEnd];
        if (!s_razorBlockKeywords.Contains(keyword))
            return null;

        // Build title from @keyword(...)
        string trimmed = text[atIndex..].TrimEnd();
        if (trimmed.Length > 60)
            trimmed = trimmed[..57] + "...";

        return trimmed;
    }

    private static bool IsStandaloneBrace(string lineText, int braceColumn)
    {
        for (int i = 0; i < lineText.Length; i++)
        {
            if (i == braceColumn) continue;
            if (!char.IsWhiteSpace(lineText[i])) return false;
        }
        return true;
    }

    private static int SkipStringLiteral(string text, int startIndex)
    {
        char quote = text[startIndex];

        // Check for verbatim/interpolated string prefixes
        if (quote == '"')
        {
            // Check for @" or $" or $@" or @$" preceding
            int i = startIndex + 1;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }
                if (text[i] == quote)
                    return i;
                i++;
            }
            return text.Length - 1;
        }

        // Single-quoted char literal
        int j = startIndex + 1;
        while (j < text.Length)
        {
            if (text[j] == '\\')
            {
                j += 2;
                continue;
            }
            if (text[j] == quote)
                return j;
            j++;
        }
        return text.Length - 1;
    }

    private static int SkipBlockComment(string text, int startIndex)
    {
        int end = text.IndexOf("*/", startIndex + 2, StringComparison.Ordinal);
        return end < 0 ? text.Length - 1 : end + 1;
    }

    private readonly record struct BraceInfo(int BraceLine, int BraceColumn, int AnchorLine, int AnchorColumn, string Title);
}
