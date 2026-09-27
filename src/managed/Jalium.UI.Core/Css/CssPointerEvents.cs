namespace Jalium.UI.Styling;

internal enum CssPointerEventsMode : byte
{
    Auto,
    None,
    Inherit,
}

internal sealed record CssPointerEventsValue(CssPointerEventsMode Mode)
{
    internal static readonly CssPointerEventsValue Auto = new(CssPointerEventsMode.Auto);
    internal static readonly CssPointerEventsValue None = new(CssPointerEventsMode.None);
    internal static readonly CssPointerEventsValue Inherit = new(CssPointerEventsMode.Inherit);
}

/// <summary>
/// CSS pointer-events excludes a box from targeting, while an explicitly auto
/// descendant may still be targeted. Native IsHitTestVisible gates the subtree.
/// </summary>
internal static class CssPointerEventsProperties
{
    private static int s_hasDeclarations;
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssPointerEvents", typeof(CssPointerEventsValue), typeof(CssPointerEventsProperties),
        new PropertyMetadata(null, static (target, _) =>
        {
            System.Threading.Volatile.Write(ref s_hasDeclarations, 1);
            if (target is UIElement) UIElement.InvalidateHitTestCache();
        }));

    internal static CssPointerEventsValue EffectiveValue(DependencyObject target)
    {
        if (System.Threading.Volatile.Read(ref s_hasDeclarations) == 0)
            return CssPointerEventsValue.Auto;
        for (var current = CssNode.Get(target); current is not null; current = CssMatcher.CssAncestor(current))
        {
            if (current.Target is UIElement native &&
                native.HasLocalOrAnimatedValue(UIElement.IsHitTestVisibleProperty))
                return CssPointerEventsValue.Auto;
            if (current.Target.GetEffectiveValueLayer(ValueProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            {
                var value = (CssPointerEventsValue)current.GetValue(ValueProperty)!;
                if (value.Mode != CssPointerEventsMode.Inherit) return value;
            }
        }
        return CssPointerEventsValue.Auto;
    }

    internal static CssPointerEventsMode Effective(DependencyObject target) => EffectiveValue(target).Mode;
}
