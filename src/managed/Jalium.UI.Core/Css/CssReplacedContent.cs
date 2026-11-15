using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Input;

namespace Jalium.UI.Styling;

/// <summary>Image replacement suppresses generated boxes, never the authored objects or their property values.</summary>
internal static class CssReplacedContent
{
    private sealed class Slot(FrameworkElement owner)
    {
        internal CssImageValue? Definition;
        internal CssGeneratedImage? Image;
        internal string? Alternative;
        internal Border? BorderPainter;
        internal CssGeneratedImage GetImage() => Image ??= new(owner, () => { owner.InvalidateMeasure(); owner.InvalidateVisual(); });
    }
    private sealed class ContentSlot
    {
        internal CssGeneratedContentValue Value = CssGeneratedContentValue.Normal;
        internal CssImageValue? Definition;
        internal string? Alternative;
    }
    private static readonly ConditionalWeakTable<FrameworkElement, Slot> Slots = new();
    private static readonly ConditionalWeakTable<FrameworkContentElement, ContentSlot> ContentSlots = new();
    private static bool _active;
    internal static bool IsActive(DependencyObject? element) => _active && (
        element is FrameworkElement owner && Slots.TryGetValue(owner, out var slot) && slot.Definition is not null ||
        element is FrameworkContentElement content && ContentSlots.TryGetValue(content, out var cslot) && cslot.Definition is not null);
    internal static bool HasReplacement(DependencyObject? element) => IsActive(element) || element is CssGeneratedText { IsImageReplacement: true };
    internal static CssGeneratedImage? Image(FrameworkElement owner) => IsActive(owner) ? Slots.GetValue(owner, static e => new(e)).Image : null;
    internal static bool TryGetContentValue(DependencyObject? owner, out CssGeneratedContentValue value)
    {
        if (_active && owner is FrameworkContentElement content && ContentSlots.TryGetValue(content, out var slot) && slot.Definition is not null)
        {
            value = slot.Value;
            return true;
        }
        value = CssGeneratedContentValue.Normal;
        return false;
    }

    internal static void Set(FrameworkContentElement content, CssGeneratedContentValue value)
    {
        var definition = value.Sequence?.Parts is [{ Kind: CssContentKind.Image, Image: { } image }] ? image : null;
        if (definition is null && !ContentSlots.TryGetValue(content, out _)) return;
        var slot = ContentSlots.GetValue(content, static _ => new());
        var before = slot.Definition is not null;
        slot.Value = value;
        slot.Definition = definition;
        slot.Alternative = value.Alternative;
        if (definition is not null) _active = true;
        content.GetExistingAutomationPeer()?.InvalidatePeer();
        CssCounters.Invalidate(CssNode.Get(content));
        if (before != (definition is not null))
        {
            if (content is Documents.TextElement textElement) textElement.NotifyTextContentChanged();
            else if (content is Documents.FlowDocument flowDocument) flowDocument.NotifyPaginationStyleChanged();
        }
    }

