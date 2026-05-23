namespace Jalium.Extensions.Configuration;

/// <summary>Helpers for the colon-delimited configuration key path syntax.</summary>
public static class ConfigurationPath
{
    public const string KeyDelimiter = ":";

    public static string Combine(params string[] pathSegments)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);
        return string.Join(KeyDelimiter, pathSegments);
    }

    public static string Combine(IEnumerable<string> pathSegments)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);
        return string.Join(KeyDelimiter, pathSegments);
    }

    public static string? GetSectionKey(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        var lastDelim = path.LastIndexOf(':');
        return lastDelim < 0 ? path : path.Substring(lastDelim + 1);
    }

    public static string? GetParentPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var lastDelim = path.LastIndexOf(':');
        return lastDelim < 0 ? null : path.Substring(0, lastDelim);
    }
}

/// <summary>Case-insensitive comparer used to merge keys across providers.</summary>
public sealed class ConfigurationKeyComparer : IComparer<string>, IEqualityComparer<string>
{
    public static ConfigurationKeyComparer Instance { get; } = new();
    private static readonly char[] s_keyDelimiter = { ':' };

    public int Compare(string? x, string? y)
    {
        var xParts = x?.Split(s_keyDelimiter, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var yParts = y?.Split(s_keyDelimiter, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var min = Math.Min(xParts.Length, yParts.Length);
        for (int i = 0; i < min; i++)
        {
            var xp = xParts[i];
            var yp = yParts[i];
            var xIsInt = int.TryParse(xp, out var xn);
            var yIsInt = int.TryParse(yp, out var yn);
            int cmp;
            if (xIsInt && yIsInt) cmp = xn.CompareTo(yn);
            else if (xIsInt) cmp = -1;
            else if (yIsInt) cmp = 1;
            else cmp = string.Compare(xp, yp, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
        }
        return xParts.Length.CompareTo(yParts.Length);
    }

    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
}
