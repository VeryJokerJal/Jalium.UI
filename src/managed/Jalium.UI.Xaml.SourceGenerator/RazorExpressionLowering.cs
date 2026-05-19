using System.Collections.Generic;
using System.Text;

namespace Jalium.UI.Xaml.SourceGenerator;

/// <summary>
/// Compile-time analysis for value-expression Razor (<c>@(expr)</c> / <c>@identifier</c> /
/// <c>$.path</c> / <c>#.path</c>) embedded in attribute values and text-content nodes.
/// Detects Razor presence, extracts dependency paths the runtime binding must observe,
/// and produces a normalised expression string the runtime evaluator can compile.
///
/// Why compile-time analysis: at runtime, <see cref="Jalium.UI.Markup.RazorExpressionAnalyzer"/>
/// re-parses every expression and walks identifiers via reflection. By pre-computing the
/// dependency list at codegen time we let the runtime skip the analyzer entirely — it just
/// builds one <see cref="Jalium.UI.MultiBinding"/> per dependency path and evaluates the
/// expression once on each tick.
/// </summary>
internal static class RazorExpressionLowering
{
    /// <summary>
    /// If <paramref name="value"/> contains any Razor value-expression, normalise it into an
    /// expression string the runtime can evaluate and extract the set of dependency paths.
    /// Returns false for pure-literal values — caller stays on the literal-string code path.
    ///
    /// Supported forms:
    ///   "@Identifier"          → expression="@Identifier" deps=["Identifier"]
    ///   "@(expr)"              → expression="@(expr)"    deps=[identifiers in expr]
    ///   "@(a + b)"             → expression="@(a + b)"   deps=["a","b"]
    ///   "@($.Foo)"             → expression="@($.Foo)"   deps=["$.Foo"]
    ///   "@(#.Bar)"             → expression="@(#.Bar)"   deps=["#.Bar"]
    ///   "literal @prop tail"   → expression unchanged, deps=["prop"] (runtime interpolation)
    /// </summary>
    public static bool TryLowerAttributeValue(string? value, out string? expression, out string[]? dependencies)
    {
        expression = null;
        dependencies = null;
        if (string.IsNullOrEmpty(value)) return false;
        if (!ContainsRazor(value!)) return false;

        // Preserve the original string — runtime evaluator already understands the syntax.
        // We only need to extract the dependency identifier set.
        expression = value;
        var deps = new HashSet<string>(System.StringComparer.Ordinal);
        ExtractDependencies(value!, deps);
        dependencies = deps.Count == 0
            ? System.Array.Empty<string>()
            : new List<string>(deps).ToArray();
        return true;
    }

    /// <summary>
    /// Extract the dependency paths a Razor <c>@if</c> condition must observe. Unlike
    /// <see cref="TryLowerAttributeValue"/> the condition is a bare C# boolean expression
    /// (no <c>@</c> sigil) — e.g. <c>Count &gt; 0</c>, <c>IsBusy &amp;&amp; $.Ready</c> — so we
    /// run the same identifier walker used inside <c>@(...)</c>. The result is handed to
    /// the runtime via <c>XamlBuilder.SetRazorIfVisibility</c> so
    /// <see cref="Jalium.UI.Markup.RazorExpressionAnalyzer"/> can skip its reflection walk
    /// (it consults the pre-registered metadata first).
    /// </summary>
    public static string[] ExtractConditionDependencies(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return System.Array.Empty<string>();
        var deps = new HashSet<string>(System.StringComparer.Ordinal);
        ExtractIdentifiersFromExpression(condition!, 0, condition!.Length, deps);
        return deps.Count == 0
            ? System.Array.Empty<string>()
            : new List<string>(deps).ToArray();
    }

