using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Projects formatting children through boxless elements; the source tree stays intact.</summary>
internal static class CssContentsLayout
{
    private static bool s_active;
    internal static void Observe(CssDisplayMode mode) { if (mode == CssDisplayMode.Contents) s_active = true; }
    internal static bool IsSpecified(DependencyObject? target) => s_active &&
        target is FrameworkElement { CssDisplayMode: CssDisplayMode.Contents };
    internal static bool IsUnusual(FrameworkElement element) =>
        element is not CssGeneratedText && !element.IsCssContentHost || CssReplacedContent.HasReplacement(element);
    internal static bool Suppressed(DependencyObject? target) => IsSpecified(target) && IsUnusual((FrameworkElement)target!);
    internal static bool IsContents(DependencyObject? target) => IsSpecified(target) &&
        !IsUnusual((FrameworkElement)target!) && CssMatcher.CssAncestor(CssNode.Get(target!)) is not null;
    internal static bool IsRoot(FrameworkElement target) => IsSpecified(target) && !IsUnusual(target) &&
        CssMatcher.CssAncestor(CssNode.Get(target)) is null;

    internal static FrameworkElement? BoxParent(Visual element)
    {
        var parent = element.VisualParent as FrameworkElement;
        while (IsContents(parent)) parent = parent!.VisualParent as FrameworkElement;
        return parent;
    }

    internal static UIElement[]? Project(FrameworkElement owner, bool includeInactive, bool painting = false)
    {
        if (!s_active) return null;
        var raw = new CssPanelChildren(owner, includeInactive, expandContents: false);
        if (!raw.Enumerate().Any(IsSpecified)) return null;
        var result = new List<UIElement>();
        foreach (var child in raw) Append(child);
        return result.ToArray();

        void Append(UIElement child)
        {
            if (Suppressed(child)) return;
            if (!IsContents(child)) { result.Add(child); return; }
            if (child.Visibility == Visibility.Collapsed || painting && child.Visibility == Visibility.Hidden) return;
            var host = (FrameworkElement)child;
            host.PrepareCssContent();
            foreach (var descendant in new CssPanelChildren(host, includeInactive, expandContents: false)) Append(descendant);
        }
    }

    internal static void Prepare(FrameworkElement owner, bool arrange)
    {
        if (!s_active) return;
        foreach (var child in new CssPanelChildren(owner, includeInactive: true, expandContents: false))
        {
            if (!IsSpecified(child)) continue;
            var host = (FrameworkElement)child;
            if (child.Visibility == Visibility.Collapsed) continue;
            if (IsContents(host)) { host.PrepareCssContent(); Prepare(host, arrange); }
            host.Measure(default);
            if (arrange) host.Arrange(default);
        }
    }

    internal static void Changed(FrameworkElement element)
    {
        Observe(element.CssDisplayMode);
        for (Visual? current = element; current is not null; current = current.VisualParent)
        {
            if (current is not UIElement ui) continue;
            ui.InvalidateMeasure(); ui.InvalidateArrange(); ui.InvalidateVisual();
            if (current is Panel panel) panel.InvalidateCssFlowOrder();
        }
        UIElement.InvalidateHitTestCache();
        element.UpdateIsVisibleFromTree(forceDescendants: true);
        Jalium.UI.Input.KeyboardFocusRevalidation.OnFocusabilityChanged(element);
    }

    internal static void CompletePaint(FrameworkElement owner)
    {
        if (!s_active) return;
        foreach (var child in new CssPanelChildren(owner, includeInactive: true, expandContents: false))
        {
            if (!IsContents(child)) continue;
            CompletePaint((FrameworkElement)child);
            // These source ancestors were bypassed by the box painter. Retire
            // their aggregate flags so a later child edit can propagate again.
            child.ClearRenderDirty();
        }
    }
}
