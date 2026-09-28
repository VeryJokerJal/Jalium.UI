using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Gives each styled element its own gradient ruler while sharing parsed CSS.</summary>
internal sealed class CssGradientValue : CssCompiledValue
{
    private readonly Brush _prototype;

    internal CssGradientValue(Brush prototype)
    {
        _prototype = prototype;
        if (_prototype.CanFreeze) _prototype.Freeze();
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var brush = _prototype.Clone();
        if (brush.CssGradientLayout is { } layout)
        {
            if (layout.DependsOnCurrentColor)
                CssColorBrushObserver.Observe(context.Element, context.CurrentColor as SolidColorBrush);
            brush.CssGradientLayout = layout.ForElement(context.Lengths, context.CurrentColorValue);
        }
        if (brush.CanFreeze) brush.Freeze();
        context.Slots.SetBackgroundImage(brush);
        return true;
    }
}
