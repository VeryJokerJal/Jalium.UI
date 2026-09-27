using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Binds parsed background positions to the styled element's length context.</summary>
internal sealed class CssBackgroundPositionsValue(CssBackgroundPosition[] positions) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var resolved = new CssBackgroundPosition[positions.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i].ObserveDependencies(context.Lengths);
            resolved[i] = positions[i].ForElement(context.Lengths);
        }
        context.Slots.SetBackgroundPositions(resolved);
        return true;
    }
}
