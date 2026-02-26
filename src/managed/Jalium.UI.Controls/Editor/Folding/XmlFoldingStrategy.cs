namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Folding strategy for XML-like documents (<c>xml</c>, <c>xaml</c>, <c>jalxaml</c>).
/// Supports element folds, multi-line comments, and multi-line CDATA sections.
/// </summary>
public sealed class XmlFoldingStrategy : IFoldingStrategy
{
    private const int MaxTitleLength = 72;

    public IEnumerable<FoldingSection> CreateFoldings(TextDocument document)
    {
        var foldings = new List<FoldingSection>();
        AddElementFoldings(document, foldings);
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

    private static void AddElementFoldings(TextDocument document, List<FoldingSection> foldings)
    {
        var openElements = new Stack<OpenElement>();
        PendingElement? pendingElement = null;

        bool inComment = false;
        int commentStartLine = 0;
        int commentStartColumn = 0;

        bool inCData = false;
        int cdataStartLine = 0;
        int cdataStartColumn = 0;

        bool inProcessingInstruction = false;

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            string lineText = document.GetLineText(lineNum);
            int index = 0;

            while (index < lineText.Length)
            {
                if (inComment)
                {
                    int commentEnd = lineText.IndexOf("-->", index, StringComparison.Ordinal);
                    if (commentEnd < 0)
                        break;

                    if (lineNum > commentStartLine)
                        foldings.Add(CreateBlockSection(document, commentStartLine, lineNum, commentStartColumn, "<!-- -->"));

                    inComment = false;
                    index = commentEnd + 3;
                    continue;
                }

                if (inCData)
                {
                    int cdataEnd = lineText.IndexOf("]]>", index, StringComparison.Ordinal);
                    if (cdataEnd < 0)
                        break;

                    if (lineNum > cdataStartLine)
                        foldings.Add(CreateBlockSection(document, cdataStartLine, lineNum, cdataStartColumn, "<![CDATA[ ]]>"));

                    inCData = false;
                    index = cdataEnd + 3;
                    continue;
                }

                if (inProcessingInstruction)
                {
                    int piEnd = lineText.IndexOf("?>", index, StringComparison.Ordinal);
                    if (piEnd < 0)
                        break;

                    inProcessingInstruction = false;
                    index = piEnd + 2;
                    continue;
                }

                if (pendingElement != null)
                {
                    if (!ConsumePendingElementLine(lineText, lineNum, ref index, pendingElement, out OpenElement? openedElement))
                        break;

                    pendingElement = null;
                    if (openedElement != null)
                        openElements.Push(openedElement);
                    continue;
                }

                int ltIndex = lineText.IndexOf('<', index);
                if (ltIndex < 0)
                    break;

                index = ltIndex;

                if (MatchesAt(lineText, index, "<!--"))
                {
                    int commentEnd = lineText.IndexOf("-->", index + 4, StringComparison.Ordinal);
                    if (commentEnd < 0)
                    {
                        inComment = true;
                        commentStartLine = lineNum;
                        commentStartColumn = index;
                        break;
                    }

                    index = commentEnd + 3;
                    continue;
                }

                if (MatchesAt(lineText, index, "<![CDATA["))
                {
                    int cdataEnd = lineText.IndexOf("]]>", index + 9, StringComparison.Ordinal);
                    if (cdataEnd < 0)
                    {
                        inCData = true;
                        cdataStartLine = lineNum;
                        cdataStartColumn = index;
                        break;
                    }

                    index = cdataEnd + 3;
                    continue;
                }

                if (MatchesAt(lineText, index, "<?"))
                {
                    int piEnd = lineText.IndexOf("?>", index + 2, StringComparison.Ordinal);
                    if (piEnd < 0)
                    {
                        inProcessingInstruction = true;
                        break;
                    }

                    index = piEnd + 2;
                    continue;
                }

                if (MatchesAt(lineText, index, "</"))
                {
                    int closeNameStart = index + 2;
                    while (closeNameStart < lineText.Length && char.IsWhiteSpace(lineText[closeNameStart]))
                        closeNameStart++;

                    if (!TryReadElementName(lineText, closeNameStart, out int closeNameEnd, out string closingName))
                    {
                        index++;
                        continue;
                    }

                    int closeTagEnd = lineText.IndexOf('>', closeNameEnd);
                    if (closeTagEnd < 0)
                        break;

                    if (TryPopMatchingElement(openElements, closingName, out OpenElement? openElement) &&
                        lineNum > openElement.StartLine)
                    {
                        var section = new FoldingSection(openElement.StartLine, lineNum, openElement.Title, openElement.StartColumn)
                        {
                            GuideStartLine = openElement.ScopeStartLine,
                            GuideEndLine = lineNum
                        };
                        foldings.Add(section);
                    }

                    index = closeTagEnd + 1;
                    continue;
                }

                if (MatchesAt(lineText, index, "<!"))
                {
                    int declarationEnd = lineText.IndexOf('>', index + 2);
                    if (declarationEnd < 0)
                        break;

                    index = declarationEnd + 1;
                    continue;
                }

                int openNameStart = index + 1;
                if (!TryReadElementName(lineText, openNameStart, out int openNameEnd, out string openingName))
                {
                    index++;
                    continue;
                }

                pendingElement = new PendingElement(
                    openingName,
                    lineNum,
                    index,
                    BuildElementTitle(lineText, index, openingName));

                index = openNameEnd;
                if (ConsumePendingElementLine(lineText, lineNum, ref index, pendingElement, out OpenElement? sameLineElement))
                {
                    pendingElement = null;
                    if (sameLineElement != null)
                        openElements.Push(sameLineElement);
                }
            }
        }
    }

