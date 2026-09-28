using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Binds background size lengths to each styled element before paint-time resolution.</summary>
internal sealed class CssBackgroundSizesValue(CssBackgroundSize[] sizes) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var resolved = new CssBackgroundSize[sizes.Length];
        for (var i = 0; i < sizes.Length; i++)
        {
            sizes[i].ObserveDependencies(context.Lengths);
            resolved[i] = sizes[i].ForElement(context.Lengths);
        }
        context.Slots.SetBackgroundSizes(resolved);
        return true;
    }
}
