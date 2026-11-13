namespace Jalium.UI.Styling;

internal sealed record CssFontFeatureValue(string Kind, string Name, int[] Indices);
internal sealed record CssFontFeatureValuesRule(string[] Families, CssFontFeatureValue[] Values,
    string? Display, int Offset, string? LayerName = null, CssCondition? Condition = null);

internal static partial class CssParser
{
    private static CssFontFeatureValuesRule? ParseFontFeatureValues(ReadOnlySpan<char> header, ReadOnlySpan<char> body, int offset)
    {
        var familyReader = new CssTokenReader(header);
        if (!CssFontFamilyValue.TryRead(ref familyReader, out var families) || !familyReader.AtEnd ||
            families.Items.Any(item => item.IsGeneric || item.Name.Length == 0)) return null;
        var definitions = new List<CssFontFeatureValue>(); string? display = null;
        int pos = 0, line = 1; var ignoredDiagnostics = new List<CssParseDiagnostic>();
        while (pos < body.Length)
        {
            SkipWhitespaceAndComments(body, ref pos, ref line);
            if (pos == body.Length) break;
            if (body[pos] == ';') { pos++; continue; }
            if (body[pos] == '@')
            {
                var kind = SkipAtRule(body, ref pos, ref line, out var start).ToLowerInvariant();
                var brace = start; var braceLine = line; ScanToTopLevelOpenBrace(body, ref brace, ref braceLine);
                if (brace >= pos || body[pos - 1] != '}' || !StripComments(body[start..brace]).Trim().IsEmpty ||
                    !CssFontAlternates.FunctionNames.Contains(kind) && kind != "historical-forms") continue;
                foreach (var declaration in ParseInlineDeclarations(body[(brace + 1)..(pos - 1)].ToString(), null, preserveNameCase: true))
                {
                    if (declaration.Important || CssCustomProperties.ContainsVariable(declaration.RawValue)) continue;
                    var reader = new CssTokenReader(declaration.RawValue); var indices = new List<int>();
                    while (indices.Count < 4096 && reader.TryReadInteger(out int index, 0)) indices.Add(index);
                    if (!reader.AtEnd || indices.Count == 0 || kind == "character-variant" && indices.Count > 2 ||
                        kind is not ("styleset" or "character-variant") && indices.Count != 1) continue;
                    definitions.Add(new(kind, declaration.PropertyName, indices.ToArray()));
                }
                continue;
            }
            if (TryReadNestedDeclaration(body, ref pos, ref line, ignoredDiagnostics, out var declarations))
            {
                foreach (var declaration in declarations)
                    if (declaration.PropertyName.Equals("font-display", StringComparison.OrdinalIgnoreCase) && !declaration.Important)
                    {
                        var reader = new CssTokenReader(declaration.RawValue);
                        if (reader.TryReadIdent(out var value) && reader.AtEnd && value.ToString().ToLowerInvariant() is { } name &&
                            name is "auto" or "block" or "swap" or "fallback" or "optional") display = name;
                    }
                continue;
            }
            // Discard an invalid qualified item, preserving following balanced rules.
            ScanNestedItem(body, ref pos, ref line, allowCurlyValue: false);
            if (pos < body.Length && body[pos] == '{') { pos++; SkipBlockRemainder(body, ref pos, ref line); }
            else if (pos < body.Length) pos++;
        }
        return new(families.Items.Select(item => item.Name).ToArray(), definitions.ToArray(), display, offset);
    }
}
