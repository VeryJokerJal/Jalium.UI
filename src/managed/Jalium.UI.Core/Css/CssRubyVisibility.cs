using System.Runtime.CompilerServices;

namespace Jalium.UI.Styling;

/// <summary>Used Ruby hiding; no authored visibility value is replaced.</summary>
internal static class CssRubyVisibility
{
    private sealed record State(WeakReference<FrameworkElement> Owner, bool Hidden, WeakReference<DependencyObject>? IgnoreRoot);
    private sealed class Cache
    {
        internal long Version = -1, Tree;
        internal bool Hidden;
        internal WeakReference<DependencyObject>? IgnoreRoot;
    }
    private static readonly ConditionalWeakTable<DependencyObject, State> s_states = new();
    private static readonly ConditionalWeakTable<DependencyObject, Cache> s_cache = new();
    private static bool s_enabled;
    private static long s_version;

    internal static void Set(UIElement target, FrameworkElement owner, bool hidden, bool ignoreCollapse, DependencyObject? collapseRoot = null)
    {
        collapseRoot = ignoreCollapse ? collapseRoot ?? target : null;
        if (s_states.TryGetValue(target, out var prior))
        {
            DependencyObject? priorRoot = null; prior.IgnoreRoot?.TryGetTarget(out priorRoot);
            if (prior.Hidden == hidden && ReferenceEquals(priorRoot, collapseRoot) && (collapseRoot is not null || prior.IgnoreRoot is null) &&
                prior.Owner.TryGetTarget(out var old) && ReferenceEquals(old, owner)) return;
            s_states.Remove(target);
        }
        else if (!hidden && !ignoreCollapse) return;
        if (hidden || ignoreCollapse)
        {
            s_states.Add(target, new(new(owner), hidden, collapseRoot is null ? null : new(collapseRoot))); s_enabled = true;
        }
        System.Threading.Interlocked.Increment(ref s_version);
        UIElement.InvalidateHitTestCache(); target.InvalidateVisual(); target.UpdateIsVisibleFromTree(forceDescendants: true);
    }

    internal static void Clear(UIElement target, FrameworkElement owner)
    {
        if (s_states.TryGetValue(target, out var state) && state.Owner.TryGetTarget(out var current) && ReferenceEquals(current, owner))
            Set(target, owner, false, false);
    }

    internal static bool Suppressed(DependencyObject target) => s_enabled && Resolve(CssNode.Get(target)).Hidden;

    internal static bool IgnoresCollapse(DependencyObject target, DependencyObject provider)
    {
        if (!s_enabled || Resolve(CssNode.Get(target)).IgnoreRoot is not { } weak || !weak.TryGetTarget(out var root)) return false;
        // Only the annotation's own/inherited collapse is disabled by merge;
        // a descendant's independent visibility declaration still applies.
        for (var node = CssNode.Get(root); node is not null; node = node.FrameworkParent)
            if (ReferenceEquals(node.Target, provider)) return true;
        return false;
    }

    private static Cache Resolve(CssNode node)
    {
        var version = System.Threading.Volatile.Read(ref s_version); var tree = FrameworkElement.InheritanceTreeVersion;
        var cache = s_cache.GetValue(node.Target, static _ => new());
        if (node.Target is FrameworkElement && cache.Version == version && cache.Tree == tree) return cache;
        var hidden = false; WeakReference<DependencyObject>? ignore = null;
        if (node.FrameworkParent is { } parent)
        { var inherited = Resolve(parent); hidden = inherited.Hidden; ignore = inherited.IgnoreRoot; }
        if (s_states.TryGetValue(node.Target, out var state) && state.Owner.TryGetTarget(out var owner))
            for (var current = node.FrameworkParent; current is not null; current = current.FrameworkParent)
                if (ReferenceEquals(current.Target, owner))
                { hidden |= state.Hidden; if (state.IgnoreRoot is { } root) ignore = root; break; }
        cache.Version = version; cache.Tree = tree; cache.Hidden = hidden; cache.IgnoreRoot = ignore;
        return cache;
    }
}