    internal static void Set(FrameworkElement owner, CssGeneratedContentValue value)
    {
        var definition = value.Sequence?.Parts is [{ Kind: CssContentKind.Image, Image: { } image }] ? image : null;
        if (definition is null && !Slots.TryGetValue(owner, out _)) return;
        var slot = Slots.GetValue(owner, static e => new(e));
        var before = slot.Definition is not null;
        slot.Definition = definition;
        slot.Alternative = value.Alternative;
        if (definition is not null)
        {
            _active = true;
            var child = slot.GetImage();
            owner.HasCssGeneratedChildren = true;
            owner.AttachCssGeneratedChild(child);
            child.Bind(definition);
        }
        else if (slot.Image is { } previous)
        { previous.Retire(); owner.DetachCssGeneratedChild(previous); }
        if (before != (definition is not null))
        {
            CssDisplayProperties.RefreshVisibility(owner);
            // The formatting view hides these children. Refresh the native tree
            // as well so a previously focused source cannot keep its visible cache.
            var pending = new Stack<Visual>();
            foreach (var native in owner.SnapshotAttachedVisualChildren())
                if (!ReferenceEquals(native, slot.Image)) pending.Push(native);
            var visited = new HashSet<Visual>();
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current)) continue;
                if (current is UIElement ui) ui.UpdateIsVisibleFromTree(forceDescendants: true);
                foreach (var child in current.SnapshotAttachedVisualChildren()) pending.Push(child);
            }
            KeyboardFocusRevalidation.OnFocusabilityChanged(owner);
        }
        owner.InvalidateMeasure(); owner.InvalidateVisual();
        owner.GetAutomationPeer()?.InvalidatePeer();
        CssCounters.Invalidate(owner);
    }

    internal static void Refresh(DependencyObject owner)
    {
        if (owner is FrameworkElement element && IsActive(element) && Slots.TryGetValue(element, out var slot))
            slot.GetImage().Bind(slot.Definition!);
    }

    internal static bool IsSuppressed(DependencyObject target)
    {
        if (!_active) return false;
        for (var child = target as Visual; child?.VisualParent is FrameworkElement parent; child = parent)
            if (IsActive(parent) && !ReferenceEquals(child, Image(parent)) && child is not CssGeneratedText { Kind: CssPseudoElement.Marker }) return true;
        for (var node = CssNode.Existing(target)?.FrameworkParent; node is not null; node = node.FrameworkParent)
            if (node.Target is FrameworkContentElement content && IsActive(content) &&
                (target is not CssGeneratedText markerText || !markerText.Kind.IsMarker())) return true;
        return false;
    }

    internal static bool TryAlternative(DependencyObject? owner, out string alternative)
    {
        alternative = "";
        if (!IsActive(owner)) return false;
        if (owner is FrameworkElement element)
        {
            alternative = Slots.GetValue(element, static e => new(e)).Alternative ?? "";
            return true;
        }
        if (owner is FrameworkContentElement content && ContentSlots.TryGetValue(content, out var cslot))
        {
            alternative = cslot.Alternative ?? "";
            return true;
        }
        return false;
    }
    internal static void SetAlternative(DependencyObject owner, string? value)
    {
        if (!IsActive(owner)) return;
        if (owner is FrameworkElement element)
        {
            var slot = Slots.GetValue(element, static e => new(e));
            if (slot.Alternative == value) return;
            slot.Alternative = value;
            element.GetAutomationPeer()?.InvalidatePeer();
        }
        else if (owner is FrameworkContentElement content && ContentSlots.TryGetValue(content, out var cslot))
        {
            if (cslot.Alternative == value) return;
            cslot.Alternative = value;
            content.GetExistingAutomationPeer()?.InvalidatePeer();
        }
    }

    internal static Size Measure(FrameworkElement owner, Size available)
        => CssReplacedImageSizing.Measure(owner, Image(owner)!, available);
    internal static Size Arrange(FrameworkElement owner, Size size)
    { CssReplacedImageSizing.Arrange(owner, Image(owner)!, size); return size; }

    internal static bool Background(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement owner || !(IsActive(owner) || CssContainmentProperties.IsContentSkipped(owner))) return false;
        var property = CssDependencyPropertyLookup.Find(owner.GetType(), "Background");
        var background = property is null ? null : owner.GetValue(property) as Brush;
        if (background is null) return true;
        var radii = Radii(owner);
        var geometry = new CssRoundedRectangleGeometry(new(owner.RenderSize), radii);
        void Shape(Brush brush) => context.DrawGeometry(brush, null, geometry);
        var (border, padding) = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width);
        if (!CssBackgroundPainter.TryDraw(owner, property!, background, context, new(owner.RenderSize), radii, border, padding, Shape)) Shape(background);
        return true;
    }
    internal static bool Border(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement owner || !(IsActive(owner) || CssContainmentProperties.IsContentSkipped(owner))) return false;
        var slot = Slots.GetValue(owner, static e => new(e));
        if (CssBorderPaintProperties.Get(owner) is not null)
            CssBorderAdornment.Draw(owner, context, ref slot.BorderPainter);
        else if (CssDependencyPropertyLookup.Find(owner.GetType(), "BorderBrush") is { } property && owner.GetValue(property) is Brush brush)
        {
            var border = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Border;
            var outer = new Rect(owner.RenderSize); var radii = Radii(owner);
            var path = new PathGeometry { FillRule = FillRule.EvenOdd };
            path.Figures.Add(CssRoundedRectangleGeometry.Figure(outer, radii));
            var inner = ControlRenderGeometry.GetContentRect(outer, border);
            if (inner.Width > 0 && inner.Height > 0) path.Figures.Add(CssRoundedRectangleGeometry.Figure(inner, radii.Inset(border)));
            context.DrawGeometry(brush, null, path);
        }
        return true;
    }
    internal static CssUsedBorderRadii Radii(FrameworkElement owner, Size? size = null)
    {
        if (CssTableLayout.CollapsedDecoration(owner)) return default;
        var usedSize = size ?? owner.RenderSize;
        return CssBorderRadiusProperties.Get(owner)?.Resolve(usedSize) ??
            (CssDependencyPropertyLookup.Find(owner.GetType(), "CornerRadius") is { } property && owner.GetValue(property) is CornerRadius corners
                ? CssBackgroundPainter.CircularRadii(corners).Normalize(usedSize) : default);
    }
}
