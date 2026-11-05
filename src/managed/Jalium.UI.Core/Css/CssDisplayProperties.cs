using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal readonly record struct CssDisplaySpecification(CssDisplayMode Mode, bool Inline = false, bool None = false, bool ListItem = false,
    bool LegacyBox = false);
internal enum CssVisibilityMode : byte { Visible, Hidden, Collapse, Inherit }

internal static class CssDisplayProperties
{
    private static int s_hasVisibilityDeclarations;

    internal static readonly CssDisplaySpecification Initial = new(CssDisplayMode.Native, Inline: true);

    internal static bool HasVisibilityDeclarations =>
        System.Threading.Volatile.Read(ref s_hasVisibilityDeclarations) != 0;

    internal static readonly DependencyProperty SpecificationProperty = DependencyProperty.RegisterAttached(
        "CssDisplay", typeof(CssDisplaySpecification), typeof(CssDisplayProperties),
        new PropertyMetadata(default(CssDisplaySpecification), static (target, change) =>
        {
            CssRubyWritingMode.Invalidate();
            CssFirstLine.StructureChanged(target);
            if (target is UIElement element)
            {
                SyncAnimatedPresentation(element);
                element.InvalidateMeasure();
                for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
                {
                    parent.InvalidateMeasure();
                    if (!CssDisplayLayout.IsFlow(parent) && !parent.CanFragmentCssInlineContent) break;
                }
            }
            if (target is FrameworkElement framework) CssLineClampProperties.RefreshMode(framework);
            if (target is Jalium.UI.Documents.TextElement text) text.NotifyTextContentChanged();
            else if (target is Jalium.UI.Documents.FlowDocument document) document.NotifyPaginationStyleChanged();
            // A child's computed display depends on the nearest generating
            // parent (flex/grid blockification and contents ancestor skipping).
            if(change.OldValue is CssDisplaySpecification oldDisplay && change.NewValue is CssDisplaySpecification newDisplay && oldDisplay.Mode!=newDisplay.Mode)
                foreach(var child in CssNode.Get(target).EnumerateChildren())CssEvaluationScheduler.InvalidateSubtree(child);
            if (change.OldValue is CssDisplaySpecification previous && change.NewValue is CssDisplaySpecification current && previous.ListItem != current.ListItem)
            {
                var node = CssNode.Get(target);
                CssCounters.Invalidate(node);
                CssEvaluationScheduler.InvalidateElement(node);
            }
        }));

    internal static bool SuppressesBoxTree(DependencyObject source)
    {
        for (var node = CssNode.Get(source); node is not null; node = node.FrameworkParent)
            if (node.GetValue(SpecificationProperty) is CssDisplaySpecification { None: true } ||
                CssContentsLayout.Suppressed(node.Target)) return true;
        return false;
    }
    internal static readonly DependencyProperty VisibilityProperty = DependencyProperty.RegisterAttached(
        "CssVisibility", typeof(CssVisibilityMode), typeof(CssDisplayProperties),
        new PropertyMetadata(CssVisibilityMode.Visible, static (target, _) =>
        {
            if (target is Jalium.UI.Documents.TextElement text) text.NotifyTextContentChanged();
            else if (target is Jalium.UI.Documents.FlowDocument document) document.NotifyPaginationStyleChanged();
        }));

    internal static CssDisplaySpecification Computed(CssNode node)
        => Compute(node, SpecifiedOrInitial(node));

    internal static CssDisplaySpecification Compute(CssNode node, CssDisplaySpecification specified)
    {
        if (specified.None) return specified;
        var parent = CssMatcher.CssAncestor(node);
        if (specified.Mode == CssDisplayMode.Contents)
        {
            if (node.Target is FrameworkElement element && CssContentsLayout.IsUnusual(element))
                return new(CssDisplayMode.Native, None: true);
            return parent is null ? new(CssDisplayMode.Block) : specified;
        }
        while (parent is not null && SpecifiedOrInitial(parent).Mode == CssDisplayMode.Contents &&
            !SpecifiedOrInitial(parent).None && CssMatcher.CssAncestor(parent) is not null &&
            (parent.Target is not FrameworkElement host || !CssContentsLayout.IsUnusual(host)))
            parent = CssMatcher.CssAncestor(parent);
        var positioned = node.CssLayout?.ComputedPosition is CssPositionKeyword.Absolute or CssPositionKeyword.Fixed;
        var floated = node.GetValue(CssFloatProperties.FloatProperty) is CssFloatSide side &&
            side != CssFloatSide.None;
        var flexOrGridItem = parent is not null &&
            parent.Target.GetEffectiveValueLayer(SpecificationProperty) is not null &&
            parent.GetValue(SpecificationProperty) is CssDisplaySpecification
                { Mode: CssDisplayMode.Flex or CssDisplayMode.Grid, None: false } parentDisplay &&
            !(parentDisplay.LegacyBox && parent.Target is FrameworkElement legacyParent &&
                CssLineClampProperties.UsedMode(legacyParent, parentDisplay.Mode) == CssDisplayMode.FlowRoot);
        if (parent is not null && !positioned && !floated && !flexOrGridItem)
            return CssRubyRoles.IsBox(SpecifiedOrInitial(parent).Mode) ? specified with { Inline = true } : specified;
        return specified.Mode == CssDisplayMode.Native || CssTableLayout.IsPart(specified.Mode) || CssRubyRoles.IsPart(specified.Mode)
            ? specified with { Mode = CssDisplayMode.Block, Inline = false }
            : specified with { Inline = false };
    }

