using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Argument parsing and stable random-base selection for CSS Values 5 random-item().</summary>
internal static class CssRandomItem
{
    private static readonly ConditionalWeakTable<CssNode, ConcurrentDictionary<CacheKey, double>> s_values = new();

    private readonly record struct CacheKey(string? Name, string? PropertyScope, bool Auto);
    private readonly record struct RandomKey(double? Fixed, string? Name, bool ElementScoped,
        bool PropertyScoped, bool IndexScoped, string? UserAgentScope, bool Auto);

    internal static bool HasValidSpreadArguments(ReadOnlySpan<char> arguments)
    {
        var reader = new CssTokenReader(arguments);
        var first = true;
        do
        {
            if (!reader.TryReadUntilTopLevelComma(out var item) ||
                first && item.IsEmpty || !TryReadItem(item, out _)) return false;
            first = false;
            if (reader.AtEnd) return true;
            if (!reader.TryReadComma()) return false;
            if (reader.AtEnd) return true;
        } while (true);
    }

    internal static bool TryParseArguments(string arguments, out string key, out List<string> options)
    {
        key = string.Empty;
        options = [];
        var reader = new CssTokenReader(arguments);
        if (!reader.TryReadUntilTopLevelComma(out var first) || first.IsEmpty ||
            !TryReadItem(first, out key) || key.Length == 0 || !reader.TryReadComma()) return false;
        do
        {
            if (reader.AtEnd) { options.Add(string.Empty); break; }
            if (!reader.TryReadUntilTopLevelComma(out var item) || !TryReadItem(item, out var option)) return false;
            options.Add(option);
            if (reader.AtEnd) break;
            if (!reader.TryReadComma()) return false;
        } while (true);
        return options.Count > 0;
    }

    private static bool TryReadItem(ReadOnlySpan<char> item, out string value)
    {
        value = string.Empty;
        var reader = new CssTokenReader(item);
        var significant = reader.Remaining;
        if (!significant.IsEmpty && significant[0] == '{')
        {
            var depth = 0;
            for (var i = 0; i < significant.Length; i++)
            {
                if (significant[i] == '/' && i + 1 < significant.Length && significant[i + 1] == '*')
                { i = CssTokenReader.SkipComment(significant, i) - 1; continue; }
                if (significant[i] is '\'' or '"')
                { i = CssTokenReader.SkipString(significant, i) - 1; continue; }
                if (significant[i] == '\\' && CssSyntax.ReadEscape(significant, ref i, out _)) { i--; continue; }
                if (significant[i] == '{') depth++;
                else if (significant[i] == '}' && --depth == 0)
                {
                    var trailing = new CssTokenReader(significant[(i + 1)..]);
                    if (!trailing.AtEnd) return false;
                    value = significant[1..i].ToString();
                    return CssDeclarationValueSyntax.IsValid(value);
                }
            }
            return false;
        }
        var nesting = 0;
        for (var i = 0; i < significant.Length; i++)
        {
            if (significant[i] == '/' && i + 1 < significant.Length && significant[i + 1] == '*')
            { i = CssTokenReader.SkipComment(significant, i) - 1; continue; }
            if (significant[i] is '\'' or '"')
            { i = CssTokenReader.SkipString(significant, i) - 1; continue; }
            if (significant[i] == '\\' && CssSyntax.ReadEscape(significant, ref i, out _)) { i--; continue; }
            if (significant[i] is '(' or '[') nesting++;
            else if (significant[i] is ')' or ']') nesting--;
            else if (significant[i] is '{' or '}' && nesting == 0) return false;
        }
        value = item.ToString();
        return CssDeclarationValueSyntax.IsValid(value);
    }

    internal static bool TrySelect(string key, int count, CssNode? element, string? property,
        int functionIndex, out int selected)
    {
        selected = 0;
        if (count < 1 || !TryBase(key, element, property, functionIndex, out var random)) return false;
        selected = Math.Min((int)(random * count), count - 1);
        return true;
    }

    internal static bool TryBase(string key, CssNode? element, string? property,
        int functionIndex, out double random)
    {
        random = 0;
        if (!TryParseKey(key, out var parsed)) return false;
        random = parsed.Fixed ?? RandomBase(parsed, element, property, functionIndex);
        return true;
    }

