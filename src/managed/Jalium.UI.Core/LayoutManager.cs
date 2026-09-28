using System.Diagnostics;
using System.Linq;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Per-pass layout telemetry returned by <see cref="LayoutManager.UpdateLayout"/>.
/// Lets the Window publish a per-frame "Layout" breakdown to DevTools so we can
/// tell apart "measure ate 20 ms" from "arrange ate 20 ms" from "we ran 12
/// invalidate→measure iterations because the tree is fighting itself".
/// </summary>
internal readonly struct LayoutPassResult
{
    public readonly long TotalTicks;
    public readonly long MeasureTicks;
    public readonly long ArrangeTicks;
    public readonly int MeasureCount;
    public readonly int ArrangeCount;
    public readonly int Iterations;

    public LayoutPassResult(long totalTicks, long measureTicks, long arrangeTicks,
                            int measureCount, int arrangeCount, int iterations)
    {
        TotalTicks = totalTicks;
        MeasureTicks = measureTicks;
        ArrangeTicks = arrangeTicks;
        MeasureCount = measureCount;
        ArrangeCount = arrangeCount;
        Iterations = iterations;
    }
}

/// <summary>
/// Manages the layout invalidation cycle for a visual tree.
/// Provides queue-based measure/arrange processing.
/// Similar to WPF's LayoutManager.
/// </summary>
internal sealed class LayoutManager
{
    private readonly HashSet<UIElement> _measureQueue = new();
    private readonly HashSet<UIElement> _arrangeQueue = new();
    private readonly List<UIElement> _measureSorted = new();
    private readonly List<UIElement> _arrangeSorted = new();
    private readonly Dictionary<Visual, int> _depthCache = new();
    private bool _isUpdating;
    private int _layoutIterations;
    private const int MaxLayoutIterations = 250;
    // Iteration count that means "this tree is fighting itself" rather than
    // "a couple of invalidations settled". Well clear of any legitimate pass.
    private const int RunawayLayoutIterationThreshold = 32;
    private bool _reportedRunawayLayout;

    /// <summary>
    /// Names the elements driving a pass that will not settle. A tree that keeps
    /// re-invalidating itself was previously invisible: the loop just truncated at
    /// <see cref="MaxLayoutIterations"/> and the frame shipped with whatever
    /// geometry it had, so an oscillation showed up as jitter on screen with
    /// nothing in any log to point at it.
    /// </summary>
    [Conditional("DEBUG")]
    private void ReportRunawayLayoutIfNeeded()
    {
        if (_reportedRunawayLayout || _layoutIterations < RunawayLayoutIterationThreshold)
        {
            return;
        }

        _reportedRunawayLayout = true;

        var culprits = _measureQueue.Concat(_arrangeQueue)
            .Distinct()
            .Take(5)
            .Select(e => $"{e.GetType().Name}(depth={GetCachedDepth(e)})");

        Debug.WriteLine(
            $"[Jalium.Layout] layout has not settled after {_layoutIterations} iterations " +
            $"(cap {MaxLayoutIterations}); still queued: {string.Join(", ", culprits)}. " +
            "Something in this subtree invalidates layout from inside measure or arrange.");
    }

    /// <summary>
    /// Queues an element for re-measurement.
    /// Measure invalidation implies arrange invalidation (matching WPF behavior).
    /// </summary>
    public void InvalidateMeasure(UIElement? element)
    {
        if (element is null)
            return;

        if (_measureQueue.Add(element))
        {
            _arrangeQueue.Add(element);
            PropagateInvalidMeasureUp(element);
        }
    }

    /// <summary>
    /// Queues an element for re-arrangement.
    /// </summary>
    public void InvalidateArrange(UIElement? element)
    {
        if (element is null)
            return;

        if (_arrangeQueue.Add(element))
        {
            PropagateInvalidArrangeUp(element);
        }
    }

    /// <summary>
    /// Removes an element from all queues (e.g., when removed from tree).
    /// </summary>
    public void Remove(UIElement? element)
    {
        if (element is null)
            return;

        _measureQueue.Remove(element);
        _arrangeQueue.Remove(element);
    }

    /// <summary>
    /// Gets whether there are any elements pending layout.
    /// </summary>
    public bool HasPendingLayout => _measureQueue.Count > 0 || _arrangeQueue.Count > 0;

