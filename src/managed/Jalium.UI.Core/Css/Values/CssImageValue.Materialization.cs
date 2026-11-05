using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

internal sealed partial class CssImageValue
{
    private readonly record struct Materialized(Brush? Brush, double Density = 1);

    private Materialized Materialize(in CssApplyContext context, CssNode consumer, bool content,
        double? resolution, List<CssBackgroundImageResource> resources, bool border = false, bool mask = false, bool maskReference = false)
    {
        if (_options is not null)
            return Select(context) is { } selected ? selected.Image.Materialize(context, consumer, content, selected.Resolution, resources, border, mask) : default;
        if (_annotation is { } annotation)
        {
            var annotatedSource = annotation.Image?.Materialize(context, consumer, content, resolution, resources, border, mask) ?? default;
            return new(new ImageBrush
            {
                Stretch = Stretch.Fill,
                CssImageLayout = new CssAnnotatedImageLayout(annotatedSource.Brush, annotatedSource.Density, annotation.Fallback?.Brush(context),
                    annotation.Fragment, annotation.Direction, context.Element, resources.Distinct().ToArray()),
            });
        }
        if (_gradient is not null) return new(Gradient(context));
        if (_color is not null)
        {
            var brush = _color.Brush(context); brush.CssImageLayout = new CssSolidImageLayout(brush.Color);
            return new(brush);
        }
        if (_crossFade is not null)
        {
            var leaves = new List<(CssImageValue? Image, CssImageColor? Color, double Weight)>();
            if (!Flatten(context, 1, leaves)) return default;
            var parts = new List<CssCrossFadePart>();
            foreach (var leaf in leaves)
            {
                var part = leaf.Image is { } definition ? definition.Materialize(context, consumer, content, null, resources, border, mask)
                    : new Materialized(leaf.Color!.Brush(context));
                parts.Add(new(part.Brush, part.Density, leaf.Weight));
            }
            var visual = context.Element.VisualOwner;
            var dpi = visual is null ? 1 : VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(visual) as Visual ?? visual).DpiScaleY;
            return new(new ImageBrush { Stretch = Stretch.Fill, CssImageLayout = new CssCrossFadeLayout(parts.ToArray(), resources.Distinct().ToArray(), dpi) });
        }
        var prototype = mask ? maskReference ? _maskReference : _mask : _background;
        if (prototype is null) return default;
        var previous = consumer.Target is CssGeneratedImage image ? image.ActiveResources :
            mask ? border ? (consumer.GetValue(CssMaskBorderProperties.SourceProperty) as CssMaskBorderSource)?.Resources ?? []
                : (consumer.GetValue(CssMaskProperties.ImagesProperty) as CssMaskImages)?.Resources ?? [] :
            consumer.GetValue(border ? CssBorderImageProperties.ResourcesProperty : CssBackgroundImageResource.ActiveProperty) as CssBackgroundImageResource[] ?? [];
        var resource = resources.FirstOrDefault(prototype.Matches) ?? previous.FirstOrDefault(prototype.Matches) ?? prototype;
        if (!resources.Contains(resource)) resources.Add(resource);
        var source = resource.Get(consumer, content);
        if (source is null) return default;
        var density = source is BitmapSource or AnimatedBitmap ? resolution ?? NaturalResolution(source) : 1;
        return new(new ImageBrush(source) { Stretch = Stretch.Fill }, density);
    }

    private CssShapeImageValue ShapeValue(in CssApplyContext context, List<CssShapeImageResource>? resources = null, double? resolution = null)
    {
        var used = resources ?? [];
        if (_options is not null)
            return Select(context) is { } selected ? selected.Image.ShapeValue(context, used, selected.Resolution) : new(null, null);
        if (_annotation is { } annotation)
        {
            var source = annotation.Image?.ShapeValue(context, used, resolution);
            return CssAnnotatedImageLayout.Shape(source, annotation.Fallback?.Brush(context), annotation.Fragment,
                annotation.Direction, context.Element);
        }
        if (_crossFade is null)
        {
            var resource = _shape;
            if (resource is not null)
            {
                var previous = (context.Element.GetValue(CssShapeOutsideProperties.ValueProperty) as CssShapeImageValue)?.Resources ?? [];
                resource = used.FirstOrDefault(resource.Matches) ?? previous.FirstOrDefault(resource.Matches) ?? resource;
                if (!used.Contains(resource)) used.Add(resource);
            }
            return new(resource, _gradient is not null ? Gradient(context) : _color?.Brush(context), resolution: resolution);
        }
        var leaves = new List<(CssImageValue? Image, CssImageColor? Color, double Weight)>();
        if (!Flatten(context, 1, leaves)) return new(null, null);
        var parts = new List<(CssShapeImageValue Image, double Weight)>();
        foreach (var leaf in leaves)
            parts.Add((leaf.Image is { } image ? image.ShapeValue(context, used) : new(null, leaf.Color!.Brush(context)), leaf.Weight));
        return CssCrossFadeShape.Create(parts.ToArray());
    }
}