    private static bool ConsumePendingElementLine(
        string lineText,
        int lineNum,
        ref int index,
        PendingElement pendingElement,
        out OpenElement? openedElement)
    {
        openedElement = null;

        while (index < lineText.Length)
        {
            char c = lineText[index];

            if (pendingElement.InQuote)
            {
                if (c == pendingElement.QuoteChar)
                    pendingElement.InQuote = false;

                index++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                pendingElement.InQuote = true;
                pendingElement.QuoteChar = c;
                index++;
                continue;
            }

            if (c == '>')
            {
                bool selfClosing = pendingElement.LastSignificantChar == '/';
                if (!selfClosing)
                {
                    openedElement = new OpenElement(
                        pendingElement.Name,
                        pendingElement.StartLine,
                        pendingElement.StartColumn,
                        lineNum,
                        pendingElement.Title);
                }

                index++;
                return true;
            }

            if (!char.IsWhiteSpace(c))
                pendingElement.LastSignificantChar = c;

            index++;
        }

        return false;
    }

    private static bool TryPopMatchingElement(Stack<OpenElement> openElements, string closingName, out OpenElement? openElement)
    {
        while (openElements.Count > 0)
        {
            var candidate = openElements.Pop();
            if (string.Equals(candidate.Name, closingName, StringComparison.Ordinal))
            {
                openElement = candidate;
                return true;
            }
        }

        openElement = null;
        return false;
    }

    private static bool TryReadElementName(string text, int startIndex, out int endIndex, out string name)
    {
        endIndex = startIndex;
        name = string.Empty;

        if (startIndex < 0 || startIndex >= text.Length)
            return false;

        char first = text[startIndex];
        if (!IsElementNameStartChar(first))
            return false;

        int i = startIndex + 1;
        while (i < text.Length && IsElementNameChar(text[i]))
            i++;

        endIndex = i;
        name = text[startIndex..i];
        return true;
    }

    private static bool IsElementNameStartChar(char c)
    {
        return char.IsLetter(c) || c is '_' or ':';
    }

    private static bool IsElementNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or ':' or '-' or '.';
    }

    private static bool MatchesAt(string text, int index, string pattern)
    {
        return index >= 0 &&
               index + pattern.Length <= text.Length &&
               string.CompareOrdinal(text, index, pattern, 0, pattern.Length) == 0;
    }

    private static FoldingSection CreateBlockSection(TextDocument document, int startLine, int endLine, int startColumn, string fallbackTitle)
    {
        string title = BuildBlockTitle(document.GetLineText(startLine), startColumn, fallbackTitle);
        return new FoldingSection(startLine, endLine, title, startColumn)
        {
            GuideStartLine = startLine,
            GuideEndLine = endLine
        };
    }

    private static string BuildElementTitle(string lineText, int startColumn, string name)
    {
        string seed = startColumn >= 0 && startColumn < lineText.Length
            ? lineText[startColumn..].Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(seed))
            seed = $"<{name}>";

        return TrimTitle(seed);
    }

    private static string BuildBlockTitle(string lineText, int startColumn, string fallbackTitle)
    {
        string seed = startColumn >= 0 && startColumn < lineText.Length
            ? lineText[startColumn..].Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(seed))
            seed = fallbackTitle;

        return TrimTitle(seed);
    }

    private static string TrimTitle(string text)
    {
        if (text.Length <= MaxTitleLength)
            return text;

        return text[..(MaxTitleLength - 3)] + "...";
    }

    private sealed class OpenElement(string name, int startLine, int startColumn, int scopeStartLine, string title)
    {
        public string Name { get; } = name;
        public int StartLine { get; } = startLine;
        public int StartColumn { get; } = startColumn;
        public int ScopeStartLine { get; } = scopeStartLine;
        public string Title { get; } = title;
    }

    private sealed class PendingElement(string name, int startLine, int startColumn, string title)
    {
        public string Name { get; } = name;
        public int StartLine { get; } = startLine;
        public int StartColumn { get; } = startColumn;
        public string Title { get; } = title;
        public bool InQuote { get; set; }
        public char QuoteChar { get; set; }
        public char LastSignificantChar { get; set; }
    }
}
