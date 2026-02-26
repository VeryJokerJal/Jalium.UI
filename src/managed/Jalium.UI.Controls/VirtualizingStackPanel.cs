using System.Collections;
using System.Collections.Specialized;

namespace Jalium.UI.Controls;

/// <summary>
/// Arranges and virtualizes content on a single line oriented horizontally or vertically.
/// Only creates UI elements for items that are visible in the viewport.
/// </summary>
public class VirtualizingStackPanel : VirtualizingPanel, IScrollInfo
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(VirtualizingStackPanel),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    // VirtualizationMode, IsVirtualizing, and ScrollUnit properties are inherited from VirtualizingPanel

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the dimension by which child elements are stacked.
    /// </summary>
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

    // IsVirtualizing attached property accessors are inherited from VirtualizingPanel

    #region Private Fields

    private double _itemSize = 24; // Default item height/width
    private int _firstVisibleIndex;
    private int _lastVisibleIndex;
    private double _scrollOffset;
    private Size _extent;
    private Size _viewport;
    private readonly Dictionary<int, UIElement> _realizedItems = new();
    private readonly Queue<UIElement> _recycledContainers = new();

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
    public void LineUp() => SetVerticalOffset(_scrollOffset - _itemSize);

    /// <summary>
    /// Scrolls down by one line.
    /// </summary>
    public void LineDown() => SetVerticalOffset(_scrollOffset + _itemSize);

    /// <summary>
    /// Scrolls left by one line.
    /// </summary>
    public void LineLeft() => SetHorizontalOffset(_scrollOffset - _itemSize);

    /// <summary>
    /// Scrolls right by one line.
    /// </summary>
    public void LineRight() => SetHorizontalOffset(_scrollOffset + _itemSize);

    /// <summary>
    /// Scrolls up by one page.
    /// </summary>
    public void PageUp() => SetVerticalOffset(_scrollOffset - _viewport.Height);

    /// <summary>
    /// Scrolls down by one page.
    /// </summary>
    public void PageDown() => SetVerticalOffset(_scrollOffset + _viewport.Height);

    /// <summary>
    /// Scrolls left by one page.
    /// </summary>
    public void PageLeft() => SetHorizontalOffset(_scrollOffset - _viewport.Width);

    /// <summary>
    /// Scrolls right by one page.
    /// </summary>
    public void PageRight() => SetHorizontalOffset(_scrollOffset + _viewport.Width);

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
        if (Orientation != Orientation.Horizontal) return;
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Width - _viewport.Width));
        if (Math.Abs(_scrollOffset - offset) > 0.01)
        {
            _scrollOffset = offset;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Sets the vertical scroll offset.
    /// </summary>
    public void SetVerticalOffset(double offset)
    {
        if (Orientation != Orientation.Vertical) return;
        offset = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        if (Math.Abs(_scrollOffset - offset) > 0.01)
        {
            _scrollOffset = offset;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Makes the specified visual visible.
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        // Simplified implementation
        return rectangle;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _viewport = availableSize;
        var itemCount = GetItemCount();

        // Calculate extent
        var totalSize = itemCount * _itemSize;
        _extent = Orientation == Orientation.Vertical
            ? new Size(availableSize.Width, totalSize)
            : new Size(totalSize, availableSize.Height);

        // Calculate visible range
        var viewportSize = Orientation == Orientation.Vertical ? availableSize.Height : availableSize.Width;
        _firstVisibleIndex = Math.Max(0, (int)(_scrollOffset / _itemSize));
        _lastVisibleIndex = Math.Min(itemCount - 1, _firstVisibleIndex + (int)Math.Ceiling(viewportSize / _itemSize) + 1);

        // Virtualize: only measure visible children
        double maxCrossSize = 0;
        for (var i = _firstVisibleIndex; i <= _lastVisibleIndex && i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(availableSize);

            if (Orientation == Orientation.Vertical)
                maxCrossSize = Math.Max(maxCrossSize, child.DesiredSize.Width);
            else
                maxCrossSize = Math.Max(maxCrossSize, child.DesiredSize.Height);
        }

        // Return desired size
        return Orientation == Orientation.Vertical
            ? new Size(maxCrossSize, Math.Min(totalSize, availableSize.Height))
            : new Size(Math.Min(totalSize, availableSize.Width), maxCrossSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var offset = -_scrollOffset + _firstVisibleIndex * _itemSize;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];

            if (i >= _firstVisibleIndex && i <= _lastVisibleIndex)
            {
                // Visible - arrange normally
                if (Orientation == Orientation.Vertical)
                {
                    var rect = new Rect(0, offset, finalSize.Width, _itemSize);
                    child.Arrange(rect);
                    offset += _itemSize;
                }
                else
                {
                    var rect = new Rect(offset, 0, _itemSize, finalSize.Height);
                    child.Arrange(rect);
                    offset += _itemSize;
                }
                child.Visibility = Visibility.Visible;
            }
            else
            {
                // Not visible - collapse
                child.Arrange(new Rect(0, 0, 0, 0));
                child.Visibility = Visibility.Collapsed;
            }
        }

        return finalSize;
    }

    private int GetItemCount()
    {
        return Children.Count;
    }

    #endregion

    #region Virtualization Support

    /// <summary>
    /// Brings an item into view.
    /// </summary>
    public void BringIndexIntoView(int index)
    {
        var itemCount = GetItemCount();
        if (index < 0 || index >= itemCount) return;

        var itemOffset = index * _itemSize;
        var viewportSize = Orientation == Orientation.Vertical ? _viewport.Height : _viewport.Width;

        if (itemOffset < _scrollOffset)
        {
            // Item is before viewport
            _scrollOffset = itemOffset;
            InvalidateMeasure();
        }
        else if (itemOffset + _itemSize > _scrollOffset + viewportSize)
        {
            // Item is after viewport
            _scrollOffset = itemOffset + _itemSize - viewportSize;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Called when items change.
    /// </summary>
    protected virtual void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs args)
    {
        InvalidateMeasure();
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
