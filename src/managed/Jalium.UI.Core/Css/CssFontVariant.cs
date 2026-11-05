namespace Jalium.UI.Styling;

internal static class CssFontVariant
{
    internal static readonly string[] Longhands = ["font-variant-ligatures", "font-variant-position",
        "font-variant-caps", "font-variant-alternates", "font-variant-numeric", "font-variant-east-asian", "font-variant-emoji"];

    internal static bool Expand(ref CssTokenReader reader, CssCompileContext context, List<CssCompiledDeclaration> output)
    {
        var probe = reader;
        if (probe.TryReadIdent(out var word) && probe.AtEnd &&
            (word.Equals("normal", StringComparison.OrdinalIgnoreCase) || word.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            bool none = word.Equals("none", StringComparison.OrdinalIgnoreCase);
            for (int i = 0; i < Longhands.Length; i++)
            {
                var value = new CssTokenReader(none && i == 0 ? "none" : "normal");
                output.Add(new(Longhands[i], Parse(i, ref value)!, false));
            }
            reader = probe;
            return true;
        }

        // Groups can interleave. Each longhand remains responsible for duplicate,
        // exclusive keyword and function argument validation.
        var groups = new List<string>?[Longhands.Length];
        probe = reader;
        while (!probe.AtEnd)
        {
            var raw = probe.Remaining;
            int start = probe.Position;
            var token = probe;
            int group;
            if (token.TryReadFunction(out var function, out _))
            {
                group = -1;
                foreach (var name in CssFontAlternates.FunctionNames)
                    if (function.Equals(name, StringComparison.OrdinalIgnoreCase)) { group = 3; break; }
            }
            else if (token.TryReadIdent(out word))
                group = word.ToString().ToLowerInvariant() switch
                {
                    "common-ligatures" or "no-common-ligatures" or "discretionary-ligatures" or "no-discretionary-ligatures"
                        or "historical-ligatures" or "no-historical-ligatures" or "contextual" or "no-contextual" => 0,
                    "sub" or "super" => 1,
                    "small-caps" or "all-small-caps" or "petite-caps" or "all-petite-caps" or "unicase" or "titling-caps" => 2,
                    "historical-forms" => 3,
                    "lining-nums" or "oldstyle-nums" or "proportional-nums" or "tabular-nums" or "diagonal-fractions"
                        or "stacked-fractions" or "ordinal" or "slashed-zero" => 4,
                    "jis78" or "jis83" or "jis90" or "jis04" or "simplified" or "traditional" or "full-width" or "proportional-width" or "ruby" => 5,
                    "text" or "emoji" or "unicode" => 6,
                    _ => -1,
                };
            else return false;
            if (group < 0) return false;
            (groups[group] ??= []).Add(raw[..(token.Position - start)].ToString());
            probe = token;
        }
        if (groups.All(group => group is null)) return false;
        var expanded = new List<CssCompiledDeclaration>(Longhands.Length);
        for (int i = 0; i < Longhands.Length; i++)
        {
            var value = new CssTokenReader(groups[i] is { } values ? string.Join(' ', values) : "normal");
            var parsed = Parse(i, ref value);
            if (parsed is null || !value.AtEnd) return false;
            expanded.Add(new(Longhands[i], parsed, false));
        }
        output.AddRange(expanded);
        reader = probe;
        return true;
    }

    private static CssCompiledValue? Parse(int group, ref CssTokenReader reader) => group switch
    {
        0 => CssFontLigatures.ParseVariant(ref reader), 1 => CssFontPosition.Parse(ref reader),
        2 => CssFontCaps.Parse(ref reader), 3 => CssFontAlternates.Parse(ref reader),
        4 => CssFontNumeric.Parse(ref reader), 5 => CssFontEastAsian.Parse(ref reader),
        _ => CssFontEmoji.Parse(ref reader),
    };
}
