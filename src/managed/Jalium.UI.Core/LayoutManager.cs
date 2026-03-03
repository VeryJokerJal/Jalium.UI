namespace Jalium.UI;

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

    /// <summary>
    /// Queues an element for re-measurement.
    /// Measure invalidation implies arrange invalidation (matching WPF behavior).
    /// </summary>
    public void InvalidateMeasure(UIElement element)
    {
        if (_measureQueue.Add(element))
        {
            _arrangeQueue.Add(element);
            PropagateInvalidMeasureUp(element);
        }
    }

    /// <summary>
    /// Queues an element for re-arrangement.
    /// </summary>
    public void InvalidateArrange(UIElement element)
    {
        if (_arrangeQueue.Add(element))
        {
            PropagateInvalidArrangeUp(element);
        }
    }

    /// <summary>
    /// Removes an element from all queues (e.g., when removed from tree).
    /// </summary>
    public void Remove(UIElement element)
    {
        _measureQueue.Remove(element);
        _arrangeQueue.Remove(element);
    }

    /// <summary>
    /// Gets whether there are any elements pending layout.
    /// </summary>
    public bool HasPendingLayout => _measureQueue.Count > 0 || _arrangeQueue.Count > 0;

    /// <summary>
    /// Processes all pending measure and arrange operations.
    /// Called by Window before rendering.
    /// </summary>
    /// <param name="root">The root element (Window).</param>
    /// <param name="availableSize">The available size for the root.</param>
    public void UpdateLayout(UIElement root, Size availableSize)
    {
        if (_isUpdating)
            return;

        _isUpdating = true;
        _layoutIterations = 0;

        try
        {
            // If queues are empty, do a full tree layout.
            if (_measureQueue.Count == 0 && _arrangeQueue.Count == 0)
            {
                root.Measure(availableSize);
                root.Arrange(new Rect(0, 0, availableSize.Width, availableSize.Height));
                return;
            }

            // Iterative layout: measure and arrange may trigger further invalidations.
            while ((_measureQueue.Count > 0 || _arrangeQueue.Count > 0)
                   && _layoutIterations < MaxLayoutIterations)
            {
                _layoutIterations++;

                // Process measure queue: sort by depth (shallowest first).
                if (_measureQueue.Count > 0)
                {
                    _measureSorted.Clear();
                    _measureSorted.AddRange(_measureQueue);
                    _measureQueue.Clear();

                    // Pre-compute depths for all elements before sorting
                    PrecomputeDepths(_measureSorted);
                    _measureSorted.Sort((a, b) => GetCachedDepth(a).CompareTo(GetCachedDepth(b)));

                    foreach (var element in _measureSorted)
                    {
                        if (!element.IsMeasureValid)
                        {
                            var measureSize = element == root
                                ? availableSize
                                : element.PreviousAvailableSize;

                            element.Measure(measureSize);
                        }
                    }
                }

                // Process arrange queue: sort by depth (shallowest first).
                if (_arrangeQueue.Count > 0)
                {
                    _arrangeSorted.Clear();
                    _arrangeSorted.AddRange(_arrangeQueue);
                    _arrangeQueue.Clear();

                    PrecomputeDepths(_arrangeSorted);
                    _arrangeSorted.Sort((a, b) => GetCachedDepth(a).CompareTo(GetCachedDepth(b)));

                    foreach (var element in _arrangeSorted)
                    {
                        if (!element.IsArrangeValid)
                        {
                            var rect = element == root
                                ? new Rect(0, 0, availableSize.Width, availableSize.Height)
                                : element.PreviousFinalRect;

                            element.Arrange(rect);
                        }
                    }
                }
            }

        }
        finally
        {
            _isUpdating = false;
            _depthCache.Clear();
        }
    }

    private void PropagateInvalidArrangeUp(UIElement element)
    {
        var parent = element.VisualParent as UIElement;
        while (parent != null)
        {
            if (parent.IsArrangeValid)
                parent.MarkArrangeInvalid();
            _arrangeQueue.Add(parent);

            parent = parent.VisualParent as UIElement;
        }
    }

    private void PropagateInvalidMeasureUp(UIElement element)
    {
        var parent = element.VisualParent as UIElement;
        while (parent != null)
        {
            if (parent.IsMeasureValid)
                parent.MarkMeasureInvalid();
            _measureQueue.Add(parent);
            _arrangeQueue.Add(parent);

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

    private int GetCachedDepth(Visual element)
    {
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
}
