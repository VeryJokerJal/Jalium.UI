using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>The CSS font style, including an explicitly specified oblique angle.</summary>
internal readonly record struct CssComputedFontStyle(FontStyle Native, double? AngleDegrees = null)
{
    internal static CssComputedFontStyle Normal => new(FontStyles.Normal);
}

/// <summary>Retains the computed oblique angle beside the native font face class.</summary>
internal sealed class CssFontStyleValue(CssComputedFontStyle style) : CssCompiledValue
{
    internal static bool TryRead(ref CssTokenReader reader, out CssComputedFontStyle style)
    {
        style = default;
        var probe = reader;
        if (!probe.TryReadIdent(out var keyword)) return false;
        if (keyword.Equals("normal", StringComparison.OrdinalIgnoreCase))
            style = CssComputedFontStyle.Normal;
        else if (keyword.Equals("italic", StringComparison.OrdinalIgnoreCase))
            style = new(FontStyles.Italic);
        else if (keyword.Equals("oblique", StringComparison.OrdinalIgnoreCase))
        {
            style = new(FontStyles.Oblique);
            var angleReader = probe;
            if (angleReader.TryReadNumber(out var number, out var unit) &&
                unit is CssUnit.Deg or CssUnit.Rad or CssUnit.Grad or CssUnit.Turn)
            {
                if (!CssUnitConversion.TryToDegrees(number, unit, out var degrees) ||
                    !double.IsFinite(degrees)) return false;
                if (angleReader.NumberWasCalculated)
                    degrees = Math.Clamp(degrees, -90, 90);
                else if (degrees is < -90 or > 90) return false;
                style = new(FontStyles.Oblique, degrees);
                probe = angleReader;
            }
        }
        else return false;

        reader = probe;
        return true;
    }

    internal static CssComputedFontStyle Computed(CssNode node)
    {
        for (var current = node; current is not null; current = CssMatcher.CssAncestor(current))
        {
            var property = CssDependencyPropertyLookup.Find(current.Target.GetType(), "FontStyle");
            if (property is null) continue;
            var layer = current.Target.GetEffectiveValueLayer(property);
            var native = current.Target.HasLocalOrAnimatedValue(property) ||
                layer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
            if (native) return FromNative(current.GetValue(property));
            if (current.CssRuntimeState?.FontStyleComputed is { } computed) return computed;
            if (layer is not null) return FromNative(current.GetValue(property));
        }
        return CssComputedFontStyle.Normal;
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        if (!new CssNamedValue("font-style", "FontStyle", style.Native).TryApply(in context, sink))
            return false;
        if (context.Element.CssRuntimeState is { } state) state.FontStyleComputed = style;
        return true;
    }

    private static CssComputedFontStyle FromNative(object? value)
        => new(value is FontStyle style ? style : FontStyles.Normal);
}
