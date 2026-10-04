using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssUserSelect : byte { Auto, Text, None, Contain, All }

internal static class CssUserSelectProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssUserSelect", typeof(CssUserSelect), typeof(CssUserSelectProperties),
        new PropertyMetadata(CssUserSelect.Auto, Changed));

    internal static void Register() => CssPropertyRegistry.Register(new CssPropertyDescriptor
    {
        Name = "user-select", Kind = CssPropertyKind.Longhand,
        StorageProperty = ValueProperty, TransitionTargetDpName = ValueProperty.Name,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            if (!reader.TryReadIdent(out var ident) || !reader.AtEnd) return null;
            var value = ident.ToString().ToLowerInvariant() switch
            {
                "auto" => CssUserSelect.Auto,
                "text" => CssUserSelect.Text,
                "none" => CssUserSelect.None,
                "contain" => CssUserSelect.Contain,
                "all" => CssUserSelect.All,
                _ => (CssUserSelect?)null,
            };
            return value is { } mode ? new CssImmediateValue(ValueProperty, mode) : null;
        },
    });

    // user-select is computed as a non-inherited keyword. Only auto takes its used
    // value from the parent: all and none propagate, while text/contain become text.
    // A tree with no user-select declaration retains native XAML selection defaults.
    internal static CssUserSelect? Resolve(DependencyObject target)
    {
        if (!CssEngine.IsActive) return null;
        var node = CssNode.Get(target);
        var authored = false;
        var isSelf = true;
        for (; node is not null; node = CssMatcher.CssAncestor(node), isSelf = false)
        {
            var layer = node.Target.GetEffectiveValueLayer(ValueProperty);
            var cssValue = layer is DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;
            var cssAnimation = node.Target is UIElement animated &&
                animated.HasAutomaticTransition(ValueProperty);
            if (!cssValue && !cssAnimation)
                continue;
            authored = true;
            var value = (CssUserSelect)node.GetValue(ValueProperty)!;
            if (value == CssUserSelect.Auto) continue;
            if (isSelf || value is CssUserSelect.All or CssUserSelect.None)
                return value;
            return CssUserSelect.Text;
        }
        return authored ? CssUserSelect.Text : null;
    }

    internal static bool AllowsSelection(DependencyObject target, bool nativeEnabled, bool nativeLocal)
    {
        if (nativeLocal) return nativeEnabled;
        return Resolve(target) switch
        {
            CssUserSelect.None => false,
            null => nativeEnabled,
            _ => true,
        };
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is UIElement element) Refresh(element);
    }

    private static void Refresh(UIElement element)
    {
        if (element is Label label) label.RefreshCssUserSelect();
        if (element is TextBlock text) text.RefreshCssUserSelect();
        for (var i = 0; i < element.InternalVisualChildrenCount; i++)
            if (element.InternalGetVisualChild(i) is UIElement child) Refresh(child);
    }
}
