using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Diagnostics;

/// <summary>
/// Global allow-list for Diagnostics notifications. Inspection tools (the DevTools
/// window itself) register themselves here so that their own measure/arrange,
/// routed events, and binding updates do not pollute the logs they show.
/// Layout/Binding/RoutedEvent diagnostics consult <see cref="ShouldIgnore(Visual)"/>
/// before recording a sample.
/// </summary>
public static class DiagnosticsScope
{
    // Stored as weak references so that closing a DevTools window (or tearing down a
    // test harness) does not keep visuals alive forever.
    private static readonly ConditionalWeakTable<Visual, object> s_excludedRoots = new();
    private static readonly List<WeakReference<Visual>> s_excludedList = new();

    // Once we prove that a specific visual lives inside an excluded subtree we
    // memoize the answer — that way later diagnostics calls for the same visual
    // short-circuit, and the element stays ignored even if it is briefly
    // detached from the tree (common for Popups, recycled containers, etc.).
    private static readonly ConditionalWeakTable<Visual, object> s_blessedIgnored = new();

    private static readonly object s_lock = new();

    /// <summary>
    /// Register a visual root whose entire subtree should be skipped by all
    /// Diagnostics hooks. Safe to call more than once for the same root.
    /// </summary>
    public static void ExcludeRoot(Visual root)
    {
        if (root == null) return;
        lock (s_lock)
        {
            if (s_excludedRoots.TryGetValue(root, out _)) return;
            s_excludedRoots.Add(root, s_sentinel);
            s_excludedList.Add(new WeakReference<Visual>(root));
        }

        // Also tag the subtree with an O(1) inheritable flag so future children
        // added via AddVisualChild inherit it immediately. This closes the race
        // where a child's first Measure fires before the VisualParent chain
        // check can see the excluded root (common under VSP recycling).
        root.MarkDiagnosticsIgnoredSubtree();
    }

    /// <summary>
    /// Stop excluding the subtree rooted at <paramref name="root"/>.
    /// </summary>
    public static void IncludeRoot(Visual root)
    {
        if (root == null) return;
        lock (s_lock)
        {
            s_excludedRoots.Remove(root);
            for (int i = s_excludedList.Count - 1; i >= 0; i--)
            {
                if (!s_excludedList[i].TryGetTarget(out var r) || ReferenceEquals(r, root))
                    s_excludedList.RemoveAt(i);
            }
        }
    }

    // Hard cap on how deep we ever walk the visual parent chain. Guards against
    // pathological trees (or accidental cycles) turning a diagnostics hook into
    // an infinite loop.
    private const int MaxWalkDepth = 256;

    /// <summary>
    /// True when <paramref name="element"/> is either an excluded root itself or a
    /// descendant of one. O(depth × excluded-root-count) on first call per visual;
    /// subsequent calls for the same visual are O(1) via the blessed cache.
    /// </summary>
    public static bool ShouldIgnore(Visual? element)
    {
        if (element == null) return false;

        // Primary fast path: the flag is set at AddVisualChild time, so any
        // visual attached under an excluded root resolves in O(1). This also
        // works when the parent chain is momentarily broken (VSP recycling,
        // mid-attach Measure).
        if (element.IsDiagnosticsIgnored) return true;

        // Legacy fast path: visuals we've already blessed via the fallback
        // parent-walk stay ignored even after detach.
        if (s_blessedIgnored.TryGetValue(element, out _))
            return true;

        lock (s_lock)
        {
            if (s_excludedList.Count == 0) return false;
        }

        // Walk the visual parent chain and memoize every ancestor we visit so
        // that future calls short-circuit even if the element gets detached.
        int depth = 0;
        Visual? cur = element;
        List<Visual>? path = null;
        while (cur != null && depth < MaxWalkDepth)
        {
            if (s_excludedRoots.TryGetValue(cur, out _))
            {
                // Remember every visual on the path from element → excluded root.
                AddToBlessed(element);
                if (path != null)
                {
                    foreach (var v in path) AddToBlessed(v);
                }
                return true;
            }
            (path ??= new List<Visual>(8)).Add(cur);
            cur = cur.VisualParent;
            depth++;
        }
        return false;
    }

    private static void AddToBlessed(Visual v)
    {
        if (!s_blessedIgnored.TryGetValue(v, out _))
            s_blessedIgnored.Add(v, s_sentinel);
    }

    /// <summary>
    /// Fast path for RoutedEventArgs — checks the event source chain.
    /// </summary>
    public static bool ShouldIgnoreEvent(RoutedEventArgs args)
    {
        return ShouldIgnore(args.Source as Visual)
            || ShouldIgnore(args.OriginalSource as Visual);
    }

    private static readonly object s_sentinel = new();

    // ── Thread-local creation scope ──────────────────────────────────────
    // Any Visual constructed while a scope is active has _isDiagnosticsIgnored
    // set automatically. Closes the gap where a DevTools-owned element like
    // DevToolsTreeViewItem fires InvalidateMeasure from its constructor (via
    // DP defaults / Header / Foreground sets) before it's attached to the
    // parent chain — constructor-time invalidations were showing up in Layout
    // stats because the flag inheritance via AddVisualChild hadn't happened yet.
    [ThreadStatic] private static int s_ignoredCreationDepth;

    public static bool IsInIgnoredCreationScope => s_ignoredCreationDepth > 0;

    /// <summary>
    /// Enter a scope in which every newly-constructed <see cref="Visual"/> is
    /// marked as diagnostics-ignored on this thread. Dispose the returned
    /// struct to exit. Nesting is supported — only the outermost dispose exits.
    /// </summary>
    public static IgnoredCreationScope BeginIgnoredCreation()
    {
        s_ignoredCreationDepth++;
        return default;
    }

    public readonly struct IgnoredCreationScope : IDisposable
    {
        public void Dispose()
        {
            if (s_ignoredCreationDepth > 0) s_ignoredCreationDepth--;
        }
    }
}
