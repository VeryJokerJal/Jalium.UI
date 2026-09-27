using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal readonly record struct CssFontFamilyItem(string Name, bool IsGeneric);

/// <summary>Preserves the ordered CSS family list before generic names map to native fonts.</summary>
internal sealed class CssComputedFontFamily(CssFontFamilyItem[] items)
{
    private readonly string[] _renderingNames = items.Select(static item =>
        item.IsGeneric ? item.Name switch
        {
            "sans-serif" or "system-ui" or "ui-sans-serif" => "Segoe UI",
            "serif" or "ui-serif" => "Times New Roman",
            "monospace" or "ui-monospace" => "Consolas",
            _ => item.Name,
        } : item.Name).ToArray();

    internal static CssComputedFontFamily Initial { get; } = new([new("sans-serif", true)]);
    internal IReadOnlyList<CssFontFamilyItem> Items { get; } = items;

    internal IReadOnlyList<string> RenderingNames => _renderingNames;

    internal string RenderingSource => string.Join(", ", _renderingNames.Select(static name =>
        name.Contains(',') ? "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : name));
}

internal sealed class CssFontFamilyValue(CssComputedFontFamily family) : CssCompiledValue
{
    internal static bool TryRead(ref CssTokenReader reader, out CssComputedFontFamily family)
    {
        family = CssComputedFontFamily.Initial;
        var probe = reader;
        var items = new List<CssFontFamilyItem>();
        do
        {
            if (!probe.TryReadUntilTopLevelComma(out var segment) ||
                !TryReadItem(segment, out var item)) return false;
            items.Add(item);
            if (probe.AtEnd) break;
            if (!probe.TryReadComma() || probe.AtEnd) return false;
        } while (true);
        family = new(items.ToArray());
        reader = probe;
        return true;
    }

    internal static CssComputedFontFamily Computed(CssNode node)
    {
        for (var current = node; current is not null; current = CssMatcher.CssAncestor(current))
        {
            var property = CssDependencyPropertyLookup.Find(current.Target.GetType(), "FontFamily");
            if (property is null) continue;
            var layer = current.Target.GetEffectiveValueLayer(property);
            var native = current.Target.HasLocalOrAnimatedValue(property) ||
                layer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
            if (native) return FromNative(current.GetValue(property));
            if (current.CssRuntimeState?.FontFamilyComputed is { } computed) return computed;
            if (layer is not null) return FromNative(current.GetValue(property));
        }
        return CssComputedFontFamily.Initial;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var native = new FontFamily(family.RenderingSource)
        {
            IsCssFamily = true,
            CssComputedFamily = family,
        };
        if (!new CssNamedValue("font-family", "FontFamily", native).TryApply(in context, sink))
            return false;
        if (context.Element.CssRuntimeState is { } state) state.FontFamilyComputed = family;
        return true;
    }

    private static bool TryReadItem(ReadOnlySpan<char> segment, out CssFontFamilyItem item)
    {
        item = default;
        var reader = new CssTokenReader(segment);
        if (reader.TryReadString(out var quoted))
        {
            if (!reader.AtEnd) return false;
            item = new(quoted, false);
            return true;
        }

        reader = new CssTokenReader(segment);
        if (reader.TryReadFunction(out var function, out var arguments))
        {
            if (!function.Equals("generic", StringComparison.OrdinalIgnoreCase) ||
                !arguments.TryReadIdent(out var script) || !arguments.AtEnd || !reader.AtEnd)
                return false;
            var name = script.ToString().ToLowerInvariant();
            if (name is not ("fangsong" or "kai" or "khmer-mul" or "nastaliq")) return false;
            item = new($"generic({name})", true);
            return true;
        }

        reader = new CssTokenReader(segment);
        var names = new List<string>();
        while (reader.TryReadIdent(out var ident))
        {
            var name = ident.ToString();
            if (IsReserved(name)) return false;
            names.Add(name);
        }
        if (names.Count == 0 || !reader.AtEnd) return false;
        if (names.Count == 1 && IsGeneric(names[0]))
            item = new(names[0].ToLowerInvariant(), true);
        else
        {
            if (names.Any(IsGeneric)) return false;
            item = new(string.Join(" ", names), false);
        }
        return true;
    }

    private static bool IsGeneric(string name) => name.ToLowerInvariant() is
        "serif" or "sans-serif" or "system-ui" or "cursive" or "fantasy" or "math" or
        "monospace" or "ui-serif" or "ui-sans-serif" or "ui-monospace" or "ui-rounded";

    private static bool IsReserved(string name) => name.ToLowerInvariant() is
        "initial" or "inherit" or "unset" or "revert" or "revert-layer" or
        "caption" or "icon" or "menu" or "message-box" or "small-caption" or
        "status-bar" or "default";

    private static CssComputedFontFamily FromNative(object? value)
    {
        if (value is not FontFamily family || family.Source.Length == 0)
            return CssComputedFontFamily.Initial;
        if (family.CssComputedFamily is { } computed) return computed;
        var reader = new CssTokenReader(family.Source);
        var items = new List<CssFontFamilyItem>();
        while (reader.TryReadUntilTopLevelComma(out var segment))
        {
            var nameReader = new CssTokenReader(segment);
            var name = nameReader.TryReadString(out var quoted) && nameReader.AtEnd
                ? quoted : segment.Trim().ToString();
            items.Add(new(name, false));
            if (reader.AtEnd || !reader.TryReadComma()) break;
        }
        return items.Count > 0 ? new(items.ToArray()) : CssComputedFontFamily.Initial;
    }
}
