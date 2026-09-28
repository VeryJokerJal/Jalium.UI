namespace Jalium.UI.Styling;

/// <summary>RFC 5646 canonical and extlang forms for Selectors 4 :lang() matching.</summary>
internal static class CssLanguageTagCanonicalizer
{
    internal static string ToExtlangForm(string value)
    {
        if (value.Length == 0) return value;
        var lower = AsciiLower(value);
        if (lower.StartsWith("x-", StringComparison.Ordinal)) return lower;
        var parts = lower.Split('-');
        if (!parts.Contains("*", StringComparer.Ordinal)) SortExtensions(parts);

        lower = string.Join('-', parts);
        if (CssLanguageTagData.TagAliases.TryGetValue(lower, out var preferred))
            parts = preferred.Split('-');

        if (CssLanguageTagData.LanguageAliases.TryGetValue(parts[0], out preferred))
            parts[0] = preferred;

        // An extlang's preferred value replaces its primary language prefix in
        // canonical form. Reintroduce that prefix only after all other aliases.
        if (parts.Length > 1 && CssLanguageTagData.ExtlangPrefixes.TryGetValue(parts[1], out var prefix) &&
            parts[0] == prefix)
            parts = parts[1..];

        for (var i = 1; i < parts.Length; i++)
        {
            if (IsSingleton(parts[i])) break; // Extensions and private-use subtags are opaque.
            if (CssLanguageTagData.RegionAliases.TryGetValue(parts[i], out preferred) ||
                CssLanguageTagData.VariantAliases.TryGetValue(parts[i], out preferred))
                parts[i] = preferred;
        }

        if (CssLanguageTagData.ExtlangPrefixes.TryGetValue(parts[0], out prefix))
            return prefix + "-" + string.Join('-', parts);
        return string.Join('-', parts);
    }

    private static void SortExtensions(string[] parts)
    {
        var start = Array.FindIndex(parts, 1, parts.Length - 1, IsSingleton);
        if (start < 0 || parts[start] == "x") return;

        var groups = new List<string[]>();
        var index = start;
        while (index < parts.Length && parts[index] != "x")
        {
            if (!IsSingleton(parts[index])) return;
            var next = index + 1;
            while (next < parts.Length && !IsSingleton(parts[next])) next++;
            if (next == index + 1) return;
            groups.Add(parts[index..next]);
            index = next;
        }
        if (groups.Count < 2) return;
        groups.Sort(static (left, right) => StringComparer.Ordinal.Compare(left[0], right[0]));
        var target = start;
        foreach (var group in groups)
            foreach (var subtag in group) parts[target++] = subtag;
    }

    private static bool IsSingleton(string part) => part.Length == 1 &&
        (part[0] is >= 'a' and <= 'z' or >= '0' and <= '9');

    private static string AsciiLower(string value)
    {
        return string.Create(value.Length, value, static (target, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                target[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
    }
}
