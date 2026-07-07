using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Virtualization;

namespace Jalium.UI.Controls;

/// <summary>
/// Arranges and virtualizes content on a single line oriented horizontally or vertically.
/// Generates and recycles item containers based on the current viewport plus cache.
/// </summary>
public class VirtualizingStackPanel : VirtualizingPanel, IScrollInfo
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VirtualizingStackPanel),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Spacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(VirtualizingStackPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the dimension by which child elements are stacked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the uniform distance, in device-independent pixels, inserted between
    /// adjacent realized items along the stacking axis. Spacing is accounted for when
    /// sizing the extent, resolving scroll offsets, and laying out realized containers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
    }

    private double EffectiveSpacing
    {
        get
        {
            var value = Spacing;
            return (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;
        }
    }

    /// <summary>
    /// Returns the scroll-axis offset (including cumulative spacing) for the start of
    /// the item at <paramref name="index"/>.
    /// </summary>
    private double GetSpacedOffset(int index)
    {
        var baseOffset = _heightIndex.GetOffsetForIndex(index);
        var spacing = EffectiveSpacing;
        if (spacing <= 0 || index <= 0) return baseOffset;
        return baseOffset + index * spacing;
    }

    /// <summary>
    /// Returns the total scroll-axis extent including inter-item spacing.
    /// </summary>
    private double GetSpacedExtent()
    {
        var count = _heightIndex.Count;
        if (count <= 0) return 0;
        var spacing = EffectiveSpacing;
        return _heightIndex.TotalHeight + Math.Max(0, count - 1) * spacing;
    }

    /// <summary>
    /// Binary-search the item index whose visual band contains the given spaced offset.
    /// The inter-item gap (spacing band) after item i is attributed to item i.
    /// </summary>
    private int GetIndexAtSpacedOffset(double spacedOffset)
    {
        var spacing = EffectiveSpacing;
        if (spacing <= 0) return _heightIndex.GetIndexAtOffset(spacedOffset);

        var count = _heightIndex.Count;
        if (count <= 0) return -1;
        if (spacedOffset <= 0) return 0;
        if (spacedOffset >= GetSpacedExtent()) return count - 1;

        int lo = 0;
        int hi = count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            double midStart = _heightIndex.GetOffsetForIndex(mid) + mid * spacing;
            double nextStart = midStart + _heightIndex.GetHeightAt(mid);
            if (mid < count - 1) nextStart += spacing;

            if (spacedOffset < midStart) hi = mid - 1;
            else if (spacedOffset >= nextStart) lo = mid + 1;
            else return mid;
        }
        return Math.Clamp(lo, 0, count - 1);
    }

    /// <summary>
    /// Gets or sets the virtualization mode.
    /// </summary>
    /// <remarks>
    /// When this panel is the items host of a templated <see cref="ItemsControl"/>, the getter
    /// reflects the OWNING control's attached value (owner precedence). The panel-local setter is only
    /// honored for code-only panels with no owner.
    /// </remarks>
    public VirtualizationMode VirtualizationMode
    {
        get => GetVirtualizationMode(GetOwner());
        set => SetVirtualizationMode(this, value);
    }

    /// <summary>
    /// Gets or sets the scroll unit.
    /// </summary>
    /// <remarks>
    /// When this panel is the items host of a templated <see cref="ItemsControl"/>, the getter
    /// reflects the OWNING control's attached value (owner precedence). The panel-local setter is only
    /// honored for code-only panels with no owner.
    /// </remarks>
    public ScrollUnit ScrollUnit
    {
        get => GetScrollUnit(GetOwner());
        set => SetScrollUnit(this, value);
    }

    #endregion

    #region Private Fields

    private readonly SortedList<int, UIElement> _realizedContainers = new();
    private ItemHeightIndex _heightIndex = new(28);
    private RealizationWindow _currentWindow = RealizationWindow.Empty;

    // Two-offset model (T7): _requestedOffset is what the IScrollInfo caller asked for (kept sticky,
    // may be +Infinity for scroll-to-end); _computedOffset is the coerced value committed against the
    // live extent and surfaced through HorizontalOffset/VerticalOffset. The commit is SYNCHRONOUS so
    // Jalium's synchronous-pull ScrollViewer/StackPanel/ItemsPresenter read-back contract is preserved.
    private double _requestedOffset;
    private double _computedOffset;
    private Size _extent;
    private Size _viewport;
    private double _maxCrossAxis;
    private int _lastKnownItemCount = -1;

    // Reusable buffer to avoid allocations in RecycleOutsideWindow / DetachRealizedRange.
    private readonly List<int> _recycleBuffer = new();

    // Incremental-collection-change machinery (T6).
    private bool _measureInProgress;
    private bool _itemsChangedDuringMeasure;
    private bool _structureDirty;
    private readonly List<KeyValuePair<int, UIElement>> _shiftBuffer = new();

    #endregion

    #region IScrollInfo Implementation

    /// <summary>
    /// Gets or sets whether the panel can scroll horizontally.
    /// </summary>
    public bool CanHorizontallyScroll { get; set; }

    /// <summary>
    /// Gets or sets whether the panel can scroll vertically.
    /// </summary>
    public bool CanVerticallyScroll { get; set; }

    /// <summary>
    /// Gets the horizontal extent of the content.
    /// </summary>
    public double ExtentWidth => _extent.Width;

    /// <summary>
    /// Gets the vertical extent of the content.
    /// </summary>
    public double ExtentHeight => _extent.Height;

    /// <summary>
    /// Gets the horizontal viewport size.
    /// </summary>
    public double ViewportWidth => _viewport.Width;

    /// <summary>
    /// Gets the vertical viewport size.
    /// </summary>
    public double ViewportHeight => _viewport.Height;

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset => Orientation == Orientation.Horizontal ? _computedOffset : 0;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset => Orientation == Orientation.Vertical ? _computedOffset : 0;

    /// <summary>
    /// Gets or sets the scroll owner.
    /// </summary>
    public ScrollViewer? ScrollOwner { get; set; }

    /// <summary>
    /// Scrolls up by one line.
    /// </summary>
    public void LineUp()
    {
        if (Orientation == Orientation.Vertical)
        {
            ScrollByLine(-1);
        }
        else
        {
            SetHorizontalOffset(_computedOffset - GetAverageItemSize());
        }
    }

    /// <summary>
    /// Scrolls down by one line.
    /// </summary>
    public void LineDown()
    {
        if (Orientation == Orientation.Vertical)
        {
            ScrollByLine(1);
        }
        else
        {
            SetHorizontalOffset(_computedOffset + GetAverageItemSize());
        }
    }

    /// <summary>
    /// Scrolls left by one line.
    /// </summary>
    public void LineLeft()
    {
        if (Orientation == Orientation.Horizontal)
        {
            ScrollByLine(-1);
        }
        else
        {
            SetVerticalOffset(_computedOffset - GetAverageItemSize());
        }
    }

    /// <summary>
    /// Scrolls right by one line.
    /// </summary>
    public void LineRight()
    {
        if (Orientation == Orientation.Horizontal)
        {
            ScrollByLine(1);
        }
        else
        {
            SetVerticalOffset(_computedOffset + GetAverageItemSize());
        }
    }

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    public void PageUp() => SetOffset(_computedOffset - GetViewportAxisSize());

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    public void PageDown() => SetOffset(_computedOffset + GetViewportAxisSize());

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    public void PageLeft() => SetOffset(_computedOffset - GetViewportAxisSize());

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    public void PageRight() => SetOffset(_computedOffset + GetViewportAxisSize());

    /// <summary>
    /// Handles mouse wheel scrolling.
    /// </summary>
    public void MouseWheelUp() => LineUp();

    /// <summary>
    /// Handles mouse wheel scrolling.
    /// </summary>
    public void MouseWheelDown() => LineDown();

    /// <summary>
    /// Handles mouse wheel scrolling.
    /// </summary>
    public void MouseWheelLeft() => LineLeft();

    /// <summary>
    /// Handles mouse wheel scrolling.
    /// </summary>
    public void MouseWheelRight() => LineRight();

    /// <summary>
    /// Sets the horizontal scroll offset.
    /// </summary>
    public void SetHorizontalOffset(double offset)
    {
        if (Orientation != Orientation.Horizontal)
        {
            return;
        }

        SetOffset(offset);
    }

    /// <summary>
    /// Sets the vertical scroll offset.
    /// </summary>
    public void SetVerticalOffset(double offset)
    {
        if (Orientation != Orientation.Vertical)
        {
            return;
        }

        SetOffset(offset);
    }

    /// <summary>
    /// Makes the specified visual visible.
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (ItemContainerGenerator != null && visual is DependencyObject container)
        {
            var index = ItemContainerGenerator.IndexFromContainer(container);
            if (index >= 0)
            {
                BringIndexIntoView(index);
            }
        }

        return rectangle;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!ShouldVirtualize())
        {
            return MeasureNonVirtualized(availableSize);
        }

        _measureInProgress = true;
        try
        {
            _itemsChangedDuringMeasure = false;
            var result = MeasureVirtualizedPass(availableSize);

            // A reentrant structural mutation (e.g. a binding side-effect in PrepareContainerForItem
            // that edited the collection) was deferred during the pass above. Re-derive once from the
            // post-mutation generator state. Hard cap of one retry to avoid hangs if a prepare
            // side-effect mutates the collection on every pass.
            if (_itemsChangedDuringMeasure)
            {
                _itemsChangedDuringMeasure = false;

                // The deferred change left the realized map + height index keyed by PRE-mutation
                // indices (OnItemsChanged returned early without touching them). RealizeContainer
                // hands back a cached container for an already-present index WITHOUT consulting the
                // generator, and EnsureHeightIndex only grows the tail — so a retry over that stale
                // state would keep inserted/removed/moved items in their old slots until a later full
                // reset. Reset to a clean baseline first so the retry re-realizes every index from the
                // post-mutation generator. (Reentrant structural mutation is rare, so the full
                // re-realize on this path is an acceptable cost for correctness.)
                ResetVirtualizationState(GetItemCount());
                result = MeasureVirtualizedPass(availableSize);
            }

            return result;
        }
        finally
        {
            _measureInProgress = false;
        }
    }

    private Size MeasureVirtualizedPass(Size availableSize)
    {
        var itemCount = GetItemCount();
        EnsureHeightIndex(itemCount);

        _viewport = CoerceViewport(availableSize);
        var viewportAxisSize = GetAxisSize(_viewport);

        if (itemCount == 0)
        {
            ClearRealizedContainers(recycle: true);
            _currentWindow = RealizationWindow.Empty;
            _structureDirty = false;
            UpdateExtent(itemCount, availableSize);
            return FinishMeasurePass(availableSize);
        }

        var owner = GetOwner();
        var cache = GetCacheLength(owner);
        var cacheUnit = GetCacheLengthUnit(owner);
        var cacheBefore = ToCachePixels(cache.CacheBeforeViewport, cacheUnit, viewportAxisSize);
        var cacheAfter = ToCachePixels(cache.CacheAfterViewport, cacheUnit, viewportAxisSize);

        // Re-resolve the requested offset (which may be the +Infinity scroll-to-end sentinel) against
        // the live extent for this pass's window math, then commit it.
        _computedOffset = CoerceOffset(_requestedOffset);
        var windowStartOffset = Math.Max(0, _computedOffset - cacheBefore);
        var windowEndOffset = _computedOffset + viewportAxisSize + cacheAfter;

        var startIndex = Math.Max(0, GetIndexAtSpacedOffset(windowStartOffset));
        var endIndex = Math.Min(itemCount - 1,
            Math.Max(startIndex, GetIndexAtSpacedOffset(windowEndOffset)));

        var newWindow = endIndex >= startIndex
            ? new RealizationWindow(startIndex, endIndex)
            : RealizationWindow.Empty;

        // Fast path: if the realization window hasn't changed AND no structural mutation occurred,
        // skip re-realize and recycle. The _structureDirty guard closes the "hole" bug: after an
        // in-window insert the recomputed window can numerically equal _currentWindow, and the fast
        // path (which only re-measures already-realized children) would otherwise never realize the
        // inserted index, leaving a blank gap. We must still re-measure any child whose measure was
        // invalidated (e.g. a TreeViewItem that expanded/collapsed) so height offsets stay correct.
        if (!_structureDirty && newWindow.Equals(_currentWindow) && _realizedContainers.Count > 0)
        {
            var anyHeightChanged = false;
            for (int i = 0; i < _realizedContainers.Count; i++)
            {
                var child = _realizedContainers.Values[i];
                if (!child.IsMeasureValid)
                {
                    var childAvailable = Orientation == Orientation.Vertical
                        ? new Size(availableSize.Width, double.PositiveInfinity)
                        : new Size(double.PositiveInfinity, availableSize.Height);
                    child.Measure(childAvailable);
                    var axis = GetAxisSize(child.DesiredSize);
                    var idx = _realizedContainers.Keys[i];
                    var oldHeight = _heightIndex.GetHeightAt(idx);
                    if (Math.Abs(axis - oldHeight) > 0.5)
                    {
                        _heightIndex.SetMeasuredHeight(idx, axis);
                        anyHeightChanged = true;
                    }
                }
            }

            UpdateExtent(itemCount, availableSize);

            if (!anyHeightChanged)
            {
                return FinishMeasurePass(availableSize);
            }

            // Heights changed — the realization window boundaries may have shifted,
            // so fall through to the full realization path below.
        }

        endIndex = RealizeWindow(startIndex, windowEndOffset, availableSize);
        _currentWindow = endIndex >= startIndex
            ? new RealizationWindow(startIndex, endIndex)
            : RealizationWindow.Empty;

        RecycleOutsideWindow(_currentWindow);
        _structureDirty = false;
        UpdateExtent(itemCount, availableSize);

        return FinishMeasurePass(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!ShouldVirtualize())
        {
            return ArrangeNonVirtualized(finalSize);
        }

        _viewport = CoerceViewport(finalSize);
        _computedOffset = CoerceOffset(_computedOffset);

        // SortedList iterates in key order — no allocation needed
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            var child = _realizedContainers.Values[i];
            var itemOffset = GetSpacedOffset(index) - _computedOffset;
            var itemExtent = _heightIndex.GetHeightAt(index);

            if (Orientation == Orientation.Vertical)
            {
                child.Arrange(new Rect(0, itemOffset, finalSize.Width, itemExtent));
            }
            else
            {
                child.Arrange(new Rect(itemOffset, 0, itemExtent, finalSize.Height));
            }

            child.Visibility = Visibility.Visible;
        }

        UpdateExtent(GetItemCount(), finalSize);
        return finalSize;
    }

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        var bounds = VisualBounds;
        if (!bounds.Contains(point) || Visibility != Visibility.Visible)
        {
            return null;
        }

        var localPoint = new Point(point.X - bounds.X, point.Y - bounds.Y);

        // Respect the layout clip the renderer applies — realized containers may
        // have VisualBounds that extend past the panel viewport along the scroll
        // axis, so a permissive hit-test would otherwise select an item that was
        // clipped away.
        if (!IsPointInsideLayoutClip(localPoint))
        {
            return null;
        }

        if (_realizedContainers.Count > 0)
        {
            var axisOffset = Orientation == Orientation.Vertical
                ? localPoint.Y + _computedOffset
                : localPoint.X + _computedOffset;

            var estimatedIndex = GetIndexAtSpacedOffset(axisOffset);
            int neighborChecks = 0;
            if (estimatedIndex >= 0)
            {
                if (TryHitRealizedContainer(estimatedIndex, localPoint, out var directHit))
                {
                    return directHit;
                }

                for (int delta = 1; delta <= 2; delta++)
                {
                    neighborChecks++;
                    if (TryHitRealizedContainer(estimatedIndex - delta, localPoint, out var beforeHit))
                    {
                        return beforeHit;
                    }

                    neighborChecks++;
                    if (TryHitRealizedContainer(estimatedIndex + delta, localPoint, out var afterHit))
                    {
                        return afterHit;
                    }
                }
            }
        }

        return base.HitTestCore(point);
    }

    #endregion

    #region Virtualization Support

    /// <inheritdoc />
    protected override void BringIndexIntoViewOverride(int index)
    {
        var itemCount = GetItemCount();
        if (index < 0 || index >= itemCount)
        {
            return;
        }

        var itemStart = GetSpacedOffset(index);
        var itemEnd = itemStart + _heightIndex.GetHeightAt(index);
        var viewportAxis = GetViewportAxisSize();
        if (viewportAxis <= 0)
        {
            SetOffset(itemStart);
            return;
        }

        if (itemStart < _computedOffset)
        {
            SetOffset(itemStart);
        }
        else if (itemEnd > _computedOffset + viewportAxis)
        {
            SetOffset(itemEnd - viewportAxis);
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        // Reentrant structural mutation during measure (e.g. a binding side-effect in
        // PrepareContainerForItem editing the collection): do NOT mutate the height index / realized
        // map under the in-flight realize loop. Defer to the bounded restart in MeasureOverride.
        if (_measureInProgress)
        {
            _itemsChangedDuringMeasure = true;
            _structureDirty = true;
            InvalidateMeasure();
            return;
        }

        var itemCount = GetItemCount();

        // The first structural event (before any measure established the count) initializes cleanly
        // via the full reset path — the incremental primitives assume an established index.
        if (_lastKnownItemCount < 0)
        {
            ResetVirtualizationState(itemCount);
            InvalidateMeasure();
            return;
        }

        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
            {
                var idx = args.ItemIndex;
                var n = args.ItemCount;
                if (idx < 0 || n <= 0)
                {
                    ResetVirtualizationState(itemCount);
                    break;
                }

                _heightIndex.InsertRange(idx, n);
                ShiftRealizedKeys(idx, n);
                if (!_currentWindow.IsEmpty)
                {
                    if (idx <= _currentWindow.StartIndex)
                    {
                        _currentWindow = new RealizationWindow(_currentWindow.StartIndex + n, _currentWindow.EndIndex + n);
                    }
                    else if (idx <= _currentWindow.EndIndex)
                    {
                        _currentWindow = RealizationWindow.Empty;
                    }

                    // idx > EndIndex: window unchanged.
                }

                _lastKnownItemCount = itemCount;
                _structureDirty = true;
                break;
            }

            case NotifyCollectionChangedAction.Remove:
            {
                var idx = args.ItemIndex;
                var n = args.ItemCount;
                if (idx < 0 || n <= 0)
                {
                    ResetVirtualizationState(itemCount);
                    break;
                }

                // The generator already recycled the removed containers; drop the panel's visuals +
                // bookkeeping for [idx, idx+n) (do NOT recycle again), then shift the trailing keys.
                DetachRealizedRange(idx, n);
                _heightIndex.RemoveRange(idx, n);
                ShiftRealizedKeys(idx + n, -n);
                if (!_currentWindow.IsEmpty)
                {
                    if (idx + n <= _currentWindow.StartIndex)
                    {
                        _currentWindow = new RealizationWindow(_currentWindow.StartIndex - n, _currentWindow.EndIndex - n);
                    }
                    else if (idx > _currentWindow.EndIndex)
                    {
                        // window entirely after the removal: unchanged.
                    }
                    else
                    {
                        _currentWindow = RealizationWindow.Empty;
                    }
                }

                _lastKnownItemCount = itemCount;
                _structureDirty = true;
                break;
            }

            case NotifyCollectionChangedAction.Replace:
            {
                var idx = args.ItemIndex;
                var n = args.ItemCount;
                if (idx < 0 || n <= 0)
                {
                    ResetVirtualizationState(itemCount);
                    break;
                }

                // The generator already recycled the old containers; drop the panel's visual +
                // bookkeeping so the next measure re-realizes with the new item. The item count is
                // unchanged, so heights stay and re-measure on realize; window stays put.
                DetachRealizedRange(idx, n);
                _lastKnownItemCount = itemCount;
                _structureDirty = true;
                break;
            }

            case NotifyCollectionChangedAction.Move:
            {
                var oldIdx = args.OldItemIndex;
                var newIdx = args.ItemIndex;
                var n = args.ItemCount;
                if (oldIdx < 0 || newIdx < 0 || n <= 0 || oldIdx == newIdx)
                {
                    ResetVirtualizationState(itemCount);
                    break;
                }

                var lo = Math.Min(oldIdx, newIdx);
                var hi = Math.Max(oldIdx, newIdx) + n;
                DetachRealizedRange(lo, hi - lo);
                _heightIndex.MoveRange(oldIdx, newIdx, n);
                if (!_currentWindow.IsEmpty && !(hi <= _currentWindow.StartIndex || lo > _currentWindow.EndIndex))
                {
                    _currentWindow = RealizationWindow.Empty;
                }

                _lastKnownItemCount = itemCount;
                _structureDirty = true;
                break;
            }

            default:
                ResetVirtualizationState(itemCount);
                break;
        }

        InvalidateMeasure();
    }

    private void ResetVirtualizationState(int itemCount)
    {
        _heightIndex.Reset(itemCount, _heightIndex.EstimatedHeight);
        ClearRealizedContainers(recycle: true);
        _currentWindow = RealizationWindow.Empty;
        _lastKnownItemCount = itemCount;
        _structureDirty = true;
    }

    /// <summary>
    /// Removes the realized container (visual child + bookkeeping) for each key in
    /// [<paramref name="start"/>, start+count). The generator has already recycled these entries, so
    /// this only drops the panel's view of them; the next measure re-realizes. Visuals are detached
    /// BEFORE any key shift so Children stays ascending-by-key.
    /// </summary>
    private void DetachRealizedRange(int start, int count)
    {
        if (_realizedContainers.Count == 0 || count <= 0)
        {
            return;
        }

        _recycleBuffer.Clear();
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var key = _realizedContainers.Keys[i];
            if (key >= start && key < start + count)
            {
                _recycleBuffer.Add(key);
            }
        }

        for (int i = 0; i < _recycleBuffer.Count; i++)
        {
            var key = _recycleBuffer[i];
            var child = _realizedContainers[key];
            var visualIndex = Children.IndexOf(child);
            if (visualIndex >= 0)
            {
                RemoveInternalChildRange(visualIndex, 1);
            }

            _realizedContainers.Remove(key);
        }
    }

    /// <summary>
    /// Shifts every realized key &gt;= <paramref name="fromIndex"/> by <paramref name="delta"/>,
    /// preserving the container objects. Does NOT touch Children: a uniform shift of a contiguous
    /// suffix preserves ascending order, matching the generator's own _realizedItems shift so the
    /// panel keys stay equal to the generator keys.
    /// </summary>
    private void ShiftRealizedKeys(int fromIndex, int delta)
    {
        if (_realizedContainers.Count == 0 || delta == 0)
        {
            return;
        }

        _shiftBuffer.Clear();
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var key = _realizedContainers.Keys[i];
            if (key >= fromIndex)
            {
                _shiftBuffer.Add(new KeyValuePair<int, UIElement>(key, _realizedContainers.Values[i]));
            }
        }

        for (int i = 0; i < _shiftBuffer.Count; i++)
        {
            _realizedContainers.Remove(_shiftBuffer[i].Key);
        }

        for (int i = 0; i < _shiftBuffer.Count; i++)
        {
            _realizedContainers[_shiftBuffer[i].Key + delta] = _shiftBuffer[i].Value;
        }
    }

    /// <inheritdoc />
    internal override void OnClearChildren()
    {
        base.OnClearChildren();
        _realizedContainers.Clear();
        _currentWindow = RealizationWindow.Empty;
    }

    #endregion

    #region Internal Helpers

    private bool ShouldVirtualize()
    {
        return GetIsVirtualizing(GetOwner()) && ItemContainerGenerator != null;
    }

    private int GetItemCount()
    {
        return ItemContainerGenerator?.ItemCount ?? Children.Count;
    }

    private void EnsureHeightIndex(int itemCount)
    {
        if (_lastKnownItemCount == itemCount)
        {
            return;
        }

        if (_lastKnownItemCount < 0)
        {
            _heightIndex.Reset(itemCount, _heightIndex.EstimatedHeight);
        }
        else
        {
            _heightIndex.EnsureCount(itemCount);
        }

        _lastKnownItemCount = itemCount;
    }

    private int RealizeWindow(int startIndex, double windowEndOffset, Size availableSize)
    {
        _maxCrossAxis = 0;
        var endIndex = startIndex - 1;
        var index = startIndex;
        var currentOffset = GetSpacedOffset(startIndex);

        while (index < _heightIndex.Count && currentOffset <= windowEndOffset)
        {
            var child = RealizeContainer(index);
            if (child == null)
            {
                break;
            }

            var childAvailable = Orientation == Orientation.Vertical
                ? new Size(availableSize.Width, double.PositiveInfinity)
                : new Size(double.PositiveInfinity, availableSize.Height);

            child.Measure(childAvailable);
            var axis = GetAxisSize(child.DesiredSize);
            var cross = GetCrossAxisSize(child.DesiredSize);
            _heightIndex.SetMeasuredHeight(index, axis);
            _maxCrossAxis = Math.Max(_maxCrossAxis, cross);

            currentOffset = GetSpacedOffset(index + 1);
            endIndex = index;
            index++;
        }

        return endIndex;
    }

    private UIElement? RealizeContainer(int index)
    {
        if (_realizedContainers.TryGetValue(index, out var existing))
        {
            return existing;
        }

        if (ItemContainerGenerator == null)
        {
            return null;
        }

        var container = ItemContainerGenerator.GetOrCreateContainerForIndex(index, out var isNewlyRealized);
        if (container is not UIElement child)
        {
            return null;
        }

        if (isNewlyRealized)
        {
            ItemContainerGenerator.PrepareItemContainer(container);
        }

        _realizedContainers[index] = child;

        // SortedList.IndexOfKey gives us the sorted position directly — O(log n)
        var insertPosition = _realizedContainers.IndexOfKey(index);

        if (Children.IndexOf(child) < 0)
        {
            if (insertPosition >= Children.Count)
            {
                AddInternalChild(child);
            }
            else
            {
                InsertInternalChild(insertPosition, child);
            }
        }

        return child;
    }

    private bool TryHitRealizedContainer(int index, Point localPoint, out HitTestResult? hitResult)
    {
        hitResult = null;
        if (!_realizedContainers.TryGetValue(index, out var child) || child.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (!child.VisualBounds.Contains(localPoint))
        {
            return false;
        }

        if (child is FrameworkElement frameworkElement)
        {
            hitResult = frameworkElement.HitTest(localPoint);
            return hitResult != null;
        }

        return false;
    }

    private void RecycleOutsideWindow(RealizationWindow window)
    {
        if (_realizedContainers.Count == 0)
        {
            return;
        }

        // Collect indices to recycle using a reusable buffer — zero allocations.
        // Keep-alive: a container marked IsContainerVirtualizable=false is never virtualized away,
        // even when it scrolls out of the realization window (it stays realized + arranged off-screen).
        // This is the prerequisite hook for anchored scrolling, focus retention, and DataGrid edit rows.
        _recycleBuffer.Clear();
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            if (!window.Contains(index) && GetIsContainerVirtualizable(_realizedContainers.Values[i]))
            {
                _recycleBuffer.Add(index);
            }
        }

        var isRecycling = VirtualizationMode == VirtualizationMode.Recycling;
        for (int i = 0; i < _recycleBuffer.Count; i++)
        {
            var index = _recycleBuffer[i];
            var child = _realizedContainers[index];

            // SortedList keeps children in order — the visual index matches the sorted position
            // before removal, so find it by position rather than scanning Children
            var visualIndex = Children.IndexOf(child);
            if (visualIndex >= 0)
            {
                RemoveInternalChildRange(visualIndex, 1);
            }

            _realizedContainers.Remove(index);

            if (ItemContainerGenerator != null)
            {
                if (isRecycling)
                {
                    ItemContainerGenerator.RecycleIndex(index);
                }
                else
                {
                    ItemContainerGenerator.RemoveIndex(index);
                }
            }
        }
    }

    private void ClearRealizedContainers(bool recycle)
    {
        if (_realizedContainers.Count == 0)
        {
            Children.Clear();
            return;
        }

        var isRecycling = recycle && VirtualizationMode == VirtualizationMode.Recycling;
        for (int i = _realizedContainers.Count - 1; i >= 0; i--)
        {
            var index = _realizedContainers.Keys[i];

            if (ItemContainerGenerator != null)
            {
                if (isRecycling)
                {
                    ItemContainerGenerator.RecycleIndex(index);
                }
                else
                {
                    ItemContainerGenerator.RemoveIndex(index);
                }
            }
        }

        _realizedContainers.Clear();
        Children.Clear();
    }

    private void ScrollByLine(int direction)
    {
        if (ScrollUnit != ScrollUnit.Item)
        {
            SetOffset(_computedOffset + direction * GetAverageItemSize());
            return;
        }

        var current = GetIndexAtSpacedOffset(_computedOffset);
        var target = Math.Clamp(current + direction, 0, Math.Max(0, _heightIndex.Count - 1));
        SetOffset(GetSpacedOffset(target));
    }

    private void SetOffset(double offset)
    {
        if (double.IsNaN(offset))
        {
            return;
        }

        // Floor negatives; preserve +Infinity (the scroll-to-end sentinel).
        var requested = offset < 0 ? 0 : offset;
        var coerced = CoerceOffset(requested);

        if (_requestedOffset == requested && Math.Abs(coerced - _computedOffset) <= 0.01)
        {
            return;
        }

        // Keep _requestedOffset sticky (may be +Infinity) so a later extent growth re-resolves it.
        _requestedOffset = requested;

        // COMMIT synchronously so HorizontalOffset/VerticalOffset read back the coerced value in the
        // same call — this preserves Jalium's synchronous-pull ScrollViewer / smooth-scroll / inertia
        // / drag-coalesce contract (which all read the offset immediately after the setter).
        if (Math.Abs(coerced - _computedOffset) > 0.01)
        {
            _computedOffset = coerced;
        }

        InvalidateMeasure();
    }

    private double CoerceOffset(double offset)
    {
        // Use the LIVE extent (not the _extent field, which UpdateExtent only refreshes at the END of
        // MeasureOverride). On the virtualized path that is GetSpacedExtent() (reflects count changes
        // immediately — e.g. tree Collapse All); on the non-virtualized path the height index is
        // empty, so clamp against the measured _extent instead.
        var maxOffset = Math.Max(0, GetExtentForCoerce() - GetViewportAxisSize());
        if (double.IsNaN(offset))
        {
            return 0;
        }

        // +Infinity is the scroll-to-end sentinel; Math.Clamp resolves it to maxOffset.
        return Math.Clamp(offset, 0, maxOffset);
    }

    private double GetExtentForCoerce() =>
        ShouldVirtualize() ? GetSpacedExtent() : GetAxisSize(_extent);

    /// <summary>
    /// Commits the desired size for a measure pass. First re-coerces <see cref="_computedOffset"/>
    /// against the final (post-realization) extent so an extent shift during this pass (count change,
    /// average-size convergence, or a +Infinity scroll-to-end resolving as more rows realize) is
    /// reflected; if that changes the committed offset without a setter call, the scroll owner is
    /// notified so its thumb / ScrollChanged stay in sync.
    /// </summary>
    private Size FinishMeasurePass(Size availableSize)
    {
        CommitComputedOffset(_requestedOffset);
        return CoerceDesiredSize(availableSize);
    }

    private void CommitComputedOffset(double requested)
    {
        var coerced = CoerceOffset(requested);
        if (Math.Abs(coerced - _computedOffset) > 0.01)
        {
            _computedOffset = coerced;
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    private void UpdateExtent(int itemCount, Size availableSize)
    {
        var axisExtent = itemCount <= 0 ? 0 : GetSpacedExtent();
        var crossExtent = _maxCrossAxis;
        if (Orientation == Orientation.Vertical)
        {
            var width = double.IsInfinity(availableSize.Width) ? crossExtent : Math.Max(crossExtent, availableSize.Width);
            _extent = new Size(width, axisExtent);
        }
        else
        {
            var height = double.IsInfinity(availableSize.Height) ? crossExtent : Math.Max(crossExtent, availableSize.Height);
            _extent = new Size(axisExtent, height);
        }
    }

    private Size CoerceViewport(Size availableSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            var width = CoerceFinite(availableSize.Width, _viewport.Width);
            var height = CoerceFinite(availableSize.Height, _viewport.Height);
            return new Size(width, height);
        }

        return new Size(
            CoerceFinite(availableSize.Width, _viewport.Width),
            CoerceFinite(availableSize.Height, _viewport.Height));
    }

    private static double CoerceFinite(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback > 0 ? fallback : 0;
        }

        return Math.Max(0, value);
    }

    private Size CoerceDesiredSize(Size availableSize)
    {
        var axis = Math.Min(GetAxisSize(_extent), GetAxisSize(_viewport));
        var cross = _maxCrossAxis;
        if (Orientation == Orientation.Vertical)
        {
            var width = double.IsInfinity(availableSize.Width) ? cross : Math.Min(cross, availableSize.Width);
            return new Size(width, axis);
        }

        var height = double.IsInfinity(availableSize.Height) ? cross : Math.Min(cross, availableSize.Height);
        return new Size(axis, height);
    }

    private double GetAverageItemSize()
    {
        var average = _heightIndex.EstimatedHeight;
        return average > 0 ? average : 24;
    }

    private double GetViewportAxisSize()
    {
        var viewportAxis = GetAxisSize(_viewport);
        return viewportAxis > 0 ? viewportAxis : 0;
    }

    private double ToCachePixels(double cacheValue, VirtualizationCacheLengthUnit unit, double viewportAxisSize)
    {
        if (cacheValue <= 0)
        {
            return 0;
        }

        return unit switch
        {
            VirtualizationCacheLengthUnit.Pixel => cacheValue,
            VirtualizationCacheLengthUnit.Item => cacheValue * GetAverageItemSize(),
            VirtualizationCacheLengthUnit.Page => cacheValue * viewportAxisSize,
            _ => cacheValue
        };
    }

    private double GetAxisSize(Size size) => Orientation == Orientation.Vertical ? size.Height : size.Width;

    private double GetCrossAxisSize(Size size) => Orientation == Orientation.Vertical ? size.Width : size.Height;

    private Size MeasureNonVirtualized(Size availableSize)
    {
        var spacing = EffectiveSpacing;
        double axis = 0;
        double cross = 0;
        bool sawVisible = false;
        foreach (UIElement child in Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(availableSize);
            if (sawVisible) axis += spacing;
            axis += GetAxisSize(child.DesiredSize);
            cross = Math.Max(cross, GetCrossAxisSize(child.DesiredSize));
            sawVisible = true;
        }

        if (Orientation == Orientation.Vertical)
        {
            _extent = new Size(cross, axis);
            _viewport = CoerceViewport(availableSize);
            _computedOffset = CoerceOffset(_requestedOffset);
            return new Size(cross, Math.Min(axis, availableSize.Height));
        }

        _extent = new Size(axis, cross);
        _viewport = CoerceViewport(availableSize);
        _computedOffset = CoerceOffset(_requestedOffset);
        return new Size(Math.Min(axis, availableSize.Width), cross);
    }

    private Size ArrangeNonVirtualized(Size finalSize)
    {
        var spacing = EffectiveSpacing;
        var offset = -_computedOffset;
        bool sawVisible = false;
        foreach (UIElement child in Children)
        {
            if (sawVisible) offset += spacing;
            var axisExtent = GetAxisSize(child.DesiredSize);
            if (Orientation == Orientation.Vertical)
            {
                child.Arrange(new Rect(0, offset, finalSize.Width, axisExtent));
            }
            else
            {
                child.Arrange(new Rect(offset, 0, axisExtent, finalSize.Height));
            }

            offset += axisExtent;
            sawVisible = true;
        }

        return finalSize;
    }

    #endregion

    #region Property Changed

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizingStackPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion
}

