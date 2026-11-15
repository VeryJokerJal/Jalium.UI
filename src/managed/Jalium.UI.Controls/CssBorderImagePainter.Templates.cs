using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static partial class CssBorderImagePainter
{
    private sealed record TemplateRoute(FrameworkElement Root, FrameworkElement Host, FrameworkElement[] Ancestors);
    private static readonly ConditionalWeakTable<Control, TemplateRoute> TemplateRoutes = new();
    internal static FrameworkElement StyleOwner(FrameworkElement owner) => owner.CssBorderImageTemplatePaintOwner ?? owner;
    internal static bool HasTemplateRoute(FrameworkElement owner) => owner is Control control && TemplateRoutes.TryGetValue(control, out _);

    internal static void InvalidateTemplate(DependencyObject target)
    {
        if (target is Control control && TemplateRoutes.TryGetValue(control, out var route)) route.Host.InvalidateVisual();
    }

    internal static void PrepareTemplate(Visual visual)
    {
        if (visual is not Control owner) return;
        TemplateRoutes.TryGetValue(owner, out var previous);
        var root = owner.TemplateRootInternal;
        var active = owner.GetValue(CssBorderImageProperties.SourceProperty) is not null && !CssReplacedContent.IsActive(owner);
        if (active && root is not null && previous is not null && ReferenceEquals(root, previous.Root) && SameBox(previous.Host, owner)) return;
        if (previous is not null)
        {
            previous.Host.CssBorderImageTemplatePaintOwner = null;
            foreach (var ancestor in previous.Ancestors) ancestor.CssBorderImageTemplateInkOwner = null;
            previous.Host.InvalidateVisual(); TemplateRoutes.Remove(owner);
        }
        if (!active || root is null) return;
        FrameworkElement? host = null;
        var pending = new Queue<FrameworkElement>(); pending.Enqueue(root); var count = 0;
        while (pending.TryDequeue(out var candidate) && count++ < 4096)
        {
            if (candidate is Border && SameBox(candidate, owner)) { host = candidate; break; }
            if (candidate is Control && !ReferenceEquals(candidate, root)) continue;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(candidate); index++)
                if (VisualTreeHelper.GetChild(candidate, index) is FrameworkElement child) pending.Enqueue(child);
        }
        host ??= SameBox(root, owner) ? root : null;
        if (host is null) return;
        var ancestors = new List<FrameworkElement>();
        for (Visual? item = host; item is FrameworkElement ancestor && !ReferenceEquals(ancestor, owner); item = ancestor.VisualParent)
        { ancestor.CssBorderImageTemplateInkOwner = owner; ancestors.Add(ancestor); }
        host.CssBorderImageTemplatePaintOwner = owner;
        TemplateRoutes.Add(owner, new(root, host, ancestors.ToArray())); host.InvalidateVisual();
    }

    private static bool SameBox(FrameworkElement host, FrameworkElement owner)
    {
        if (host.RenderSize != owner.RenderSize || host.RenderSize.Width <= 0 || host.RenderSize.Height <= 0) return false;
        var position = host.TransformToAncestor(owner);
        return Math.Abs(position.X) < .001 && Math.Abs(position.Y) < .001;
    }
}
