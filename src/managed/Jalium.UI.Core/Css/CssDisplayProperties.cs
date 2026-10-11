using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal readonly record struct CssDisplaySpecification(CssDisplayMode Mode, bool Inline = false, bool None = false);
internal enum CssVisibilityMode : byte { Visible, Hidden, Collapse, Inherit }

internal static class CssDisplayProperties
{
    private static int s_hasVisibilityDeclarations;

    internal static readonly CssDisplaySpecification Initial = new(CssDisplayMode.Native, Inline: true);

    internal static bool HasVisibilityDeclarations =>
        System.Threading.Volatile.Read(ref s_hasVisibilityDeclarations) != 0;

    internal static readonly DependencyProperty SpecificationProperty = DependencyProperty.RegisterAttached(
        "CssDisplay", typeof(CssDisplaySpecification), typeof(CssDisplayProperties),
        new PropertyMetadata(default(CssDisplaySpecification), static (target, _) =>
        {
            if (target is UIElement element) SyncAnimatedPresentation(element);
        }));
    internal static readonly DependencyProperty VisibilityProperty = DependencyProperty.RegisterAttached(
        "CssVisibility", typeof(CssVisibilityMode), typeof(CssDisplayProperties),
        new PropertyMetadata(CssVisibilityMode.Visible));

    internal static CssDisplaySpecification Computed(CssNode node)
        => Compute(node, SpecifiedOrInitial(node));

    internal static CssDisplaySpecification Compute(CssNode node, CssDisplaySpecification specified)
    {
        if (specified.None) return specified;
        var parent = CssMatcher.CssAncestor(node);
        var positioned = node.CssLayout?.ComputedPosition is CssPositionKeyword.Absolute or CssPositionKeyword.Fixed;
        var floated = node.GetValue(CssFloatProperties.FloatProperty) is CssFloatSide side &&
            side != CssFloatSide.None;
        var flexOrGridItem = parent is not null &&
            parent.Target.GetEffectiveValueLayer(SpecificationProperty) is not null &&
            parent.GetValue(SpecificationProperty) is CssDisplaySpecification
                { Mode: CssDisplayMode.Flex or CssDisplayMode.Grid, None: false };
        if (parent is not null && !positioned && !floated && !flexOrGridItem) return specified;
        return specified.Mode == CssDisplayMode.Native
            ? new(CssDisplayMode.Block)
            : specified with { Inline = false };
    }

    private static CssDisplaySpecification SpecifiedOrInitial(CssNode node)
        => node.Target.GetEffectiveValueLayer(SpecificationProperty) is null
            ? Initial : (CssDisplaySpecification)node.GetValue(SpecificationProperty)!;

    internal static void RefreshVisibility(UIElement element)
    {
        System.Threading.Volatile.Write(ref s_hasVisibilityDeclarations, 1);
        UIElement.InvalidateHitTestCache();
        element.InvalidateVisual();
        element.UpdateIsVisibleFromTree(forceDescendants: true);
        var affectsFlexLayout = element.VisualParent is Panel parent && IsFlexContainer(parent);
        affectsFlexLayout |= InvalidateFlexLayoutsInSubtree(element);
        if (affectsFlexLayout)
            for (Visual? current = element; current is not null; current = current.VisualParent)
                if (current is UIElement uiElement) uiElement.InvalidateMeasure();
    }

    internal static bool IsCollapsedFlexItem(UIElement element) =>
        HasVisibilityDeclarations && element.VisualParent is Panel parent &&
        IsFlexContainer(parent) &&
        element is not FrameworkElement { CssLayout.Position: CssPositionMode.Absolute } &&
        EffectiveVisibility(element) == Visibility.Collapsed;

    private static bool IsFlexContainer(Panel panel) =>
        panel.CssDisplayMode == CssDisplayMode.Flex ||
        panel is FlexPanel && panel.CssDisplayMode == CssDisplayMode.Native;

    private static bool InvalidateFlexLayoutsInSubtree(UIElement element)
    {
        var found = element is Panel panel && IsFlexContainer(panel);
        if (found) element.InvalidateMeasure();
        for (var i = 0; i < element.InternalVisualChildrenCount; i++)
            if (element.InternalGetVisualChild(i) is UIElement child)
                found |= InvalidateFlexLayoutsInSubtree(child);
        return found;
    }

