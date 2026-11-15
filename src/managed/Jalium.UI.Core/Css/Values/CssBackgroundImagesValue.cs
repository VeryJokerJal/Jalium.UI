using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Materializes every CSS background image for one styled element.</summary>
internal sealed class CssBackgroundImagesValue : CssCompiledValue
{
    private readonly CssImageValue?[] _prototypes;

    internal CssBackgroundImagesValue(List<CssImageValue?> images)
    {
        _prototypes = images.ToArray();
    }

    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var images = new Brush?[_prototypes.Length];
        var resources = new List<CssBackgroundImageResource>();
        for (var i = 0; i < images.Length; i++)
        {
            if (_prototypes[i]?.Select(context) is not { } selection) continue;
            images[i] = selection.Image.Background(context, selection.Resolution, out _, resources);
        }

        context.Slots.SetBackgroundImages(images);
        sink.Set(CssBackgroundImageResource.ActiveProperty, resources.ToArray());
        return true;
    }
}