    /// <summary>
    /// Emit a C# array-literal expression for the dependency list. Empty list emits
    /// <c>global::System.Array.Empty&lt;string&gt;()</c> to avoid one allocation per call.
    /// </summary>
    public static string EmitDependencyArray(string[] dependencies)
    {
        if (dependencies.Length == 0)
            return "global::System.Array.Empty<string>()";
        var sb = new StringBuilder();
        sb.Append("new string[] { ");
        for (var i = 0; i < dependencies.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('"');
            foreach (var c in dependencies[i])
            {
                if (c == '\\' || c == '"') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append('"');
        }
        sb.Append(" }");
        return sb.ToString();
    }

    /// <summary>
    /// True if the value contains at least one Razor-expression sigil that the runtime
    /// will interpret. The `@@` escape is recognised so that purely-escaped strings
    /// (e.g. demo text "@@ItemCount") do NOT trigger lowering.
    /// </summary>
    private static bool ContainsRazor(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '@')
            {
                // `@@` escape → literal '@' at runtime, not a Razor expression.
                if (i + 1 < value.Length && value[i + 1] == '@')
                {
                    i++;
                    continue;
                }
                return true;
            }
            if ((c == '$' || c == '#') && i + 1 < value.Length && value[i + 1] == '.')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Walk the value and collect every identifier path that the runtime binding must
    /// observe. Recognises:
    ///   <c>@Identifier</c>       — emits "Identifier"
    ///   <c>@A.B.C</c>            — emits "A.B.C" (dotted path)
    ///   <c>@(expr)</c>           — walks inside parens and emits every bare identifier
    ///   <c>$.Path</c>            — emits "$.Path" (self-reference, RazorValueResolver handles)
    ///   <c>#.Path</c>            — emits "#.Path" (data-context-only reference)
    /// String literals inside @(...) are skipped (their contents are not dependencies).
    /// </summary>
    private static void ExtractDependencies(string value, HashSet<string> deps)
    {
        var i = 0;
        while (i < value.Length)
        {
            var c = value[i];

            // Self/data references at top level.
            if ((c == '$' || c == '#') && i + 1 < value.Length && value[i + 1] == '.')
            {
                var start = i;
                i += 2;
                while (i < value.Length && IsPathChar(value[i])) i++;
                deps.Add(value.Substring(start, i - start));
                continue;
            }

            if (c != '@')
            {
                i++;
                continue;
            }

            // Skip `@@` escape.
            if (i + 1 < value.Length && value[i + 1] == '@')
            {
                i += 2;
                continue;
            }

            i++; // consume '@'
            if (i >= value.Length) break;

            // @(expr) → walk inside parentheses balanced.
            if (value[i] == '(')
            {
                var depth = 1;
                i++;
                var exprStart = i;
                while (i < value.Length && depth > 0)
                {
                    var ch = value[i];
                    if (ch == '"' || ch == '\'')
                    {
                        // Skip string literal — contents are not dependencies.
                        var quote = ch;
                        i++;
                        while (i < value.Length && value[i] != quote)
                        {
                            if (value[i] == '\\' && i + 1 < value.Length) i++;
                            i++;
                        }
                        if (i < value.Length) i++;
                        continue;
                    }
                    if (ch == '(') depth++;
                    else if (ch == ')') { depth--; if (depth == 0) break; }
                    i++;
                }
                ExtractIdentifiersFromExpression(value, exprStart, i, deps);
                if (i < value.Length) i++; // consume ')'
                continue;
            }

            // @Identifier or @Identifier.Path
            if (IsIdentStart(value[i]))
            {
                var start = i;
                while (i < value.Length && IsPathChar(value[i])) i++;
                if (i > start)
                {
                    deps.Add(value.Substring(start, i - start));
                }
            }
        }
    }

    /// <summary>
    /// Extract every dotted identifier from a C#-ish expression substring [start, end).
    /// Skips keywords (true/false/null/etc.) and numeric literals. Preserves `$.x` and
    /// `#.x` prefixes so the runtime path-resolver dispatches correctly.
    /// </summary>
    private static void ExtractIdentifiersFromExpression(string src, int start, int end, HashSet<string> deps)
    {
        var i = start;
        while (i < end)
        {
            var c = src[i];

            // Skip string literals — already handled by caller for the outer @() walk,
            // but expressions inside @() may contain nested string literals too.
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < end && src[i] != quote)
                {
                    if (src[i] == '\\' && i + 1 < end) i++;
                    i++;
                }
                if (i < end) i++;
                continue;
            }

            // $.Path / #.Path inside an expression.
            if ((c == '$' || c == '#') && i + 1 < end && src[i + 1] == '.')
            {
                var s = i;
                i += 2;
                while (i < end && IsPathChar(src[i])) i++;
                deps.Add(src.Substring(s, i - s));
                continue;
            }

            // Skip numbers.
            if (c >= '0' && c <= '9')
            {
                while (i < end && (char.IsDigit(src[i]) || src[i] == '.' || src[i] == 'e' || src[i] == 'E' ||
                                   src[i] == '+' || src[i] == '-' || src[i] == 'L' || src[i] == 'l' ||
                                   src[i] == 'F' || src[i] == 'f' || src[i] == 'D' || src[i] == 'd' ||
                                   src[i] == 'M' || src[i] == 'm' || src[i] == 'u' || src[i] == 'U'))
                {
                    i++;
                }
                continue;
            }

            // Identifier or keyword.
            if (IsIdentStart(c))
            {
                var s = i;
                while (i < end && IsPathChar(src[i])) i++;
                var ident = src.Substring(s, i - s);
                if (!IsKeyword(ident))
                {
                    deps.Add(ident);
                }
                continue;
            }

            i++;
        }
    }

    private static bool IsIdentStart(char c) => c == '_' || char.IsLetter(c);
    private static bool IsPathChar(char c) => c == '_' || c == '.' || char.IsLetterOrDigit(c);

    private static bool IsKeyword(string s)
    {
        // Keywords appearing in conditional/numeric/cast/null-handling expressions.
        return s switch
        {
            "true" or "false" or "null" or "new" or "is" or "as" or "in" or
            "var" or "void" or "return" or "if" or "else" or "switch" or "case" or
            "default" or "typeof" or "sizeof" or "checked" or "unchecked" or
            "int" or "long" or "short" or "byte" or "sbyte" or "uint" or "ulong" or "ushort" or
            "float" or "double" or "decimal" or "bool" or "char" or "string" or "object" or
            "this" or "base" => true,
            _ => false
        };
    }
}