    private static CssDisplaySpecification SpecifiedOrInitial(CssNode node)
    {
        if (node.Target.GetEffectiveValueLayer(SpecificationProperty) is not null)
            return (CssDisplaySpecification)node.GetValue(SpecificationProperty)!;
        return node.Target switch
        {
            Jalium.UI.Documents.Table => new(CssDisplayMode.Table),
            Jalium.UI.Documents.TableRowGroup => new(CssDisplayMode.TableRowGroup),
            Jalium.UI.Documents.TableRow => new(CssDisplayMode.TableRow),
            Jalium.UI.Documents.TableCell => new(CssDisplayMode.TableCell),
            Jalium.UI.Documents.TableColumn => new(CssDisplayMode.TableColumn),
            Jalium.UI.Documents.ListItem => new(CssDisplayMode.Block, ListItem: true),
            Jalium.UI.Documents.Block or Jalium.UI.Documents.FlowDocument => new(CssDisplayMode.Block),
            _ => Initial,
        };
    }

    internal static void RefreshVisibility(UIElement element)
    {
        System.Threading.Volatile.Write(ref s_hasVisibilityDeclarations, 1);
        UIElement.InvalidateHitTestCache();
        element.InvalidateVisual();
        element.UpdateIsVisibleFromTree(forceDescendants: true);
        CssTableLayout.VisibilityChanged(element);
        var affectsFlexLayout = CssContentsLayout.BoxParent(element) is FrameworkElement parent && IsFlexContainer(parent);
        affectsFlexLayout |= InvalidateFlexLayoutsInSubtree(element);
        if (affectsFlexLayout)
            for (Visual? current = element; current is not null; current = current.VisualParent)
                if (current is UIElement uiElement) uiElement.InvalidateMeasure();
    }

    internal static bool IsCollapsedFlexItem(UIElement element) =>
        HasVisibilityDeclarations && !CssContentsLayout.IsContents(element) &&
        CssContentsLayout.BoxParent(element) is FrameworkElement parent &&
        IsFlexContainer(parent) &&
        element is not FrameworkElement { CssLayout.IsOutOfFlow: true } &&
        EffectiveVisibility(element) == Visibility.Collapsed;

    private static bool IsFlexContainer(FrameworkElement panel) =>
        panel.IsCssContentHost && (panel.CssDisplayMode == CssDisplayMode.Flex ||
        panel is FlexPanel && panel.CssDisplayMode == CssDisplayMode.Native);

    private static bool InvalidateFlexLayoutsInSubtree(UIElement element)
    {
        var found = element is FrameworkElement panel && IsFlexContainer(panel);
        if (found) element.InvalidateMeasure();
        for (var i = 0; i < element.InternalVisualChildrenCount; i++)
            if (element.InternalGetVisualChild(i) is UIElement child)
                found |= InvalidateFlexLayoutsInSubtree(child);
        return found;
    }

