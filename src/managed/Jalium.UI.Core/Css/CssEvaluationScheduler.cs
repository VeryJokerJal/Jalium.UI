using System.Runtime.CompilerServices;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Styling;

/// <summary>
/// Batches CSS re-evaluation per dispatcher (mirroring KeyboardFocusRevalidation): tree
/// changes and sheet swaps enqueue subtree roots, a Render-priority pass prunes nested
/// roots and evaluates each surviving subtree once. LayoutManager flushes pending work
/// before measuring so first-frame styling is in place (no FOUC).
/// </summary>
internal static class CssEvaluationScheduler
{
    private sealed class DispatcherState
    {
        public HashSet<CssNode> SubtreeRoots = new();
        public HashSet<CssNode> SelfElements = new();
        public bool Scheduled;
    }

    private const int MaxChainDepth = 4096;

    private static readonly ConditionalWeakTable<Dispatcher, DispatcherState> s_states = new();

    public static void InvalidateSubtree(CssNode root)
    {
        var dispatcher = root.Dispatcher;
        var state = s_states.GetValue(dispatcher, static _ => new DispatcherState());
        state.SubtreeRoots.Add(root);
        Schedule(dispatcher, state);
    }

    /// <summary>Re-evaluates a single element (subject-position pseudo-class flips).</summary>
    public static void InvalidateElement(CssNode element)
    {
        var dispatcher = element.Dispatcher;
        var state = s_states.GetValue(dispatcher, static _ => new DispatcherState());
        state.SelfElements.Add(element);
        Schedule(dispatcher, state);
    }

    /// <summary>Runs any pending evaluation synchronously (layout entry, tests).</summary>
    internal static bool HasPending(Dispatcher dispatcher)
        => s_states.TryGetValue(dispatcher, out var state) && state.Scheduled;

    public static void FlushIfPending(Dispatcher dispatcher)
    {
        if (s_states.TryGetValue(dispatcher, out var state) && state.Scheduled)
        {
            Run(state);
        }
    }

    private static void Schedule(Dispatcher dispatcher, DispatcherState state)
    {
        if (state.Scheduled || dispatcher.HasShutdownStarted)
        {
            return;
        }

        state.Scheduled = true;
        dispatcher.BeginInvoke(DispatcherPriority.Render, () => Run(state));
    }

    private static void Run(DispatcherState state)
    {
        if (!state.Scheduled)
        {
            return; // Already flushed synchronously before the dispatcher callback fired.
        }

        state.Scheduled = false;
        var roots = state.SubtreeRoots;
        var selfElements = state.SelfElements;
        if (roots.Count == 0 && selfElements.Count == 0)
        {
            return;
        }

        state.SubtreeRoots = new HashSet<CssNode>();
        state.SelfElements = new HashSet<CssNode>();

        foreach (var root in roots)
        {
            if (roots.Count > 1 && HasAncestorIn(root, roots))
            {
                continue; // Covered by an enqueued ancestor's subtree pass.
            }

            EvaluateSubtree(root);
        }

        foreach (var element in selfElements)
        {
            if (roots.Count > 0 && (roots.Contains(element) || HasAncestorIn(element, roots)))
            {
                continue; // Already evaluated within a subtree pass.
            }

            CssEngine.EvaluateElement(element);
        }
    }

    private static bool HasAncestorIn(CssNode element, HashSet<CssNode> roots)
    {
        var depth = 0;
        for (var current = element.FrameworkParent; current is not null && depth++ < MaxChainDepth;
             current = current.FrameworkParent)
        {
            if (roots.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    internal static void EvaluateSubtree(CssNode root)
    {
        var visited = new HashSet<CssNode>();
        var pending = new Stack<CssNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            CssEngine.EvaluateElement(node);
            foreach (var child in node.EnumerateChildren().Reverse()) pending.Push(child);
        }
    }
}