    /// <summary>
    /// Monotonically increasing token bumped each time UpdateLayout completes.
    /// Consumers (e.g. Window's per-frame hit-test memoize) compare this against
    /// a captured value to detect "is layout the same as when I last looked?".
    /// Wraps naturally on overflow; equality is the only operation used.
    /// </summary>
    public long Generation { get; private set; }

    /// <summary>
    /// Processes all pending measure and arrange operations.
    /// Called by Window before rendering.
    /// </summary>
    /// <param name="root">The root element (Window).</param>
    /// <param name="availableSize">The available size for the root.</param>
    public LayoutPassResult UpdateLayout(UIElement root, Size availableSize)
    {
        if (_isUpdating)
            return default;

        // Pending CSS evaluation must land before measure so first-frame styling is in
        // place (no flash of unstyled content). One static-bool read when CSS is unused.
        if (Jalium.UI.Styling.CssEngine.IsActive)
        {
            Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        }

        _isUpdating = true;
        _layoutIterations = 0;
        _reportedRunawayLayout = false;
        int measuredItems = 0;
        int arrangedItems = 0;
        // Wall-clock breakdown so DevTools can answer "is layout slow because
        // measure is heavy, arrange is heavy, or we're stuck iterating?" without
        // needing per-element diagnostics turned on. Stopwatch.GetTimestamp() is
        // ~10 ns on Windows so the inner deltas don't perturb the measurement.
        long totalStart = Stopwatch.GetTimestamp();
        long measureTicks = 0;
        long arrangeTicks = 0;

        try
        {
            // If queues are empty, do a full tree layout.
            if (_measureQueue.Count == 0 && _arrangeQueue.Count == 0)
            {
                long mStart = Stopwatch.GetTimestamp();
                root.Measure(availableSize);
                measureTicks += Stopwatch.GetTimestamp() - mStart;
                measuredItems++;

                long aStart = Stopwatch.GetTimestamp();
                root.Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
                arrangeTicks += Stopwatch.GetTimestamp() - aStart;
                arrangedItems++;
                _layoutIterations = 1;
                if (!Jalium.UI.Styling.CssEvaluationScheduler.HasPending(root.Dispatcher))
                    return new LayoutPassResult(
                        Stopwatch.GetTimestamp() - totalStart,
                        measureTicks, arrangeTicks,
                        measuredItems, arrangedItems, 1);
            }

            // Iterative layout: measure and arrange may trigger further invalidations.
            while ((_measureQueue.Count > 0 || _arrangeQueue.Count > 0 || Jalium.UI.Styling.CssEvaluationScheduler.HasPending(root.Dispatcher))
                   && _layoutIterations < MaxLayoutIterations)
            {
                _layoutIterations++;
                ReportRunawayLayoutIfNeeded();
                Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

                // Process measure queue: sort by depth (shallowest first).
                if (_measureQueue.Count > 0)
                {
                    DrainQueue(_measureQueue, _measureSorted);

                    // Pre-compute depths for all elements before sorting
                    PrecomputeDepths(_measureSorted);
                    _measureSorted.Sort((a, b) => GetCachedDepth(a).CompareTo(GetCachedDepth(b)));

                    long mStart = Stopwatch.GetTimestamp();
                    foreach (var element in _measureSorted)
                    {
                        if (!element.IsMeasureValid)
                        {
                            // Zombie guard — see IsInSameTreeAsRoot for the contract.
                            if (!IsInSameTreeAsRoot(element, root))
                                continue;

                            var measureSize = element == root
                                ? availableSize
                                : element.PreviousAvailableSize;

                            element.Measure(measureSize);
                            measuredItems++;
                        }
                    }
                    measureTicks += Stopwatch.GetTimestamp() - mStart;
                }

                // Process arrange queue: sort by depth (shallowest first).
                if (_arrangeQueue.Count > 0)
                {
                    DrainQueue(_arrangeQueue, _arrangeSorted);

                    PrecomputeDepths(_arrangeSorted);
                    _arrangeSorted.Sort((a, b) => GetCachedDepth(a).CompareTo(GetCachedDepth(b)));

                    long aStart = Stopwatch.GetTimestamp();
                    foreach (var element in _arrangeSorted)
                    {
                        if (!element.IsArrangeValid)
                        {
                            // Zombie guard — see IsInSameTreeAsRoot for the contract.
                            if (!IsInSameTreeAsRoot(element, root))
                                continue;

                            var rect = element == root
                                ? new Rect(0, 0, availableSize.Width, availableSize.Height)
                                : element.PreviousFinalRect;

                            element.Arrange(rect);
                            arrangedItems++;
                        }
                    }
                    arrangeTicks += Stopwatch.GetTimestamp() - aStart;
                }
            }
            return new LayoutPassResult(
                Stopwatch.GetTimestamp() - totalStart,
                measureTicks, arrangeTicks,
                measuredItems, arrangedItems, _layoutIterations);
        }
        finally
        {
            _isUpdating = false;
            _depthCache.Clear();
            Generation++;
        }
    }

