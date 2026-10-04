using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Materializes every CSS background image for one styled element.</summary>
internal sealed class CssBackgroundImagesValue : CssCompiledValue
{
    private readonly Brush?[] _prototypes;

    internal CssBackgroundImagesValue(List<Brush?> images)
    {
        _prototypes = images.ToArray();
        foreach (var image in _prototypes)
            if (image?.CanFreeze == true) image.Freeze();
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var images = new Brush?[_prototypes.Length];
        for (var i = 0; i < images.Length; i++)
        {
            if (_prototypes[i] is not { } prototype) continue;
            var image = prototype.Clone();
            if (image.CssGradientLayout is { } layout)
            {
                if (layout.DependsOnCurrentColor)
                    CssColorBrushObserver.Observe(context.Element, context.CurrentColor as SolidColorBrush);
                image.CssGradientLayout = layout.ForElement(context.Lengths, context.CurrentColorValue);
            }
            if (image.CanFreeze) image.Freeze();
            images[i] = image;
        }

        context.Slots.SetBackgroundImages(images);
        return true;
    }
}
