using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal interface ICssCaretAnimationHost
{
    void OnCssCaretAnimationChanged();
}

internal static class CssCaretAnimationProperties
{
    internal static readonly DependencyProperty ManualProperty = DependencyProperty.RegisterAttached(
        "CssCaretAnimationManual", typeof(bool), typeof(CssCaretAnimationProperties),
        new PropertyMetadata(false, static (target, _) =>
        {
            if (target is Visual visual)
                NotifyVisualTree(visual);
            else if (target is ICssCaretAnimationHost host)
                host.OnCssCaretAnimationChanged();
        }, null, inherits: true));

    internal static bool IsManual(DependencyObject target)
        => (bool)target.GetValue(ManualProperty)!;

    private static void NotifyVisualTree(Visual visual)
    {
        if (visual is ICssCaretAnimationHost host)
            host.OnCssCaretAnimationChanged();

        for (var index = 0; index < visual.VisualChildrenCount; index++)
            if (visual.GetVisualChild(index) is { } child)
                NotifyVisualTree(child);
    }
}
