using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Retains the computed font width percentage beside the native width class.</summary>
internal sealed class CssFontStretchValue(double percentage) : CssCompiledValue
{
    internal static bool TryRead(ref CssTokenReader reader, out double percentage)
    {
        percentage = 0;
        var probe = reader;
        if (probe.TryReadIdent(out var keyword) && probe.AtEnd)
        {
            percentage = keyword.ToString().ToLowerInvariant() switch
            {
                "ultra-condensed" => 50,
                "extra-condensed" => 62.5,
                "condensed" => 75,
                "semi-condensed" => 87.5,
                "normal" => 100,
                "semi-expanded" => 112.5,
                "expanded" => 125,
                "extra-expanded" => 150,
                "ultra-expanded" => 200,
                _ => double.NaN,
            };
            if (double.IsNaN(percentage)) return false;
            reader = probe;
            return true;
        }

        probe = reader;
        if (!probe.TryReadNumber(out var number, out var unit) || !probe.AtEnd ||
            unit != CssUnit.Percent || !double.IsFinite(number) || number < 0) return false;
        percentage = number;
        reader = probe;
        return true;
    }

    internal static double Percentage(FontStretch stretch) => stretch.ToOpenTypeStretch() switch
    {
        1 => 50, 2 => 62.5, 3 => 75, 4 => 87.5, 5 => 100,
        6 => 112.5, 7 => 125, 8 => 150, 9 => 200,
        _ => 100,
    };

    internal static double Computed(CssNode node)
    {
        for (var current = node; current is not null; current = CssMatcher.CssAncestor(current))
        {
            var property = CssDependencyPropertyLookup.Find(current.Target.GetType(), "FontStretch");
            if (property is null) continue;
            var layer = current.Target.GetEffectiveValueLayer(property);
            var native = current.Target.HasLocalOrAnimatedValue(property) ||
                layer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
            if (native) return current.GetValue(property) is FontStretch stretch ? Percentage(stretch) : 100;
            if (current.CssRuntimeState?.FontStretchPercentage is double computed) return computed;
            if (layer is not null)
                return current.GetValue(property) is FontStretch stretch ? Percentage(stretch) : 100;
        }
        return 100;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (!new CssNamedValue("font-stretch", "FontStretch", Native(percentage)).TryApply(in context, sink))
            return false;
        if (context.Element.CssRuntimeState is { } state) state.FontStretchPercentage = percentage;
        return true;
    }

    private static FontStretch Native(double percentage)
    {
        var widthClass = percentage switch
        {
            < 56.25 => 1,
            < 68.75 => 2,
            < 81.25 => 3,
            < 93.75 => 4,
            < 106.25 => 5,
            < 118.75 => 6,
            < 137.5 => 7,
            < 175 => 8,
            _ => 9,
        };
        return FontStretch.FromOpenTypeStretch(widthClass);
    }
}