    internal static Visibility EffectiveVisibility(DependencyObject target)
    {
        if (!HasVisibilityDeclarations) return Visibility.Visible;
        for (var node = CssNode.Get(target); node is not null; node = CssMatcher.CssAncestor(node))
        {
            if (node.Target is UIElement native)
            {
                var layer = native.GetEffectiveValueLayer(UIElement.VisibilityProperty);
                // A display animation mirrors layout participation into native
                // Visibility. It must not replace CSS visibility inheritance.
                if (!IsDisplayVisibilityMirror(native) &&
                    (native.HasLocalOrAnimatedValue(UIElement.VisibilityProperty) ||
                     layer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)))
                    return native.Visibility;
            }
            if (node.Target.GetEffectiveValueLayer(VisibilityProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
            {
                var mode = (CssVisibilityMode)node.GetValue(VisibilityProperty)!;
                if (mode != CssVisibilityMode.Inherit)
                    return mode switch
                    {
                        CssVisibilityMode.Hidden => Visibility.Hidden,
                        CssVisibilityMode.Collapse => Visibility.Collapsed,
                        _ => Visibility.Visible,
                    };
            }
        }
        return Visibility.Visible;
    }

    internal static CssVisibilityMode ComputedVisibility(CssNode node)
    {
        for (var current = node; current is not null; current = CssMatcher.CssAncestor(current))
        {
            if (current.Target is UIElement native)
            {
                var layer = native.GetEffectiveValueLayer(UIElement.VisibilityProperty);
                if (!IsDisplayVisibilityMirror(native) &&
                    (native.HasLocalOrAnimatedValue(UIElement.VisibilityProperty) ||
                     layer is not null and not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)))
                    return native.Visibility switch
                    {
                        Visibility.Hidden => CssVisibilityMode.Hidden,
                        Visibility.Collapsed => CssVisibilityMode.Collapse,
                        _ => CssVisibilityMode.Visible,
                    };
            }
            if (current.Target.GetEffectiveValueLayer(VisibilityProperty) is not null &&
                current.GetValue(VisibilityProperty) is CssVisibilityMode mode &&
                mode != CssVisibilityMode.Inherit)
                return mode;
        }
        return CssVisibilityMode.Visible;
    }

    private static bool IsDisplayVisibilityMirror(UIElement element) =>
        element is FrameworkElement { CssRuntimeState.AnimatedDisplayVisibilityMirror: true } &&
        !element.HasLocalValue(UIElement.VisibilityProperty) &&
        !element.HasExplicitAnimation(UIElement.VisibilityProperty);

