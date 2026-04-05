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
    /// Gets or sets the virtualization mode.
    /// </summary>
    public VirtualizationMode VirtualizationMode
    {
        get => GetVirtualizationMode(this);
        set => SetVirtualizationMode(this, value);
    }

    /// <summary>
    /// Gets or sets the scroll unit.
    /// </summary>
    public ScrollUnit ScrollUnit
    {
        get => GetScrollUnit(this);
        set => SetScrollUnit(this, value);
    }

    #endregion

    #region Private Fields

    private readonly SortedList<int, UIElement> _realizedContainers = new();
    private ItemHeightIndex _heightIndex = new(28);
    private RealizationWindow _currentWindow = RealizationWindow.Empty;
    private double _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private double _maxCrossAxis;
    private int _lastKnownItemCount = -1;

    // Reusable buffer to avoid allocations in RecycleOutsideWindow
    private readonly List<int> _recycleBuffer = new();

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
    public double HorizontalOffset => Orientation == Orientation.Horizontal ? _scrollOffset : 0;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset => Orientation == Orientation.Vertical ? _scrollOffset : 0;

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
            SetHorizontalOffset(_scrollOffset - GetAverageItemSize());
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
            SetHorizontalOffset(_scrollOffset + GetAverageItemSize());
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
            SetVerticalOffset(_scrollOffset - GetAverageItemSize());
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
            SetVerticalOffset(_scrollOffset + GetAverageItemSize());
        }
    }

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    public void PageUp() => SetOffset(_scrollOffset - GetViewportAxisSize());

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    public void PageDown() => SetOffset(_scrollOffset + GetViewportAxisSize());

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    public void PageLeft() => SetOffset(_scrollOffset - GetViewportAxisSize());

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    public void PageRight() => SetOffset(_scrollOffset + GetViewportAxisSize());

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
        var shouldVirtualize = ShouldVirtualize();
        if (!shouldVirtualize)
        {
            return MeasureNonVirtualized(availableSize);
        }

        var itemCount = GetItemCount();
        EnsureHeightIndex(itemCount);

        _viewport = CoerceViewport(availableSize);
        var viewportAxisSize = GetAxisSize(_viewport);

        if (itemCount == 0)
        {
            ClearRealizedContainers(recycle: true);
            _currentWindow = RealizationWindow.Empty;
            UpdateExtent(itemCount, availableSize);
            return CoerceDesiredSize(availableSize);
        }

        var cache = GetCacheLength(this);
        var cacheUnit = GetCacheLengthUnit(this);
        var cacheBefore = ToCachePixels(cache.CacheBeforeViewport, cacheUnit, viewportAxisSize);
        var cacheAfter = ToCachePixels(cache.CacheAfterViewport, cacheUnit, viewportAxisSize);

        _scrollOffset = CoerceOffset(_scrollOffset);
        var windowStartOffset = Math.Max(0, _scrollOffset - cacheBefore);
        var windowEndOffset = _scrollOffset + viewportAxisSize + cacheAfter;

        var startIndex = Math.Max(0, _heightIndex.GetIndexAtOffset(windowStartOffset));
        var endIndex = Math.Min(itemCount - 1,
            Math.Max(startIndex, _heightIndex.GetIndexAtOffset(windowEndOffset)));

        var newWindow = endIndex >= startIndex
            ? new RealizationWindow(startIndex, endIndex)
            : RealizationWindow.Empty;

        // Fast path: if the realization window hasn't changed, skip re-realize and recycle.
        // However, we must still re-measure any child whose measure has been invalidated
        // (e.g. a TreeViewItem that expanded/collapsed), so height offsets stay correct.
        if (newWindow.Equals(_currentWindow) && _realizedContainers.Count > 0)
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

            if (anyHeightChanged)
            {
                // Heights changed — the realization window boundaries may have shifted,
                // so fall through to the full realization path below.
            }
            else
            {
                return CoerceDesiredSize(availableSize);
            }
        }

        endIndex = RealizeWindow(startIndex, windowEndOffset, availableSize);
        _currentWindow = endIndex >= startIndex
            ? new RealizationWindow(startIndex, endIndex)
            : RealizationWindow.Empty;

        RecycleOutsideWindow(_currentWindow);
        UpdateExtent(itemCount, availableSize);

        return CoerceDesiredSize(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!ShouldVirtualize())
        {
            return ArrangeNonVirtualized(finalSize);
        }

        _viewport = CoerceViewport(finalSize);
        _scrollOffset = CoerceOffset(_scrollOffset);

        // SortedList iterates in key order — no allocation needed
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            var child = _realizedContainers.Values[i];
            var itemOffset = _heightIndex.GetOffsetForIndex(index) - _scrollOffset;
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
        if (_realizedContainers.Count > 0)
        {
            var axisOffset = Orientation == Orientation.Vertical
                ? localPoint.Y + _scrollOffset
                : localPoint.X + _scrollOffset;

            var estimatedIndex = _heightIndex.GetIndexAtOffset(axisOffset);
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

        var itemStart = _heightIndex.GetOffsetForIndex(index);
        var itemEnd = itemStart + _heightIndex.GetHeightAt(index);
        var viewportAxis = GetViewportAxisSize();
        if (viewportAxis <= 0)
        {
            SetOffset(itemStart);
            return;
        }

        if (itemStart < _scrollOffset)
        {
            SetOffset(itemStart);
        }
        else if (itemEnd > _scrollOffset + viewportAxis)
        {
            SetOffset(itemEnd - viewportAxis);
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        var itemCount = GetItemCount();
        _heightIndex.Reset(itemCount, _heightIndex.EstimatedHeight);
        ClearRealizedContainers(recycle: true);
        _currentWindow = RealizationWindow.Empty;
        InvalidateMeasure();
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
        return GetIsVirtualizing(this) && ItemContainerGenerator != null;
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
        var currentOffset = _heightIndex.GetOffsetForIndex(startIndex);

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

            currentOffset = _heightIndex.GetOffsetForIndex(index + 1);
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

        // Collect indices to recycle using a reusable buffer — zero allocations
        _recycleBuffer.Clear();
        for (int i = 0; i < _realizedContainers.Count; i++)
        {
            var index = _realizedContainers.Keys[i];
            if (!window.Contains(index))
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
            SetOffset(_scrollOffset + direction * GetAverageItemSize());
            return;
        }

        var current = _heightIndex.GetIndexAtOffset(_scrollOffset);
        var target = Math.Clamp(current + direction, 0, Math.Max(0, _heightIndex.Count - 1));
        SetOffset(_heightIndex.GetOffsetForIndex(target));
    }

    private void SetOffset(double offset)
    {
        var coerced = CoerceOffset(offset);
        if (Math.Abs(coerced - _scrollOffset) <= 0.01)
        {
            return;
        }

        _scrollOffset = coerced;
        InvalidateMeasure();
    }

    private double CoerceOffset(double offset)
    {
        var maxOffset = Math.Max(0, GetAxisSize(_extent) - GetViewportAxisSize());
        if (double.IsNaN(offset) || double.IsInfinity(offset))
        {
            return 0;
        }

        return Math.Clamp(offset, 0, maxOffset);
    }

    private void UpdateExtent(int itemCount, Size availableSize)
    {
        var axisExtent = itemCount <= 0 ? 0 : _heightIndex.TotalHeight;
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
        double axis = 0;
        double cross = 0;
        foreach (UIElement child in Children)
        {
            child.Visibility = Visibility.Visible;
            child.Measure(availableSize);
            axis += GetAxisSize(child.DesiredSize);
            cross = Math.Max(cross, GetCrossAxisSize(child.DesiredSize));
        }

        if (Orientation == Orientation.Vertical)
        {
            _extent = new Size(cross, axis);
            _viewport = CoerceViewport(availableSize);
            return new Size(cross, Math.Min(axis, availableSize.Height));
        }

        _extent = new Size(axis, cross);
        _viewport = CoerceViewport(availableSize);
        return new Size(Math.Min(axis, availableSize.Width), cross);
    }

    private Size ArrangeNonVirtualized(Size finalSize)
    {
        var offset = -_scrollOffset;
        foreach (UIElement child in Children)
        {
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
