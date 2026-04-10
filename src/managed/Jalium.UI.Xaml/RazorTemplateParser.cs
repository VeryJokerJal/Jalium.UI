using System.Diagnostics.CodeAnalysis;
using System.Text;
using Jalium.UI;

namespace Jalium.UI.Markup;

internal static class RazorScriptingFeature
{
    [FeatureSwitchDefinition("Jalium.UI.Markup.RazorScripting")]
    internal static bool IsSupported => AppContext.TryGetSwitch("Jalium.UI.Markup.RazorScripting", out bool enabled) ? enabled : true;
}

internal enum RazorSegmentKind
{
    Literal,
    Path,
    Expression,
    Code
}

internal sealed record RazorExpressionPlan(
    string Expression,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers);

internal sealed record RazorCodeBlockPlan(
    string Code,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> RootIdentifiers,
    IReadOnlyList<string> DeclaredIdentifiers);

internal sealed record RazorSegment(
    RazorSegmentKind Kind,
    string Text,
    RazorExpressionPlan? ExpressionPlan = null,
    RazorCodeBlockPlan? CodeBlockPlan = null);

internal sealed class RazorTemplate
{
    public RazorTemplate(IReadOnlyList<RazorSegment> segments)
    {
        Segments = segments;

        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var rootIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var knownLocals = new HashSet<string>(StringComparer.Ordinal);
        var outputSegments = new List<RazorSegment>();
        var renderedSegmentCount = 0;
        var hasRenderableLiteral = false;
        var hasCodeBlocks = false;

        foreach (var segment in segments)
        {
            switch (segment.Kind)
            {
                case RazorSegmentKind.Literal:
                    if (!string.IsNullOrEmpty(segment.Text))
                    {
                        renderedSegmentCount++;
                        hasRenderableLiteral = true;
                    }
                    break;

                case RazorSegmentKind.Path:
                    renderedSegmentCount++;
                    outputSegments.Add(segment);
                    AddExternalDependency(segment.Text, knownLocals, dependencies, rootIdentifiers);
                    break;

                case RazorSegmentKind.Expression:
                    renderedSegmentCount++;
                    outputSegments.Add(segment);
                    if (segment.ExpressionPlan != null)
                    {
                        AddExternalDependencies(segment.ExpressionPlan.Dependencies, knownLocals, dependencies, rootIdentifiers);
                    }
                    break;

                case RazorSegmentKind.Code:
                    hasCodeBlocks = true;
                    if (segment.CodeBlockPlan != null)
                    {
                        AddExternalDependencies(segment.CodeBlockPlan.Dependencies, knownLocals, dependencies, rootIdentifiers);
                        foreach (var declaredIdentifier in segment.CodeBlockPlan.DeclaredIdentifiers)
                        {
                            if (!string.IsNullOrWhiteSpace(declaredIdentifier))
                            {
                                knownLocals.Add(declaredIdentifier);
                            }
                        }
                    }
                    break;
            }
        }

        Dependencies = dependencies.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        RootIdentifiers = rootIdentifiers.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
        HasCodeBlocks = hasCodeBlocks;
        HasRenderableLiteral = hasRenderableLiteral;
        RenderedSegmentCount = renderedSegmentCount;
        SingleOutputSegment = !hasRenderableLiteral && outputSegments.Count == 1 ? outputSegments[0] : null;
    }

    public IReadOnlyList<RazorSegment> Segments { get; }

    public IReadOnlyList<string> Dependencies { get; }

    public IReadOnlyList<string> RootIdentifiers { get; }

    public bool HasCodeBlocks { get; }

    public bool HasRenderableLiteral { get; }

    public int RenderedSegmentCount { get; }

    public RazorSegment? SingleOutputSegment { get; }

    public bool HasDynamicSegments => Segments.Any(static s => s.Kind != RazorSegmentKind.Literal);

    public bool IsSinglePath =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Path;

    public bool IsSingleExpression =>
        Segments.Count == 1 && Segments[0].Kind == RazorSegmentKind.Expression;

    public bool IsMixed =>
        HasDynamicSegments && RenderedSegmentCount > 1;

    public bool IsSingleComputedValue =>
        !HasRenderableLiteral && SingleOutputSegment != null;

    public string? SinglePath => IsSinglePath ? Segments[0].Text : null;

    public RazorExpressionPlan? SingleExpressionPlan =>
        IsSingleExpression ? Segments[0].ExpressionPlan : null;

    private static void AddExternalDependencies(
        IEnumerable<string> candidateDependencies,
        HashSet<string> knownLocals,
        HashSet<string> dependencies,
        HashSet<string> rootIdentifiers)
    {
        foreach (var candidate in candidateDependencies)
        {
            AddExternalDependency(candidate, knownLocals, dependencies, rootIdentifiers);
        }
    }

    private static void AddExternalDependency(
        string candidateDependency,
        HashSet<string> knownLocals,
        HashSet<string> dependencies,
        HashSet<string> rootIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(candidateDependency))
            return;

        var rootIdentifier = GetRootIdentifier(candidateDependency);
        if (string.IsNullOrWhiteSpace(rootIdentifier) || knownLocals.Contains(rootIdentifier))
            return;

