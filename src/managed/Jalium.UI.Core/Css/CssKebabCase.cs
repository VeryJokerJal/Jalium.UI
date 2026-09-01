namespace Jalium.UI.Styling;

/// <summary>
/// kebab-case ↔ PascalCase conversion for the dependency-property fallback channel
/// ("is-tab-stop" ↔ "IsTabStop"). Round-trips: ToKebab(ToPascal(x)) == x for well-formed input.
/// </summary>
internal static class CssKebabCase
{
    /// <summary>Converts "is-tab-stop" to "IsTabStop". Returns false for malformed input.</summary>
    public static bool TryToPascal(string kebab, out string pascal)
    {
        pascal = string.Empty;
        if (string.IsNullOrEmpty(kebab) || kebab[0] == '-' || kebab[^1] == '-')
        {
            return false;
        }

        var builder = new System.Text.StringBuilder(kebab.Length);
        var upperNext = true;
        foreach (var c in kebab)
        {
            if (c == '-')
            {
                if (upperNext)
                {
                    return false; // "a--b"
                }

                upperNext = true;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }

            builder.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }

        pascal = builder.ToString();
        return true;
    }

    /// <summary>Converts "IsTabStop" to "is-tab-stop" and "ZIndex" to "z-index" (runs of capitals split per letter).</summary>
    public static string ToKebab(string pascal)
    {
        if (string.IsNullOrEmpty(pascal))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(pascal.Length + 4);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
