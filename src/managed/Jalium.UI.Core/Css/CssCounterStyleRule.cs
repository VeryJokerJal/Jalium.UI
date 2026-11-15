namespace Jalium.UI.Styling;

internal enum CssCounterSystem : byte { Cyclic, Numeric, Alphabetic, Symbolic, Additive, Fixed, Extends, Roman, Predefined }
internal enum CssPredefinedCounterAlgorithm : byte
{
    None,
    JapaneseInformal,
    JapaneseFormal,
    KoreanHangulFormal,
    KoreanHanjaInformal,
    KoreanHanjaFormal,
    SimpChineseInformal,
    SimpChineseFormal,
    TradChineseInformal,
    TradChineseFormal,
    EthiopicNumeric,
}
internal readonly record struct CssCounterRange(int Minimum, int Maximum);
internal readonly record struct CssAdditiveSymbol(int Weight, string Symbol);

/// <summary>Null descriptors remain unspecified, so extends can inherit before defaults apply.</summary>
internal sealed record CssCounterStyleDefinition
{
    internal CssCounterSystem? System { get; init; }
    internal CssPredefinedCounterAlgorithm PredefinedAlgorithm { get; init; }
    internal string? Extends { get; init; }
    internal int? First { get; init; }
    internal string[]? Symbols { get; init; }
    internal CssAdditiveSymbol[]? Additive { get; init; }
    internal string[]? Negative { get; init; }
    internal string? Prefix { get; init; }
    internal string? Suffix { get; init; }
    // Empty array is explicit auto, null is unspecified.
    internal CssCounterRange[]? Ranges { get; init; }
    internal int? PadWidth { get; init; }
    internal string? PadSymbol { get; init; }
    internal string? Fallback { get; init; }
    internal string? SpeakAs { get; init; }
    internal bool HasExplicitSpeakAs { get; init; }
    internal bool UpperRoman { get; init; }
    internal IReadOnlyDictionary<char, string>? ImageSyntaxes { get; init; }
    internal IReadOnlyDictionary<char, CssImageValue>? ImageSymbols { get; init; }
}

