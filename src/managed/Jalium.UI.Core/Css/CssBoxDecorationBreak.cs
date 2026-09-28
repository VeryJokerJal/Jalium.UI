namespace Jalium.UI.Styling;

internal enum CssBoxDecorationBreak : byte
{
    Slice,
    Clone,
}

internal static class CssBoxDecorationBreakProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBoxDecorationBreak", typeof(CssBoxDecorationBreak), typeof(CssBoxDecorationBreakProperties),
        new PropertyMetadata(CssBoxDecorationBreak.Slice, Changed));

    internal static CssBoxDecorationBreak Value(DependencyObject element)
        => (CssBoxDecorationBreak)(element.GetValue(ValueProperty) ?? CssBoxDecorationBreak.Slice);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is UIElement element) element.InvalidateVisual();
        else if (target is Jalium.UI.Documents.TextElement textElement)
            textElement.NotifyTextContentChanged();
    }
}
