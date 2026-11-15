using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Computed Ruby writing modes; authored values and bindings stay in their original layers.</summary>
internal static class CssRubyWritingMode
{
    private static bool s_enabled;
    private static long s_epoch;
    private static readonly ConditionalWeakTable<DependencyObject, Cache> s_cache = new();
    private sealed class Cache
    {
        internal long Epoch = -1, ModeVersion, PositionVersion, TreeVersion;
        internal bool Forced;
        internal WeakReference<DependencyObject>? Source;
    }
    internal static void ObservePosition(object? value)
    {
        if (value is CssRubyPosition.InterCharacter) s_enabled = true;
    }
    internal static void Invalidate() => System.Threading.Interlocked.Increment(ref s_epoch);
    internal static bool IsInterCharacterAnnotation(DependencyObject target)
        => s_enabled && ForcesAnnotation(CssNode.Get(target));
    internal static bool IsAnnotationContext(DependencyObject target)
        => Role(CssNode.Get(target)) is CssDisplayMode.RubyText or CssDisplayMode.RubyTextContainer;

    internal static object? Coerce(DependencyObject target, object? authored)
    {
        if (!s_enabled) return authored;
        var source = Resolve(CssNode.Get(target));
        if (source.Forced) return CssWritingMode.VerticalRl;
        return source.Source is { } weak && weak.TryGetTarget(out var provider) && !ReferenceEquals(provider, target)
            ? provider.GetValue(CssWritingModeProperties.WritingModeProperty) : authored;
    }

    private static Cache Resolve(CssNode node)
    {
        var property = CssWritingModeProperties.WritingModeProperty;
        long epoch = System.Threading.Volatile.Read(ref s_epoch), mode = property.InheritanceVersion,
            position = CssWritingModeProperties.RubyPositionProperty.InheritanceVersion,
            tree = FrameworkElement.InheritanceTreeVersion;
        var cache = s_cache.GetValue(node.Target, static _ => new());
        // Content-element parentage has no native visual-tree version. Resolve
        // it live; cache only visual providers, with weak source ownership.
        bool visual = node.Target is FrameworkElement;
        if (visual && cache.Epoch == epoch && cache.ModeVersion == mode && cache.PositionVersion == position && cache.TreeVersion == tree &&
            (cache.Forced || cache.Source is null || cache.Source.TryGetTarget(out _))) return cache;
        bool forced = ForcesAnnotation(node);
        WeakReference<DependencyObject>? source = null;
        if (!forced)
        {
            bool own = node.Target.HasAnimatedValue(property) || node.Target.HasCssAnimatedValue(property) || node.Target.GetEffectiveValueLayer(property) is not null;
            bool inheritedCss = !node.Target.HasLocalOrAnimatedValue(property) && !node.Target.HasCssAnimatedValue(property) &&
                node.CssRuntimeState?.InheritedBoxObservers?.ContainsKey("writing-mode") == true;
            if (own && !inheritedCss) source = new(node.Target);
            else if (node.FrameworkParent is { } parent)
            {
                var inherited = Resolve(parent); forced = inherited.Forced; source = inherited.Source;
            }
        }
        cache.Epoch = epoch; cache.ModeVersion = mode; cache.PositionVersion = position; cache.TreeVersion = tree;
        cache.Forced = forced; cache.Source = source;
        return cache;
    }

    private static CssDisplayMode Role(CssNode node) => node.Target is FrameworkElement element
        ? element.CssDisplayMode : node.Target.GetEffectiveValueLayer(CssDisplayProperties.SpecificationProperty) is null
        ? CssDisplayMode.Native : CssDisplayProperties.Computed(node).Mode;

    private static CssNode? BoxParent(CssNode node)
    {
        var parent = node.FrameworkParent;
        while (parent is not null && Role(parent) == CssDisplayMode.Contents) parent = parent.FrameworkParent;
        return parent;
    }

    private static bool ForcesAnnotation(CssNode node)
    {
        if (Role(node) != CssDisplayMode.RubyText || BoxParent(node) is not { } parent) return false;
        // Direct rt boxes have an anonymous annotation container inheriting
        // from their ruby host. The position on an explicit rtc owns its level.
        if (CssWritingModeProperties.GetRubyPosition(parent.Target) != CssRubyPosition.InterCharacter) return false;
        var ruby = Role(parent) == CssDisplayMode.RubyTextContainer ? BoxParent(parent) : parent;
        return ruby is not null && !CssWritingModeProperties.IsVertical(CssWritingModeProperties.GetWritingMode(ruby.Target));
    }
}
