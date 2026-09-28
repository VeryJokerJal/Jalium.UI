using Jalium.UI.Media.Effects;

namespace Jalium.UI.Styling;

/// <summary>Inherited CSS shadow effect for text and its decorations.</summary>
internal static class CssTextShadowProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssTextShadow", typeof(Effect), typeof(CssTextShadowProperties),
        new PropertyMetadata(null, Changed, null, inherits: true));

    internal static Effect? Value(DependencyObject element)
        => element.GetValue(ValueProperty) as Effect;

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        NotifyPresentationChanged(target);
    }

    internal static void NotifyPresentationChanged(DependencyObject target)
    {
        if (target is UIElement element) element.InvalidateVisual();
        else if (target is Jalium.UI.Documents.TextElement textElement)
            textElement.NotifyTextContentChanged();
        else if (target is Jalium.UI.Documents.FlowDocument document)
            document.NotifyTextPresentationChanged();
    }
}
