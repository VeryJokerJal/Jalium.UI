using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>
/// Resolves scroll-dependent positions after the outer arrange has committed all ancestor
/// origins. Only registered positioned boxes are revisited, including during the physical
/// scrolling translation fast path. The visual tree and the normal-flow slots stay intact.
/// </summary>
internal static class CssPositionedLayout
{
    private sealed class Entry
    {
        internal Point Offset;
        internal Size? MeasuredBasis;
        internal Size ParentSize;
        internal WeakReference<Visual>? PaintOwner;
    }

    private sealed class Viewport { internal Size Size; }
    private sealed class PaintGroup { internal readonly List<WeakReference<FrameworkElement>> Elements = []; }
    private sealed class Registry
    {
        internal readonly List<WeakReference<FrameworkElement>> Elements = [];
        internal readonly ConditionalWeakTable<Visual, Viewport> Viewports = new();
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Entry> Entries = new();
    private static readonly ConditionalWeakTable<Dispatcher, Registry> Registries = new();
    private static readonly ConditionalWeakTable<Visual, PaintGroup> PaintGroups = new();
    [ThreadStatic] private static int s_arrangeDepth;
    [ThreadStatic] private static bool s_refreshing;
    [ThreadStatic] private static Visual? s_promotedChild;
    internal static bool IsActive { get; private set; }

    private static bool ShouldRegister(FrameworkElement element) =>
        element.CssLayout?.Position is CssPositionMode.Fixed or CssPositionMode.Sticky or CssPositionMode.Absolute ||
        CssCompositingProperties.HasExplicitZIndex(element) && CssCompositingProperties.ZIndexApplies(element);

    internal static void UpdateStackingPromotion(FrameworkElement element)
    {
        Entries.TryGetValue(element, out var previous);
        var register = ShouldRegister(element);
        if (previous is null && !register) return;
        if (!register)
        {
            if (previous is not null)
            {
                Move(element, previous, default);
                SetPaintOwner(element, previous, null);
            }
            Entries.Remove(element);
            if (Registries.TryGetValue(element.Dispatcher, out var registry))
                registry.Elements.RemoveAll(reference => !reference.TryGetTarget(out var target) || target == element);
            if (element.VisualParent is Panel oldParent) oldParent.InvalidateCssFlowOrder();
            UIElement.InvalidateHitTestCache();
            return;
        }
        if (previous is null)
        {
            Entries.Add(element, new());
            Registries.GetOrCreateValue(element.Dispatcher).Elements.Add(new(element));
            IsActive = true;
        }
        if (element.VisualParent is Panel parent) parent.InvalidateCssFlowOrder();
        if (element.IsArrangeValid && element.VisualParent is not null && s_arrangeDepth == 0 && !s_refreshing)
        {
            Refresh(element);
        }
        if (Entries.TryGetValue(element, out var entry) &&
            entry.PaintOwner?.TryGetTarget(out var owner) == true &&
            owner is UIElement host)
        {
            host.InvalidateVisual();
        }
        UIElement.InvalidateHitTestCache();
    }

    internal static void StyleChanged(FrameworkElement element)
    {
        Entries.TryGetValue(element, out var previous);
        var register = ShouldRegister(element);
        if (previous is null && !register) return;
        // Changing flow participation need not change DesiredSize. Invalidate the
        // containing panels explicitly so their cached flow allocations are discarded.
        for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            if (ancestor is UIElement ui) ui.InvalidateMeasure();
        if (previous is not null)
        {
            Move(element, previous, default);
            previous.MeasuredBasis = null;
            SetPaintOwner(element, previous, null);
        }
        if (!register)
        {
            Entries.Remove(element);
            if (Registries.TryGetValue(element.Dispatcher, out var registry))
                registry.Elements.RemoveAll(reference => !reference.TryGetTarget(out var target) || target == element);
            if (element.VisualParent is Panel oldParent) oldParent.InvalidateCssFlowOrder();
            return;
        }
        if (previous is null)
        {
            Entries.Add(element, new());
            Registries.GetOrCreateValue(element.Dispatcher).Elements.Add(new(element));
        }
        if (element.VisualParent is Panel parent) parent.InvalidateCssFlowOrder();
        IsActive = true;
    }

    internal static void RecordLayout(FrameworkElement element)
    {
        if (IsActive && Entries.TryGetValue(element, out var entry)) entry.Offset = default;
    }

    internal static void RecordAvailable(UIElement element, Size available)
    {
        if (!IsActive || element.VisualParent is not null) return;
        if (element is FrameworkElement root)
            available = new Size(Finite(root.CssPreferredWidth(available.Width), available.Width),
                Finite(root.CssPreferredHeight(available.Height), available.Height));
        if (double.IsFinite(available.Width) && double.IsFinite(available.Height))
            Registries.GetOrCreateValue(element.Dispatcher).Viewports.GetOrCreateValue(element).Size = available;
    }