    private static void DrainQueue(HashSet<UIElement> source, List<UIElement> destination)
    {
        destination.Clear();

        foreach (var element in source)
        {
            if (element is not null)
            {
                destination.Add(element);
            }
        }

        source.Clear();
    }

    // Both propagations stop at a layout-isolated element: its host lays it out from
    // a rectangle the host derives itself, so nothing above the host can be affected
    // by it. The element is already queued by the caller before this runs, so it is
    // never starved. Walking to the root anyway is what turned a scroll bar fading
    // in — or appearing — into a whole extra window-wide measure and arrange round.
    private void PropagateInvalidArrangeUp(UIElement element)
    {
        if (element.IsLayoutIsolated)
            return;

        var parent = element.VisualParent as UIElement;
        while (parent != null)
        {
            if (parent.IsArrangeValid)
                parent.MarkArrangeInvalid();
            _arrangeQueue.Add(parent);

            // The host itself still has to run: stop once it is queued.
            if (parent.IsLayoutIsolated)
                return;

            parent = parent.VisualParent as UIElement;
        }
    }

    private void PropagateInvalidMeasureUp(UIElement element)
    {
        if (element.IsLayoutIsolated)
            return;

        var parent = element.VisualParent as UIElement;
        while (parent != null)
        {
            if (parent.IsMeasureValid)
                parent.MarkMeasureInvalid();
            _measureQueue.Add(parent);
            _arrangeQueue.Add(parent);

            if (parent.IsLayoutIsolated)
                return;

            parent = parent.VisualParent as UIElement;
        }
    }

    /// <summary>
    /// Pre-computes depth for all elements in the list, caching intermediate results.
    /// Each parent chain is walked at most once due to memoization.
    /// </summary>
    private void PrecomputeDepths(List<UIElement> elements)
    {
        _depthCache.Clear();
        foreach (var element in elements)
        {
            GetCachedDepth(element);
        }
    }

    private int GetCachedDepth(Visual? element)
    {
        if (element is null)
            return -1;

        if (_depthCache.TryGetValue(element, out int cached))
            return cached;

        int depth = 0;
        var parent = element.VisualParent;
        if (parent != null)
        {
            depth = GetCachedDepth(parent) + 1;
        }

        _depthCache[element] = depth;
        return depth;
    }

    /// <summary>
    /// True when <paramref name="element"/> still belongs to the same visual tree as
    /// <paramref name="root"/> (identical tree tops walking <see cref="DependencyObject.VisualParent"/>).
    ///
    /// The sorted snapshots outlive the queues: an entry detached by an EARLIER entry's
    /// Measure/Arrange in the same drain (template re-apply, items-host teardown) is out of
    /// reach of the detach-time queue removal, yet would still be processed — a "zombie"
    /// measure/arrange with real side effects (e.g. a retired items host stealing containers).
    /// Because detached (FrameworkElement-derived) elements cannot re-enqueue (FindLayoutManager
    /// resolves no host), the drained snapshot is the only path that can layout them, so it must re-check
    /// connectivity per element AT PROCESSING TIME — a check at drain/sort time would miss
    /// mid-pass detaches, and memoizing per drain would go equally stale.
    ///
    /// Tree-top identity is deliberate, NOT "element reaches root": PopupWindow calls
    /// UpdateLayout with its content child as root, so live ancestors (the popup host itself)
    /// sit ABOVE root while PropagateInvalidMeasureUp queues them — they must keep processing.
    ///
    /// Skipped elements are skipped, never removed or revalidated: if one re-attaches,
    /// OnVisualParentChanged re-invalidates and re-enqueues it, so nothing is starved.
    /// </summary>
    private static bool IsInSameTreeAsRoot(UIElement element, UIElement root)
    {
        Visual top = element;
        while (top.VisualParent is { } parent)
            top = parent;

        Visual rootTop = root;
        while (rootTop.VisualParent is { } parent)
            rootTop = parent;

        return ReferenceEquals(top, rootTop);
    }
}
