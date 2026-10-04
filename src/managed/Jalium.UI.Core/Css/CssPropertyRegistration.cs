namespace Jalium.UI.Styling;

/// <summary>A name-defining rule, retained in source order independently of ordinary declaration cascading.</summary>
internal sealed record CssPropertyRegistration(string Name, CssPropertySyntax Syntax, bool Inherits, string? InitialValue, int Offset)
{
    internal Uri? BaseUri { get; set; }
    internal CssCondition? Condition { get; init; }
    internal string? LayerName { get; init; }

    internal static CssPropertyRegistration? Parse(ReadOnlySpan<char> header, ReadOnlySpan<char> body, int offset)
    {
        var nameReader = new CssTokenReader(header);
        if (!nameReader.TryReadIdent(out var name) || !nameReader.AtEnd || !name.StartsWith("--", StringComparison.Ordinal) || name.Length == 2) return null;
        var descriptorReader = new CssTokenReader(CssParser.StripComments(body));
        CssPropertySyntax? syntax = null;
        bool? inherits = null;
        string? initial = null;
        while (!descriptorReader.AtEnd)
        {
            if (!descriptorReader.TryReadUntilTopLevelDelimiter(';', out var declaration)) break;
            descriptorReader.TryReadDelimiter(';');
            var reader = new CssTokenReader(declaration);
            if (!reader.TryReadIdent(out var descriptor) || !reader.TryReadDelimiter(':')) continue;
            var raw = reader.Remaining.ToString();
            if (!CssDeclarationValue.IsValid(raw)) continue;
            switch (descriptor.ToString().ToLowerInvariant())
            {
                case "syntax":
                    if (reader.TryReadString(out var text) && reader.AtEnd && CssPropertySyntax.Parse(text) is { } parsed) syntax = parsed;
                    break;
                case "inherits":
                    if (reader.TryReadIdent(out var keyword) && reader.AtEnd)
                    {
                        if (keyword.Equals("true", StringComparison.OrdinalIgnoreCase)) inherits = true;
                        else if (keyword.Equals("false", StringComparison.OrdinalIgnoreCase)) inherits = false;
                    }
                    break;
                case "initial-value": initial = raw; break;
            }
        }
        if (syntax is null || inherits is null || initial is null && !syntax.Universal) return null;
        if (initial is not null)
        {
            if (CssCustomProperties.ContainsVariable(initial) || CssPropertyMetadata.IsWideKeyword(initial)) return null;
            // The registration retains the specified initial value: viewport-relative
            // initial values are independent of CSS but still change with the host viewport.
            var context = new CssPropertyValueContext(new(14, 14, 14, 100, 100), initial: true);
            if (!syntax.TryCompute(initial, context, out _)) return null;
        }
        return new(name.ToString(), syntax, inherits.Value, initial, offset);
    }
}
