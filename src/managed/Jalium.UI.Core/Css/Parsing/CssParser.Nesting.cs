namespace Jalium.UI.Styling;

internal static partial class CssParser
{
    private static bool TryReadNestedDeclaration(ReadOnlySpan<char> text, ref int position, ref int line,
        List<CssParseDiagnostic> diagnostics, out List<CssDeclaration> declarations)
    {
        declarations = [];
        var reader = new CssTokenReader(text[position..]);
        if (!reader.TryReadIdent(out var name) || !reader.TryReadDelimiter(':')) return false;
        var start = position; var startLine = line;
        var end = position + reader.Position; var endLine = line;
        ScanNestedItem(text, ref end, ref endLine, name.StartsWith("--", StringComparison.Ordinal));
        if (end < text.Length && text[end] == '{') return false;
        var localDiagnostics = new List<CssParseDiagnostic>();
        declarations = ParseInlineDeclarations(text[start..end].ToString(), localDiagnostics);
        foreach (var diagnostic in localDiagnostics) diagnostics.Add(new(diagnostic.Severity, diagnostic.Message, startLine + diagnostic.Line - 1));
        position = end; line = endLine;
        if (position < text.Length && text[position] == ';') position++;
        return true;
    }

    /// <summary>Finds a rule/declaration boundary while preserving strings, escapes and component-value blocks.</summary>
    private static void ScanNestedItem(ReadOnlySpan<char> text, ref int position, ref int line, bool allowCurlyValue)
    {
        var parentheses = 0; var brackets = 0; var braces = 0;
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\\' && CssSyntax.ReadEscape(text, ref position, out _)) continue;
            if (c is '\'' or '"') { position = CssTokenReader.SkipString(text, position) - 1; }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '*') { SkipComment(text, ref position, ref line); continue; }
            else if (c == '\n') line++;
            else if (c == '(') parentheses++;
            else if (c == ')') parentheses = Math.Max(0, parentheses - 1);
            else if (c == '[') brackets++;
            else if (c == ']') brackets = Math.Max(0, brackets - 1);
            else if (parentheses == 0 && brackets == 0)
            {
                if (c == '{') { if (!allowCurlyValue && braces == 0) return; braces++; }
                else if (c == '}') { if (braces == 0) return; braces--; }
                else if (c == ';' && braces == 0) return;
            }
            position++;
        }
    }
}