    internal static bool HasVisibilityStyleOnPath(DependencyObject target)
    {
        if (!HasVisibilityDeclarations) return false;
        for (var node = CssNode.Get(target); node is not null; node = CssMatcher.CssAncestor(node))
        {
            if (node.Target.GetEffectiveValueLayer(VisibilityProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
                return true;
        }
        return false;
    }

    internal static void SyncAnimatedPresentation(UIElement element)
    {
        if (element is not FrameworkElement { CssRuntimeState: { } state }) return;
        if (!element.HasAnimatedValue(SpecificationProperty) &&
            !element.HasCssAnimatedValue(SpecificationProperty))
        {
            ReleaseAnimatedPresentation(element);
            return;
        }

        var display = (CssDisplaySpecification)element.GetValue(SpecificationProperty)!;
        SetMirror(element, CssDisplayLayout.ModeProperty, display.Mode,
            ref state.AnimatedDisplayModeMirror);
        SetMirror(element, CssDisplayLayout.InlineProperty, display.Inline,
            ref state.AnimatedDisplayInlineMirror);
        SetMirror(element, UIElement.VisibilityProperty,
            display.None ? Visibility.Collapsed : Visibility.Visible,
            ref state.AnimatedDisplayVisibilityMirror);
        UIElement.InvalidateHitTestCache();
        if (IsExitInert(element))
            Jalium.UI.Input.KeyboardFocusRevalidation.OnFocusabilityChanged(element);
    }

    internal static void ReleaseAnimatedPresentation(UIElement element)
    {
        if (element is not FrameworkElement { CssRuntimeState: { } state }) return;
        ReleaseMirror(element, CssDisplayLayout.ModeProperty, ref state.AnimatedDisplayModeMirror);
        ReleaseMirror(element, CssDisplayLayout.InlineProperty, ref state.AnimatedDisplayInlineMirror);
        ReleaseMirror(element, UIElement.VisibilityProperty, ref state.AnimatedDisplayVisibilityMirror);
        state.HadRenderedStyle = element.Visibility != Visibility.Collapsed;
        UIElement.InvalidateHitTestCache();
    }

    internal static bool IsExitInert(UIElement element)
    {
        if (!CssEngine.IsActive) return false;
        for (Visual? node = element; node is not null; node = node.VisualParent)
        {
            if (node is not UIElement current ||
                current.HasLocalValue(UIElement.VisibilityProperty) ||
                current.HasExplicitAnimation(UIElement.VisibilityProperty)) continue;
            if (current is FrameworkElement { CssRuntimeState.Applied: { } applied } &&
                applied.TryGetValue(SpecificationProperty, out var value) &&
                value.Value is CssDisplaySpecification { None: true } &&
                current.HasAutomaticTransition(SpecificationProperty))
                return true;
        }
        return false;
    }

    internal static void OnAppliedStyleChanged(UIElement element)
    {
        UIElement.InvalidateHitTestCache();
        if (IsExitInert(element))
        {
            Jalium.UI.Input.KeyboardFocusRevalidation.OnFocusabilityChanged(element);
            if (UIElement.MouseCapturedElement is { } mouse && IsInSubtree(mouse, element))
                mouse.ReleaseMouseCapture();
            if (UIElement.StylusCapturedElement is { } stylus && IsInSubtree(stylus, element))
                stylus.ReleaseStylusCapture();
            foreach (var touch in element.TouchesCapturedWithin.ToArray())
                if (touch.Captured is UIElement captured)
                    captured.ReleaseTouchCapture(touch);
        }
    }

    private static bool IsInSubtree(UIElement child, UIElement root)
    {
        for (Visual? node = child; node is not null; node = node.VisualParent)
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private static void SetMirror(UIElement element, DependencyProperty property, object value, ref bool owned)
    {
        if (element.HasLocalValue(property) || element.HasExplicitAnimation(property))
        {
            if (owned && !element.HasExplicitAnimation(property)) element.ClearAnimatedValue(property);
            owned = false;
            return;
        }
        owned = true;
        element.SetAnimatedValue(property, value, holdEndValue: false);
    }

    private static void ReleaseMirror(UIElement element, DependencyProperty property, ref bool owned)
    {
        if (!owned) return;
        owned = false;
        if (!element.HasExplicitAnimation(property)) element.ClearAnimatedValue(property);
    }

    internal static void Register() => CssPropertyRegistry.Register(new()
    {
        Name = "display", Kind = CssPropertyKind.Longhand, StorageProperty = SpecificationProperty,
        TransitionTargetDpName = SpecificationProperty.Name,
        Parse = (ref CssTokenReader reader, CssCompileContext _) =>
        {
            if (!reader.TryReadIdent(out var first)) return null;
            var token = first.ToString().ToLowerInvariant();
            if (reader.AtEnd)
            {
                CssDisplaySpecification? display = token switch
                {
                    "none" => new(CssDisplayMode.Native, None: true),
                    "block" => new(CssDisplayMode.Block), "flow-root" => new(CssDisplayMode.FlowRoot),
                    "inline" => new(CssDisplayMode.Native, true), "inline-block" => new(CssDisplayMode.FlowRoot, true),
                    "flex" => new(CssDisplayMode.Flex), "inline-flex" => new(CssDisplayMode.Flex, true),
                    "grid" => new(CssDisplayMode.Grid), "inline-grid" => new(CssDisplayMode.Grid, true),
                    _ => null,
                };
                return display is { } value ? new CssDisplayValue(value) : null;
            }
            if (!reader.TryReadIdent(out var second) || !reader.AtEnd) return null;
            var inside = second.ToString().ToLowerInvariant();
            if (inside is "block" or "inline") (token, inside) = (inside, token);
            if (token is not ("block" or "inline")) return null;
            var inline = token == "inline";
            CssDisplayMode? mode = inside switch
            {
                "flow" => inline ? CssDisplayMode.Native : CssDisplayMode.Block,
                "flow-root" => CssDisplayMode.FlowRoot, "flex" => CssDisplayMode.Flex, "grid" => CssDisplayMode.Grid, _ => null,
            };
            return mode is { } parsed ? new CssDisplayValue(new(parsed, inline)) : null;
        },
    });
}

internal sealed class CssDisplayValue(CssDisplaySpecification display) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.SpecificationProperty, display);
        sink.Set(CssDisplayLayout.ModeProperty, display.Mode);
        sink.Set(CssDisplayLayout.InlineProperty, display.Inline);
        var visibility = context.Slots.Visibility ??= new();
        visibility.DisplayNone = display.None;
        visibility.FromState |= context.Slots.CurrentContributionIsState;
        return true;
    }
}

internal sealed class CssVisibilityValue(Visibility value) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.VisibilityProperty, value switch
        {
            Visibility.Hidden => CssVisibilityMode.Hidden,
            Visibility.Collapsed => CssVisibilityMode.Collapse,
            _ => CssVisibilityMode.Visible,
        });
        var visibility = context.Slots.Visibility ??= new();
        visibility.FromState |= context.Slots.CurrentContributionIsState;
        return true;
    }
}

internal sealed class CssInheritedVisibilityValue : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.VisibilityProperty, CssVisibilityMode.Inherit);
        var visibility = context.Slots.Visibility ??= new();
        visibility.FromState |= context.Slots.CurrentContributionIsState;
        return true;
    }
}

internal sealed class CssVisibilityParts
{
    public bool? DisplayNone;
    public bool FromState;
    internal void Flush(ICssSetterSink sink)
    {
        sink.CurrentValueIsState = FromState;
        // CSS hidden/collapse keeps layout and allows a visible descendant.
        // Native Visibility gates a subtree, so only display:none maps to Collapsed.
        sink.Set(UIElement.VisibilityProperty, DisplayNone == true ? Visibility.Collapsed : Visibility.Visible);
    }
}
