using System.Globalization;

namespace Jalium.UI.Styling;

internal sealed record CssFontSource(string Reference, bool Local, string? Format, string[] Technologies);
internal readonly record struct CssUnicodeRange(int Start, int End);

internal sealed record CssFontFaceRule(
    string Family, CssFontSource[] Sources, double MinimumWeight, double MaximumWeight,
    int Style, double MinimumWidth, double MaximumWidth, CssUnicodeRange[] Ranges,
    string Display, int Offset, string? LayerName = null, CssCondition? Condition = null)
{
    internal Uri? BaseUri;
    internal ICssResourceResolver? Resolver;
    internal bool Contains(int codepoint) => Ranges.Any(range => codepoint >= range.Start && codepoint <= range.End);

    internal static CssFontFaceRule? Parse(ReadOnlySpan<char> header, ReadOnlySpan<char> body, int offset)
    {
        if (!CssParser.StripComments(header).Trim().IsEmpty) return null;
        string? family = null; CssFontSource[]? sources = null;
        double minWeight = 400, maxWeight = 400, minWidth = 100, maxWidth = 100;
        var style = 0; var display = "auto";
        CssUnicodeRange[] ranges = [new(0, 0x10ffff)];
        foreach (var declaration in CssParser.ParseInlineDeclarations(body.ToString(), null))
        {
            if (declaration.Important || CssCustomProperties.ContainsVariable(declaration.RawValue)) continue;
            var raw = declaration.RawValue;
            switch (declaration.PropertyName)
            {
                case "font-family": if (FamilyName(raw) is { } name) family = name; break;
                case "src": if (ParseSources(raw) is { } parsed) sources = parsed; break;
                case "font-weight":
                    if (Range(raw, false, out var low, out var high)) { minWeight = low; maxWeight = high; }
                    break;
                case "font-stretch": case "font-width":
                    if (Range(raw, true, out low, out high)) { minWidth = low; maxWidth = high; }
                    break;
                case "font-style":
                    var reader = new CssTokenReader(raw);
                    if (!reader.TryReadIdent(out var keyword)) break;
                    var candidate = keyword.ToString().ToLowerInvariant() switch { "normal" => 0, "italic" => 1, "oblique" => 2, _ => -1 };
                    if (candidate == 2 && !reader.AtEnd)
                    {
                        if (!reader.TryReadNumber(out var angle, out var unit) || !CssUnitConversion.TryToDegrees(angle, unit, out angle) || angle is < -90 or > 90) break;
                        if (!reader.AtEnd && (!reader.TryReadNumber(out var end, out unit) || !CssUnitConversion.TryToDegrees(end, unit, out end) || end < angle || end > 90)) break;
                    }
                    if (candidate >= 0 && reader.AtEnd) style = candidate;
                    break;
                case "font-display":
                    var value = raw.Trim().ToLowerInvariant();
                    if (value is "auto" or "block" or "swap" or "fallback" or "optional") display = value;
                    break;
                case "unicode-range": if (UnicodeRanges(raw) is { } parsedRanges) ranges = parsedRanges; break;
            }
        }
        return family is null || sources is null ? null : new(family, sources, minWeight, maxWeight, style, minWidth, maxWidth, ranges, display, offset);
    }

    internal static string? FamilyName(string text)
    {
        var reader = new CssTokenReader(text);
        if (reader.TryReadString(out var quoted)) return reader.AtEnd && quoted.Length > 0 ? quoted : null;
        var words = new List<string>();
        while (reader.TryReadIdent(out var name)) words.Add(name.ToString());
        if (!reader.AtEnd || words.Count == 0) return null;
        var family = string.Join(" ", words);
        if (CssPropertyMetadata.IsWideKeyword(family) || family.ToLowerInvariant() is
            "serif" or "sans-serif" or "monospace" or "cursive" or "fantasy" or "system-ui" or "default" or "none") return null;
        return family;
    }

    private static CssFontSource[]? ParseSources(string text)
    {
        var result = new List<CssFontSource>(); var reader = new CssTokenReader(text);
        while (!reader.AtEnd)
        {
            if (!reader.TryReadFunction(out var function, out var args)) return null;
            string? reference;
            var local = function.Equals("local", StringComparison.OrdinalIgnoreCase);
            if (local) reference = FamilyName(args.Remaining.ToString());
            else if (function.Equals("url", StringComparison.OrdinalIgnoreCase) &&
                CssImport.TryParse("url(" + args.Remaining.ToString() + ");", 0, out var import)) reference = import!.Reference;
            else return null;
            if (string.IsNullOrEmpty(reference)) return null;
            string? format = null; string[] technologies = [];
            var probe = reader;
            if (probe.TryReadFunction(out function, out args) && function.Equals("format", StringComparison.OrdinalIgnoreCase))
            {
                if (local) return null;
                if (args.TryReadString(out var name)) format = name.ToLowerInvariant();
                else if (args.TryReadIdent(out var identifier)) format = identifier.ToString().ToLowerInvariant();
                if (format is null || !args.AtEnd) return null;
                reader = probe;
            }
            probe = reader;
            if (probe.TryReadFunction(out function, out args) && function.Equals("tech", StringComparison.OrdinalIgnoreCase))
            {
                if (local) return null;
                var names = new List<string>();
                do
                {
                    if (!args.TryReadIdent(out var name)) return null;
                    names.Add(name.ToString().ToLowerInvariant());
                } while (args.TryReadComma());
                if (!args.AtEnd) return null;
                technologies = names.ToArray(); reader = probe;
            }
            result.Add(new(reference, local, format, technologies));
            if (reader.AtEnd) break;
            if (!reader.TryReadComma() || reader.AtEnd) return null;
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    private static bool Range(string text, bool width, out double minimum, out double maximum)
    {
        minimum = maximum = 0; var reader = new CssTokenReader(text);
        var probe = reader;
        if (probe.TryReadIdent(out var keyword) && probe.AtEnd)
        {
            var name = keyword.ToString().ToLowerInvariant();
            var value = width ? name switch
            { "ultra-condensed" => 50, "extra-condensed" => 62.5, "condensed" => 75, "semi-condensed" => 87.5,
                "normal" => 100, "semi-expanded" => 112.5, "expanded" => 125, "extra-expanded" => 150, "ultra-expanded" => 200, _ => 0 }
                : name switch { "normal" => 400, "bold" => 700, _ => 0 };
            minimum = maximum = value; return value > 0;
        }
        if (!reader.TryReadNumber(out minimum, out var unit) || unit != (width ? CssUnit.Percent : CssUnit.None) || !double.IsFinite(minimum) || minimum <= 0 || !width && minimum is < 1 or > 1000) return false;
        maximum = minimum;
        if (!reader.AtEnd && (!reader.TryReadNumber(out maximum, out unit) || unit != (width ? CssUnit.Percent : CssUnit.None) ||
            !double.IsFinite(maximum) || maximum < minimum || !width && maximum > 1000)) return false;
        return reader.AtEnd;
    }

    private static CssUnicodeRange[]? UnicodeRanges(string text)
    {
        var ranges = new List<CssUnicodeRange>();
        foreach (var item in text.Split(','))
        {
            var token = item.Trim();
            if (!token.StartsWith("u+", StringComparison.OrdinalIgnoreCase)) return null;
            var value = token[2..]; int start, end;
            if (value.Contains('?'))
            {
                var first = value.IndexOf('?');
                if (value.Length > 6 || value[first..].Any(c => c != '?') ||
                    !int.TryParse(value.Replace('?', '0'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out start) ||
                    !int.TryParse(value.Replace('?', 'F'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out end)) return null;
            }
            else
            {
                var parts = value.Split('-');
                if (parts.Length is < 1 or > 2 || parts.Any(part => part.Length is < 1 or > 6) ||
                    !int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out start)) return null;
                end = start;
                if (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out end)) return null;
            }
            if (start < 0 || end > 0x10ffff || end < start) return null;
            ranges.Add(new(start, end));
        }
        return ranges.Count > 0 ? ranges.ToArray() : null;
    }
}
