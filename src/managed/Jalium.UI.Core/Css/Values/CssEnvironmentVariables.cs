using System.Text;

namespace Jalium.UI.Styling;

/// <summary>Resolves CSS environment variables and their fallbacks.</summary>
internal static class CssEnvironmentVariables
{
    private const int MaxDepth = 32;
    private const int MaxLength = 1_048_576;

    internal static bool IsSupportedName(ReadOnlySpan<char> name)
        => name.Equals("safe-area-inset-top", StringComparison.Ordinal) ||
           name.Equals("safe-area-inset-right", StringComparison.Ordinal) ||
           name.Equals("safe-area-inset-bottom", StringComparison.Ordinal) ||
           name.Equals("safe-area-inset-left", StringComparison.Ordinal);

    internal static bool IsValidFunction(string arguments)
    {
        var reader = new CssTokenReader(arguments);
        if (!reader.TryReadUntilTopLevelComma(out var first)) return false;
        var name = new CssTokenReader(first);
        if (!name.TryReadIdent(out var identifier) ||
            CssPropertyMetadata.IsWideKeyword(identifier.ToString()) ||
            identifier.Equals("default", StringComparison.OrdinalIgnoreCase)) return false;
        while (!name.AtEnd)
            if (!name.TryReadInteger(out _, minimum: 0) || name.NumberWasCalculated) return false;
        return reader.AtEnd || reader.TryReadComma();
    }

    internal static bool TrySubstitute(string source, CssNode? element, out string result, int depth = 0)
    {
        result = string.Empty;
        if (depth >= MaxDepth || source.Length > MaxLength) return false;
        if (!source.Contains("env", StringComparison.OrdinalIgnoreCase) && !source.Contains('\\'))
        { result = source; return true; }
        var output = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length;)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = CssTokenReader.SkipComment(source, i);
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            if (source[i] is '\'' or '"')
            {
                var end = CssTokenReader.SkipString(source, i);
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            if (CssSyntax.IsNameStart(source, i))
            {
                var reader = new CssTokenReader(source.AsSpan(i));
                if (reader.TryReadFunction(out var name, out var arguments))
                {
                    if (name.Equals("env", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Resolve(arguments.Remaining.ToString(), element, out var replacement, depth + 1)) return false;
                        output.Append("/**/").Append(replacement).Append("/**/");
                    }
                    else if (name.Equals("first-valid", StringComparison.OrdinalIgnoreCase))
                    {
                        output.Append(source.AsSpan(i, reader.Position));
                    }
                    else if (name.Equals("attr", StringComparison.OrdinalIgnoreCase))
                    {
                        // attr() substitutes its own first argument and selected fallback later.
                        output.Append(source.AsSpan(i, reader.Position));
                    }
                    else
                    {
                        if (!TrySubstitute(arguments.Remaining.ToString(), element, out var replacement, depth + 1)) return false;
                        output.Append(name).Append('(').Append(replacement).Append(')');
                    }
                    if (output.Length > MaxLength) return false;
                    i += reader.Position;
                    continue;
                }
            }
            if (source[i] == '\\')
            {
                var end = i;
                if (!CssSyntax.ReadEscape(source, ref end, out _)) return false;
                output.Append(source.AsSpan(i, end - i)); i = end; continue;
            }
            // A dimension, hash, or at-keyword may contain the letters "env" but
            // cannot contain an env() function token at that position.
            var tokenEnd = CssCustomProperties.SkipNonFunctionToken(source, i);
            output.Append(source.AsSpan(i, tokenEnd - i));
            i = tokenEnd;
            if (output.Length > MaxLength) return false;
        }
        result = output.ToString();
        return true;
    }

    internal static bool TryResolveFunction(string arguments, CssNode? element, out string result)
        => Resolve(arguments, element, out result, depth: 0, deferFallback: true);

    private static bool Resolve(string arguments, CssNode? element, out string result, int depth,
        bool deferFallback = false)
    {
        result = string.Empty;
        if (!IsValidFunction(arguments)) return false;
        var reader = new CssTokenReader(arguments);
        if (!reader.TryReadUntilTopLevelComma(out var first)) return false;
        var name = new CssTokenReader(first);
        if (!name.TryReadIdent(out var identifier)) return false;
        var value = name.AtEnd ? SafeAreaValue(identifier, element) : null;
        if (value is not null) { result = value; return true; }
        if (reader.AtEnd || !reader.TryReadComma()) return false;
        if (deferFallback) { result = reader.Remaining.ToString(); return true; }
        return TrySubstitute(reader.Remaining.ToString(), element, out result, depth);
    }

    private static string? SafeAreaValue(ReadOnlySpan<char> name, CssNode? element)
    {
        double? inset = name.ToString() switch
        {
            "safe-area-inset-top" => HostWindow(element)?.SafeAreaInsets.Top ?? 0,
            "safe-area-inset-right" => HostWindow(element)?.SafeAreaInsets.Right ?? 0,
            "safe-area-inset-bottom" => HostWindow(element)?.SafeAreaInsets.Bottom ?? 0,
            "safe-area-inset-left" => HostWindow(element)?.SafeAreaInsets.Left ?? 0,
            _ => null,
        };
        return inset is { } value && double.IsFinite(value)
            ? CssPropertySyntax.Number(Math.Max(0, value)) + "px" : null;
    }

    private static Window? HostWindow(CssNode? element)
    {
        for (var current = element; current is not null; current = current.FrameworkParent)
            if (Window.GetWindow(current.Target) is { } window) return window;
        return null;
    }
}