    internal static Visibility EffectiveVisibility(DependencyObject target)
    {
        if (CssRubyVisibility.Suppressed(target)) return Visibility.Collapsed;
        if (CssTableLayout.ColumnSuppressed(target)) return Visibility.Collapsed;
        if (CssTableLayout.TrackSuppressed(target)) return Visibility.Collapsed;
        if (CssReplacedContent.IsSuppressed(target)) return Visibility.Collapsed;
        if (CssContentsLayout.Suppressed(target)) return Visibility.Collapsed;
        if (!HasVisibilityDeclarations) return Visibility.Visible;
        for (var node = CssNode.Get(target); node is not null; node = CssMatcher.CssAncestor(node))
        {
            // Anonymous boxes use native visibility to enable their implementation
            // content. That switch does not override the source's CSS inheritance.
            if (node.Target is UIElement native && node.Target is not ICssAnonymousBox)
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
                        CssVisibilityMode.Collapse => CssRubyVisibility.IgnoresCollapse(target, node.Target) ? Visibility.Visible : Visibility.Collapsed,
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
            if (current.Target is UIElement native && current.Target is not ICssAnonymousBox)
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
        if (element is FrameworkElement tablePart && CssTableLayout.IsInternal(tablePart.CssDisplayMode)) RefreshVisibility(element);
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
            var tokens = new List<string>();
            while (reader.TryReadIdent(out var identToken)) tokens.Add(identToken.ToString().ToLowerInvariant());
            if (!reader.AtEnd || tokens.Count is < 1 or > 3) return null;
            if (tokens.Count == 1)
            {
                CssDisplaySpecification? legacy = tokens[0] switch
                {
                    "none" => new(CssDisplayMode.Native, None: true),
                    "contents" => new(CssDisplayMode.Contents),
                    "inline-block" => new(CssDisplayMode.FlowRoot, true),
                    "-webkit-box" => new(CssDisplayMode.Flex, LegacyBox: true),
                    "-webkit-inline-box" => new(CssDisplayMode.Flex, true, LegacyBox: true),
                    "inline-flex" => new(CssDisplayMode.Flex, true), "inline-grid" => new(CssDisplayMode.Grid, true),
                    "table" => new(CssDisplayMode.Table), "inline-table" => new(CssDisplayMode.Table, true),
                    "table-row" => new(CssDisplayMode.TableRow), "table-cell" => new(CssDisplayMode.TableCell),
                    "table-row-group" => new(CssDisplayMode.TableRowGroup), "table-header-group" => new(CssDisplayMode.TableHeaderGroup),
                    "table-footer-group" => new(CssDisplayMode.TableFooterGroup), "table-column" => new(CssDisplayMode.TableColumn),
                    "table-column-group" => new(CssDisplayMode.TableColumnGroup),
                    "table-caption" => new(CssDisplayMode.TableCaption),
                    "ruby" => new(CssDisplayMode.Ruby, true),
                    "ruby-base" => new(CssDisplayMode.RubyBase, true),
                    "ruby-text" => new(CssDisplayMode.RubyText, true),
                    "ruby-base-container" => new(CssDisplayMode.RubyBaseContainer, true),
                    "ruby-text-container" => new(CssDisplayMode.RubyTextContainer, true),
                    _ => null,
                };
                if (legacy is { } value) return new CssDisplayValue(value);
            }
            bool? inline = null; CssDisplayMode? mode = null; var listItem = false;
            foreach (var token in tokens)
            {
                if (token is "block" or "inline")
                { if (inline is not null) return null; inline = token == "inline"; }
                else if (token == "list-item") { if (listItem) return null; listItem = true; }
                else
                {
                    if (mode is not null) return null;
                    mode = token switch { "flow" => CssDisplayMode.Native, "flow-root" => CssDisplayMode.FlowRoot,
                        "flex" => CssDisplayMode.Flex, "grid" => CssDisplayMode.Grid, "table" => CssDisplayMode.Table, "ruby" => CssDisplayMode.Ruby, _ => null };
                    if (mode is null) return null;
                }
            }
            if (listItem && mode is CssDisplayMode.Flex or CssDisplayMode.Grid or CssDisplayMode.Table or CssDisplayMode.Ruby) return null;
            var usedInline = inline ?? false;
            var usedMode = mode ?? CssDisplayMode.Native;
            if (usedMode == CssDisplayMode.Native && (!usedInline || listItem)) usedMode = CssDisplayMode.Block;
            return new CssDisplayValue(new(usedMode, usedInline, ListItem: listItem));
        },
    });
}

internal sealed class CssDisplayValue(CssDisplaySpecification display) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        sink.Set(CssDisplayProperties.SpecificationProperty, display);
        // Detached controls can be intrinsic-size probes or reusable inline
        // components. Parent-dependent blockification begins when attached.
        var used = context.Element.FrameworkParent is null ? display : CssDisplayProperties.Compute(context.Element, display);
        sink.Set(CssDisplayLayout.ModeProperty, used.Mode);
        sink.Set(CssDisplayLayout.InlineProperty, used.Inline);
        var visibility = context.Slots.Visibility ??= new();
        visibility.DisplayNone = display.None || display.Mode == CssDisplayMode.Contents &&
            context.Element.Target is FrameworkElement element && CssContentsLayout.IsUnusual(element);
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