internal sealed record CssCounterStyleRule(string Name, CssCounterStyleDefinition Definition,
    CssDeclaration[] Descriptors, int Offset, CssCondition? Condition = null, string? LayerName = null)
{
    internal Uri? BaseUri { get; set; }
    internal ICssResourceResolver? ResourceResolver { get; set; }

    internal static CssCounterStyleRule? Parse(ReadOnlySpan<char> header, ReadOnlySpan<char> body, int offset)
    {
        var reader = new CssTokenReader(header);
        return reader.TryReadIdent(out var name) && reader.AtEnd
            ? Parse(name.ToString(), CssParser.ParseInlineDeclarations(body.ToString(), null).ToArray(), offset) : null;
    }

    internal static CssCounterStyleRule? Parse(string name, CssDeclaration[] descriptors, int offset)
    {
        if (!CssCounters.IsName(name)) return null;
        name = CssCounterStyles.NormalizeName(name);
        if (name is "decimal" or "disc" or "circle" or "square" or "disclosure-open" or "disclosure-closed") return null;
        var definition = new CssCounterStyleDefinition();
        var imageSyntaxes = new Dictionary<char, string>();
        foreach (var declaration in descriptors)
        {
            if (declaration.Important || CssCustomProperties.ContainsSubstitution(declaration.RawValue)) continue;
            var reader = new CssTokenReader(declaration.RawValue);
            switch (declaration.PropertyName)
            {
                case "system":
                    if (!reader.TryReadIdent(out var ident)) break;
                    var system = ReadSystem(ident);
                    var first = 1; string? extends = null;
                    if (system == CssCounterSystem.Fixed && !reader.AtEnd && !CssCounters.ReadInteger(ref reader, out first)) break;
                    if (system == CssCounterSystem.Extends && !ReadName(ref reader, out extends)) break;
                    if (system is not null && reader.AtEnd) definition = definition with { System = system, First = first, Extends = extends };
                    break;
                case "symbols":
                    var symbols = ReadSymbols(ref reader, identifiers: true, imageSyntaxes);
                    if (symbols is { Length: > 0 } && reader.AtEnd) definition = definition with { Symbols = symbols };
                    break;
                case "additive-symbols":
                    var additive = new List<CssAdditiveSymbol>(); var valid = true;
                    do
                    {
                        var pair = reader; var integerFirst = CssCounters.ReadInteger(ref pair, out var weight);
                        if (integerFirst) reader = pair;
                        if (!ReadSymbol(ref reader, true, imageSyntaxes, out var symbol) ||
                            !integerFirst && !CssCounters.ReadInteger(ref reader, out weight) || weight < 0 ||
                            additive.Count > 0 && weight >= additive[^1].Weight) { valid = false; break; }
                        additive.Add(new(weight, symbol));
                    } while (reader.TryReadComma());
                    if (valid && additive.Count > 0 && reader.AtEnd) definition = definition with { Additive = additive.ToArray() };
                    break;
                case "negative":
                    symbols = ReadSymbols(ref reader, identifiers: true, imageSyntaxes);
                    if (symbols is { Length: 1 or 2 } && reader.AtEnd) definition = definition with { Negative = symbols };
                    break;
                case "prefix": case "suffix":
                    if (ReadSymbol(ref reader, true, imageSyntaxes, out var affix) && reader.AtEnd)
                        definition = declaration.PropertyName == "prefix" ? definition with { Prefix = affix } : definition with { Suffix = affix };
                    break;
                case "range":
                    var ranges = ReadRanges(ref reader);
                    if (ranges is not null && reader.AtEnd) definition = definition with { Ranges = ranges };
                    break;
                case "pad":
                    var probe = reader; var widthFirst = CssCounters.ReadInteger(ref probe, out var width);
                    if (widthFirst) reader = probe;
                    if (ReadSymbol(ref reader, true, imageSyntaxes, out var padSymbol) &&
                        (widthFirst || CssCounters.ReadInteger(ref reader, out width)) && width >= 0 && reader.AtEnd)
                        definition = definition with { PadWidth = width, PadSymbol = padSymbol };
                    break;
                case "fallback": case "speak-as":
                    if (ReadName(ref reader, out var reference) && reader.AtEnd)
                        definition = declaration.PropertyName == "fallback" ? definition with { Fallback = reference } : definition with
                        {
                            SpeakAs = reference.ToLowerInvariant() is "auto" or "bullets" or "numbers" or "words" or "spell-out" ? reference.ToLowerInvariant() : reference,
                            HasExplicitSpeakAs = true,
                        };
                    break;
            }
        }
        if (imageSyntaxes.Count > 0) definition = definition with { ImageSyntaxes = imageSyntaxes };
        var usedSystem = definition.System ?? CssCounterSystem.Symbolic;
        var count = definition.Symbols?.Length ?? 0;
        if (usedSystem == CssCounterSystem.Extends ? definition.Symbols is not null || definition.Additive is not null :
            usedSystem == CssCounterSystem.Additive ? definition.Additive is not { Length: > 0 } :
            count < (usedSystem is CssCounterSystem.Numeric or CssCounterSystem.Alphabetic ? 2 : 1)) return null;
        return new(name, definition, descriptors, offset);
    }

    internal static CssCounterSystem? ReadSystem(ReadOnlySpan<char> name) => name.ToString().ToLowerInvariant() switch
    {
        "cyclic" => CssCounterSystem.Cyclic, "numeric" => CssCounterSystem.Numeric, "alphabetic" => CssCounterSystem.Alphabetic,
        "symbolic" => CssCounterSystem.Symbolic, "additive" => CssCounterSystem.Additive, "fixed" => CssCounterSystem.Fixed,
        "extends" => CssCounterSystem.Extends, _ => null,
    };

    internal static bool ReadName(ref CssTokenReader reader, out string name)
    {
        name = string.Empty;
        if (!reader.TryReadIdent(out var ident) || !CssCounters.IsName(ident)) return false;
        name = CssCounterStyles.NormalizeName(ident.ToString()); return true;
    }

    private static bool ReadSymbol(ref CssTokenReader reader, bool identifiers, Dictionary<char, string>? imageSyntaxes, out string value)
    {
        if (reader.TryReadString(out value)) return true;
        var imageProbe = reader;
        var start = imageProbe.Position;
        var raw = imageProbe.Remaining;
        if (CssImageValue.TryRead(ref imageProbe, new CssCompileContext(), out var parsedImage) && parsedImage is not null)
        {
            var syntax = raw[..(imageProbe.Position - start)].ToString();
            reader = imageProbe;
            if (imageSyntaxes is not null)
            {
                var token = (char)(0xE000 + imageSyntaxes.Count);
                imageSyntaxes[token] = syntax;
                value = token.ToString();
            }
            else value = syntax;
            return true;
        }
        if (!identifiers || !reader.TryReadIdent(out var ident)) return false;
        value = ident.ToString();
        return value.ToLowerInvariant() is not ("default" or "initial" or "inherit" or "unset" or "revert" or "revert-layer");
    }
    internal static string[]? ReadSymbols(ref CssTokenReader reader, bool identifiers, Dictionary<char, string>? imageSyntaxes = null)
    {
        var values = new List<string>();
        while (!reader.AtEnd)
        {
            if (!ReadSymbol(ref reader, identifiers, imageSyntaxes, out var value)) return null;
            values.Add(value);
        }
        return values.ToArray();
    }
    private static CssCounterRange[]? ReadRanges(ref CssTokenReader reader)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("auto", StringComparison.OrdinalIgnoreCase) && probe.AtEnd)
        { reader = probe; return []; }
        var ranges = new List<CssCounterRange>();
        do
        {
            if (!Bound(ref reader, true, out var min) || !Bound(ref reader, false, out var max) || min > max) return null;
            ranges.Add(new(min, max));
        } while (reader.TryReadComma());
        return ranges.ToArray();
    }
    private static bool Bound(ref CssTokenReader reader, bool minimum, out int value)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var ident) && ident.Equals("infinite", StringComparison.OrdinalIgnoreCase))
        { reader = probe; value = minimum ? int.MinValue : int.MaxValue; return true; }
        return CssCounters.ReadInteger(ref reader, out value);
    }
}