// VirtualizationMode and ScrollUnit enums are defined in VirtualizingPanel.cs

#region Supporting Types

/// <summary>
/// Provides scroll information for panels.
/// </summary>
public interface IScrollInfo
{
    /// <summary>
    /// Gets or sets whether the panel can scroll horizontally.
    /// </summary>
    bool CanHorizontallyScroll { get; set; }

    /// <summary>
    /// Gets or sets whether the panel can scroll vertically.
    /// </summary>
    bool CanVerticallyScroll { get; set; }

    /// <summary>
    /// Gets the horizontal extent.
    /// </summary>
    double ExtentWidth { get; }

    /// <summary>
    /// Gets the vertical extent.
    /// </summary>
    double ExtentHeight { get; }

    /// <summary>
    /// Gets the horizontal viewport size.
    /// </summary>
    double ViewportWidth { get; }

    /// <summary>
    /// Gets the vertical viewport size.
    /// </summary>
    double ViewportHeight { get; }

    /// <summary>
    /// Gets the horizontal offset.
    /// </summary>
    double HorizontalOffset { get; }

    /// <summary>
    /// Gets the vertical offset.
    /// </summary>
    double VerticalOffset { get; }

    /// <summary>
    /// Gets or sets the scroll owner.
    /// </summary>
    ScrollViewer? ScrollOwner { get; set; }

