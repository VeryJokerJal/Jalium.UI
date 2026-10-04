namespace Jalium.UI.Media;

internal enum CssBackgroundBox
{
    Border,
    Padding,
    Content,
}

internal readonly record struct CssBackgroundPaintLayer(
    Brush? Image, CssBackgroundBox Origin, CssBackgroundBox Clip);

/// <summary>Per-layer CSS box choices kept beside the effective Background value.</summary>
internal sealed class CssBackgroundPaintState
{
    internal Brush? Composite { get; }
    internal Brush? Color { get; }
    internal CssBackgroundBox ColorClip { get; }
    internal CssBackgroundPaintLayer[] Layers { get; }
    internal CssBackgroundSize[] Sizes { get; }
    internal CssBackgroundPosition[] Positions { get; }
    internal CssBackgroundRepeat[] Repeats { get; }
    internal CssBackgroundBox[] Origins { get; }
    internal CssBackgroundBox[] Clips { get; }

    internal CssBackgroundPaintState(Brush? composite, Brush? color,
        CssBackgroundBox colorClip, CssBackgroundPaintLayer[] layers,
        CssBackgroundSize[] sizes, CssBackgroundPosition[] positions,
        CssBackgroundRepeat[] repeats, CssBackgroundBox[] origins, CssBackgroundBox[] clips)
    {
        Composite = composite;
        Color = color;
        ColorClip = colorClip;
        Layers = layers;
        Sizes = sizes;
        Positions = positions;
        Repeats = repeats;
        Origins = origins;
        Clips = clips;
    }
}

internal static class CssBackgroundPaintProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBackgroundPaint", typeof(CssBackgroundPaintState),
        typeof(CssBackgroundPaintProperties), new PropertyMetadata(null,
            static (element, _) => (element as UIElement)?.InvalidateVisual()));

    internal static CssBackgroundPaintState? Get(DependencyObject element)
        => (CssBackgroundPaintState?)element.GetValue(ValueProperty);
}
