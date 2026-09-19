namespace Jalium.UI.Styling;

internal sealed record CssLayerDeclaration(string Name, int Offset, CssCondition? Condition = null, bool Anonymous = false);

/// <summary>First-appearance order at each layer nesting level.</summary>
internal sealed class CssLayerOrder
{
    private readonly Dictionary<string, Dictionary<string, int>> _siblings = new(StringComparer.Ordinal);
    public int[] GetKey(string? name)
    {
        if (name is null) return [];
        var parts = name.Split('.');
        var key = new int[parts.Length];
        var parent = string.Empty;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!_siblings.TryGetValue(parent, out var siblings)) _siblings[parent] = siblings = new(StringComparer.Ordinal);
            if (!siblings.TryGetValue(parts[i], out var index)) siblings[parts[i]] = index = siblings.Count;
            key[i] = index;
            parent += "." + parts[i];
        }
        return key;
    }

    public static int Compare(int[] left, int[] right)
    {
        for (var i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            // Unlayered declarations are the final implicit layer in each parent.
            var a = i < left.Length ? left[i] : int.MaxValue;
            var b = i < right.Length ? right[i] : int.MaxValue;
            if (a != b) return a.CompareTo(b);
        }
        return 0;
    }

    internal static string AnonymousName() => "#anonymous-" + Guid.NewGuid().ToString("N");

    internal static string? NormalizeName(string name)
    {
        var reader = new CssTokenReader(name);
        var parts = new List<string>();
        do
        {
            if (!reader.TryReadIdent(out var part)) return null;
            parts.Add(CssDeclarationValue.Identifier(part.ToString()));
            if (reader.AtEnd) break;
            if (!reader.TryReadDelimiter('.') || reader.AtEnd) return null;
        } while (true);
        return string.Join('.',parts);
    }

    public static bool IsName(string name) => NormalizeName(name) is not null;
}
