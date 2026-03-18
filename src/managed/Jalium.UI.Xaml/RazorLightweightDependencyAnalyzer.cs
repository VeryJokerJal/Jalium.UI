using System.Text.RegularExpressions;

namespace Jalium.UI.Markup;

/// <summary>
/// Lightweight regex-based C# expression dependency analyzer used as a fallback
/// when Roslyn is trimmed under NativeAOT. Handles the common patterns emitted by
/// the Jalium build task; complex edge cases (lambdas, nested scopes) may report
/// extra dependencies but will never miss root identifiers.
/// </summary>
internal static class RazorLightweightDependencyAnalyzer
{
    private static readonly Regex IdentifierRegex = new(
        @"(?<![.""\w])(?<id>[_a-zA-Z]\w*(?:\.[_a-zA-Z]\w*)*)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.Ordinal)
    {
        "abstract", "and", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch",
        "char", "checked", "class", "const", "continue", "decimal", "default", "delegate", "do", "double",
        "dynamic", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float",
        "for", "foreach", "global", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "not", "null", "object", "operator", "or", "out", "override",
        "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "var", "virtual", "void", "volatile", "when", "while", "with",
        "nameof", "Write", "WriteLiteral"
    };

    public static RazorCSharpDependencyAnalysis AnalyzeExpression(string expression)
    {
        var dependencies = ExtractDependencies(expression);
        var rootIdentifiers = dependencies
            .Select(RazorCSharpDependencyAnalyzer.GetRootIdentifier)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal)
            .ToArray();

        return new RazorCSharpDependencyAnalysis(
            dependencies.OrderBy(static v => v, StringComparer.Ordinal).ToArray(),
            rootIdentifiers,
            Array.Empty<string>());
    }

    public static RazorCSharpDependencyAnalysis AnalyzeCodeBlock(string code)
    {
        var dependencies = ExtractDependencies(code);
        var rootIdentifiers = dependencies
            .Select(RazorCSharpDependencyAnalyzer.GetRootIdentifier)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal)
            .ToArray();

        return new RazorCSharpDependencyAnalysis(
            dependencies.OrderBy(static v => v, StringComparer.Ordinal).ToArray(),
            rootIdentifiers,
            Array.Empty<string>());
    }

    private static HashSet<string> ExtractDependencies(string code)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in IdentifierRegex.Matches(code))
        {
            var path = match.Groups["id"].Value;
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var root = GetRoot(path);
            if (ReservedIdentifiers.Contains(root))
                continue;

            // Skip paths that look like type names (PascalCase followed by '(' or '<').
            var endPos = match.Index + match.Length;
            if (endPos < code.Length && (code[endPos] == '(' || code[endPos] == '<'))
            {
                // Trim the method/generic call part — keep parent path if present.
                var lastDot = path.LastIndexOf('.');
                if (lastDot > 0)
                {
                    path = path[..lastDot];
                }
                else
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                dependencies.Add(path);
            }
        }

        return dependencies;
    }

    private static string GetRoot(string path)
    {
        var dotIndex = path.IndexOf('.');
        return dotIndex < 0 ? path : path[..dotIndex];
    }
}
