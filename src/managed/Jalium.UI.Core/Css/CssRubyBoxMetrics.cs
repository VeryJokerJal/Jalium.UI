using Jalium.UI.Controls;
using Jalium.UI.Interop;

namespace Jalium.UI.Styling;

/// <summary>Used inline font areas inside a Ruby source; authored typography remains unchanged.</summary>
internal static class CssRubyBoxMetrics
{
    private sealed record Context(WeakReference<UIElement> Root, WeakReference<FrameworkElement> Owner, bool FontArea);
    private static bool s_enabled;
    internal static readonly DependencyProperty ContextProperty = DependencyProperty.RegisterAttached(
        "RubySourceMetrics", typeof(Context), typeof(CssRubyBoxMetrics),
        new PropertyMetadata(null, Changed, null, inherits: true));

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs change)
    {
        if (target is TextBlock text) text.InvalidateCssTextLayout();
        else if (target is UIElement element) element.InvalidateCssAllocation();
    }

    internal static void Set(UIElement element, FrameworkElement owner, bool fontArea)
    {
        if (element.ReadLocalValue(ContextProperty) is Context prior && prior.FontArea == fontArea &&
            prior.Owner.TryGetTarget(out var old) && ReferenceEquals(old, owner)) return;
        s_enabled = true;
        element.SetValue(ContextProperty, new Context(new(element), new(owner), fontArea));
    }

    internal static void Clear(UIElement element, FrameworkElement owner)
    {
        if (element.ReadLocalValue(ContextProperty) is Context prior && prior.Owner.TryGetTarget(out var old) && ReferenceEquals(old, owner))
            element.ClearValue(ContextProperty);
    }

    internal static bool IsFontArea(DependencyObject target)
    {
        if (!s_enabled) return false;
        var context = target.GetValue(ContextProperty) as Context;
        while (context is not null && context.Root.TryGetTarget(out var root))
        {
            bool attached = false;
            if (context.Owner.TryGetTarget(out var owner))
                for (DependencyObject? node = root; node is not null; node = Parent(node))
                    if (ReferenceEquals(node, owner)) { attached = true; break; }
            if (!attached)
            {
                // A retained source can move without its old formatter running.
                // Its stale local marker must not mask a new owner's context.
                context = Parent(root)?.GetValue(ContextProperty) as Context;
                continue;
            }
            if (!context.FontArea) return false;
            for (DependencyObject? node = target; node is not null; node = Parent(node))
            {
                if (Independent(node)) return false;
                if (ReferenceEquals(node, root)) return true;
            }
            return false;
        }
        return false;
    }

    private static DependencyObject? Parent(DependencyObject target) => target switch
    {
        FrameworkElement element => element.VisualParent as DependencyObject ?? element.Parent,
        FrameworkContentElement content => content.Parent,
        _ => null,
    };

    internal static bool Eligible(UIElement element) => !Independent(element);

    internal static bool IsStructural(FrameworkElement element) =>
        element.CssDisplayMode is CssDisplayMode.RubyBaseContainer or CssDisplayMode.RubyTextContainer &&
        !CssReplacedContent.IsActive(element);

    internal static bool IgnoresSizes(FrameworkElement element)
    {
        if (element.CssDisplayMode == CssDisplayMode.Native || element.CssDisplayMode == CssDisplayMode.Block && element.CssInlineOuter)
            return element is TextBlock && IsFontArea(element) && !CssReplacedContent.IsActive(element);
        if (!CssRubyRoles.IsBox(element.CssDisplayMode)) return false;
        if (CssReplacedContent.IsActive(element)) return false;
        return IsStructural(element) || element.CssDisplayMode == CssDisplayMode.RubyBase ||
            element.CssDisplayMode == CssDisplayMode.RubyText && !CssRubyWritingMode.IsInterCharacterAnnotation(element) ||
            element.CssDisplayMode == CssDisplayMode.Ruby && element.CssInlineOuter;
    }

    private static bool Independent(DependencyObject target) => target is FrameworkElement element &&
        (CssReplacedContent.IsActive(element) || element.CssDisplayMode == CssDisplayMode.Block && !element.CssInlineOuter ||
            element.CssDisplayMode is CssDisplayMode.FlowRoot or CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table ||
            element.CssDisplayMode == CssDisplayMode.Native && element is Control);

    internal static (double Height, double Baseline) FontBounds(double fontSize, in TextMetrics metrics)
    {
        var ascent = float.IsFinite(metrics.Ascent) ? Math.Max(0, metrics.Ascent) : fontSize * .8;
        var descent = float.IsFinite(metrics.Descent) ? Math.Max(0, metrics.Descent) : fontSize * .2;
        return (ascent + descent, ascent);
    }
}