    internal static bool HasFixedKey(ReadOnlySpan<char> arguments)
    {
        var reader = new CssTokenReader(arguments);
        return reader.TryReadUntilTopLevelComma(out var first) &&
            TryParseKey(first.ToString(), out var key) && key.Fixed.HasValue;
    }

    internal static bool HasValidNumberArguments(string arguments)
    {
        var reader = new CssTokenReader(arguments);
        if (reader.TryReadUntilTopLevelComma(out var first) && reader.TryReadComma() &&
            TryParseKey(first.ToString(), out _))
            arguments = "fixed 0," + reader.Remaining.ToString();
        return CssMathExpression.Parse("random", arguments) is not null;
    }

    internal static bool TryResolveNumberArguments(string arguments, CssNode? element,
        string? property, int functionIndex, out string resolved)
    {
        resolved = string.Empty;
        var reader = new CssTokenReader(arguments);
        if (!reader.TryReadUntilTopLevelComma(out var first) || !reader.TryReadComma()) return false;
        var keyed = TryParseKey(first.ToString(), out _);
        var key = keyed ? first.ToString() : "auto";
        if (!TryBase(key, element, property, functionIndex, out var random)) return false;
        resolved = "fixed " + random.ToString("R", CultureInfo.InvariantCulture) + "," +
            (keyed ? reader.Remaining.ToString() : arguments);
        return true;
    }

    private static bool TryParseKey(string text, out RandomKey key)
    {
        key = default;
        var reader = new CssTokenReader(text);
        if (!reader.TryReadIdent(out var first)) return false;
        if (first.Equals("auto", StringComparison.OrdinalIgnoreCase) && reader.AtEnd)
        { key = new(null, null, true, false, true, null, true); return true; }
        if (first.Equals("fixed", StringComparison.OrdinalIgnoreCase))
        {
            if (!reader.TryReadNumber(out var value, out var unit) || unit != CssUnit.None ||
                !double.IsFinite(value) || value < 0 || value > 1 || !reader.AtEnd) return false;
            key = new(value == 1 ? Math.BitDecrement(1d) : value, null, false, false, false, null, false);
            return true;
        }
        string? name = null;
        string? uaScope = null;
        var elementScoped = false;
        var propertyScoped = false;
        var indexScoped = false;
        do
        {
            var token = first.ToString();
            if (token.Equals("element-scoped", StringComparison.OrdinalIgnoreCase))
            { if (elementScoped) return false; elementScoped = true; }
            else if (token.Equals("property-scoped", StringComparison.OrdinalIgnoreCase))
            { if (propertyScoped || indexScoped || uaScope is not null) return false; propertyScoped = true; }
            else if (token.Equals("property-index-scoped", StringComparison.OrdinalIgnoreCase))
            { if (propertyScoped || indexScoped || uaScope is not null) return false; indexScoped = true; }
            else if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
            { if (name is not null) return false; name = token; }
            else if (token.StartsWith("ua-", StringComparison.OrdinalIgnoreCase))
            { if (propertyScoped || indexScoped || uaScope is not null) return false; uaScope = token; }
            else return false;
        } while (reader.TryReadIdent(out first));
        if (!reader.AtEnd) return false;
        key = new(null, name, elementScoped, propertyScoped, indexScoped, uaScope, false);
        return true;
    }

    private static double RandomBase(RandomKey key, CssNode? element, string? property, int functionIndex)
    {
        if (element is null) return Random.Shared.NextDouble();
        var owner = element;
        if (!key.ElementScoped)
            while (owner.FrameworkParent is { } parent) owner = parent;
        var values = s_values.GetValue(owner, static _ => new());
        var propertyScope = key.UserAgentScope ??
            (key.IndexScoped ? $"ua-{property}-{functionIndex + 1}" :
             key.PropertyScoped ? $"ua-{property}" : null);
        var cacheKey = new CacheKey(key.Name, propertyScope, key.Auto);
        return values.GetOrAdd(cacheKey, static _ => Random.Shared.NextDouble());
    }
}

internal sealed class CssRandomContext(string? property, bool resolveNumeric = true)
{
    internal string? Property { get; } = property;
    internal bool ResolveNumeric { get; } = resolveNumeric;
    internal int NextIndex { get; private set; }
    internal int ClaimIndex() => NextIndex++;
}
