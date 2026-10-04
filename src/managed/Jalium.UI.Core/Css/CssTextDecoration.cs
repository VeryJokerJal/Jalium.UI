using Jalium.UI.Media;

namespace Jalium.UI.Styling;

[Flags]
internal enum CssTextDecorationLine : byte
{
    None = 0,
    Underline = 1,
    Overline = 2,
    LineThrough = 4,
    SpellingError = 8,
    GrammarError = 16,
}

internal enum CssTextDecorationStyle : byte
{
    Solid,
    Double,
    Dotted,
    Dashed,
    Wavy,
}

internal enum CssTextDecorationSkipInset : byte
{
    None,
    Auto,
}

internal readonly record struct CssTextDecorationInset(
    CssLayoutLength Start, CssLayoutLength End, bool Auto)
{
    internal static CssTextDecorationInset Zero =>
        new(CssLayoutLength.Px(0), CssLayoutLength.Px(0), false);

    internal bool UsesPercent => Start.IsPercent || End.IsPercent;
}

internal readonly record struct CssTextDecorationThickness(double Pixels, bool FromFont)
{
    internal static CssTextDecorationThickness Auto => new(double.NaN, false);
    internal static CssTextDecorationThickness Font => new(double.NaN, true);
    internal static CssTextDecorationThickness Length(double pixels) => new(pixels, false);
}

[Flags]
internal enum CssTextUnderlinePosition : byte
{
    Auto = 0,
    FromFont = 1,
    Under = 2,
    Left = 4,
    Right = 8,
}

/// <summary>Independent CSS longhands consumed by text renderers after the cascade.</summary>
internal static class CssTextDecorationProperties
{
    internal static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationLine", typeof(CssTextDecorationLine), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextDecorationLine.None, Changed));

    internal static readonly DependencyProperty StyleProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationStyle", typeof(CssTextDecorationStyle), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextDecorationStyle.Solid, Changed));

