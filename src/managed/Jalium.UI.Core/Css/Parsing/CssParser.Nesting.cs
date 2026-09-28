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
        List<char>? blocks = null;
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\\' && TrySkipEscape(text, ref position, ref line)) continue;
            if (c is '\'' or '"') { SkipQuotedString(text, ref position, ref line); continue; }
            else if (c == '/' && position + 1 < text.Length && text[position + 1] == '*') { SkipComment(text, ref position, ref line); continue; }
            else if (c == '\n') line++;
            else if (c == '{' && !allowCurlyValue && blocks is not { Count: > 0 }) return;
            else if (c == '}' && blocks is not { Count: > 0 }) return;
            else if (c == ';' && blocks is not { Count: > 0 }) return;
            else if (c is '(' or '[' or '{') (blocks ??= []).Add(c);
            else if (c is ')' or ']' or '}' && blocks is { Count: > 0 } && IsMatchingClose(blocks[^1], c))
            {
                blocks.RemoveAt(blocks.Count - 1);
            }
            position++;
        }
    }
}
