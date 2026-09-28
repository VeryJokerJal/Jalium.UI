using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssCaretShape { Auto, Bar, Block, Underscore }

internal static class CssCaretShapeProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssCaretShape", typeof(CssCaretShape), typeof(CssCaretShapeProperties),
        new PropertyMetadata(CssCaretShape.Auto, static (target, _) => InvalidateVisualTree(target),
            null, inherits: true));

    internal static CssCaretShape Get(DependencyObject target)
        => (CssCaretShape)target.GetValue(ValueProperty)!;

    internal static void InvalidateVisualTree(DependencyObject target)
    {
        if (target is Visual visual) InvalidateVisualTree(visual);
        else if (target is UIElement element) element.InvalidateVisual();
    }

    private static void InvalidateVisualTree(Visual visual)
    {
        if (visual is UIElement element) element.InvalidateVisual();
        for (var index = 0; index < visual.VisualChildrenCount; index++)
            if (visual.GetVisualChild(index) is { } child)
                InvalidateVisualTree(child);
    }
}
