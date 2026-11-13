using Jalium.UI.Media;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

internal static class CssCompositingProperties
{
    private sealed class Subtree { internal long Revision = -1; internal bool Blends; }
    private static readonly ConditionalWeakTable<Visual, Subtree> Subtrees = new();
    private static long s_treeRevision;
    private static bool s_everBlended;
    internal static void TreeChanged() => Interlocked.Increment(ref s_treeRevision);
    private static readonly Dictionary<string, CssBlendMode> Modes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["normal"] = CssBlendMode.Normal,
        ["multiply"] = CssBlendMode.Multiply,
        ["screen"] = CssBlendMode.Screen,
        ["overlay"] = CssBlendMode.Overlay,
        ["darken"] = CssBlendMode.Darken,
        ["lighten"] = CssBlendMode.Lighten,
        ["color-dodge"] = CssBlendMode.ColorDodge,
        ["color-burn"] = CssBlendMode.ColorBurn,
        ["hard-light"] = CssBlendMode.HardLight,
        ["soft-light"] = CssBlendMode.SoftLight,
        ["difference"] = CssBlendMode.Difference,
        ["exclusion"] = CssBlendMode.Exclusion,
        ["hue"] = CssBlendMode.Hue,
        ["saturation"] = CssBlendMode.Saturation,
        ["color"] = CssBlendMode.Color,
        ["luminosity"] = CssBlendMode.Luminosity,
        ["plus-lighter"] = CssBlendMode.PlusLighter,
    };
    internal static readonly DependencyProperty MixProperty = DependencyProperty.RegisterAttached(
        "CssMixBlendMode", typeof(CssBlendMode), typeof(CssCompositingProperties), new PropertyMetadata(CssBlendMode.Normal, Changed));
    internal static readonly DependencyProperty IsolationProperty = DependencyProperty.RegisterAttached(
        "CssIsolation", typeof(bool), typeof(CssCompositingProperties), new PropertyMetadata(false, Changed));
    internal static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
        "CssBackgroundBlendModes", typeof(CssMaskList<CssBlendMode>), typeof(CssCompositingProperties),
        new PropertyMetadata(new CssMaskList<CssBlendMode>([CssBlendMode.Normal]), Changed));
    internal static readonly DependencyProperty ZIndexProperty = DependencyProperty.RegisterAttached(
        "CssZIndex", typeof(int?), typeof(CssCompositingProperties), new PropertyMetadata(null, ZIndexChanged));
    private static void ZIndexChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is int index)
            owner.SetLayerValue(Controls.Panel.ZIndexProperty, index, DependencyObject.LayerValueSource.CssBase, allowAutoTransition: false);
        else
            owner.ClearLayerValue(Controls.Panel.ZIndexProperty, DependencyObject.LayerValueSource.CssBase, allowAutoTransition: false);
        if (owner is FrameworkElement fe)
            CssPositionedLayout.UpdateStackingPromotion(fe);
        if (owner is UIElement ui)
        {
            CssPositionedLayout.ContainingBlockChanged(ui);
            if (ui.VisualParent is Controls.Panel panel)
                panel.InvalidateCssFlowOrder();
            ui.InvalidateArrange();
            ui.InvalidateVisual();
        }
    }
    internal static CssBlendMode Mix(DependencyObject owner) => (CssBlendMode)owner.GetValue(MixProperty)!;
    internal static bool Isolate(DependencyObject owner) => (bool)owner.GetValue(IsolationProperty)!;
    internal static CssBlendMode Background(DependencyObject owner, int index) => ((CssMaskList<CssBlendMode>)owner.GetValue(BackgroundProperty)!).At(index);
    internal static bool HasBackgroundBlend(DependencyObject owner) => ((CssMaskList<CssBlendMode>)owner.GetValue(BackgroundProperty)!).Values.Any(v => v != CssBlendMode.Normal);
    internal static bool ContainsBlend(Visual root)
    {
        if (!s_everBlended) return false;
        var revision = Interlocked.Read(ref s_treeRevision); var cache = Subtrees.GetOrCreateValue(root);
        if (cache.Revision == revision) return cache.Blends;
        // Populate every visited descendant's cache in postorder. Looking for
        // the first blend independently at each ancestor becomes quadratic on
        // a deep tree whose blended element is near the leaves.
        var pending = new Stack<(Visual Visual, bool Visited)>();
        var scheduled = new HashSet<Visual> { root };
        pending.Push((root, false));
        while (pending.TryPop(out var item))
        {
            var entry = Subtrees.GetOrCreateValue(item.Visual);
            if (entry.Revision == revision) continue;
            if (!item.Visited)
            {
                pending.Push((item.Visual, true));
                for (var i = item.Visual.InternalVisualChildrenCount - 1; i >= 0; i--)
                    if (item.Visual.InternalGetVisualChild(i) is { } child && scheduled.Add(child))
                        pending.Push((child, false));
                continue;
            }
            var result = Mix(item.Visual) != CssBlendMode.Normal;
            for (var i = 0; i < item.Visual.InternalVisualChildrenCount; i++)
                if (item.Visual.InternalGetVisualChild(i) is { } child &&
                    Subtrees.TryGetValue(child, out var childEntry) && childEntry.Revision == revision)
                    result |= childEntry.Blends;
            entry.Blends = result;
            entry.Revision = revision;
        }
        return cache.Blends;
    }

    internal static bool HasExplicitZIndex(UIElement element)
    {
        if (element.GetValue(ZIndexProperty) is int)
            return true;
        var z = element.GetValueSourceInternal(Controls.Panel.ZIndexProperty);
        return z.BaseValueSource is not (BaseValueSource.Default or BaseValueSource.Inherited) || z.IsAnimated;
    }

    internal static bool ZIndexApplies(UIElement element)
    {
        var z = element.GetValueSourceInternal(Controls.Panel.ZIndexProperty);
        if (z.BaseValueSource == BaseValueSource.Local && element.GetValue(ZIndexProperty) is null)
            return true;
        if (element is FrameworkElement fe && fe.CssLayout?.Position is not null and not CssPositionMode.Static)
            return true;
        var boxParent = CssContentsLayout.BoxParent(element) ?? element.VisualParent;
        return boxParent is Controls.FlexPanel or Controls.Grid ||
               boxParent is FrameworkElement { CssDisplayMode: CssDisplayMode.Flex or CssDisplayMode.Grid };
    }

    internal static int EffectiveZIndex(UIElement element)
    {
        if (!HasExplicitZIndex(element) || !ZIndexApplies(element))
            return 0;
        return Controls.Panel.GetZIndex(element);
    }

    internal static bool CreatesStackingContext(FrameworkElement owner)
    {
        if (Mix(owner) != CssBlendMode.Normal || Isolate(owner)) return true;
        if (owner.Opacity < 1 || owner.RenderTransform is not null || owner.BackdropEffect is { HasEffect: true } ||
            CssTransform3D.HasPerspective(owner) || CssTransform3D.IsPreserve3DAuthored(owner) ||
            CssMaskProperties.Active(owner) || owner.GetValue(CssClipPathProperties.ValueProperty) is CssClipPathValue { Shape: not CssClipShape.None }) return true;
        if (owner.Effect is Media.Effects.Effect effect && (!effect.CssBoxShadowLayer || effect.CssFilterLayer)) return true;
        if (owner.CssLayout?.Position is CssPositionMode.Fixed or CssPositionMode.Sticky) return true;
        return HasExplicitZIndex(owner) && ZIndexApplies(owner);
    }

    internal static bool CreatesGroup(FrameworkElement owner)
    {
        if (Mix(owner) != CssBlendMode.Normal || Isolate(owner)) return true;
        if (!ContainsBlend(owner)) return false;
        return CreatesStackingContext(owner);
    }
    internal static bool TryRead(ref CssTokenReader reader, out CssBlendMode mode)
    {
        mode = default; var probe = reader;
        if (!probe.TryReadIdent(out var name) || !Modes.TryGetValue(name.ToString(), out mode)) return false;
        reader = probe; return true;
    }
    private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (args.Property == MixProperty)
        {
            (owner as Visual)?.SetBackdropReplayCandidate(Visual.BackdropReplayBlend,
                args.NewValue is CssBlendMode blend && blend != CssBlendMode.Normal);
            if (args.NewValue is CssBlendMode mode && mode != CssBlendMode.Normal) s_everBlended = true;
            TreeChanged();
        }
        CssDocumentConsumers.Invalidate(CssNode.Get(owner));
        (owner as UIElement)?.InvalidateVisual();
        if (owner is FrameworkElement element && args.Property != BackgroundProperty) element.InvalidateArrange();
    }
    internal static void Register()
    {
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "mix-blend-mode",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = MixProperty,
            TransitionTargetDpName = MixProperty.Name,
            Parse = static (ref CssTokenReader r, CssCompileContext c) => TryRead(ref r, out var mode) && r.AtEnd ? new CssImmediateValue(MixProperty, mode) : null,
        });
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "isolation",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = IsolationProperty,
            TransitionTargetDpName = IsolationProperty.Name,
            Parse = static (ref CssTokenReader r, CssCompileContext c) => r.TryReadIdent(out var name) && r.AtEnd &&
                (name.Equals("auto", StringComparison.OrdinalIgnoreCase) || name.Equals("isolate", StringComparison.OrdinalIgnoreCase))
                ? new CssImmediateValue(IsolationProperty, name.Equals("isolate", StringComparison.OrdinalIgnoreCase)) : null,
        });
        CssPropertyRegistry.Register(new CssPropertyDescriptor
        {
            Name = "background-blend-mode",
            Kind = CssPropertyKind.Longhand,
            StorageProperty = BackgroundProperty,
            TransitionTargetDpName = BackgroundProperty.Name,
            Parse = static (ref CssTokenReader r, CssCompileContext c) =>
            {
                var modes = new List<CssBlendMode>();
                do
                {
                    if (!TryRead(ref r, out var mode)) return null; modes.Add(mode);
                    if (!r.TryReadComma()) break; if (r.AtEnd) return null;
                } while (true);
                return r.AtEnd ? new CssImmediateValue(BackgroundProperty, new CssMaskList<CssBlendMode>(modes.ToArray())) : null;
            },
        });
    }
}
