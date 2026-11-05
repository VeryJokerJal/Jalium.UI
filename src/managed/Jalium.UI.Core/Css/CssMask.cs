using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssMaskMode { MatchSource, Alpha, Luminance }
internal enum CssMaskComposite { Add, Subtract, Intersect, Exclude }
internal enum CssMaskBox { Border, Padding, Content, Fill, Stroke, View, NoClip }

internal sealed class CssMaskList<T>(T[] values) : IEquatable<CssMaskList<T>>
{
    internal T[] Values { get; } = values;
    internal T At(int index) => Values[index % Values.Length];
    public bool Equals(CssMaskList<T>? other) => other is not null && Values.SequenceEqual(other.Values);
    public override bool Equals(object? other) => other is CssMaskList<T> list && Equals(list);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var value in Values) hash.Add(value); return hash.ToHashCode(); }
}

internal sealed class CssMaskImages(CssImageValue?[] definitions, Brush?[] brushes, CssBackgroundImageResource[] resources)
{
    internal CssImageValue?[] Definitions { get; } = definitions;
    internal Brush?[] Brushes { get; } = brushes;
    internal CssBackgroundImageResource[] Resources { get; } = resources;
    internal bool Active => Definitions.Any(value => value is not null);
    internal static readonly CssMaskImages None = new([null], [null], []);
    internal static CssMaskImages Materialize(CssImageValue?[] definitions, in CssApplyContext context)
    {
        var brushes = new Brush?[definitions.Length]; var resources = new List<CssBackgroundImageResource>();
        if (context.Element.Target is FrameworkContentElement && definitions.Any(image => image is not null))
        {
            CssDiagnostics.Report("mask-image", CssDiagnosticReason.LossyConversion, null,
                "Document inline masking is not yet rendered");
            return new(definitions, brushes, []);
        }
        for (var i = 0; i < definitions.Length; i++)
            if (definitions[i]?.Select(context) is { } selection)
                brushes[i] = selection.Image.Background(context, selection.Resolution, out _, resources, mask: true, maskReference: definitions[i]!.DirectUrl);
        return new(definitions, brushes, resources.ToArray());
    }
}

internal static class CssMaskProperties
{
    private static DependencyProperty Property<T>(string name, T value) => DependencyProperty.RegisterAttached(
        name, typeof(T), typeof(CssMaskProperties), new PropertyMetadata(value, Changed));
    internal static readonly DependencyProperty ImagesProperty = Property("CssMaskImages", CssMaskImages.None);
    internal static readonly DependencyProperty ModesProperty = Property("CssMaskModes", new CssMaskList<CssMaskMode>([CssMaskMode.MatchSource]));
    internal static readonly DependencyProperty RepeatsProperty = Property("CssMaskRepeats", new CssMaskList<CssBackgroundRepeat>([CssBackgroundRepeat.Both]));
    internal static readonly DependencyProperty PositionsProperty = Property("CssMaskPositions", new CssMaskList<CssBackgroundPosition>([CssBackgroundPosition.Initial]));
    internal static readonly DependencyProperty OriginsProperty = Property("CssMaskOrigins", new CssMaskList<CssMaskBox>([CssMaskBox.Border]));
    internal static readonly DependencyProperty ClipsProperty = Property("CssMaskClips", new CssMaskList<CssMaskBox>([CssMaskBox.Border]));
    internal static readonly DependencyProperty SizesProperty = Property("CssMaskSizes", new CssMaskList<CssBackgroundSize>([new(CssBackgroundSizeMode.Auto)]));
    internal static readonly DependencyProperty CompositesProperty = Property("CssMaskComposites", new CssMaskList<CssMaskComposite>([CssMaskComposite.Add]));
    internal static readonly DependencyProperty TypeProperty = Property("CssMaskType", CssMaskMode.Luminance);
    internal static bool Active(DependencyObject owner) => owner.GetValue(ImagesProperty) is CssMaskImages { Active: true } ||
        CssMaskBorderProperties.Active(owner);
    internal static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        Jalium.UI.Controls.CssMaskPainter.Invalidate(owner);
        CssDocumentConsumers.Invalidate(CssNode.Get(owner));
        // Masking changes the painting group, including promoted fixed children.
        if (args.Property == ImagesProperty && owner is FrameworkElement element &&
            (args.OldValue is CssMaskImages { Active: true }) != (args.NewValue is CssMaskImages { Active: true })) element.InvalidateArrange();
        (owner as UIElement)?.InvalidateVisual();
    }
}

internal static class CssMaskSampling
{
    internal static double Sample(byte[] pixels, int width, int height, Rect bounds, Point point)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return 0;
        var u = (point.X - bounds.X) / bounds.Width; var v = (point.Y - bounds.Y) / bounds.Height;
        if (!(u >= 0 && u < 1 && v >= 0 && v < 1)) return 0;
        var x = Math.Clamp(u * width - .5, 0, width - 1d); var y = Math.Clamp(v * height - .5, 0, height - 1d);
        var x0 = (int)x; var y0 = (int)y; var x1 = Math.Min(width - 1, x0 + 1); var y1 = Math.Min(height - 1, y0 + 1);
        var fx = x - x0; var fy = y - y0;
        return ((pixels[y0 * width + x0] * (1 - fx) + pixels[y0 * width + x1] * fx) * (1 - fy) +
            (pixels[y1 * width + x0] * (1 - fx) + pixels[y1 * width + x1] * fx) * fy) / 255;
    }
}
