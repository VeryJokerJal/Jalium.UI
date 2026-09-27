namespace Jalium.UI.Styling;

/// <summary>Tracks the CSS auto color separately from the native outline brush.</summary>
internal static class CssOutlineAutoColorProperties
{
    internal static readonly DependencyProperty AutoProperty = DependencyProperty.RegisterAttached(
        "CssOutlineAutoColor", typeof(bool), typeof(CssOutlineAutoColorProperties),
        new PropertyMetadata(false, static (target, _) =>
        {
            if (target is UIElement element) element.InvalidateVisual();
        }));

    internal static bool IsAuto(DependencyObject target)
        => (bool)target.GetValue(AutoProperty)!;

    internal static bool Apply(in CssApplyContext context, ICssSetterSink sink)
        => ApplyComputed(context, sink, computedAuto: true);

    internal static bool ApplyComputed(in CssApplyContext context, ICssSetterSink sink, bool computedAuto)
    {
        var current = context.CurrentColor;
        CssColorBrushObserver.Observe(context.Element, current as Jalium.UI.Media.SolidColorBrush);
        sink.Set(FrameworkElement.OutlineBrushProperty, current);
        sink.Set(AutoProperty, computedAuto);
        return true;
    }
}
