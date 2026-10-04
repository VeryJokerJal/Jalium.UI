using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Inherited CSS insertion-caret color; null preserves the native caret choice.</summary>
internal static class CssCaretColorProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssCaretColor", typeof(Brush), typeof(CssCaretColorProperties),
        new PropertyMetadata(null, static (target, _) =>
        {
            CssCaretShapeProperties.InvalidateVisualTree(target);
        }, null, inherits: true));

    internal static Brush? Get(DependencyObject target) => (Brush?)target.GetValue(ValueProperty);
}
