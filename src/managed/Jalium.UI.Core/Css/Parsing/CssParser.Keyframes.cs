namespace Jalium.UI.Styling;

internal static partial class CssParser
{
    private static CssKeyframesRule? ParseKeyframes(ReadOnlySpan<char> prelude,
        ReadOnlySpan<char> body, int offset, int bodyLine, List<CssParseDiagnostic> diagnostics,
        CssNamespaceContext? namespaces)
    {
        var nameReader = new CssTokenReader(StripComments(prelude));
        string name;
        if (!nameReader.TryReadString(out name))
        {
            if (!nameReader.TryReadIdent(out var ident)) return null;
            name = ident.ToString();
            if (name.ToLowerInvariant() is "none" or "initial" or "inherit" or
                "unset" or "revert" or "revert-layer")
                return null;
        }
        if (!nameReader.AtEnd) return null;

        var blocks = new List<CssKeyframeBlock>();
        var position = 0;
        var line = bodyLine;
        while (position < body.Length)
        {
            SkipWhitespaceAndComments(body, ref position, ref line);
            if (position >= body.Length) break;
            if (body[position] == ';') { position++; continue; }

            var selectorLine = line;
            var start = position;
            ScanNestedItem(body, ref position, ref line, allowCurlyValue: false);
            if (position >= body.Length || body[position] != '{')
            {
                diagnostics.Add(new(CssDiagnosticSeverity.Warning,
                    "invalid @keyframes block; expected '{' after a keyframe selector", selectorLine));
                if (position < body.Length) position++;
                continue;
            }

            var selectorText = StripComments(body[start..position]);
            position++;
            var declarationStart = position;
            var declarationLine = line;
            SkipBlockRemainder(body, ref position, ref line);
            var declarationEnd = position > declarationStart && body[position - 1] == '}'
                ? position - 1 : position;
            if (!TryParseKeyframeSelectors(selectorText, out var offsets))
            {
                diagnostics.Add(new(CssDiagnosticSeverity.Warning,
                    "invalid @keyframes selector; block skipped", selectorLine));
                continue;
            }

            var declarationDiagnostics = new List<CssParseDiagnostic>();
            var declarations = ParseInlineDeclarations(body[declarationStart..declarationEnd].ToString(),
                    declarationDiagnostics)
                .Where(static declaration => !declaration.Important &&
                    declaration.PropertyName != "animation" &&
                    (!declaration.PropertyName.StartsWith("animation-", StringComparison.Ordinal) ||
                     declaration.PropertyName == "animation-timing-function"))
                .ToArray();
            foreach (var diagnostic in declarationDiagnostics)
                diagnostics.Add(new(diagnostic.Severity, diagnostic.Message,
                    declarationLine + diagnostic.Line - 1));
            blocks.Add(new(offsets, declarations));
        }

        return new(name, blocks.ToArray(), offset) { Namespaces = namespaces };
    }

    private static bool TryParseKeyframeSelectors(ReadOnlySpan<char> text, out double[] offsets)
    {
        var reader = new CssTokenReader(text);
        var parsed = new List<double>();
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var segment) || segment.IsEmpty)
            {
                offsets = [];
                return false;
            }
            var valueReader = new CssTokenReader(segment);
            if (valueReader.TryReadIdent(out var keyword) && valueReader.AtEnd)
            {
                if (keyword.Equals("from", StringComparison.OrdinalIgnoreCase)) parsed.Add(0);
                else if (keyword.Equals("to", StringComparison.OrdinalIgnoreCase)) parsed.Add(1);
                else { offsets = []; return false; }
            }
            else
            {
                valueReader = new CssTokenReader(segment);
                if (!valueReader.TryReadNumber(out var value, out var unit) ||
                    unit != CssUnit.Percent || !valueReader.AtEnd ||
                    !double.IsFinite(value) || value is < 0 or > 100)
                {
                    offsets = [];
                    return false;
                }
                parsed.Add(value / 100);
            }
            if (reader.AtEnd) break;
            if (!reader.TryReadComma() || reader.AtEnd) { offsets = []; return false; }
        } while (true);
        offsets = parsed.ToArray();
        return offsets.Length > 0;
    }
}