    internal static readonly DependencyProperty ColorProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationColor", typeof(Brush), typeof(CssTextDecorationProperties),
        new PropertyMetadata(null, Changed));

    // Keep auto and from-font distinct so the painter can use selected-font metrics.
    internal static readonly DependencyProperty ThicknessProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationThickness", typeof(CssTextDecorationThickness), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextDecorationThickness.Auto, Changed));

    internal static readonly DependencyProperty UnderlineOffsetProperty = DependencyProperty.RegisterAttached(
        "CssTextUnderlineOffset", typeof(CssLayoutLength), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssLayoutLength.Auto, Changed, null, inherits: true));

    internal static readonly DependencyProperty UnderlinePositionProperty = DependencyProperty.RegisterAttached(
        "CssTextUnderlinePosition", typeof(CssTextUnderlinePosition), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextUnderlinePosition.Auto, Changed, null, inherits: true));

    internal static readonly DependencyProperty SkipInsetProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationSkipInset", typeof(CssTextDecorationSkipInset), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextDecorationSkipInset.None, Changed, null, inherits: true));

    internal static readonly DependencyProperty InsetProperty = DependencyProperty.RegisterAttached(
        "CssTextDecorationInset", typeof(CssTextDecorationInset), typeof(CssTextDecorationProperties),
        new PropertyMetadata(CssTextDecorationInset.Zero, Changed));

    internal static CssTextDecorationLine Line(DependencyObject element)
        => (CssTextDecorationLine)(element.GetValue(LineProperty) ?? CssTextDecorationLine.None);

    internal static CssTextDecorationStyle Style(DependencyObject element)
        => (CssTextDecorationStyle)(element.GetValue(StyleProperty) ?? CssTextDecorationStyle.Solid);

    internal static Brush? Color(DependencyObject element) => element.GetValue(ColorProperty) as Brush;

    internal static double Thickness(DependencyObject element)
        => ((CssTextDecorationThickness)(element.GetValue(ThicknessProperty) ??
            CssTextDecorationThickness.Auto)).Pixels;

    internal static bool ThicknessFromFont(DependencyObject element)
        => ((CssTextDecorationThickness)(element.GetValue(ThicknessProperty) ??
            CssTextDecorationThickness.Auto)).FromFont;

    internal static CssLayoutLength UnderlineOffset(DependencyObject element)
        => (CssLayoutLength)(element.GetValue(UnderlineOffsetProperty) ?? CssLayoutLength.Auto);

    internal static CssTextUnderlinePosition UnderlinePosition(DependencyObject element)
        => (CssTextUnderlinePosition)(element.GetValue(UnderlinePositionProperty) ?? CssTextUnderlinePosition.Auto);

    internal static CssTextDecorationSkipInset SkipInset(DependencyObject element)
        => (CssTextDecorationSkipInset)(element.GetValue(SkipInsetProperty) ?? CssTextDecorationSkipInset.None);

    internal static CssTextDecorationInset Inset(DependencyObject element)
        => (CssTextDecorationInset)(element.GetValue(InsetProperty) ?? CssTextDecorationInset.Zero);

    internal static double ResolveUnderlineOffset(DependencyObject element, double fallbackFontSize)
    {
        var offset = UnderlineOffset(element);
        if (offset.IsAuto) return double.NaN;
        var fontSizeProperty = CssDependencyPropertyLookup.Find(element.GetType(), "FontSize");
        var fontSize = fontSizeProperty is not null && element.GetValue(fontSizeProperty) is double size &&
            double.IsFinite(size) && size > 0 ? size : fallbackFontSize;
        return offset.Resolve(fontSize, 0);
    }

    /// <summary>Preserves the public TextDecorations value while CSS longhands remain independent.</summary>
    internal static void ComposeNativeValue(CssNode element,
        Dictionary<DependencyProperty, AppliedCssValue> values, Brush? foreground)
    {
        var nativeProperty = CssDependencyPropertyLookup.Find(element.GetType(), "TextDecorations");
        if (nativeProperty?.PropertyType != typeof(TextDecorationCollection) ||
            !values.TryGetValue(LineProperty, out var lineValue) ||
            lineValue.Value is not CssTextDecorationLine lines) return;

        var layer = lineValue.Layer;
        foreach (var property in new[] { StyleProperty, ColorProperty, ThicknessProperty })
            if (values.TryGetValue(property, out var part) &&
                part.Layer == DependencyObject.LayerValueSource.CssState)
                layer = part.Layer;

        TextDecorationCollection? collection = null;
        if (lines != CssTextDecorationLine.None)
        {
            var nativeForeground = CssDependencyPropertyLookup.Find(element.GetType(), "Foreground") is { } foregroundProperty
                ? element.GetValue(foregroundProperty) as Brush : null;
            var brush = values.TryGetValue(ColorProperty, out var color)
                ? color.Value as Brush : foreground ?? nativeForeground;
            var thickness = values.TryGetValue(ThicknessProperty, out var size) &&
                size.Value is CssTextDecorationThickness { Pixels: var number } &&
                double.IsFinite(number) ? Math.Max(0, number) : 1;
            collection = new TextDecorationCollection();
            if ((lines & (CssTextDecorationLine.SpellingError | CssTextDecorationLine.GrammarError)) != 0)
                collection.Add(new TextDecoration(TextDecorationLocation.Underline,
                    (lines & CssTextDecorationLine.SpellingError) != 0 ? Brushes.Red : Brushes.Green,
                    thickness, 0, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel));
            if ((lines & CssTextDecorationLine.Underline) != 0)
                collection.Add(new TextDecoration(TextDecorationLocation.Underline, brush, thickness,
                    0, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel));
            if ((lines & CssTextDecorationLine.Overline) != 0)
                collection.Add(new TextDecoration(TextDecorationLocation.OverLine, brush, thickness,
                    0, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel));
            if ((lines & CssTextDecorationLine.LineThrough) != 0)
                collection.Add(new TextDecoration(TextDecorationLocation.Strikethrough, brush, thickness,
                    0, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel));
            // A native Foreground brush can be user-owned and mutable. Freezing the
            // synthesized collection must never freeze that brush as a side effect.
            if (brush?.IsFrozen != false && collection.CanFreeze) collection.Freeze();
        }
        values[nativeProperty] = new AppliedCssValue(layer, collection);
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is UIElement element) element.InvalidateVisual();
        else if (target is Jalium.UI.Documents.TextElement textElement)
            textElement.NotifyTextContentChanged();
    }
}
