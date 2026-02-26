using System.Text.RegularExpressions;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// A rule that matches a regex pattern and assigns a token classification.
/// </summary>
public sealed class HighlightingRule
{
    public Regex Pattern { get; }
    public TokenClassification Classification { get; }

    public HighlightingRule(string pattern, TokenClassification classification, RegexOptions options = RegexOptions.None)
    {
        Pattern = new Regex(pattern, options | RegexOptions.Compiled);
        Classification = classification;
    }

    public HighlightingRule(Regex pattern, TokenClassification classification)
    {
        Pattern = pattern;
        Classification = classification;
    }
}

/// <summary>
/// A rule for multi-line spans (e.g., block comments, multi-line strings).
/// </summary>
public sealed class SpanRule
{
    public Regex StartPattern { get; }
    public Regex EndPattern { get; }
    public TokenClassification Classification { get; }

    public SpanRule(string startPattern, string endPattern, TokenClassification classification)
    {
        StartPattern = new Regex(startPattern, RegexOptions.Compiled);
        EndPattern = new Regex(endPattern, RegexOptions.Compiled);
        Classification = classification;
    }
}

/// <summary>
/// State object for tracking multi-line spans across lines.
/// </summary>
internal sealed class HighlighterState
{
    /// <summary>
    /// The currently active span rule (null if not inside a multi-line span).
    /// </summary>
    public SpanRule? ActiveSpan { get; init; }

    public static readonly HighlighterState Default = new() { ActiveSpan = null };

    public override bool Equals(object? obj) =>
        obj is HighlighterState other && ReferenceEquals(ActiveSpan, other.ActiveSpan);

    public override int GetHashCode() => ActiveSpan?.GetHashCode() ?? 0;
}

/// <summary>
/// Regex-based syntax highlighter with support for single-line rules and multi-line span rules.
/// </summary>
public sealed class RegexSyntaxHighlighter : ISyntaxHighlighter
{
    private readonly List<HighlightingRule> _rules = [];
    private readonly List<SpanRule> _spanRules = [];

    /// <summary>
    /// Gets the list of single-line highlighting rules.
    /// Rules are applied in order; first match wins for any character position.
    /// </summary>
    public IList<HighlightingRule> Rules => _rules;

    /// <summary>
    /// Gets the list of multi-line span rules (e.g., block comments).
    /// </summary>
    public IList<SpanRule> SpanRules => _spanRules;

    public object? GetInitialState() => HighlighterState.Default;

    public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(
        int lineNumber, string lineText, object? stateAtLineStart)
    {
        var state = stateAtLineStart as HighlighterState ?? HighlighterState.Default;
        var tokens = new List<SyntaxToken>();
        var covered = new bool[lineText.Length];
        SpanRule? activeSpanAtEnd = state.ActiveSpan;

        int scanStart = 0;

        // If we're inside a multi-line span from a previous line, continue it
        if (state.ActiveSpan != null)
        {
            var endMatch = state.ActiveSpan.EndPattern.Match(lineText);
            if (endMatch.Success)
            {
                int spanEnd = endMatch.Index + endMatch.Length;
                tokens.Add(new SyntaxToken(0, spanEnd, state.ActiveSpan.Classification));
                MarkCovered(covered, 0, spanEnd);
                scanStart = spanEnd;
                activeSpanAtEnd = null;
            }
            else
            {
                // Entire line is inside the span
                tokens.Add(new SyntaxToken(0, lineText.Length, state.ActiveSpan.Classification));
                return (tokens.ToArray(), new HighlighterState { ActiveSpan = state.ActiveSpan });
            }
        }

        // Scan for span starts
        for (int pos = scanStart; pos < lineText.Length; pos++)
        {
            if (covered[pos]) continue;

            bool matchedSpan = false;
            foreach (var spanRule in _spanRules)
            {
                var startMatch = spanRule.StartPattern.Match(lineText, pos);
                if (startMatch.Success && startMatch.Index == pos)
                {
                    // Check if span ends on same line
                    int afterStart = startMatch.Index + startMatch.Length;
                    var endMatch = spanRule.EndPattern.Match(lineText, afterStart);
                    if (endMatch.Success)
                    {
                        int spanEnd = endMatch.Index + endMatch.Length;
                        tokens.Add(new SyntaxToken(pos, spanEnd - pos, spanRule.Classification));
                        MarkCovered(covered, pos, spanEnd);
                        pos = spanEnd - 1;
                    }
                    else
                    {
                        // Span continues to next line
                        tokens.Add(new SyntaxToken(pos, lineText.Length - pos, spanRule.Classification));
                        MarkCovered(covered, pos, lineText.Length);
                        activeSpanAtEnd = spanRule;
                        pos = lineText.Length;
                    }
                    matchedSpan = true;
                    break;
                }
            }

            if (matchedSpan) continue;

            // Try single-line rules at this position
            foreach (var rule in _rules)
            {
                var match = rule.Pattern.Match(lineText, pos);
                if (match.Success && match.Index == pos && match.Length > 0)
                {
                    if (!IsAnyCovered(covered, match.Index, match.Index + match.Length))
                    {
                        tokens.Add(new SyntaxToken(match.Index, match.Length, rule.Classification));
                        MarkCovered(covered, match.Index, match.Index + match.Length);
                        pos = match.Index + match.Length - 1;
                    }
                    break;
                }
            }
        }

        // Fill uncovered ranges with PlainText
        // Sort tokens by position first
        tokens.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));

        var result = new List<SyntaxToken>();
        int lastEnd = 0;
        foreach (var token in tokens)
        {
            if (token.StartOffset > lastEnd)
            {
                result.Add(new SyntaxToken(lastEnd, token.StartOffset - lastEnd, TokenClassification.PlainText));
            }
            result.Add(token);
            lastEnd = token.StartOffset + token.Length;
        }
        if (lastEnd < lineText.Length)
        {
            result.Add(new SyntaxToken(lastEnd, lineText.Length - lastEnd, TokenClassification.PlainText));
        }

        var endState = new HighlighterState { ActiveSpan = activeSpanAtEnd };
        return (result.ToArray(), endState);
    }

    private static void MarkCovered(bool[] covered, int start, int end)
    {
        for (int i = start; i < end && i < covered.Length; i++)
            covered[i] = true;
    }

    private static bool IsAnyCovered(bool[] covered, int start, int end)
    {
        for (int i = start; i < end && i < covered.Length; i++)
            if (covered[i]) return true;
        return false;
    }

    /// <summary>
    /// Creates a C#-like syntax highlighter with common rules.
    /// </summary>
    public static RegexSyntaxHighlighter CreateCSharpHighlighter()
    {
        var h = new RegexSyntaxHighlighter();

        // Multi-line spans
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));

        // Single-line rules (order matters)
        h.Rules.Add(new HighlightingRule(@"///.*$", TokenClassification.XmlDoc, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"#\w+", TokenClassification.Preprocessor));
        h.Rules.Add(new HighlightingRule(@"@""(?:[^""]|"""")*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"\$""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|foreach|while|do|switch|case|default|break|continue|return|throw|try|catch|finally|goto|yield)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(abstract|async|await|base|bool|byte|char|checked|class|const|decimal|delegate|double|enum|event|explicit|extern|false|fixed|float|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|this|true|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|where|when)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?[fFdDmMlLuU]?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0x[0-9a-fA-F]+[lLuU]?", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"\[[\w]+\]", TokenClassification.Attribute));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));

        return h;
    }
}