    /// <summary>
    /// Scrolls up by one line.
    /// </summary>
    void LineUp();

    /// <summary>
    /// Scrolls down by one line.
    /// </summary>
    void LineDown();

    /// <summary>
    /// Scrolls left by one line.
    /// </summary>
    void LineLeft();

    /// <summary>
    /// Scrolls right by one line.
    /// </summary>
    void LineRight();

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    void PageUp();

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    void PageDown();

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    void PageLeft();

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    void PageRight();

    /// <summary>
    /// Handles mouse wheel up.
    /// </summary>
    void MouseWheelUp();

    /// <summary>
    /// Handles mouse wheel down.
    /// </summary>
    void MouseWheelDown();

    /// <summary>
    /// Handles mouse wheel left.
    /// </summary>
    void MouseWheelLeft();

    /// <summary>
    /// Handles mouse wheel right.
    /// </summary>
    void MouseWheelRight();

    /// <summary>
    /// Sets the horizontal offset.
    /// </summary>
    void SetHorizontalOffset(double offset);

    /// <summary>
    /// Sets the vertical offset.
    /// </summary>
    void SetVerticalOffset(double offset);

    /// <summary>
    /// Makes a visual visible.
    /// </summary>
    Rect MakeVisible(Visual visual, Rect rectangle);
}

#endregion
