using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Virtualization;

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
    private int _itemsPerRow;
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
        if (ItemContainerGenerator != null && visual is DependencyObject container)
        {
            var index = ItemContainerGenerator.IndexFromContainer(container);
            if (index >= 0) BringIndexIntoView(index);
        }
        return rectangle;
    }

    private double LineSize
    {
        get
        {
            var s = Orientation == Orientation.Horizontal ? _itemHeight : _itemWidth;
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
        _viewport = CoerceViewport(availableSize);

        if (itemCount == 0)
        {
            ClearRealizedContainers(recycle: true);
            _extent = new Size(0, 0);
            return new Size(0, 0);
        }

        // Resolve item size — explicit ItemWidth/Height take priority;
        // otherwise we measure index 0 once and reuse the size.
        ResolveItemSize(availableSize, itemCount);
        if (_itemWidth <= 0 || _itemHeight <= 0)
        {
            // Could not determine — fall back to non-virtualizing measure.
            return MeasureNonVirtualized(availableSize);
        }

        var crossAxis = Orientation == Orientation.Horizontal ? availableSize.Width : availableSize.Height;
        var crossItemSize = Orientation == Orientation.Horizontal ? _itemWidth : _itemHeight;
        if (double.IsInfinity(crossAxis) || crossAxis <= 0)
        {
            _itemsPerRow = 1;
        }
        else
        {
            _itemsPerRow = Math.Max(1, (int)Math.Floor(crossAxis / crossItemSize));
        }

        var totalRows = (itemCount + _itemsPerRow - 1) / _itemsPerRow;
        var rowSize = Orientation == Orientation.Horizontal ? _itemHeight : _itemWidth;

        var viewportAxisSize = GetViewportAxisSize();
        var owner = GetOwner();
        var cache = GetCacheLength(owner);
        var cacheUnit = GetCacheLengthUnit(owner);
        var cacheBefore = ToCachePixels(cache.CacheBeforeViewport, cacheUnit, viewportAxisSize, rowSize);
        var cacheAfter = ToCachePixels(cache.CacheAfterViewport, cacheUnit, viewportAxisSize, rowSize);

        _scrollOffset = CoerceOffset(_scrollOffset, totalRows, rowSize);
        var windowStart = Math.Max(0, _scrollOffset - cacheBefore);
        var windowEnd = _scrollOffset + viewportAxisSize + cacheAfter;

        var firstRow = Math.Max(0, (int)Math.Floor(windowStart / rowSize));
        var lastRow = Math.Min(totalRows - 1, (int)Math.Floor(windowEnd / rowSize));

        var firstIndex = firstRow * _itemsPerRow;
        var lastIndex = Math.Min(itemCount - 1, (lastRow + 1) * _itemsPerRow - 1);

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
        UpdateExtent(totalRows, rowSize, availableSize);

        if (!reachedAll)
        {
            // Cache-area realization deferred — re-measure next frame to fill
            // in the missing containers without blocking this one.
            if (!_deferredCatchUpPending)
            {
                _deferredCatchUpPending = true;
                Dispatcher.CurrentDispatcher?.BeginInvoke(InvalidateMeasureIfDeferredCatchUp);
            }
        }

        return CoerceDesiredSize(availableSize);
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

        _viewport = CoerceViewport(finalSize);

        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            var child = _realizedContainers.Values[i];
            int row = index / _itemsPerRow;
            int col = index % _itemsPerRow;

            double x, y;
            if (Orientation == Orientation.Horizontal)
            {
                x = col * _itemWidth;
                y = row * _itemHeight - _scrollOffset;
            }
            else
            {
                x = row * _itemWidth - _scrollOffset;
                y = col * _itemHeight;
            }

            child.Arrange(new Rect(x, y, _itemWidth, _itemHeight));
            child.Visibility = Visibility.Visible;
        }

        return finalSize;
    }

    #endregion

    #region Virtualization Support

    /// <inheritdoc />
    protected override void BringIndexIntoViewOverride(int index)
    {
        var itemCount = GetItemCount();
        if (index < 0 || index >= itemCount || _itemsPerRow <= 0) return;

        var rowSize = Orientation == Orientation.Horizontal ? _itemHeight : _itemWidth;
        int row = index / _itemsPerRow;
        double rowStart = row * rowSize;
        double rowEnd = rowStart + rowSize;
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
        ClearRealizedContainers(recycle: true);
        InvalidateMeasure();
    }

    /// <inheritdoc />
    internal override void OnClearChildren()
    {
        base.OnClearChildren();
        _realizedContainers.Clear();
    }

    #endregion

    #region Internal Helpers

    private bool ShouldVirtualize()
    {
        return GetIsVirtualizing(GetOwner()) && ItemContainerGenerator != null;
    }

    private int GetItemCount() => ItemContainerGenerator?.ItemCount ?? Children.Count;

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

        // Need to measure index 0 to derive size. Realize it temporarily.
        var probe = RealizeContainer(0);
        if (probe == null) { _itemWidth = _itemHeight = 0; return; }

        probe.Measure(new Size(
            wExplicit ? dpW : double.PositiveInfinity,
            hExplicit ? dpH : double.PositiveInfinity));
        var ds = probe.DesiredSize;
        _itemWidth = wExplicit ? dpW : Math.Max(1, ds.Width);
        _itemHeight = hExplicit ? dpH : Math.Max(1, ds.Height);
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
        if (ItemContainerGenerator == null) return null;

        var container = ItemContainerGenerator.GetOrCreateContainerForIndex(index, out var isNewlyRealized);
        if (container is not UIElement child) return null;

        if (isNewlyRealized) ItemContainerGenerator.PrepareItemContainer(container);

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
            var visualIndex = Children.IndexOf(child);
            if (visualIndex >= 0) RemoveInternalChildRange(visualIndex, 1);
            _realizedContainers.Remove(index);

            if (ItemContainerGenerator != null)
            {
                if (isRecycling) ItemContainerGenerator.RecycleIndex(index);
                else ItemContainerGenerator.RemoveIndex(index);
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
                if (isRecycling) ItemContainerGenerator.RecycleIndex(index);
                else ItemContainerGenerator.RemoveIndex(index);
            }
        }
        _realizedContainers.Clear();
        Children.Clear();
    }

    private void SetOffset(double offset)
    {
        var totalRows = _itemsPerRow > 0
            ? (GetItemCount() + _itemsPerRow - 1) / _itemsPerRow : 0;
        var rowSize = Orientation == Orientation.Horizontal ? _itemHeight : _itemWidth;
        var coerced = CoerceOffset(offset, totalRows, rowSize);
        if (Math.Abs(coerced - _scrollOffset) <= 0.01) return;
        _scrollOffset = coerced;
        InvalidateMeasure();
    }

    private double CoerceOffset(double offset, int totalRows, double rowSize)
    {
        if (double.IsNaN(offset) || double.IsInfinity(offset)) return 0;
        var maxOffset = Math.Max(0, totalRows * rowSize - GetViewportAxisSize());
        return Math.Clamp(offset, 0, maxOffset);
    }

    private double GetViewportAxisSize()
    {
        var size = Orientation == Orientation.Horizontal ? _viewport.Height : _viewport.Width;
        return size > 0 ? size : 0;
    }

    private void UpdateExtent(int totalRows, double rowSize, Size availableSize)
    {
        var axisExtent = totalRows * rowSize;
        if (Orientation == Orientation.Horizontal)
        {
            var width = double.IsInfinity(availableSize.Width)
                ? _itemWidth * _itemsPerRow : availableSize.Width;
            _extent = new Size(width, axisExtent);
        }
        else
        {
            var height = double.IsInfinity(availableSize.Height)
                ? _itemHeight * _itemsPerRow : availableSize.Height;
            _extent = new Size(axisExtent, height);
        }
    }

    private Size CoerceViewport(Size availableSize)
    {
        return new Size(
            CoerceFinite(availableSize.Width, _viewport.Width),
            CoerceFinite(availableSize.Height, _viewport.Height));
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
        // Plain WrapPanel-style layout when virtualization is disabled or
        // item sizes can't be resolved.
        double rowAxis = 0;     // running cross-axis width per row
        double maxRow = 0;
        double totalAxis = 0;   // accumulated scroll-axis height
        double rowMaxAxis = 0;  // tallest item in the current row

        foreach (UIElement child in Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(availableSize);
            var ds = child.DesiredSize;
            var cross = Orientation == Orientation.Horizontal ? ds.Width : ds.Height;
            var axis  = Orientation == Orientation.Horizontal ? ds.Height : ds.Width;
            var available = Orientation == Orientation.Horizontal ? availableSize.Width : availableSize.Height;

            if (rowAxis + cross > available && rowAxis > 0)
            {
                totalAxis += rowMaxAxis;
                maxRow = Math.Max(maxRow, rowAxis);
                rowAxis = 0;
                rowMaxAxis = 0;
            }
            rowAxis += cross;
            rowMaxAxis = Math.Max(rowMaxAxis, axis);
        }
        totalAxis += rowMaxAxis;
        maxRow = Math.Max(maxRow, rowAxis);

        if (Orientation == Orientation.Horizontal)
        {
            _extent = new Size(maxRow, totalAxis);
            return new Size(maxRow, Math.Min(totalAxis, availableSize.Height));
        }
        _extent = new Size(totalAxis, maxRow);
        return new Size(Math.Min(totalAxis, availableSize.Width), maxRow);
    }

    private Size ArrangeNonVirtualized(Size finalSize)
    {
        double rowAxis = 0;
        double rowMaxAxis = 0;
        double offset = 0;

        foreach (UIElement child in Children)
        {
            var ds = child.DesiredSize;
            var cross = Orientation == Orientation.Horizontal ? ds.Width : ds.Height;
            var axis  = Orientation == Orientation.Horizontal ? ds.Height : ds.Width;
            var available = Orientation == Orientation.Horizontal ? finalSize.Width : finalSize.Height;

            if (rowAxis + cross > available && rowAxis > 0)
            {
                offset += rowMaxAxis;
                rowAxis = 0;
                rowMaxAxis = 0;
            }

            double x, y, w, h;
            if (Orientation == Orientation.Horizontal)
            {
                x = rowAxis; y = offset - _scrollOffset; w = cross; h = axis;
            }
            else
            {
                x = offset - _scrollOffset; y = rowAxis; w = axis; h = cross;
            }
            child.Arrange(new Rect(x, y, w, h));

            rowAxis += cross;
            rowMaxAxis = Math.Max(rowMaxAxis, axis);
        }
        return finalSize;
    }

    #endregion

    #region Property Changed

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizingWrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion
}
