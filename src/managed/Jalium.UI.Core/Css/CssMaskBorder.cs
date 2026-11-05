namespace Jalium.UI.Styling;

/// <summary>A non-none source remains a mask even when selection or decoding fails.</summary>
internal sealed class CssMaskBorderSource(CssImageValue? definition, CssBorderImageSource? image)
{
    internal CssImageValue? Definition { get; } = definition;
    private CssBorderImageSource? _image = image;
    private long[] _revisions = image?.Resources.Select(resource => resource.Revision).ToArray() ?? [];
    internal CssBorderImageSource? Image => _image;
    internal static readonly CssMaskBorderSource None = new(null, null);
    internal bool Active => Definition is not null;
    internal CssBackgroundImageResource[] Resources => Image?.Resources ?? [];

    internal CssBorderImageSource? ResolveImage(FrameworkElement owner)
    {
        var revisions = Resources.Select(resource => resource.Revision).ToArray();
        if (!_revisions.AsSpan().SequenceEqual(revisions))
        {
            // A discrete animation may have kept this value off the active
            // property while its resource completed. Rebind its live source at
            // paint time rather than retaining the pre-load placeholder.
            var node = CssNode.Get(owner);
            var context = new CssApplyContext(node, CssEngine.BuildLengthContext(node), new());
            _image = Materialize(Definition, context).Image;
            _revisions = Resources.Select(resource => resource.Revision).ToArray();
        }
        return _image;
    }

    internal static CssMaskBorderSource Materialize(CssImageValue? definition, in CssApplyContext context)
    {
        if (definition is null) return None;
        if (context.Element.Target is FrameworkContentElement)
        {
            CssDiagnostics.Report("mask-border-source", CssDiagnosticReason.LossyConversion, null,
                "Document inline masking is not yet rendered");
            return new(definition, null);
        }
        var resources = new List<CssBackgroundImageResource>();
        var image = definition.Select(context) is { } selection
            ? selection.Image.Border(context, selection.Resolution, resources, mask: true) : null;
        return new(definition, image);
    }
}

internal static class CssMaskBorderProperties
{
    private static DependencyProperty Property<T>(string name, T value) => DependencyProperty.RegisterAttached(
        name, typeof(T), typeof(CssMaskBorderProperties), new PropertyMetadata(value, Changed));
    internal static readonly DependencyProperty SourceProperty = Property("CssMaskBorderSource", CssMaskBorderSource.None);
    internal static readonly DependencyProperty SliceProperty = Property("CssMaskBorderSlice", CssBorderImageMeasures.Uniform(0, CssUnit.None, true));
    internal static readonly DependencyProperty WidthProperty = Property("CssMaskBorderWidth", new CssBorderImageMeasures(
        Enumerable.Repeat(new CssBorderImageMeasure(default, Auto: true), 4).ToArray(), default));
    internal static readonly DependencyProperty OutsetProperty = Property("CssMaskBorderOutset", CssBorderImageMeasures.Uniform(0, CssUnit.None, true));
    internal static readonly DependencyProperty RepeatProperty = Property("CssMaskBorderRepeat", new CssBorderImageTiling());
    internal static readonly DependencyProperty ModeProperty = Property("CssMaskBorderMode", CssMaskMode.Alpha);
    internal static bool Active(DependencyObject owner) => owner.GetValue(SourceProperty) is CssMaskBorderSource { Active: true };

    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        CssMaskProperties.Changed(owner, args);
        if (args.Property == SourceProperty && owner is FrameworkElement element &&
            (args.OldValue is CssMaskBorderSource { Active: true }) != (args.NewValue is CssMaskBorderSource { Active: true }))
            element.InvalidateArrange();
    }
}
