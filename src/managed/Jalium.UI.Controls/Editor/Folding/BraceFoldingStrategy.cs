namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Folding strategy that creates folds based on matching curly braces.
/// </summary>
public sealed class BraceFoldingStrategy : IFoldingStrategy
{
    public char OpeningBrace { get; set; } = '{';
    public char ClosingBrace { get; set; } = '}';

    public IEnumerable<FoldingSection> CreateFoldings(TextDocument document)
    {
        var foldings = new List<FoldingSection>();
        AddBraceFoldings(document, foldings);
        AddRegionFoldings(document, foldings);

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

    private void AddBraceFoldings(TextDocument document, List<FoldingSection> foldings)
    {
        var openStack = new Stack<(int lineNumber, int column)>(); // opening brace locations

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            var lineText = document.GetLineText(lineNum);

            for (int i = 0; i < lineText.Length; i++)
            {
                char c = lineText[i];
                if (c == OpeningBrace)
                {
                    openStack.Push((lineNum, i));
                }
                else if (c == ClosingBrace && openStack.Count > 0)
                {
                    var start = openStack.Pop();
                    int startLine = start.lineNumber;
                    int startColumn = start.column;
                    if (lineNum > startLine)
                    {
                        int anchorLine = startLine;
                        int anchorColumn = startColumn;
                        var firstLineText = document.GetLineText(startLine);
                        if (IsStandaloneOpeningBrace(firstLineText, startColumn) && startLine > 1)
                        {
                            var previousLineText = document.GetLineText(startLine - 1);
                            if (!string.IsNullOrWhiteSpace(previousLineText))
                            {
                                anchorLine = startLine - 1;
                                anchorColumn = previousLineText.TrimEnd().Length;
                                firstLineText = previousLineText;
                            }
                        }

                        string titleSeed;
                        if (anchorColumn > 0 && anchorColumn <= firstLineText.Length)
                        {
                            titleSeed = firstLineText[..anchorColumn].TrimEnd();
                            if (string.IsNullOrWhiteSpace(titleSeed))
                                titleSeed = firstLineText.Trim();
                        }
                        else
                        {
                            titleSeed = firstLineText.Trim();
                        }

                        if (string.IsNullOrEmpty(titleSeed))
                            titleSeed = "...";

                        var title = titleSeed.Length > 60
                            ? titleSeed[..57] + "..."
                            : titleSeed;

                        if (lineNum > anchorLine)
                        {
                            var section = new FoldingSection(anchorLine, lineNum, title, anchorColumn)
                            {
                                GuideStartLine = startLine,
                                GuideEndLine = lineNum
                            };
                            foldings.Add(section);
                        }
                    }
                }
            }
        }
    }

    private static void AddRegionFoldings(TextDocument document, List<FoldingSection> foldings)
    {
        var regionStack = new Stack<(int lineNumber, int startColumn, string title)>();

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            string lineText = document.GetLineText(lineNum);
            if (!TryParseRegionDirective(lineText, out bool isRegionStart, out int startColumn, out string title))
                continue;

            if (isRegionStart)
            {
                regionStack.Push((lineNum, startColumn, title));
                continue;
            }

            if (regionStack.Count == 0)
                continue;

            var start = regionStack.Pop();
            if (lineNum > start.lineNumber)
            {
                var section = new FoldingSection(start.lineNumber, lineNum, start.title, start.startColumn)
                {
                    GuideStartLine = start.lineNumber,
                    GuideEndLine = lineNum
                };
                foldings.Add(section);
            }
        }
    }

    private static bool TryParseRegionDirective(string lineText, out bool isRegionStart, out int startColumn, out string title)
    {
        isRegionStart = false;
        startColumn = -1;
        title = string.Empty;
        if (string.IsNullOrEmpty(lineText))
            return false;

        int index = 0;
        while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
            index++;
        if (index >= lineText.Length || lineText[index] != '#')
            return false;

        startColumn = index;

        int tokenStart = index + 1;
        while (tokenStart < lineText.Length && char.IsWhiteSpace(lineText[tokenStart]))
            tokenStart++;
        if (tokenStart >= lineText.Length)
            return false;

        int tokenEnd = tokenStart;
        while (tokenEnd < lineText.Length && char.IsLetter(lineText[tokenEnd]))
            tokenEnd++;

        if (tokenEnd == tokenStart)
            return false;

        ReadOnlySpan<char> token = lineText.AsSpan(tokenStart, tokenEnd - tokenStart);
        if (token.Equals("region".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            isRegionStart = true;
            string name = lineText[tokenEnd..].Trim();
            title = name.Length > 0 ? $"#region {name}" : "#region";
            return true;
        }

        if (token.Equals("endregion".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            isRegionStart = false;
            title = "#endregion";
            return true;
        }

        return false;
    }

    private bool IsStandaloneOpeningBrace(string lineText, int braceColumn)
    {
        if (braceColumn < 0 || braceColumn >= lineText.Length || lineText[braceColumn] != OpeningBrace)
            return false;

        // Allman style: only whitespace before and after the opening brace.
        if (!string.IsNullOrWhiteSpace(lineText[..braceColumn]))
            return false;

        if (!string.IsNullOrWhiteSpace(lineText[(braceColumn + 1)..]))
            return false;

        return true;
    }
}