    internal static void BeginArrange() => s_arrangeDepth++;
    internal static void EndArrange(UIElement element, bool completed)
    {
        if (--s_arrangeDepth == 0 && completed && !s_refreshing) Refresh(element);
    }

    internal static void ContainingBlockChanged(UIElement ancestor)
    {
        if (!IsActive || !Registries.TryGetValue(ancestor.Dispatcher, out var registry)) return;
        foreach (var reference in registry.Elements)
        {
            if (!reference.TryGetTarget(out var element) || !ShouldRegister(element)) continue;
            for (var parent = element.VisualParent; parent is not null; parent = parent.VisualParent)
                if (parent == ancestor)
                {
                    if (Entries.TryGetValue(element, out var entry)) entry.MeasuredBasis = null;
                    element.InvalidateMeasure();
                    element.InvalidateArrange();
                    for (var current = element.VisualParent; current is not null; current = current.VisualParent)
                        if (current is UIElement ui) { ui.InvalidateMeasure(); ui.InvalidateArrange(); }
                    break;
                }
        }
    }

    internal static bool EscapesScrollport(Visual element, Visual scrollport)
    {
        if (!IsActive) return false;
        for (var current = element; current is not null && current != scrollport; current = current.VisualParent)
        {
            if (current is FrameworkElement { CssLayout.Position: CssPositionMode.Fixed } fixedBox)
            {
                var containing = FixedContainingAncestor(fixedBox);
                if (containing is null || containing == scrollport) return true;
                for (var ancestor = containing.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
                    if (ancestor == scrollport) return false;
                return true;
            }
            if (current is FrameworkElement { CssLayout.Position: CssPositionMode.Absolute } absBox)
            {
                var containing = AbsoluteContainingAncestor(absBox);
                if (containing is null || containing == scrollport) return true;
                for (var ancestor = containing.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
                    if (ancestor == scrollport) return false;
                return true;
            }
        }
        return false;
    }

    internal static bool BypassesOverflowClip(Visual descendant, Visual ancestor)
    {
        if (!IsActive) return false;
        for (var current = descendant; current is not null && current != ancestor; current = current.VisualParent)
        {
            if (current is FrameworkElement { CssLayout.Position: CssPositionMode.Fixed } fixedBox)
            {
                var containing = (Visual?)FixedContainingAncestor(fixedBox) ?? Root(fixedBox);
                for (var skipped = fixedBox.VisualParent; skipped is not null && skipped != containing; skipped = skipped.VisualParent)
                    if (skipped == ancestor) return true;
            }
            else if (current is FrameworkElement { CssLayout.Position: CssPositionMode.Absolute } absBox)
            {
                var containing = (Visual?)AbsoluteContainingAncestor(absBox) ?? Root(absBox);
                for (var skipped = absBox.VisualParent; skipped is not null && skipped != containing; skipped = skipped.VisualParent)
                    if (skipped == ancestor) return true;
            }
        }
        return false;
    }

    private static Visual Root(Visual element)
    {
        while (element.VisualParent is { } parent) element = parent;
        return element;
    }

    private static Size ViewportSize(FrameworkElement element, Size fallback)
    {
        if (Css.GetViewportMetrics(element) is { } metrics) return metrics.Dynamic;
        var root = Root(element);
        if (Registries.GetOrCreateValue(element.Dispatcher).Viewports.TryGetValue(root, out var viewport))
            return viewport.Size;
        if (root is FrameworkElement framework)
            return new Size(Finite(framework.CssPreferredWidth(fallback.Width),
                    framework.RenderSize.Width > 0 ? framework.RenderSize.Width : fallback.Width),
                Finite(framework.CssPreferredHeight(fallback.Height),
                    framework.RenderSize.Height > 0 ? framework.RenderSize.Height : fallback.Height));
        return fallback;
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? Math.Max(0, value) : fallback;

    internal static bool EstablishesFixedContainingBlock(FrameworkElement framework) =>
        !CssContentsLayout.IsContents(framework) &&
        (framework.RenderTransform is not null || framework.LayoutTransform is not null ||
         CssTransform3D.HasPerspective(framework) || CssTransform3D.IsPreserve3DAuthored(framework) ||
         framework.Effect is Effect { CssFilterLayer: true } or EffectGroup { CssCombinedShadowAndFilter: true } ||
         framework.BackdropEffect is { HasEffect: true } ||
         CssContainerProperties.HasSizeContainment(framework) ||
         CssContainmentProperties.HasLayoutContainment(framework) || CssContainmentProperties.HasPaintContainment(framework));

    internal static bool EstablishesAbsoluteContainingBlock(FrameworkElement framework) =>
        !CssContentsLayout.IsContents(framework) &&
        (framework.CssLayout?.Position is not null and not CssPositionMode.Static ||
         EstablishesFixedContainingBlock(framework));

    internal static FrameworkElement? FixedContainingAncestor(FrameworkElement element)
    {
        for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            if (ancestor is FrameworkElement framework && EstablishesFixedContainingBlock(framework))
                return framework;
        return null;
    }

    internal static FrameworkElement? AbsoluteContainingAncestor(FrameworkElement element)
    {
        for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
            if (ancestor is FrameworkElement framework && EstablishesAbsoluteContainingBlock(framework))
                return framework;
        return null;
    }

    internal static bool UsesAncestorContainingBlock(FrameworkElement element)
    {
        if (element.CssLayout?.Position != CssPositionMode.Absolute)
            return false;
        var boxParent = CssContentsLayout.BoxParent(element) ?? element.VisualParent;
        if (boxParent is null)
            return false;
        var containing = (Visual?)AbsoluteContainingAncestor(element) ?? Root(element);
        return !ReferenceEquals(containing, boxParent);
    }

    private static Rect FixedContainingBox(FrameworkElement element, Size fallback)
    {
        if (FixedContainingAncestor(element) is { } ancestor)
        {
            var size = ancestor.CssArrangeSizeForChildren;
            if (size.Width == 0 && size.Height == 0)
                size = ancestor.RenderSize;
            if (size.Width == 0 && size.Height == 0)
                size = new Size(Finite(ancestor.CssPreferredWidth(fallback.Width), fallback.Width),
                    Finite(ancestor.CssPreferredHeight(fallback.Height), fallback.Height));
            var (border, _) = CssBoxMetrics.BackgroundInsets(ancestor, ancestor.CssLayout?.ContainingWidthCache ?? size.Width);
            return new Rect(border.Left, border.Top, Math.Max(0, size.Width - border.Left - border.Right),
                Math.Max(0, size.Height - border.Top - border.Bottom));
        }
        return new Rect(ViewportSize(element, fallback));
    }

    internal static Rect AbsoluteContainingBox(FrameworkElement element, Size fallback)
    {
        if (AbsoluteContainingAncestor(element) is { } ancestor)
        {
            var size = ancestor.CssArrangeSizeForChildren;
            if (size.Width == 0 && size.Height == 0)
                size = ancestor.RenderSize;
            if (size.Width == 0 && size.Height == 0)
                size = new Size(Finite(ancestor.CssPreferredWidth(fallback.Width), fallback.Width),
                    Finite(ancestor.CssPreferredHeight(fallback.Height), fallback.Height));
            var (border, _) = CssBoxMetrics.BackgroundInsets(ancestor, ancestor.CssLayout?.ContainingWidthCache ?? size.Width);
            return new Rect(border.Left, border.Top, Math.Max(0, size.Width - border.Left - border.Right),
                Math.Max(0, size.Height - border.Top - border.Bottom));
        }
        if (Root(element) is FrameworkElement rootFe && !ReferenceEquals(rootFe, element))
        {
            var size = rootFe.CssArrangeSizeForChildren;
            if (size.Width == 0 && size.Height == 0)
                size = rootFe.RenderSize;
            if (size.Width == 0 && size.Height == 0)
                size = ViewportSize(element, fallback);
            var (border, _) = CssBoxMetrics.BackgroundInsets(rootFe, rootFe.CssLayout?.ContainingWidthCache ?? size.Width);
            return new Rect(border.Left, border.Top, Math.Max(0, size.Width - border.Left - border.Right),
                Math.Max(0, size.Height - border.Top - border.Bottom));
        }
        return new Rect(ViewportSize(element, fallback));
    }

    internal static Size MeasureContainingSize(FrameworkElement element, Size fallback) =>
        element.CssLayout?.Position == CssPositionMode.Fixed ? FixedContainingBox(element, fallback).Size :
        UsesAncestorContainingBlock(element) ? AbsoluteContainingBox(element, fallback).Size : fallback;

    internal static void MeasureFixed(FrameworkElement element, Size fallback)
    {
        var basis = FixedContainingBox(element, fallback).Size;
        if (Entries.TryGetValue(element, out var entry))
        {
            if (entry.MeasuredBasis != basis) element.InvalidateMeasure();
            entry.MeasuredBasis = basis;
        }
        CssLayoutBox.MeasureWithContainingBlock(element,
            CssAbsoluteLayout.ComputeMeasureConstraint(element.CssLayout!, basis, element), basis);
    }

    internal static void ArrangeFixed(FrameworkElement element, Size parentSize)
    {
        if (Entries.TryGetValue(element, out var entry)) entry.ParentSize = parentSize;
        MeasureFixed(element, parentSize);
        var containing = FixedContainingBox(element, parentSize);
        var layout = element.CssLayout!;
        var containingElement = FixedContainingAncestor(element) ?? Root(element) as FrameworkElement;
        var boxParent = (CssContentsLayout.BoxParent(element) ?? element.VisualParent) as Visual;
        var parentOriginInContaining = Origin(boxParent, containingElement);
        Point staticParentOuter = new(parentOriginInContaining.X - containing.X, parentOriginInContaining.Y - containing.Y);
        var slot = CssAbsoluteLayout.ComputeSlot(layout, containing.Size, element.DesiredSize,
            element.CssPreferredWidth(containing.Width), element.CssPreferredHeight(containing.Height), element,
            containingElement?.FlowDirection == FlowDirection.RightToLeft, staticParentOuter);
        CssLayoutBox.ArrangeWithContainingBlock(element,
            new Rect(slot.X + containing.X, slot.Y + containing.Y, slot.Width, slot.Height), containing.Size);
    }

    internal static void MeasureAbsolute(FrameworkElement element, Size fallback)
    {
        var basis = AbsoluteContainingBox(element, fallback).Size;
        if (Entries.TryGetValue(element, out var entry))
        {
            if (entry.MeasuredBasis != basis) element.InvalidateMeasure();
            entry.MeasuredBasis = basis;
        }
        CssLayoutBox.MeasureWithContainingBlock(element,
            CssAbsoluteLayout.ComputeMeasureConstraint(element.CssLayout!, basis, element), basis);
    }

    internal static void ArrangeAbsolute(FrameworkElement element, Size parentSize)
    {
        if (Entries.TryGetValue(element, out var entry)) entry.ParentSize = parentSize;
        MeasureAbsolute(element, parentSize);
        var containing = AbsoluteContainingBox(element, parentSize);
        var layout = element.CssLayout!;
        var containingElement = AbsoluteContainingAncestor(element) ?? Root(element) as FrameworkElement;
        var boxParent = (CssContentsLayout.BoxParent(element) ?? element.VisualParent) as Visual;
        var parentOriginInContaining = Origin(boxParent, containingElement);
        Point staticParentOuter = new(parentOriginInContaining.X - containing.X, parentOriginInContaining.Y - containing.Y);
        var slot = CssAbsoluteLayout.ComputeSlot(layout, containing.Size, element.DesiredSize,
            element.CssPreferredWidth(containing.Width), element.CssPreferredHeight(containing.Height), element,
            containingElement?.FlowDirection == FlowDirection.RightToLeft, staticParentOuter);
        CssLayoutBox.ArrangeWithContainingBlock(element,
            new Rect(slot.X + containing.X, slot.Y + containing.Y, slot.Width, slot.Height), containing.Size);
    }

    private static Point Origin(Visual? element, Visual? ancestor)
    {
        var x = 0d; var y = 0d;
        for (; element is not null && element != ancestor; element = element.VisualParent)
        {
            if (element is UIElement ui)
            {
                var bounds = ui.VisualBounds;
                x += bounds.X + ui.RenderOffset.X; y += bounds.Y + ui.RenderOffset.Y;
            }
        }
        return new(x, y);
    }

    private static int Depth(Visual element)
    {
        var depth = 0;
        for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent) depth++;
        return depth;
    }

    private static Visual ResolveStackingAncestor(FrameworkElement element, Visual root)
    {
        for (var current = element.VisualParent; current is not null; current = current.VisualParent)
        {
            if (!CssContentsLayout.IsContents(current) &&
                (CssMaskProperties.Active(current) ||
                 current is FrameworkElement fe && CssCompositingProperties.CreatesStackingContext(fe)))
            {
                return current;
            }
        }
        return root;
    }

    private static Visual ResolveFixedPaintOwner(FrameworkElement element, Visual ancestor)
    {
        var paintOwner = ancestor;
        for (var current = element.VisualParent; current is not null; current = current.VisualParent)
        {
            if ((CssMaskProperties.Active(current) || current is FrameworkElement composition && CssCompositingProperties.CreatesGroup(composition)) && !CssContentsLayout.IsContents(current))
            { paintOwner = current; break; }
            if (current == ancestor) break;
        }
        return paintOwner;
    }

    private static Visual ResolveAbsolutePaintOwner(FrameworkElement element, Visual containing, Visual root)
    {
        var target = containing;
        if (CssCompositingProperties.HasExplicitZIndex(element))
        {
            var stacking = ResolveStackingAncestor(element, root);
            if (Depth(stacking) < Depth(target))
                target = stacking;
        }
        var paintOwner = target;
        for (var current = element.VisualParent; current is not null; current = current.VisualParent)
        {
            if ((CssMaskProperties.Active(current) || current is FrameworkElement composition && CssCompositingProperties.CreatesGroup(composition)) && !CssContentsLayout.IsContents(current))
            { paintOwner = current; break; }
            if (current == target) break;
        }
        return paintOwner;
    }

    private static void Refresh(UIElement arranged)
    {
        if (!Registries.TryGetValue(arranged.Dispatcher, out var registry)) return;
        var root = Root(arranged);
        if (arranged == root && arranged is FrameworkElement framework)
            registry.Viewports.GetOrCreateValue(root).Size = framework.RenderSize;
        s_refreshing = true;
        try
        {
            var elements = new List<FrameworkElement>();
            for (var i = registry.Elements.Count - 1; i >= 0; i--)
            {
                if (!registry.Elements[i].TryGetTarget(out var element) || !Entries.TryGetValue(element, out _))
                { registry.Elements.RemoveAt(i); continue; }
                if (element.IsArrangeValid && element.VisualParent is not null && Root(element) == root)
                    elements.Add(element);
            }
            // An ancestor's sticky/fixed/absolute shift contributes to its descendants' coordinates.
            foreach (var element in elements.OrderBy(Depth))
            {
                var entry = Entries.GetValue(element, static _ => new());
                if (element.CssLayout?.Position == CssPositionMode.Fixed)
                {
                    if (entry.MeasuredBasis != FixedContainingBox(element, entry.ParentSize).Size)
                        ArrangeFixed(element, entry.ParentSize);
                    var ancestor = (Visual?)FixedContainingAncestor(element) ?? root;
                    var paintOwner = ResolveFixedPaintOwner(element, ancestor);
                    SetPaintOwner(element, entry, paintOwner);
                    var origin = Origin(element.VisualParent, ancestor);
                    Move(element, entry, new(-origin.X, -origin.Y));
                }
                else if (element.CssLayout?.Position == CssPositionMode.Absolute)
                {
                    var containing = (Visual?)AbsoluteContainingAncestor(element) ?? root;
                    if (UsesAncestorContainingBlock(element))
                    {
                        ArrangeAbsolute(element, entry.ParentSize);
                        var origin = Origin(element.VisualParent, containing);
                        Move(element, entry, new(-origin.X, -origin.Y));
                    }
                    else
                    {
                        Move(element, entry, default);
                    }
                    var paintOwner = ResolveAbsolutePaintOwner(element, containing, root);
                    bool keepLocal = ReferenceEquals(paintOwner, element.VisualParent) &&
                        element.VisualParent is Panel &&
                        !CssDisplayLayout.HasBoxFormatter((FrameworkElement)element.VisualParent);
                    SetPaintOwner(element, entry, keepLocal ? null : paintOwner);
                }
                else if (element.CssLayout?.Position == CssPositionMode.Sticky)
                {
                    Move(element, entry, StickyOffset(element, entry));
                    if (CssCompositingProperties.HasExplicitZIndex(element))
                    {
                        var paintOwner = ResolveStackingAncestor(element, root);
                        bool keepLocal = ReferenceEquals(paintOwner, element.VisualParent) && element.VisualParent is Panel;
                        SetPaintOwner(element, entry, keepLocal ? null : paintOwner);
                    }
                    else
                    {
                        SetPaintOwner(element, entry, null);
                    }
                }
                else if (CssCompositingProperties.HasExplicitZIndex(element) && CssCompositingProperties.ZIndexApplies(element))
                {
                    Move(element, entry, default);
                    var paintOwner = ResolveStackingAncestor(element, root);
                    bool keepLocal = ReferenceEquals(paintOwner, element.VisualParent) &&
                        element.VisualParent is Panel &&
                        !CssDisplayLayout.HasBoxFormatter((FrameworkElement)element.VisualParent);
                    SetPaintOwner(element, entry, keepLocal ? null : paintOwner);
                }
                else
                {
                    Move(element, entry, default);
                    SetPaintOwner(element, entry, null);
                }
            }
        }
        finally { s_refreshing = false; }
    }

    private static (Visual Ancestor, Rect Bounds) Scrollport(FrameworkElement element)
    {
        for (var ancestor = element.VisualParent; ancestor is not null; ancestor = ancestor.VisualParent)
        {
            if (ancestor is ScrollViewer viewer) return (viewer, viewer.CssScrollport);
            if (ancestor is ScrollContentPresenter presenter)
                return (presenter, new Rect(presenter.RenderSize));
            if (ancestor is FrameworkElement framework && CssOverflowProperties.Get(framework) is { } overflow &&
                (overflow.X is CssOverflowMode.Hidden or CssOverflowMode.Auto or CssOverflowMode.Scroll ||
                 overflow.Y is CssOverflowMode.Hidden or CssOverflowMode.Auto or CssOverflowMode.Scroll))
            {
                var (border, _) = CssBoxMetrics.BackgroundInsets(framework, framework.RenderSize.Width);
                return (framework, new Rect(border.Left, border.Top,
                    Math.Max(0, framework.RenderSize.Width - border.Left - border.Right),
                    Math.Max(0, framework.RenderSize.Height - border.Top - border.Bottom)));
            }
        }
        return (Root(element), new Rect(ViewportSize(element, element.RenderSize)));
    }

    private static Point StickyOffset(FrameworkElement element, Entry entry)
    {
        var (scroll, viewport) = Scrollport(element);
        var parentOrigin = Origin(element.VisualParent, scroll);
        var bounds = element.VisualBounds;
        var x = parentOrigin.X + bounds.X - entry.Offset.X;
        var y = parentOrigin.Y + bounds.Y - entry.Offset.Y;
        var containing = element.VisualParent is FrameworkElement parent
            ? parent.RenderSize : viewport.Size;
        var containingOrigin = parentOrigin;
        if (element.VisualParent is StackPanel { ScrollOwner: not null } provider)
        {
            containing = new Size(Math.Max(containing.Width, provider.ExtentWidth), Math.Max(containing.Height, provider.ExtentHeight));
            containingOrigin = new(parentOrigin.X - provider.HorizontalOffset, parentOrigin.Y - provider.VerticalOffset);
        }
        var insets = element.VisualParent is FrameworkElement container
            ? CssBoxMetrics.ContentInsets(container, containing.Width) : default;
        var margin = CssBoxMetrics.Margin(element, Math.Max(0, containing.Width - insets.Left - insets.Right));
        var layout = element.CssLayout!;
        return new(
            StickyAxis(x, bounds.Width, viewport.Left, viewport.Width,
                containingOrigin.X + insets.Left, containingOrigin.X + containing.Width - insets.Right,
                margin.Left, margin.Right, CssRelativeLayout.CssInset(element, 0, layout.InsetLeft),
                CssRelativeLayout.CssInset(element, 2, layout.InsetRight), CssRelativeLayout.PreferRight(element)),
            StickyAxis(y, bounds.Height, viewport.Top, viewport.Height,
                containingOrigin.Y + insets.Top, containingOrigin.Y + containing.Height - insets.Bottom,
                margin.Top, margin.Bottom, CssRelativeLayout.CssInset(element, 1, layout.InsetTop),
                CssRelativeLayout.CssInset(element, 3, layout.InsetBottom), false));
    }

    internal static double StickyAxis(double position, double extent, double viewportStart, double viewportExtent,
        double containingStart, double containingEnd, double marginStart, double marginEnd,
        CssLayoutLength start, CssLayoutLength end, bool preferEnd)
    {
        var hasStart = start.IsSet && !start.IsAuto;
        var hasEnd = end.IsSet && !end.IsAuto;
        if (!hasStart && !hasEnd) return 0;
        var startInset = hasStart ? start.Resolve(viewportExtent, 0) : 0;
        var endInset = hasEnd ? end.Resolve(viewportExtent, 0) : 0;
        // Oversized boxes reduce the logical end inset, which can become negative.
        var deficit = extent - (viewportExtent - startInset - endInset);
        if (deficit > 0)
        {
            if (preferEnd) startInset -= deficit;
            else endInset -= deficit;
        }
        var desired = position;
        if (hasEnd) desired = Math.Min(desired, viewportStart + viewportExtent - endInset - extent);
        if (hasStart) desired = Math.Max(desired, viewportStart + startInset);
        var shift = desired - position;
        // Do not move a top-only box upward (or a bottom-only box downward) merely
        // because the containing block has already passed outside the scrollport.
        if (shift > 0)
            return Math.Max(0, Math.Min(shift, containingEnd - position - extent -
                Math.Min(marginEnd, Math.Max(0, containingEnd - position - extent - marginEnd))));
        if (shift < 0)
            return Math.Min(0, Math.Max(shift, containingStart - position +
                Math.Min(marginStart, Math.Max(0, position - marginStart - containingStart))));
        return 0;
    }

    private static void Move(FrameworkElement element, Entry entry, Point offset)
    {
        if (entry.Offset == offset) return;
        var bounds = element.VisualBounds;
        var window = element.GetWindowHostOrNull();
        if (window?.ShouldTrackLayoutDirtyBounds == true)
            window.AddDirtyRect(element.GetDirtyRenderBounds());
        element.SetVisualBounds(new Rect(bounds.X - entry.Offset.X + offset.X,
            bounds.Y - entry.Offset.Y + offset.Y, bounds.Width, bounds.Height));
        entry.Offset = offset;
        UIElement.InvalidateHitTestCache();
        element.InvalidateComposition();
        if (entry.PaintOwner?.TryGetTarget(out var owner) == true && owner is UIElement host)
            host.InvalidateComposition();
    }

    private static void SetPaintOwner(FrameworkElement element, Entry entry, Visual? owner)
    {
        var previous = entry.PaintOwner?.TryGetTarget(out var oldOwner) == true ? oldOwner : null;
        if (previous == owner) return;
        if (previous is not null && PaintGroups.TryGetValue(previous, out var oldGroup))
        {
            oldGroup.Elements.RemoveAll(reference => !reference.TryGetTarget(out var target) || target == element);
            if (previous is UIElement ui) ui.InvalidateVisual();
        }
        entry.PaintOwner = owner is null ? null : new(owner);
        if (owner is null) return;
        PaintGroups.GetOrCreateValue(owner).Elements.Add(new(element));
        if (owner is UIElement host) host.InvalidateVisual();
    }

    internal readonly record struct PaintChild(Visual Visual, Point ParentOrigin = default, bool Promoted = false);

    // Promoted fixed boxes paint in their containing block, outside intermediate
    // overflow clips. Their original visual/logical parents and event routes remain.
    internal static List<PaintChild>? PaintChildren(Visual owner)
    {
        var projection = owner is FrameworkElement contentsHost ? CssContentsLayout.Project(contentsHost, includeInactive: false, painting: true) : null;
        var formatting = projection is not null || owner is FrameworkElement contentHost && owner is not Panel && CssDisplayLayout.HasBoxFormatter(contentHost);
        List<PaintChild>? result = formatting ? [] : null;
        if (IsActive && PaintGroups.TryGetValue(owner, out var group))
        {
            for (var i = group.Elements.Count - 1; i >= 0; i--)
            {
                if (!group.Elements[i].TryGetTarget(out var element) ||
                    !Entries.TryGetValue(element, out var entry) ||
                    entry.PaintOwner?.TryGetTarget(out var target) != true || target != owner ||
                    !ShouldRegister(element))
                { group.Elements.RemoveAt(i); continue; }
                if (Root(element) != Root(owner) || !PathIsVisible(element, owner, false)) continue;
                (result ??= []).Add(new(element, Origin(element.VisualParent, owner), true));
            }
        }
        if (result is null) return null;
        if (projection is not null)
        {
            foreach (var child in projection) if (!DefersPaint(child)) result.Add(new(child));
        }
        else for (var i = 0; i < owner.InternalVisualChildrenCount; i++)
            if (owner.InternalGetVisualChild(i) is { } child && !DefersPaint(child)) result.Add(new(child));
        var sourceOrder = projection?.Select((child, index) => (child, index)).ToDictionary(entry => (Visual)entry.child, entry => entry.index);
        result.Sort((left, right) =>
        {
            var lz = ZIndex(left.Visual); var rz = ZIndex(right.Visual);
            if (lz != rz) return lz.CompareTo(rz);
            var lr = left.Promoted ? (lz < 0 ? -1 : 3) : owner is FrameworkElement lp && left.Visual is UIElement lu ? CssFloatProperties.PaintRank(lp, lu) : 0;
            var rr = right.Promoted ? (rz < 0 ? -1 : 3) : owner is FrameworkElement rp && right.Visual is UIElement ru ? CssFloatProperties.PaintRank(rp, ru) : 0;
            if (lr != rr) return lr.CompareTo(rr);
            if (sourceOrder is not null && sourceOrder.TryGetValue(left.Visual, out var li) && sourceOrder.TryGetValue(right.Visual, out var ri))
            {
                if (owner is FlexPanel || owner is FrameworkElement { CssDisplayMode: CssDisplayMode.Flex or CssDisplayMode.Grid })
                {
                    var order = FlexPanel.GetOrder((UIElement)left.Visual).CompareTo(FlexPanel.GetOrder((UIElement)right.Visual));
                    if (order != 0) return order;
                }
                return li.CompareTo(ri);
            }
            return CompareTreeOrder(left.Visual, right.Visual, owner);
        });
        return result;
    }

    private static int ZIndex(Visual visual) => visual is OverlayLayer or Jalium.UI.Documents.AdornerLayer
        ? int.MaxValue : visual is UIElement element ? CssCompositingProperties.EffectiveZIndex(element) : 0;

    private static int CompareTreeOrder(Visual left, Visual right, Visual owner)
    {
        static List<(int Order, int Index)> Path(Visual current, Visual ancestor)
        {
            var result = new List<(int Order, int Index)>();
            while (current != ancestor && current.VisualParent is { } parent)
            {
                var index = 0;
                if (parent is Panel panel && current is UIElement ui) index = panel.CssLayoutChildren.IndexOf(ui);
                else for (; index < parent.InternalVisualChildrenCount; index++)
                    if (parent.InternalGetVisualChild(index) == current) break;
                var order = current is UIElement element && parent is FrameworkElement container &&
                    (container is FlexPanel || container.CssDisplayMode is CssDisplayMode.Flex or CssDisplayMode.Grid)
                    ? FlexPanel.GetOrder(element) : 0;
                result.Add((order, index)); current = parent;
            }
            result.Reverse(); return result;
        }
        var l = Path(left, owner); var r = Path(right, owner);
        for (var i = 0; i < Math.Min(l.Count, r.Count); i++)
            if (l[i] != r[i]) return l[i].CompareTo(r[i]);
        return l.Count.CompareTo(r.Count);
    }

    internal static bool DefersPaint(Visual child) => IsActive && child != s_promotedChild &&
        child is FrameworkElement element && ShouldRegister(element) &&
        Entries.TryGetValue(element, out var entry) && entry.PaintOwner?.TryGetTarget(out _) == true;

    internal static bool PathIsVisible(Visual child, Visual owner, bool hitTest, Point? ownerPoint = null)
    {
        for (var current = child; current != owner; current = current.VisualParent!)
        {
            if (current is null) return false;
            if (current is UIElement ui && (ui.Visibility != Visibility.Visible ||
                CssDisplayProperties.IsCollapsedFlexItem(ui) || CssDisplayProperties.IsExitInert(ui) ||
                hitTest && !ui.IsHitTestVisible)) return false;
            if (hitTest && ownerPoint is { } point && current != child && current is UIElement clipped)
            {
                var origin = Origin(current, owner);
                var local = new Point(point.X - origin.X, point.Y - origin.Y);
                if (clipped.Clip is { } clip && !clip.FillContains(local) ||
                    CssClipPathProperties.GetGeometry(clipped) is { } cssClip && !cssClip.FillContains(local)) return false;
                if (!BypassesOverflowClip(child, clipped) &&
                    (!clipped.IsPointInsideLayoutClip(local) || !clipped.IsPointInsideChildLayoutClip(local))) return false;
            }
        }
        return true;
    }

    internal static void RenderPromoted(Visual owner, DrawingContext context, PaintChild child)
    {
        var savedChild = s_promotedChild;
        if (context is not IOffsetDrawingContext offsets)
        {
            try { s_promotedChild = child.Visual; owner.RenderChildVisualInline(context, child.Visual); }
            finally { s_promotedChild = savedChild; }
            return;
        }
        var savedOffset = offsets.Offset;
        var opacity = 1d;
        for (var current = child.Visual.VisualParent; current is not null && current != owner; current = current.VisualParent)
            if (current is UIElement ui) opacity *= ui.Opacity;
        if (opacity <= 0) return;
        var opacityContext = opacity < 1 ? context as IOpacityDrawingContext : null;
        var clips = 0;
        try
        {
            s_promotedChild = child.Visual;
            // Explicit native/CSS path clips always clip descendants. Overflow/scrollport
            // layout clips are bypassed only when the containing block is an ancestor of
            // the overflow container.
            for (var current = child.Visual.VisualParent; current is not null && current != owner; current = current.VisualParent)
                if (current is UIElement ui)
                {
                    var origin = Origin(current, owner);
                    offsets.Offset = new(savedOffset.X + origin.X, savedOffset.Y + origin.Y);
                    if (BypassesOverflowClip(child.Visual, ui))
                    {
                        if (ui.Clip is { } clip) { context.PushClip(clip); clips++; }
                    }
                    else
                    {
                        if (ui.GetLayoutClip() is { } layoutClip) { context.PushClip(layoutClip); clips++; }
                        if (ui.GetChildLayoutClip() is { } childClip) { context.PushClip(childClip); clips++; }
                    }
                    if (CssClipPathProperties.GetGeometry(ui) is { } path) { context.PushClip(path); clips++; }
                }
            offsets.Offset = new(savedOffset.X + child.ParentOrigin.X, savedOffset.Y + child.ParentOrigin.Y);
            opacityContext?.PushOpacity(opacity);
            owner.RenderChildVisualInline(context, child.Visual);
        }
        finally
        {
            opacityContext?.PopOpacity();
            while (clips-- > 0) context.Pop();
            offsets.Offset = savedOffset;
            s_promotedChild = savedChild;
        }
    }
}
