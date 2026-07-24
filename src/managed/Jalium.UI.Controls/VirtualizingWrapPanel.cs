using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Virtualization;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// 在网格中按 wrap 方式排列 item 的虚拟化面板,只 realize 可视行 + cache 容器。
///
/// 设计:同 <see cref="VirtualizingStackPanel"/> 一样继承 <see cref="VirtualizingPanel"/>
/// + 实现 <see cref="IScrollInfo"/>。区别在于 wrap 后**每行多列**:
/// firstVisibleRow / lastVisibleRow 从 scrollOffset / ItemHeight 算,行内列 itemsPerRow
/// = floor(viewportWidth / ItemWidth)。
///
/// **简化约束**:本实现假设所有 item **同尺寸**。如果 <c>ItemWidth</c>/<c>ItemHeight</c>
/// 显式设置(WrapPanel 共享属性)则用之;否则从第一个 realize 出的 item DesiredSize 推断,
/// 后续所有 item 假设同 size。该约束覆盖 99% 的 wrap 网格场景(template card grid)。
/// 真正 item 尺寸不一时,fallback 到 non-virtualizing 渲染。
///
/// 详见 memory project_virtualizing_wrap_panel.md。
/// </summary>
public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    #region Dependency Properties

    /// <summary>Identifies the Orientation dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>Identifies the ItemWidth dependency property (NaN = auto-size).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>Identifies the ItemHeight dependency property (NaN = auto-size).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>Identifies the horizontal spacing between item cells.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>Identifies the vertical spacing between item cells.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(VirtualizingWrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>Orientation of the layout flow. Horizontal = wrap rows (vertical scroll).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Fixed item width; NaN = derived from the first realized item.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty)!;
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>Fixed item height; NaN = derived from the first realized item.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty)!;
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>Gets or sets the horizontal gap between item cells.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty)!;
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Gets or sets the vertical gap between item cells.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty)!;
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>Virtualization mode (Standard / Recycling).</summary>
    public VirtualizationMode VirtualizationMode
    {
        get => GetVirtualizationMode(GetOwner());
        set => SetVirtualizationMode(this, value);
    }

    #endregion

    #region Private Fields

    private readonly SortedList<int, UIElement> _realizedContainers = new();
    private double _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private double _itemWidth;     // resolved (DP value or first-item DesiredSize)
    private double _itemHeight;    // resolved
    private bool _hasResolvedAutoItemSize;
    private int _itemsPerRow;
    // The cross-axis size from the last completed Arrange is the authoritative viewport.
    // Parent grids can issue speculative, much wider Measure probes while their columns are
    // being resolved; accepting those probes here changes the wrap count and collapses the
    // scroll extent in the middle of an active drag.
    private double _arrangedCrossAxis;
    // A persistent ScrollViewer gutter can Measure at one width and Arrange at another. If
    // those widths straddle a wrap threshold, remember the exact pair so the next identical
    // probe uses the committed Arrange width and the layout converges instead of invalidating
    // Measure forever. A different probe is still accepted, which keeps real window shrinking
    // responsive even when an outer non-IScrollInfo ScrollViewer has a stale content extent.
    private double _measureProbeCrossAxis = double.NaN;
    private double _correctiveMeasureCrossAxis = double.NaN;
    private double _correctiveArrangeCrossAxis = double.NaN;
    private readonly List<int> _recycleBuffer = new();

    // Per-frame realize budget — large scroll jumps (dragging the thumb,
    // wheel bursts spanning many rows, PageUp/PageDown) used to realize the
    // entire cache window in a single measure pass. With 30+ container per
    // pass at ~3–5 ms each (template instantiation + first-time native
    // bitmap upload), frame budget blew up. Cap per-frame realize count and
    // defer the rest to the next Dispatcher turn via
    // <see cref="_deferredCatchUpPending"/>. Small scrolls fit under budget
    // and behave as before.
    private const int MaxRealizesPerFrame = 6;
    private bool _deferredCatchUpPending;

    #endregion

    #region IScrollInfo Implementation

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth  => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth  => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => Orientation == Orientation.Vertical ? _scrollOffset : 0;
    public double VerticalOffset   => Orientation == Orientation.Horizontal ? _scrollOffset : 0;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp()    { if (Orientation == Orientation.Horizontal) SetOffset(_scrollOffset - LineSize); }
    public void LineDown()  { if (Orientation == Orientation.Horizontal) SetOffset(_scrollOffset + LineSize); }
    public void LineLeft()  { if (Orientation == Orientation.Vertical)   SetOffset(_scrollOffset - LineSize); }
    public void LineRight() { if (Orientation == Orientation.Vertical)   SetOffset(_scrollOffset + LineSize); }
    public void PageUp()    => SetOffset(_scrollOffset - GetViewportAxisSize());
    public void PageDown()  => SetOffset(_scrollOffset + GetViewportAxisSize());
    public void PageLeft()  => SetOffset(_scrollOffset - GetViewportAxisSize());
    public void PageRight() => SetOffset(_scrollOffset + GetViewportAxisSize());
    public void MouseWheelUp()    => LineUp();
    public void MouseWheelDown()  => LineDown();
    public void MouseWheelLeft()  => LineLeft();
    public void MouseWheelRight() => LineRight();

    public void SetHorizontalOffset(double offset)
    {
        if (Orientation == Orientation.Vertical) SetOffset(offset);
    }

    public void SetVerticalOffset(double offset)
    {
        if (Orientation == Orientation.Horizontal) SetOffset(offset);
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (Generator != null && visual is DependencyObject container)
        {
            var index = Generator.IndexFromContainer(container);
            if (index >= 0) BringIndexIntoView(index);
        }
        return rectangle;
    }

    private double LineSize
    {
        get
        {
            var s = GetAxisStride();
            return s > 0 ? s : 24;
        }
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

        var itemCount = GetItemCount();
        // Grid/ScrollViewer can issue an intermediate measure with the full window width even
        // though this panel's already-arranged viewport occupies only one grid column. Treating
        // that transient width as the wrap width changes ItemsPerRow mid-scroll, collapses the
        // estimated extent, and clamps the active offset. The owning ScrollViewer's viewport is
        // the stable cross-axis constraint; the raw measure size still governs the scroll axis.
        var layoutSize = StabilizeCrossAxisMeasureSize(availableSize);
        _viewport = CoerceViewport(layoutSize);

        if (itemCount == 0)
        {
            ClearRealizedContainers(recycle: true);
            _extent = new Size(0, 0);
            return new Size(0, 0);
        }

        // Resolve item size — explicit ItemWidth/Height take priority;
        // otherwise we measure index 0 once and reuse the size.
        ResolveItemSize(layoutSize, itemCount);
        if (_itemWidth <= 0 || _itemHeight <= 0)
        {
            // Could not determine — fall back to non-virtualizing measure.
            return MeasureNonVirtualized(availableSize);
        }

        var crossAxis = Orientation == Orientation.Horizontal ? layoutSize.Width : layoutSize.Height;
        _itemsPerRow = CalculateItemsPerRow(crossAxis);

        var totalRows = (itemCount + _itemsPerRow - 1) / _itemsPerRow;
        var rowSize = GetAxisStride();

        var viewportAxisSize = GetViewportAxisSize();
        var owner = GetOwner();
        var cache = GetCacheLength(owner);
        var cacheUnit = GetCacheLengthUnit(owner);
        var cacheBefore = ToCachePixels(cache.CacheBeforeViewport, cacheUnit, viewportAxisSize, rowSize);
        var cacheAfter = ToCachePixels(cache.CacheAfterViewport, cacheUnit, viewportAxisSize, rowSize);

        _scrollOffset = CoerceOffset(_scrollOffset, totalRows);
        var windowStart = Math.Max(0, _scrollOffset - cacheBefore);
        var windowEnd = _scrollOffset + viewportAxisSize + cacheAfter;

        var firstRow = Math.Max(0, (int)Math.Floor(windowStart / rowSize));
        var lastRow = Math.Min(totalRows - 1, (int)Math.Floor(windowEnd / rowSize));

        var firstIndex = firstRow * _itemsPerRow;
        var lastIndex = Math.Min(itemCount - 1, (lastRow + 1) * _itemsPerRow - 1);

        // On a disjoint jump, keeping the old window alive until after realization leaves
        // the recycling pool empty while the new viewport is generated. Detach and pool the
        // old window first so the incoming viewport can reuse those containers immediately.
        // Preserve the existing realize-before-recycle order for overlapping windows, where
        // already-realized containers are still useful in place.
        if (VirtualizationMode == VirtualizationMode.Recycling &&
            IsRealizationWindowDisjoint(firstIndex, lastIndex))
        {
            RecycleOutsideRange(firstIndex, lastIndex);
        }

        // Per-frame realize budget — long-jump scrolls (drag thumb, wheel
        // burst spanning many rows) used to realize 40+ containers in one
        // frame. Each new container instantiates the DataTemplate + first-
        // time-renders its bitmaps (GetNativeBitmap → native upload of the
        // full-resolution source while BitmapDownscaleCache async-synthesizes
        // the thumb), which a single frame can't absorb. Viewport rows are
        // realized unconditionally (must be on screen), cache-before/after
        // rows compete for the remaining budget. Whatever didn't fit gets
        // deferred to the next dispatcher turn via _deferredCatchUpPending.
        var viewportFirstRow = Math.Max(0, (int)Math.Floor(_scrollOffset / rowSize));
        var viewportLastRow = Math.Min(totalRows - 1,
            (int)Math.Floor((_scrollOffset + viewportAxisSize) / rowSize));
        var viewportFirstIndex = viewportFirstRow * _itemsPerRow;
        var viewportLastIndex = Math.Min(itemCount - 1,
            (viewportLastRow + 1) * _itemsPerRow - 1);

        // Viewport always realizes — pass a huge budget so the helper never
        // bails. (Could be hundreds of items if the viewport itself is huge,
        // but the user explicitly scrolled there so this is unavoidable.)
        int unlimited = int.MaxValue;
        RealizeRange(viewportFirstIndex, viewportLastIndex, ref unlimited);

        // Cache area: budget-limited. Forward first (forward scroll is the
        // common case), then backward fills what remains.
        int budget = MaxRealizesPerFrame;
        var fwd = RealizeRange(viewportLastIndex + 1, lastIndex, ref budget);
        int bwd;
        if (firstIndex > viewportFirstIndex - 1)
        {
            bwd = 0;  // nothing to do
        }
        else if (budget > 0)
        {
            bwd = RealizeRange(firstIndex, viewportFirstIndex - 1, ref budget);
        }
        else
        {
            // Backward range non-empty but no budget left → must defer.
            // Check if any of those indices is still un-realized.
            bool anyMissing = false;
            for (int i = firstIndex; i <= viewportFirstIndex - 1; ++i)
            {
                if (!_realizedContainers.ContainsKey(i)) { anyMissing = true; break; }
            }
            bwd = anyMissing ? -1 : 0;
        }
        bool reachedAll = fwd >= 0 && bwd >= 0;

        RecycleOutsideRange(firstIndex, lastIndex);
        UpdateExtent(totalRows, layoutSize);

        if (!reachedAll)
        {
            // Cache-area realization deferred — re-measure next frame to fill
            // in the missing containers without blocking this one.
            if (!_deferredCatchUpPending)
            {
                _deferredCatchUpPending = true;
                Jalium.UI.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(InvalidateMeasureIfDeferredCatchUp);
            }
        }

        return CoerceDesiredSize(layoutSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!ShouldVirtualize())
        {
            return ArrangeNonVirtualized(finalSize);
        }

        if (_itemsPerRow <= 0 || _itemWidth <= 0 || _itemHeight <= 0)
        {
            return finalSize;
        }

        var measuredCrossAxis = Orientation == Orientation.Horizontal ? _viewport.Width : _viewport.Height;
        _viewport = CoerceViewport(finalSize);
        var arrangedCrossAxis = Orientation == Orientation.Horizontal ? _viewport.Width : _viewport.Height;
        _arrangedCrossAxis = arrangedCrossAxis;
        if (IsUsableCrossAxis(_correctiveArrangeCrossAxis) &&
            !AreClose(arrangedCrossAxis, _correctiveArrangeCrossAxis))
        {
            ClearCrossAxisCorrection();
        }

        if (CalculateItemsPerRow(arrangedCrossAxis) != _itemsPerRow)
        {
            // Re-measure only when the committed Arrange crosses a wrap-count boundary. A
            // persistent Measure/Arrange width difference caused by an Auto scrollbar gutter
            // is remembered so the corrective pass cannot repeat forever.
            if (IsFiniteNonNegative(_measureProbeCrossAxis) &&
                CalculateItemsPerRow(_measureProbeCrossAxis) !=
                CalculateItemsPerRow(arrangedCrossAxis))
            {
                _correctiveMeasureCrossAxis = _measureProbeCrossAxis;
                _correctiveArrangeCrossAxis = arrangedCrossAxis;
            }
            else
            {
                ClearCrossAxisCorrection();
            }
            InvalidateMeasure();
        }
        else if (!AreClose(measuredCrossAxis, arrangedCrossAxis))
        {
            // The row geometry is still valid, but IScrollInfo's cross-axis extent must follow
            // the committed viewport so a harmless pixel difference cannot leave fake overflow.
            _extent = Orientation == Orientation.Horizontal
                ? new Size(arrangedCrossAxis, _extent.Height)
                : new Size(_extent.Width, arrangedCrossAxis);
            ScrollOwner?.InvalidateScrollInfo();

            if (!AreClose(arrangedCrossAxis, _correctiveArrangeCrossAxis))
            {
                ClearCrossAxisCorrection();
            }
        }

        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            var child = _realizedContainers.Values[i];
            int row = index / _itemsPerRow;
            int col = index % _itemsPerRow;

            double x, y;
            if (Orientation == Orientation.Horizontal)
            {
                x = col * (_itemWidth + GetCrossSpacing());
                y = row * GetAxisStride() - _scrollOffset;
            }
            else
            {
                x = row * GetAxisStride() - _scrollOffset;
                y = col * (_itemHeight + GetCrossSpacing());
            }

            child.Arrange(new Rect(x, y, _itemWidth, _itemHeight));
            child.Visibility = Visibility.Visible;
        }

        return finalSize;
    }

    #endregion

    #region Virtualization Support

    /// <inheritdoc />
    protected internal override void BringIndexIntoView(int index)
    {
        var itemCount = GetItemCount();
        if (index < 0 || index >= itemCount || _itemsPerRow <= 0) return;

        var rowSize = GetAxisStride();
        var itemAxisSize = GetAxisItemSize();
        int row = index / _itemsPerRow;
        double rowStart = row * rowSize;
        double rowEnd = rowStart + itemAxisSize;
        double viewportAxis = GetViewportAxisSize();

        if (rowStart < _scrollOffset)
        {
            SetOffset(rowStart);
        }
        else if (rowEnd > _scrollOffset + viewportAxis)
        {
            SetOffset(rowEnd - viewportAxis);
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        ResetResolvedAutoItemSize();
        ClearRealizedContainers(recycle: true);
        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override void OnClearChildren()
    {
        base.OnClearChildren();
        _realizedContainers.Clear();
    }

    #endregion

    #region Internal Helpers

    private bool ShouldVirtualize()
    {
        return GetIsVirtualizing(GetOwner()) && Generator != null;
    }

    private int GetItemCount() => Generator?.ItemCount ?? Children.Count;

    private void ResolveItemSize(Size availableSize, int itemCount)
    {
        var dpW = ItemWidth;
        var dpH = ItemHeight;
        bool wExplicit = !double.IsNaN(dpW) && dpW > 0;
        bool hExplicit = !double.IsNaN(dpH) && dpH > 0;
        if (wExplicit && hExplicit)
        {
            _itemWidth = dpW;
            _itemHeight = dpH;
            return;
        }

        // Auto-sized wrap items are documented as uniform. Once one item has supplied
        // that uniform size, keep it until the item set, ItemWidth/ItemHeight, or a
        // realized child's natural DesiredSize changes. The latter covers theme/font
        // changes and asynchronously loaded image content without returning to the old
        // index-zero re-probe on every scroll/resize.
        // The old code remeasured index 0 with Infinity on every layout pass and then
        // immediately measured it again with the resolved fixed size. After scrolling
        // index 0 out of the cache window it was also realized and recycled every frame.
        if (_hasResolvedAutoItemSize)
        {
            if (!wExplicit || !hExplicit)
            {
                for (int index = 0; index < _realizedContainers.Count; index++)
                {
                    var realized = _realizedContainers.Values[index];
                    if (realized.IsMeasureValid)
                    {
                        continue;
                    }

                    realized.Measure(new Size(
                        wExplicit ? dpW : double.PositiveInfinity,
                        hExplicit ? dpH : double.PositiveInfinity));
                    var desired = realized.DesiredSize;
                    _itemWidth = wExplicit ? dpW : Math.Max(1, desired.Width);
                    _itemHeight = hExplicit ? dpH : Math.Max(1, desired.Height);
                    return;
                }
            }

            if (wExplicit) _itemWidth = dpW;
            if (hExplicit) _itemHeight = dpH;
            return;
        }

        // Need to measure index 0 to derive size. Realize it temporarily.
        var probe = RealizeContainer(0);
        if (probe == null) { _itemWidth = _itemHeight = 0; return; }

        probe.Measure(new Size(
            wExplicit ? dpW : double.PositiveInfinity,
            hExplicit ? dpH : double.PositiveInfinity));
        var ds = probe.DesiredSize;
        _itemWidth = wExplicit ? dpW : Math.Max(1, ds.Width);
        _itemHeight = hExplicit ? dpH : Math.Max(1, ds.Height);
        _hasResolvedAutoItemSize = true;
    }

    private void ResetResolvedAutoItemSize()
    {
        _hasResolvedAutoItemSize = false;
        _itemWidth = 0;
        _itemHeight = 0;
    }

    // Realizes (firstIndex..lastIndex) inclusive, but stops early when the
    // supplied budget would go negative. Returns the count realized; -1
    // signals "hit budget before finishing the range" so the measure pass
    // can record the need for a deferred catch-up. Already-realized indices
    // don't consume budget since they cost nothing new.
    private int RealizeRange(int firstIndex, int lastIndex, ref int budget)
    {
        if (firstIndex > lastIndex) return 0;

        int realized = 0;
        for (int index = firstIndex; index <= lastIndex; ++index)
        {
            bool wasRealized = _realizedContainers.ContainsKey(index);
            if (!wasRealized && budget <= 0)
            {
                return -1;  // budget exhausted, range unfinished
            }

            var child = RealizeContainer(index);
            if (child == null) continue;
            child.Measure(new Size(_itemWidth, _itemHeight));

            if (!wasRealized)
            {
                ++realized;
                --budget;
            }
        }
        return realized;
    }

    private void InvalidateMeasureIfDeferredCatchUp()
    {
        if (!_deferredCatchUpPending) return;
        _deferredCatchUpPending = false;
        InvalidateMeasure();
    }

    private UIElement? RealizeContainer(int index)
    {
        if (_realizedContainers.TryGetValue(index, out var existing)) return existing;
        if (Generator == null) return null;

        var container = Generator.GetOrCreateContainerForIndex(index, out var isNewlyRealized);
        if (container is not UIElement child) return null;

        if (isNewlyRealized) Generator.PrepareItemContainer(container);

        _realizedContainers[index] = child;
        var insertPosition = _realizedContainers.IndexOfKey(index);

        if (Children.IndexOf(child) < 0)
        {
            if (insertPosition >= Children.Count) AddInternalChild(child);
            else InsertInternalChild(insertPosition, child);
        }
        return child;
    }

    private void RecycleOutsideRange(int firstIndex, int lastIndex)
    {
        if (_realizedContainers.Count == 0) return;

        _recycleBuffer.Clear();
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var idx = _realizedContainers.Keys[i];
            if (idx < firstIndex || idx > lastIndex) _recycleBuffer.Add(idx);
        }

        var isRecycling = VirtualizationMode == VirtualizationMode.Recycling;
        for (int i = 0; i < _recycleBuffer.Count; i++)
        {
            var index = _recycleBuffer[i];
            var child = _realizedContainers[index];
            if (isRecycling && child is FrameworkElement container)
            {
                container.PrepareForVirtualizationRecycle(this);
            }
            var visualIndex = Children.IndexOf(child);
            if (visualIndex >= 0) RemoveInternalChildRange(visualIndex, 1);
            _realizedContainers.Remove(index);

            if (Generator != null)
            {
                if (isRecycling) Generator.RecycleIndex(index);
                else Generator.RemoveIndex(index);
            }
        }
    }

    private bool IsRealizationWindowDisjoint(int firstIndex, int lastIndex)
    {
        for (var i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            if (index >= firstIndex && index <= lastIndex)
            {
                return false;
            }
        }

        return _realizedContainers.Count > 0;
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
            if (Generator != null)
            {
                if (isRecycling) Generator.RecycleIndex(index);
                else Generator.RemoveIndex(index);
            }
        }
        _realizedContainers.Clear();
        Children.Clear();
    }

    private void SetOffset(double offset)
    {
        // Both the virtualized and non-virtualized paths maintain the authoritative extent.
        // The latter does not need _itemsPerRow, so deriving the range from that field made its
        // otherwise valid IScrollInfo implementation clamp every offset back to zero.
        var axisExtent = Orientation == Orientation.Horizontal ? _extent.Height : _extent.Width;
        var maxOffset = Math.Max(0, axisExtent - GetViewportAxisSize());
        var coerced = double.IsNaN(offset) || double.IsInfinity(offset)
            ? 0
            : Math.Clamp(offset, 0, maxOffset);
        if (Math.Abs(coerced - _scrollOffset) <= 0.01) return;
        _scrollOffset = coerced;

        // Scrolling only changes the arranged origin while the new viewport is already
        // covered by the current realization window. Avoid re-running Measure (and the
        // generator/virtualization pipeline) for every pixel of a thumb drag; request a
        // new realization pass only when an item required by the viewport is missing.
        if (IsViewportRealized(coerced))
        {
            InvalidateArrange();
        }
        else
        {
            InvalidateMeasure();
        }
    }

    private bool IsViewportRealized(double offset)
    {
        if (!ShouldVirtualize())
        {
            return true;
        }

        var itemCount = GetItemCount();
        var rowSize = GetAxisStride();
        var viewportAxisSize = GetViewportAxisSize();
        if (itemCount <= 0 || _itemsPerRow <= 0 || rowSize <= 0 || viewportAxisSize <= 0)
        {
            return false;
        }

        var totalRows = (itemCount + _itemsPerRow - 1) / _itemsPerRow;
        var firstRow = Math.Clamp((int)Math.Floor(offset / rowSize), 0, totalRows - 1);
        var lastRow = Math.Clamp(
            (int)Math.Floor((offset + viewportAxisSize) / rowSize),
            firstRow,
            totalRows - 1);
        var firstIndex = firstRow * _itemsPerRow;
        var lastIndex = Math.Min(itemCount - 1, (lastRow + 1) * _itemsPerRow - 1);

        // Cache catch-up is budgeted and can temporarily leave holes, so checking only
        // the first/last realized keys is not sufficient.
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            if (!_realizedContainers.ContainsKey(index))
            {
                return false;
            }
        }

        return true;
    }

    private double CoerceOffset(double offset, int totalRows)
    {
        if (double.IsNaN(offset) || double.IsInfinity(offset)) return 0;
        var maxOffset = Math.Max(0, GetAxisExtent(totalRows) - GetViewportAxisSize());
        return Math.Clamp(offset, 0, maxOffset);
    }

    private double GetViewportAxisSize()
    {
        var size = Orientation == Orientation.Horizontal ? _viewport.Height : _viewport.Width;
        return size > 0 ? size : 0;
    }

    private void UpdateExtent(int totalRows, Size availableSize)
    {
        var axisExtent = GetAxisExtent(totalRows);
        var crossExtent = _itemsPerRow > 0
            ? _itemsPerRow * (Orientation == Orientation.Horizontal ? _itemWidth : _itemHeight) +
              (_itemsPerRow - 1) * GetCrossSpacing()
            : 0;
        if (Orientation == Orientation.Horizontal)
        {
            var width = double.IsInfinity(availableSize.Width)
                ? crossExtent : availableSize.Width;
            _extent = new Size(width, axisExtent);
        }
        else
        {
            var height = double.IsInfinity(availableSize.Height)
                ? crossExtent : availableSize.Height;
            _extent = new Size(axisExtent, height);
        }
    }

    private double GetAxisItemSize() =>
        Orientation == Orientation.Horizontal ? _itemHeight : _itemWidth;

    private double GetAxisSpacing() =>
        SanitizeSpacing(Orientation == Orientation.Horizontal ? VerticalSpacing : HorizontalSpacing);

    private double GetCrossSpacing() =>
        SanitizeSpacing(Orientation == Orientation.Horizontal ? HorizontalSpacing : VerticalSpacing);

    private double GetAxisStride() => GetAxisItemSize() + GetAxisSpacing();

    private double GetAxisExtent(int totalRows) => totalRows <= 0
        ? 0
        : totalRows * GetAxisItemSize() + (totalRows - 1) * GetAxisSpacing();

    private static double SanitizeSpacing(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

    private static bool AreClose(double left, double right) => Math.Abs(left - right) <= 0.01;

    private Size CoerceViewport(Size availableSize)
    {
        return new Size(
            CoerceFinite(availableSize.Width, _viewport.Width),
            CoerceFinite(availableSize.Height, _viewport.Height));
    }

    private Size StabilizeCrossAxisMeasureSize(Size availableSize)
    {
        var measureCrossAxis = Orientation == Orientation.Horizontal
            ? availableSize.Width
            : availableSize.Height;
        _measureProbeCrossAxis = measureCrossAxis;

        // Once arranged, do not let a speculative Measure enlarge the wrap width. The owning
        // ScrollViewer's viewport can participate in the same transient parent layout, so an
        // owner expansion is not enough to expand before this panel is really arranged. An owner
        // shrink is authoritative, though: use it as a downward-only cap so stale wide Measure
        // constraints cannot keep a resized window permanently wide.
        var ownerCrossAxis = 0.0;
        if (ScrollOwner != null)
        {
            ownerCrossAxis = Orientation == Orientation.Horizontal
                ? ScrollOwner.ViewportWidth
                : ScrollOwner.ViewportHeight;
            if (!IsUsableCrossAxis(ownerCrossAxis))
            {
                ownerCrossAxis = Orientation == Orientation.Horizontal
                    ? ScrollOwner.RenderSize.Width
                    : ScrollOwner.RenderSize.Height;
            }
        }

        var hasArrangedCrossAxis = IsUsableCrossAxis(_arrangedCrossAxis);
        var hasOwnerCrossAxis = IsUsableCrossAxis(ownerCrossAxis);
        var stableCrossAxis = hasArrangedCrossAxis && hasOwnerCrossAxis
            ? Math.Min(_arrangedCrossAxis, ownerCrossAxis)
            : hasArrangedCrossAxis
                ? _arrangedCrossAxis
                : ownerCrossAxis;

        if (!IsUsableCrossAxis(stableCrossAxis))
            return availableSize;

        var repeatsCorrectiveProbe =
            IsFiniteNonNegative(measureCrossAxis) &&
            IsFiniteNonNegative(_correctiveMeasureCrossAxis) &&
            IsUsableCrossAxis(_correctiveArrangeCrossAxis) &&
            AreClose(measureCrossAxis, _correctiveMeasureCrossAxis) &&
            AreClose(_arrangedCrossAxis, _correctiveArrangeCrossAxis) &&
            (!hasOwnerCrossAxis || ownerCrossAxis + 0.01 >= _correctiveArrangeCrossAxis) &&
            CalculateItemsPerRow(measureCrossAxis) !=
            CalculateItemsPerRow(_correctiveArrangeCrossAxis);

        var effectiveCrossAxis = repeatsCorrectiveProbe || double.IsInfinity(measureCrossAxis)
            ? stableCrossAxis
            : Math.Min(Math.Max(0, measureCrossAxis), stableCrossAxis);

        return Orientation == Orientation.Horizontal
            ? new Size(effectiveCrossAxis, availableSize.Height)
            : new Size(availableSize.Width, effectiveCrossAxis);
    }

    private int CalculateItemsPerRow(double crossAxis)
    {
        var crossItemSize = Orientation == Orientation.Horizontal ? _itemWidth : _itemHeight;
        var crossSpacing = GetCrossSpacing();
        var stride = crossItemSize + crossSpacing;
        if (!IsUsableCrossAxis(crossAxis) || !IsUsableCrossAxis(stride))
            return 1;

        var itemCount = Math.Max(1, GetItemCount());
        var rawCount = Math.Floor((crossAxis + crossSpacing) / stride);
        if (double.IsInfinity(rawCount) || rawCount >= itemCount)
            return itemCount;

        return Math.Max(1, (int)rawCount);
    }

    private static bool IsUsableCrossAxis(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

    private static bool IsFiniteNonNegative(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    private void ClearCrossAxisCorrection()
    {
        _correctiveMeasureCrossAxis = double.NaN;
        _correctiveArrangeCrossAxis = double.NaN;
    }

    private static double CoerceFinite(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return fallback > 0 ? fallback : 0;
        return Math.Max(0, value);
    }

    private Size CoerceDesiredSize(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width)
            ? _extent.Width : Math.Min(_extent.Width, availableSize.Width);
        var h = double.IsInfinity(availableSize.Height)
            ? _extent.Height : Math.Min(_extent.Height, availableSize.Height);
        return new Size(w, h);
    }

    private double ToCachePixels(double cacheValue, VirtualizationCacheLengthUnit unit, double viewportAxisSize, double rowSize)
    {
        if (cacheValue <= 0) return 0;
        return unit switch
        {
            VirtualizationCacheLengthUnit.Pixel => cacheValue,
            VirtualizationCacheLengthUnit.Item  => cacheValue * rowSize,
            VirtualizationCacheLengthUnit.Page  => cacheValue * viewportAxisSize,
            _ => cacheValue
        };
    }

    private Size MeasureNonVirtualized(Size availableSize)
    {
        // Keep the same cell-size and spacing contract as the virtualized path so
        // toggling IsVirtualizing changes realization, not geometry.
        var previousExtent = _extent;
        var previousViewport = _viewport;
        var previousOffset = _scrollOffset;
        _viewport = CoerceViewport(availableSize);
        double lineCross = 0;
        double maxCross = 0;
        double totalAxis = 0;
        double lineAxis = 0;
        var availableCross = Orientation == Orientation.Horizontal
            ? availableSize.Width
            : availableSize.Height;
        var crossSpacing = GetCrossSpacing();
        var axisSpacing = GetAxisSpacing();
        var explicitWidth = !double.IsNaN(ItemWidth) && ItemWidth > 0;
        var explicitHeight = !double.IsNaN(ItemHeight) && ItemHeight > 0;
        var childConstraint = new Size(
            explicitWidth ? ItemWidth : double.PositiveInfinity,
            explicitHeight ? ItemHeight : double.PositiveInfinity);

        foreach (UIElement child in Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(childConstraint);
            var ds = child.DesiredSize;
            var width = explicitWidth ? ItemWidth : ds.Width;
            var height = explicitHeight ? ItemHeight : ds.Height;
            var cross = Orientation == Orientation.Horizontal ? width : height;
            var axis = Orientation == Orientation.Horizontal ? height : width;
            var requiredCross = lineCross > 0 ? crossSpacing + cross : cross;

            if (lineCross > 0 && lineCross + requiredCross > availableCross)
            {
                maxCross = Math.Max(maxCross, lineCross);
                if (totalAxis > 0)
                {
                    totalAxis += axisSpacing;
                }
                totalAxis += lineAxis;
                lineCross = 0;
                lineAxis = 0;
                requiredCross = cross;
            }

            lineCross += requiredCross;
            lineAxis = Math.Max(lineAxis, axis);
        }

        maxCross = Math.Max(maxCross, lineCross);
        if (lineCross > 0)
        {
            if (totalAxis > 0)
            {
                totalAxis += axisSpacing;
            }
            totalAxis += lineAxis;
        }

        _extent = Orientation == Orientation.Horizontal
            ? new Size(maxCross, totalAxis)
            : new Size(totalAxis, maxCross);

        // A viewport expansion or content shrink can reduce the legal range while the panel is
        // already scrolled. Clamp against the freshly computed metrics before Arrange so content
        // cannot remain stranded at a stale negative origin.
        var axisExtent = Orientation == Orientation.Horizontal ? _extent.Height : _extent.Width;
        var maxOffset = Math.Max(0, axisExtent - GetViewportAxisSize());
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);

        if (!AreClose(previousExtent.Width, _extent.Width) ||
            !AreClose(previousExtent.Height, _extent.Height) ||
            !AreClose(previousViewport.Width, _viewport.Width) ||
            !AreClose(previousViewport.Height, _viewport.Height) ||
            !AreClose(previousOffset, _scrollOffset))
        {
            ScrollOwner?.InvalidateScrollInfo();
        }
        return CoerceDesiredSize(availableSize);
    }

    private Size ArrangeNonVirtualized(Size finalSize)
    {
        _viewport = CoerceViewport(finalSize);
        double lineCross = 0;
        double lineAxis = 0;
        double offset = 0;
        var availableCross = Orientation == Orientation.Horizontal ? finalSize.Width : finalSize.Height;
        var crossSpacing = GetCrossSpacing();
        var axisSpacing = GetAxisSpacing();
        var explicitWidth = !double.IsNaN(ItemWidth) && ItemWidth > 0;
        var explicitHeight = !double.IsNaN(ItemHeight) && ItemHeight > 0;

        foreach (UIElement child in Children)
        {
            var ds = child.DesiredSize;
            var width = explicitWidth ? ItemWidth : ds.Width;
            var height = explicitHeight ? ItemHeight : ds.Height;
            var cross = Orientation == Orientation.Horizontal ? width : height;
            var axis = Orientation == Orientation.Horizontal ? height : width;
            var requiredCross = lineCross > 0 ? crossSpacing + cross : cross;

            if (lineCross > 0 && lineCross + requiredCross > availableCross)
            {
                offset += lineAxis + axisSpacing;
                lineCross = 0;
                lineAxis = 0;
                requiredCross = cross;
            }

            double x, y, w, h;
            if (Orientation == Orientation.Horizontal)
            {
                x = lineCross + (lineCross > 0 ? crossSpacing : 0);
                y = offset - _scrollOffset;
                w = width;
                h = height;
            }
            else
            {
                x = offset - _scrollOffset;
                y = lineCross + (lineCross > 0 ? crossSpacing : 0);
                w = width;
                h = height;
            }
            child.Arrange(new Rect(x, y, w, h));

            lineCross += requiredCross;
            lineAxis = Math.Max(lineAxis, axis);
        }
        return finalSize;
    }

    #endregion

    #region Property Changed

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizingWrapPanel panel)
        {
            if (e.Property == OrientationProperty)
            {
                panel._arrangedCrossAxis = 0;
                panel.ClearCrossAxisCorrection();
            }
            if (e.Property == ItemWidthProperty || e.Property == ItemHeightProperty)
            {
                panel.ResetResolvedAutoItemSize();
            }
            panel.InvalidateMeasure();
        }
    }

    #endregion
}