        dependencies.Add(candidateDependency);
        rootIdentifiers.Add(rootIdentifier);
    }

    internal static string GetRootIdentifier(string path)
    {
        var dotIndex = path.IndexOf('.');
        var bracketIndex = path.IndexOf('[');
        if (dotIndex < 0)
            return bracketIndex < 0 ? path : path[..bracketIndex];

        if (bracketIndex < 0)
            return path[..dotIndex];

        var endIndex = Math.Min(dotIndex, bracketIndex);
        return path[..endIndex];
    }
}

internal static class RazorTemplateParser
{
    private static readonly HashSet<char> PathChars = new(
    [
        '.', '_', '[', ']', '$'
    ]);

    public static RazorTemplate Parse(string value)
    {
        var segments = new List<RazorSegment>();
        var literal = new StringBuilder();
        var i = 0;

        while (i < value.Length)
        {
            var current = value[i];

            if (current == '\\' && i + 1 < value.Length && value[i + 1] == '@')
            {
                literal.Append('@');
                i += 2;
                continue;
            }

            if (current == '@')
            {
                if (i + 1 < value.Length && value[i + 1] == '@')
                {
                    literal.Append('@');
                    i += 2;
                    continue;
                }

                FlushLiteral(segments, literal);

                if (i + 1 < value.Length && value[i + 1] == '(')
                {
                    var expression = ParseExpression(value, ref i);
                    // Validate expression syntax at parse time — fail early on malformed expressions
                    try
                    {
                        var tokens = new RazorTokenizer(expression).Tokenize();
                        var parser = new RazorExpressionParser(tokens);
                        parser.ParseAndEvaluate(_ => null);
                    }
                    catch (XamlParseException)
                    {
                        throw new XamlParseException($"Razor expression compile failed: invalid expression '@({expression})'");
                    }
                    var plan = RazorExpressionAnalyzer.GetPlan(expression);
                    segments.Add(new RazorSegment(RazorSegmentKind.Expression, expression, plan));
                    continue;
                }

                if (i + 1 < value.Length && value[i + 1] == '{')
                {
                    var code = ParseCodeBlock(value, ref i);
                    var plan = RazorCodeBlockAnalyzer.GetPlan(code);
                    segments.Add(new RazorSegment(RazorSegmentKind.Code, code, null, plan));
                    continue;
                }

                var path = ParsePath(value, ref i);
                if (string.IsNullOrWhiteSpace(path))
                {
                    literal.Append('@');
                    i++;
                    continue;
                }

                segments.Add(new RazorSegment(RazorSegmentKind.Path, path));
                continue;
            }

            literal.Append(current);
            i++;
        }

        FlushLiteral(segments, literal);
        return new RazorTemplate(segments);
    }

    private static void FlushLiteral(List<RazorSegment> segments, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;

        segments.Add(new RazorSegment(RazorSegmentKind.Literal, literal.ToString()));
        literal.Clear();
    }

    private static string ParsePath(string input, ref int i)
    {
        var start = i + 1;
        if (start >= input.Length || !IsPathStart(input[start]))
            return string.Empty;

        var pos = start + 1;

        while (pos < input.Length)
        {
            var c = input[pos];
            if (IsPathPart(c))
            {
                pos++;
                continue;
            }

            break;
        }

        i = pos;
        return input[start..pos];
    }

    private static bool IsPathStart(char c) =>
        c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsPathPart(char c) =>
        char.IsLetterOrDigit(c) || PathChars.Contains(c);

    private static string ParseExpression(string input, ref int i)
    {
        return ParseDelimitedCSharp(input, ref i, i + 2, '(', ')', "Unclosed Razor expression. Expected closing ')'.");
    }

    private static string ParseCodeBlock(string input, ref int i)
    {
        return ParseDelimitedCSharp(input, ref i, i + 2, '{', '}', "Unclosed Razor code block. Expected closing '}'.");
    }

    private static string ParseDelimitedCSharp(
        string input,
        ref int i,
        int start,
        char openChar,
        char closeChar,
        string errorMessage)
    {
        var pos = start;
        var depth = 1;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;
        var verbatimString = false;
        char stringQuote = '\0';

        while (pos < input.Length)
        {
            var current = input[pos];
            var next = pos + 1 < input.Length ? input[pos + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                {
                    inLineComment = false;
                }

                pos++;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    pos += 2;
                    continue;
                }

                pos++;
                continue;
            }

            if (inString)
            {
                if (!verbatimString && escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == stringQuote)
                {
                    if (verbatimString && next == stringQuote)
                    {
                        pos += 2;
                        continue;
                    }

                    inString = false;
                    verbatimString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                if (escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = false;
                }

                pos++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                pos += 2;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                pos += 2;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                pos++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    (pos > 0 && input[pos - 1] == '@')
                    || (pos > 1 && ((input[pos - 1] == '$' && input[pos - 2] == '@') || (input[pos - 1] == '@' && input[pos - 2] == '$')));
                stringQuote = current;
                pos++;
                continue;
            }

            if (current == openChar)
            {
                depth++;
                pos++;
                continue;
            }

            if (current == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    var result = input[start..pos].Trim();
                    i = pos + 1;
                    return result;
                }

                pos++;
                continue;
            }

            pos++;
        }

        throw new XamlParseException(errorMessage);
    }
}
