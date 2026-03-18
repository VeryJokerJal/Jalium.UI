using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// A panel that displays multiple <see cref="DockItem"/> children as tabs.
/// Used within <see cref="DockSplitPanel"/> or <see cref="DockLayout"/>.
/// </summary>
[ContentProperty("Items")]
public class DockTabPanel : Selector
{
    private static readonly SolidColorBrush s_fallbackPanelBackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly SolidColorBrush s_fallbackTabStripBrush = new(Color.FromRgb(0x18, 0x18, 0x26));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(0x3C, 0x3C, 0x4D));
    private static readonly SolidColorBrush s_fallbackAccentBrush = new(Color.FromRgb(0x7A, 0xA2, 0xF7));
    private const double TabStripScrollBarThickness = 10.0;

    /// <summary>
    /// Occurs when a tab is explicitly closed via tab close action.
    /// Reorder operations do not raise this event.
    /// </summary>
    public event EventHandler<DockItemClosedEventArgs>? ItemClosed;

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedContentProperty =
        DependencyProperty.Register(nameof(SelectedContent), typeof(object), typeof(DockTabPanel),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripPlacementProperty =
        DependencyProperty.Register(nameof(TabStripPlacement), typeof(Dock), typeof(DockTabPanel),
            new PropertyMetadata(Dock.Top, OnTabStripPlacementChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripHeightProperty =
        DependencyProperty.Register(nameof(TabStripHeight), typeof(double), typeof(DockTabPanel),
            new PropertyMetadata(28.0, OnLayoutPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabStripBackgroundProperty =
        DependencyProperty.Register(nameof(TabStripBackground), typeof(Brush), typeof(DockTabPanel),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TabStripBorderBrushProperty =
        DependencyProperty.Register(nameof(TabStripBorderBrush), typeof(Brush), typeof(DockTabPanel),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedContentTransitionModeProperty =
        DependencyProperty.Register(nameof(SelectedContentTransitionMode), typeof(TransitionMode?), typeof(DockTabPanel),
            new PropertyMetadata(null, OnSelectedContentTransitionModeChanged));

    #endregion

    #region Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedContent
    {
        get => GetValue(SelectedContentProperty);
        set => SetValue(SelectedContentProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Dock TabStripPlacement
    {
        get => (Dock)(GetValue(TabStripPlacementProperty) ?? Dock.Top);
        set => SetValue(TabStripPlacementProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public double TabStripHeight
    {
        get => (double)GetValue(TabStripHeightProperty);
        set => SetValue(TabStripHeightProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Brush? TabStripBackground
    {
        get => (Brush?)GetValue(TabStripBackgroundProperty);
        set => SetValue(TabStripBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TabStripBorderBrush
    {
        get => (Brush?)GetValue(TabStripBorderBrushProperty);
        set => SetValue(TabStripBorderBrushProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public TransitionMode? SelectedContentTransitionMode
    {
        get => (TransitionMode?)GetValue(SelectedContentTransitionModeProperty);
        set => SetValue(SelectedContentTransitionModeProperty, value);
    }

    #endregion

    private readonly TransitioningContentControl _selectedContentHost;
    private bool _isDockHighlighted;
    private readonly ScrollBar _tabStripScrollBar;
    private double _tabStripScrollOffset;
    private double _tabStripExtent;
    private double _tabStripViewport;
    private bool _isTabStripScrollable;
    private Rect _tabStripRect;
    private Rect _tabHeaderViewportRect;
    private Rect _tabScrollBarRect;
    private Rect _contentRect;

    /// <summary>
    /// Indicates this panel is inside a floating window (not docked).
    /// Floating panels are excluded from dock-back hit-testing targets.
    /// </summary>
    internal bool IsFloating { get; set; }

    internal bool IsVerticalTabStrip => TabStripPlacement is Dock.Left or Dock.Right;

    internal Rect TabHeadersViewportRect => _tabHeaderViewportRect;

    internal double TabStripScrollOffsetForTesting => _tabStripScrollOffset;

    internal bool IsTabStripScrollableForTesting => _tabStripScrollBar.Visibility == Visibility.Visible;

    internal Rect TabStripScrollBarRectForTesting => _tabScrollBarRect;

    internal void SetTabStripScrollOffsetForTesting(double offset)
    {
        SetTabStripScrollOffsetInternal(offset, invalidateLayout: false);
    }

    /// <summary>
    /// When true, draws an accent border to show this panel is a valid dock target.
    /// Dock indicator buttons are rendered in a separate topmost window by <see cref="DockManager"/>.
    /// </summary>
    internal bool IsDockHighlighted
    {
        get => _isDockHighlighted;
        set
        {
            if (_isDockHighlighted != value)
            {
                _isDockHighlighted = value;
                InvalidateVisual();
            }
        }
    }

    #region Reorder Preview State

    private int _reorderInsertIndex = -1;
    private int _reorderDragItemIndex = -1;

    /// <summary>
    /// The insertion position for the reorder preview indicator.
    /// -1 means no indicator shown. 0..Items.Count represents the position before which the indicator appears.
    /// </summary>
    internal int ReorderInsertIndex
    {
        get => _reorderInsertIndex;
        set
        {
            if (_reorderInsertIndex == value) return;
            _reorderInsertIndex = value;
            // Repaint tabs to show/hide indicator
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is DockItem item)
                    item.InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// The index of the tab currently being dragged for reorder.
    /// -1 when no reorder is in progress.
    /// </summary>
    internal int ReorderDragItemIndex
    {
        get => _reorderDragItemIndex;
        set => _reorderDragItemIndex = value;
    }

    /// <summary>
    /// Calculates the insertion index based on the cursor's X position in panel coordinates.
    /// Returns -1 if the position would result in no change.
    /// </summary>
    internal int CalculateReorderInsertIndex(Point mouseInPanel)
    {
        var mousePrimary = IsVerticalTabStrip ? mouseInPanel.Y : mouseInPanel.X;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is not DockItem item) continue;

            var itemPos = item.TransformToAncestor(this);
            var itemStart = IsVerticalTabStrip ? itemPos.Y : itemPos.X;
            var itemLength = IsVerticalTabStrip ? item.ActualHeight : item.ActualWidth;
            var itemMid = itemStart + itemLength / 2;

            if (mousePrimary < itemMid)
            {
                // Insert before tab i; skip if there is no actual position change.
                if (i == _reorderDragItemIndex || i == _reorderDragItemIndex + 1)
                    return -1;
                return i;
            }
        }

        // After all tabs; skip if dragged tab is already last.
        if (_reorderDragItemIndex == Items.Count - 1)
            return -1;
        return Items.Count;
    }

    #endregion

    public DockTabPanel()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        MinWidth = 100;
        MinHeight = 100;
        ClipToBounds = true;
        _selectedContentHost = new TransitioningContentControl
        {
            ClipToBounds = true,
            TransitionMode = SelectedContentTransitionMode
        };
        AddVisualChild(_selectedContentHost);

        _tabStripScrollBar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
            IsThumbSlim = true,
            SmallChange = 32,
            LargeChange = 120,
            Focusable = false
        };
        _tabStripScrollBar.Scroll += OnTabStripScroll;
        AddVisualChild(_tabStripScrollBar);
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));

        Items.CollectionChanged += OnDockItemsChanged;

        // Register with DockManager for cross-window hit-testing.
        // Both Loaded and constructor register to handle re-parenting (e.g. DockToEdge
        // temporarily removes the panel from the tree). Register() is idempotent.
        DockManager.Register(this);
        Loaded += (_, _) => DockManager.Register(this);
        Unloaded += (_, _) => DockManager.Unregister(this);
    }

    protected override Panel CreateItemsPanel()
    {
        return new StackPanel
        {
            Orientation = IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal,
            ClipToBounds = true
        };
    }

    protected override bool IsItemItsOwnContainer(object item) => item is DockItem;

    protected override FrameworkElement GetContainerForItem(object item) => new DockItem();

    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is DockItem dockItem)
        {
            dockItem.OwnerPanel = this;
        }
    }

    private void OnDockItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is DockItem dockItem)
                    dockItem.OwnerPanel = null;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is DockItem dockItem)
                    dockItem.OwnerPanel = this;
            }
        }

        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is DockItem dockItem)
                    dockItem.OwnerPanel = this;
            }
        }

        // Auto-select first tab if none selected
        if (SelectedIndex < 0 && Items.Count > 0)
            SelectedIndex = 0;

        // Update selection state
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is DockItem dockItem)
                dockItem.IsSelected = (i == SelectedIndex);
        }

        EnsureItemsHostOrientation();
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal Rect GetTabStripInteractionRect()
    {
        if (_tabHeaderViewportRect.Width > 0 && _tabHeaderViewportRect.Height > 0)
            return _tabHeaderViewportRect;

        var stripThickness = GetEffectiveTabStripThickness(new Size(ActualWidth, ActualHeight));
        return TabStripPlacement switch
        {
            Dock.Bottom => new Rect(0, Math.Max(0, ActualHeight - stripThickness), ActualWidth, stripThickness),
            Dock.Left => new Rect(0, 0, stripThickness, ActualHeight),
            Dock.Right => new Rect(Math.Max(0, ActualWidth - stripThickness), 0, stripThickness, ActualHeight),
            _ => new Rect(0, 0, ActualWidth, stripThickness),
        };
    }

    internal void SelectTab(DockItem item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
            SelectedIndex = index;
    }

    internal void CloseItem(DockItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0) return;

        ItemClosed?.Invoke(this, new DockItemClosedEventArgs(item, index));

        Items.RemoveAt(index);

        // Always clear old selected content first so same-index reselection still refreshes the host.
        _selectedContentHost.Content = null;

        // Adjust selection
        if (Items.Count == 0)
        {
            SelectedIndex = -1;

            // Remove this empty panel from parent DockSplitPanel
            if (VisualParent is DockSplitPanel splitPanel)
                splitPanel.RemovePane(this);
        }
        else
        {
            var newIndex = Math.Min(index, Items.Count - 1);
            if (SelectedIndex == newIndex)
            {
                // SelectedIndex did not change; force content update manually.
                UpdateContainerSelection();
            }
            else
            {
                SelectedIndex = newIndex;
            }
        }
    }

    /// <summary>
    /// Moves a tab item from one index to another within this panel.
    /// </summary>
    internal void MoveItem(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Items.Count) return;
        if (newIndex < 0 || newIndex >= Items.Count) return;
        if (oldIndex == newIndex) return;

        var item = Items[oldIndex];
        Items.RemoveAt(oldIndex);
        Items.Insert(newIndex, item);

        // SelectedItem still points to the same object (it was removed and re-inserted),
        // so setting SelectedIndex may not trigger UpdateContainerSelection if Selector
        // sees SelectedItem == GetItemAt(newIndex). Force the update explicitly.
        SelectedIndex = newIndex;
        UpdateContainerSelection();
    }

    /// <summary>
    /// Detaches a content element from this panel's visual tree.
    /// Called by <see cref="DockItem.OnContentChanged"/> when content is being transferred.
    /// </summary>
    internal void ReleaseContentElement(UIElement element)
    {
        if (ReferenceEquals(_selectedContentHost.Content, element))
        {
            _selectedContentHost.Content = null;
        }
    }

    /// <summary>
    /// Adopts the new content of a selected DockItem into the visual tree.
    /// Called by <see cref="DockItem.OnContentChanged"/> when the selected item's content changes.
    /// </summary>
    internal void AdoptContentForSelectedItem(DockItem item)
    {
        // Only adopt if this item is actually the selected one
        if (Items.IndexOf(item) != SelectedIndex) return;

        SetValue(SelectedContentProperty, item.Content);

        if (item.Content is UIElement contentElement)
        {
            // Force-detach from old panel if still attached
            if (contentElement.VisualParent is DockTabPanel oldPanel && oldPanel != this)
                oldPanel.ReleaseContentElement(contentElement);
        }

        _selectedContentHost.Content = item.Content;

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    protected override void UpdateContainerSelection()
    {
        // Update IsSelected on all DockItems
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is DockItem dockItem)
                dockItem.IsSelected = (i == SelectedIndex);
        }

        _selectedContentHost.Content = null;

        // Update selected content
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            var selectedItem = Items[SelectedIndex];
            if (selectedItem is DockItem dockItem)
            {
                SetValue(SelectedContentProperty, dockItem.Content);

                if (dockItem.Content is UIElement contentElement)
                {
                    // Force-detach from old panel if still attached (e.g. after dock-back transfer)
                    if (contentElement.VisualParent is DockTabPanel oldPanel && oldPanel != this)
                        oldPanel.ReleaseContentElement(contentElement);
                }

                _selectedContentHost.Content = dockItem.Content;
            }
        }
        else
        {
            SetValue(SelectedContentProperty, null);
            _selectedContentHost.Content = null;
        }

        EnsureSelectedTabVisibleCore();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private static void OnTabStripPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            panel._tabStripScrollOffset = 0;
            panel.EnsureItemsHostOrientation();
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
            panel.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
            panel.InvalidateVisual();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
            panel.InvalidateVisual();
        }
    }

    private static void OnSelectedContentTransitionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            panel._selectedContentHost.TransitionMode = e.NewValue is TransitionMode mode ? mode : null;
            panel.InvalidateVisual();
        }
    }

    private void OnTabStripScroll(object? sender, ScrollEventArgs e)
    {
        SetTabStripScrollOffsetInternal(_tabStripScrollBar.Value, invalidateLayout: true);
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (_tabStripScrollBar.Visibility != Visibility.Visible)
            return;

        var pos = e.GetPosition(this);
        if (!GetTabStripInteractionRect().Contains(pos))
            return;

        var delta = e.Delta > 0
            ? -Math.Max(12, _tabStripScrollBar.SmallChange * 2)
            : Math.Max(12, _tabStripScrollBar.SmallChange * 2);

        SetTabStripScrollOffsetInternal(_tabStripScrollOffset + delta, invalidateLayout: true);
        e.Handled = true;
    }

    private void EnsureItemsHostOrientation()
    {
        if (ItemsHost is StackPanel stackPanel)
        {
            var orientation = IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal;
            if (stackPanel.Orientation != orientation)
                stackPanel.Orientation = orientation;
            if (!stackPanel.ClipToBounds)
                stackPanel.ClipToBounds = true;
        }

        _tabStripScrollBar.Orientation = IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal;
    }

    private double GetEffectiveTabStripThickness(Size size)
    {
        var thickness = Math.Max(0, TabStripHeight);
        if (IsVerticalTabStrip)
        {
            if (double.IsFinite(size.Width))
                thickness = Math.Min(thickness, Math.Max(0, size.Width));
        }
        else
        {
            if (double.IsFinite(size.Height))
                thickness = Math.Min(thickness, Math.Max(0, size.Height));
        }

        return thickness;
    }

    private void MeasureTabHeaders(double stripThickness)
    {
        if (ItemsHost != null)
        {
            if (IsVerticalTabStrip)
                ItemsHost.Measure(new Size(stripThickness, double.PositiveInfinity));
            else
                ItemsHost.Measure(new Size(double.PositiveInfinity, stripThickness));
            return;
        }

        var itemExtent = Math.Max(18.0, stripThickness);
        foreach (var item in Items)
        {
            if (item is not DockItem dockItem) continue;

            var itemMeasureSize = IsVerticalTabStrip
                ? new Size(stripThickness, itemExtent)
                : new Size(double.PositiveInfinity, stripThickness);
            dockItem.Measure(itemMeasureSize);
        }
    }

    private double ComputeTabStripExtent()
    {
        if (ItemsHost != null)
            return IsVerticalTabStrip ? ItemsHost.DesiredSize.Height : ItemsHost.DesiredSize.Width;

        double total = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is not DockItem dockItem) continue;
            total += GetTabPrimaryLength(dockItem);
        }

        return total;
    }

    private double GetTabPrimaryLength(DockItem item)
    {
        var desired = IsVerticalTabStrip ? item.DesiredSize.Height : item.DesiredSize.Width;
        if (desired <= 0)
            desired = IsVerticalTabStrip ? item.ActualHeight : item.ActualWidth;
        return Math.Max(0, desired);
    }

    private void RefreshTabStripScrollMetrics(Size size)
    {
        _tabStripExtent = ComputeTabStripExtent();

        var viewport = IsVerticalTabStrip ? size.Height : size.Width;
        if (!double.IsFinite(viewport) || viewport < 0)
            viewport = 0;
        _tabStripViewport = viewport;

        _isTabStripScrollable = _tabStripExtent > _tabStripViewport + 0.5;
        if (!_isTabStripScrollable)
            _tabStripScrollOffset = 0;

        ClampTabStripScrollOffset();
        UpdateTabStripScrollBarMetrics();
    }

    private void ClampTabStripScrollOffset()
    {
        var maxOffset = Math.Max(0, _tabStripExtent - _tabStripViewport);
        if (!double.IsFinite(_tabStripScrollOffset))
            _tabStripScrollOffset = 0;
        _tabStripScrollOffset = Math.Clamp(_tabStripScrollOffset, 0, maxOffset);
        SyncTabStripScrollBarValue();
    }

    private void SetTabStripScrollOffsetInternal(double offset, bool invalidateLayout)
    {
        var maxOffset = Math.Max(0, _tabStripExtent - _tabStripViewport);
        if (!double.IsFinite(offset))
            offset = 0;

        var clamped = Math.Clamp(offset, 0, maxOffset);
        if (Math.Abs(clamped - _tabStripScrollOffset) <= 0.1)
        {
            SyncTabStripScrollBarValue();
            return;
        }

        _tabStripScrollOffset = clamped;
        SyncTabStripScrollBarValue();

        if (invalidateLayout)
        {
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    private void SyncTabStripScrollBarValue()
    {
        if (Math.Abs(_tabStripScrollBar.Value - _tabStripScrollOffset) > 0.1)
            _tabStripScrollBar.Value = _tabStripScrollOffset;
    }

    private void UpdateTabStripScrollBarMetrics()
    {
        var maxOffset = Math.Max(0, _tabStripExtent - _tabStripViewport);
        var show = _isTabStripScrollable && _tabStripViewport > 0 && maxOffset > 0.5;

        _tabStripScrollBar.Orientation = IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal;
        _tabStripScrollBar.Minimum = 0;
        _tabStripScrollBar.Maximum = maxOffset;
        _tabStripScrollBar.ViewportSize = Math.Max(0, _tabStripViewport);
        _tabStripScrollBar.LargeChange = Math.Max(1, Math.Min(_tabStripViewport, maxOffset));
        _tabStripScrollBar.SmallChange = Math.Max(8, _tabStripScrollBar.LargeChange / 8);
        _tabStripScrollBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
            _tabStripScrollOffset = 0;

        SyncTabStripScrollBarValue();
    }

    private void EnsureSelectedTabVisibleCore()
    {
        if (!_isTabStripScrollable || _tabStripViewport <= 0)
            return;
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return;
        if (Items[SelectedIndex] is not DockItem selectedItem)
            return;

        double selectedStart = 0;
        for (int i = 0; i < SelectedIndex; i++)
        {
            if (Items[i] is DockItem item)
                selectedStart += GetTabPrimaryLength(item);
        }

        var selectedLength = GetTabPrimaryLength(selectedItem);
        var selectedEnd = selectedStart + selectedLength;
        var targetOffset = _tabStripScrollOffset;

        if (selectedStart < targetOffset)
            targetOffset = selectedStart;
        else if (selectedEnd > targetOffset + _tabStripViewport)
            targetOffset = selectedEnd - _tabStripViewport;

        SetTabStripScrollOffsetInternal(targetOffset, invalidateLayout: false);
    }

    private (Rect stripRect, Rect contentRect, Rect headerViewportRect, Rect scrollBarRect) ComputeLayoutRects(Size finalSize)
    {
        var stripThickness = GetEffectiveTabStripThickness(finalSize);

        Rect stripRect;
        Rect contentRect;

        switch (TabStripPlacement)
        {
            case Dock.Bottom:
                stripRect = new Rect(0, Math.Max(0, finalSize.Height - stripThickness), finalSize.Width, stripThickness);
                contentRect = new Rect(1, 1, Math.Max(0, finalSize.Width - 2), Math.Max(0, finalSize.Height - stripThickness - 1));
                break;
            case Dock.Left:
                stripRect = new Rect(0, 0, stripThickness, finalSize.Height);
                contentRect = new Rect(stripThickness, 1, Math.Max(0, finalSize.Width - stripThickness - 1), Math.Max(0, finalSize.Height - 2));
                break;
            case Dock.Right:
                stripRect = new Rect(Math.Max(0, finalSize.Width - stripThickness), 0, stripThickness, finalSize.Height);
                contentRect = new Rect(1, 1, Math.Max(0, finalSize.Width - stripThickness - 1), Math.Max(0, finalSize.Height - 2));
                break;
            default:
                stripRect = new Rect(0, 0, finalSize.Width, stripThickness);
                contentRect = new Rect(1, stripThickness, Math.Max(0, finalSize.Width - 2), Math.Max(0, finalSize.Height - stripThickness - 1));
                break;
        }

        var headerViewportRect = stripRect;
        Rect scrollBarRect = Rect.Empty;

        if (_tabStripScrollBar.Visibility == Visibility.Visible)
        {
            if (IsVerticalTabStrip)
            {
                var width = Math.Min(TabStripScrollBarThickness, stripRect.Width);
                if (width > 0)
                {
                    scrollBarRect = TabStripPlacement == Dock.Left
                        ? new Rect(stripRect.Right - width, stripRect.Y, width, stripRect.Height)
                        : new Rect(stripRect.X, stripRect.Y, width, stripRect.Height);
                }
            }
            else
            {
                var height = Math.Min(TabStripScrollBarThickness, stripRect.Height);
                if (height > 0)
                {
                    scrollBarRect = TabStripPlacement == Dock.Top
                        ? new Rect(stripRect.X, stripRect.Bottom - height, stripRect.Width, height)
                        : new Rect(stripRect.X, stripRect.Y, stripRect.Width, height);
                }
            }
        }

        return (stripRect, contentRect, headerViewportRect, scrollBarRect);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (ItemsHost == null)
            RefreshItems();

        EnsureItemsHostOrientation();

        var stripThickness = GetEffectiveTabStripThickness(availableSize);
        MeasureTabHeaders(stripThickness);

        var stripViewport = IsVerticalTabStrip
            ? new Size(stripThickness, availableSize.Height)
            : new Size(availableSize.Width, stripThickness);
        RefreshTabStripScrollMetrics(stripViewport);

        if (_tabStripScrollBar.Visibility == Visibility.Visible)
        {
            var scrollMeasureSize = IsVerticalTabStrip
                ? new Size(Math.Min(stripThickness, TabStripScrollBarThickness), availableSize.Height)
                : new Size(availableSize.Width, Math.Min(stripThickness, TabStripScrollBarThickness));
            _tabStripScrollBar.Measure(scrollMeasureSize);
        }
        else
        {
            _tabStripScrollBar.Measure(Size.Empty);
        }

        var contentWidth = Math.Max(0, availableSize.Width - (IsVerticalTabStrip ? stripThickness + 1 : 2));
        var contentHeight = Math.Max(0, availableSize.Height - (IsVerticalTabStrip ? 2 : stripThickness + 1));
        if (_selectedContentHost.Content != null)
        {
            if (contentWidth <= 0 && ActualWidth > 2)
                contentWidth = Math.Max(0, ActualWidth - (IsVerticalTabStrip ? stripThickness + 1 : 2));
            if (contentHeight <= 0 && ActualHeight > 2)
                contentHeight = Math.Max(0, ActualHeight - (IsVerticalTabStrip ? 2 : stripThickness + 1));

            _selectedContentHost.Measure(new Size(contentWidth, contentHeight));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureItemsHostOrientation();

        var stripThickness = GetEffectiveTabStripThickness(finalSize);
        var stripViewport = IsVerticalTabStrip
            ? new Size(stripThickness, finalSize.Height)
            : new Size(finalSize.Width, stripThickness);
        RefreshTabStripScrollMetrics(stripViewport);

        var (stripRect, contentRect, headerViewportRect, scrollBarRect) = ComputeLayoutRects(finalSize);
        _tabStripRect = stripRect;
        _contentRect = contentRect;
        _tabHeaderViewportRect = headerViewportRect;
        _tabScrollBarRect = scrollBarRect;

        if (ItemsHost != null)
        {
            Rect hostRect;
            if (IsVerticalTabStrip)
            {
                var hostHeight = Math.Max(_tabStripExtent, headerViewportRect.Height);
                hostRect = new Rect(headerViewportRect.X, headerViewportRect.Y - _tabStripScrollOffset, headerViewportRect.Width, hostHeight);
            }
            else
            {
                var hostWidth = Math.Max(_tabStripExtent, headerViewportRect.Width);
                hostRect = new Rect(headerViewportRect.X - _tabStripScrollOffset, headerViewportRect.Y, hostWidth, headerViewportRect.Height);
            }

            ItemsHost.Arrange(hostRect);
        }

        if (_tabStripScrollBar.Visibility == Visibility.Visible && scrollBarRect.Width > 0 && scrollBarRect.Height > 0)
            _tabStripScrollBar.Arrange(scrollBarRect);
        else
            _tabStripScrollBar.Arrange(new Rect(0, 0, 0, 0));

        _selectedContentHost.Measure(new Size(contentRect.Width, contentRect.Height));
        _selectedContentHost.Arrange(contentRect);

        return finalSize;
    }

    public override int VisualChildrenCount
    {
        get
        {
            int count = 0;
            if (ItemsHost != null) count++;
            count++;
            count++;
            return count;
        }
    }

    public override Visual? GetVisualChild(int index)
    {
        int current = 0;
        if (ItemsHost != null)
        {
            if (index == current) return ItemsHost;
            current++;
        }
        if (index == current) return _selectedContentHost;
        current++;
        if (index == current) return _tabStripScrollBar;
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    private bool TryGetSelectedTabBounds(out double selectedTabX, out double selectedTabWidth)
    {
        selectedTabX = 0;
        selectedTabWidth = 0;

        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return false;
        if (Items[SelectedIndex] is not DockItem selectedItem)
            return false;
        if (selectedItem.ActualWidth <= 0)
            return false;

        var pos = selectedItem.TransformToAncestor(this);
        selectedTabX = pos.X;
        selectedTabWidth = selectedItem.ActualWidth;
        return true;
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var panelBackground = ResolvePanelBackgroundBrush();
        var tabStripBrush = ResolveTabStripBackgroundBrush();
        var borderBrush = ResolveTabStripBorderBrush();

        // Paint a full backdrop first so anti-aliased rounded edges do not blend with
        // an uninitialized/parent surface (which can appear as white halos).
        dc.DrawRectangle(panelBackground, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var stripRect = _tabStripRect.Width > 0 && _tabStripRect.Height > 0
            ? _tabStripRect
            : GetTabStripInteractionRect();

        if (TabStripPlacement == Dock.Top)
        {
            var topY = Math.Clamp(stripRect.Bottom, 0, ActualHeight);
            var contentHeight = Math.Max(0, ActualHeight - topY);
            if (contentHeight > 0)
            {
                dc.DrawRoundedRectangle(
                    panelBackground,
                    null,
                    new Rect(0, topY, ActualWidth, contentHeight),
                    new CornerRadius(4, 4, 4, 4));
            }

            var hasActiveTab = TryGetSelectedTabBounds(out var activeTabX, out var activeTabWidth);
            if (hasActiveTab)
            {
                var activeLeft = Math.Clamp(activeTabX, stripRect.X, stripRect.Right);
                var activeRight = Math.Clamp(activeTabX + activeTabWidth, stripRect.X, stripRect.Right);
                var clampedWidth = Math.Max(0, activeRight - activeLeft);
                hasActiveTab = clampedWidth > 0;
                activeTabX = activeLeft;
                activeTabWidth = clampedWidth;
            }

            if (stripRect.Width > 0 && stripRect.Height > 0)
            {
                // Draw tab-strip background but leave the selected tab gap, same as the legacy look.
                if (hasActiveTab)
                {
                    if (activeTabX > stripRect.X)
                        dc.DrawRectangle(tabStripBrush, null, new Rect(stripRect.X, stripRect.Y, activeTabX - stripRect.X, stripRect.Height));
                    var rightStart = activeTabX + activeTabWidth;
                    if (rightStart < stripRect.Right)
                        dc.DrawRectangle(tabStripBrush, null, new Rect(rightStart, stripRect.Y, stripRect.Right - rightStart, stripRect.Height));
                }
                else
                {
                    dc.DrawRectangle(tabStripBrush, null, stripRect);
                }
            }
        }
        else
        {
            if (stripRect.Width > 0 && stripRect.Height > 0)
                dc.DrawRectangle(tabStripBrush, null, stripRect);

            if (_contentRect.Width > 0 && _contentRect.Height > 0)
                dc.DrawRectangle(panelBackground, null, _contentRect);

            var separatorPen = new Pen(borderBrush, 1);
            switch (TabStripPlacement)
            {
                case Dock.Bottom:
                    dc.DrawLine(separatorPen, new Point(0, stripRect.Y), new Point(ActualWidth, stripRect.Y));
                    break;
                case Dock.Left:
                    dc.DrawLine(separatorPen, new Point(stripRect.Right, 0), new Point(stripRect.Right, ActualHeight));
                    break;
                case Dock.Right:
                    dc.DrawLine(separatorPen, new Point(stripRect.X, 0), new Point(stripRect.X, ActualHeight));
                    break;
                default:
                    dc.DrawLine(separatorPen, new Point(0, stripRect.Bottom), new Point(ActualWidth, stripRect.Bottom));
                    break;
            }
        }

        if (IsDockHighlighted)
        {
            var highlightPen = new Pen(ResolveAccentBrush(), 2);
            dc.DrawRoundedRectangle(null, highlightPen, new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2)), 4, 4);
        }

        base.OnRender(drawingContextObj);
    }

    protected override void OnPostRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc) return;
        if (TabStripPlacement == Dock.Top)
        {
            var stripRect = _tabStripRect.Width > 0 && _tabStripRect.Height > 0
                ? _tabStripRect
                : GetTabStripInteractionRect();
            var topY = Math.Clamp(stripRect.Bottom, 0, ActualHeight);
            if (Math.Max(0, ActualHeight - topY) <= 0)
                return;

            var topBorderBrush = ResolveTabStripBorderBrush();
            var topBorderPen = new Pen(topBorderBrush, 1)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            var halfStroke = topBorderPen.Thickness * 0.5;
            var leftEdge = halfStroke;
            var topEdge = halfStroke;
            var rightEdge = Math.Max(leftEdge, ActualWidth - halfStroke);
            var bottomEdge = Math.Max(topEdge, ActualHeight - halfStroke);
            var contentRightEdge = rightEdge;
            var contentBottomEdge = bottomEdge;
            if (_contentRect.Width > 0 && _contentRect.Height > 0)
            {
                contentRightEdge = Math.Clamp(_contentRect.Right - halfStroke, leftEdge, rightEdge);
                contentBottomEdge = Math.Clamp(_contentRect.Bottom - halfStroke, topEdge, bottomEdge);
            }

            if (contentRightEdge <= leftEdge || contentBottomEdge <= topEdge)
                return;

            var hasActiveTab = TryGetSelectedTabBounds(out var selectedTabX, out var selectedTabWidth);
            if (hasActiveTab)
            {
                var originalTabX = selectedTabX;
                var clampedLeft = Math.Clamp(originalTabX, leftEdge, contentRightEdge);
                var clampedRight = Math.Clamp(originalTabX + selectedTabWidth, leftEdge, contentRightEdge);
                selectedTabX = clampedLeft;
                selectedTabWidth = Math.Max(0, clampedRight - clampedLeft);
                hasActiveTab = selectedTabWidth > 0;
            }

            topY = Math.Clamp(topY, topEdge, contentBottomEdge);
            double w = contentRightEdge;
            double h = contentBottomEdge;
            const double contentR = 4;
            const double tabTopR = 4;
            const double tabBottomR = 4;
            const double k = 0.5522847498; // cubic bezier approximation for quarter circle
            var firstTabAtLeftEdge = hasActiveTab && selectedTabX <= leftEdge + contentR + 0.5;
            var startY = firstTabAtLeftEdge ? topY : topY + contentR;

            var borderFigure = new PathFigure
            {
                StartPoint = new Point(leftEdge, startY),
                IsClosed = true,
                IsFilled = false,
            };

            if (!firstTabAtLeftEdge)
            {
                // Top-left content corner: left edge -> top edge
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(leftEdge, topY + contentR * (1 - k)),
                    new Point(leftEdge + contentR * (1 - k), topY),
                    new Point(leftEdge + contentR, topY)));
            }

            if (hasActiveTab)
            {
                double tx = selectedTabX;
                double tw = selectedTabWidth;

                // First tab touching left edge should use a square bottom-left corner.
                var isLeftEdgeTab = tx <= leftEdge + contentR + 0.5;
                if (isLeftEdgeTab)
                {
                    var tabRight = tx + tw;
                    tx = leftEdge;
                    tw = Math.Max(0, Math.Min(tabRight, w) - tx);
                }
                var canUseLeftOutward = !isLeftEdgeTab && tx - tabBottomR >= leftEdge + contentR;
                var leftJoinX = canUseLeftOutward
                    ? tx - tabBottomR
                    : (isLeftEdgeTab ? tx : Math.Clamp(tx + tabBottomR, leftEdge + contentR, w - contentR));
                if (!isLeftEdgeTab)
                    borderFigure.Segments.Add(new LineSegment(new Point(leftJoinX, topY)));

                // Selected tab bottom-left corner: bottom edge -> left edge
                if (isLeftEdgeTab)
                {
                    borderFigure.Segments.Add(new LineSegment(new Point(tx, topY - tabBottomR)));
                }
                else if (canUseLeftOutward)
                {
                    borderFigure.Segments.Add(new BezierSegment(
                        new Point(tx - tabBottomR * (1 - k), topY),
                        new Point(tx, topY - tabBottomR * (1 - k)),
                        new Point(tx, topY - tabBottomR)));
                }
                else
                {
                    borderFigure.Segments.Add(new BezierSegment(
                        new Point(tx + tabBottomR * (1 - k), topY),
                        new Point(tx, topY - tabBottomR * (1 - k)),
                        new Point(tx, topY - tabBottomR)));
                }

                // Selected tab left edge
                borderFigure.Segments.Add(new LineSegment(new Point(tx, topEdge + tabTopR)));

                // Selected tab top-left corner: left edge -> top edge
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(tx, topEdge + tabTopR * (1 - k)),
                    new Point(tx + tabTopR * (1 - k), topEdge),
                    new Point(tx + tabTopR, topEdge)));

                // Selected tab top edge
                borderFigure.Segments.Add(new LineSegment(new Point(tx + tw - tabTopR, topEdge)));

                // Selected tab top-right corner: top edge -> right edge
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(tx + tw - tabTopR * (1 - k), topEdge),
                    new Point(tx + tw, topEdge + tabTopR * (1 - k)),
                    new Point(tx + tw, topEdge + tabTopR)));

                // Selected tab right edge
                borderFigure.Segments.Add(new LineSegment(new Point(tx + tw, topY - tabBottomR)));

                // Selected tab bottom-right corner: right edge -> bottom edge
                var canUseRightOutward = tx + tw + tabBottomR <= w - contentR;
                if (canUseRightOutward)
                {
                    borderFigure.Segments.Add(new BezierSegment(
                        new Point(tx + tw, topY - tabBottomR * (1 - k)),
                        new Point(tx + tw + tabBottomR * (1 - k), topY),
                        new Point(tx + tw + tabBottomR, topY)));
                }
                else
                {
                    borderFigure.Segments.Add(new BezierSegment(
                        new Point(tx + tw, topY - tabBottomR * (1 - k)),
                        new Point(tx + tw - tabBottomR * (1 - k), topY),
                        new Point(tx + tw - tabBottomR, topY)));
                }
            }

            // Continue top edge to top-right content corner
            borderFigure.Segments.Add(new LineSegment(new Point(w - contentR, topY)));

            // Top-right content corner: top edge -> right edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(w - contentR * (1 - k), topY),
                new Point(w, topY + contentR * (1 - k)),
                new Point(w, topY + contentR)));

            // Right edge
            borderFigure.Segments.Add(new LineSegment(new Point(w, h - contentR)));

            // Bottom-right content corner: right edge -> bottom edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(w, h - contentR * (1 - k)),
                new Point(w - contentR * (1 - k), h),
                new Point(w - contentR, h)));

            // Bottom edge
            borderFigure.Segments.Add(new LineSegment(new Point(leftEdge + contentR, h)));

            // Bottom-left content corner: bottom edge -> left edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(leftEdge + contentR * (1 - k), h),
                new Point(leftEdge, h - contentR * (1 - k)),
                new Point(leftEdge, h - contentR)));

            var borderGeometry = new PathGeometry();
            borderGeometry.Figures.Add(borderFigure);
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight))));
            dc.DrawGeometry(null, topBorderPen, borderGeometry);
            dc.Pop();
            return;
        }

        if (_contentRect.Width <= 0 || _contentRect.Height <= 0) return;
        var borderBrush = ResolveTabStripBorderBrush();
        var borderPen = new Pen(borderBrush, 1);
        var half = borderPen.Thickness * 0.5;
        var left = _contentRect.X + half;
        var top = _contentRect.Y + half;
        var right = _contentRect.Right - half;
        var bottom = _contentRect.Bottom - half;
        if (right <= left || bottom <= top)
            return;

        switch (TabStripPlacement)
        {
            case Dock.Bottom:
                dc.DrawLine(borderPen, new Point(left, top), new Point(right, top));
                dc.DrawLine(borderPen, new Point(left, top), new Point(left, bottom));
                dc.DrawLine(borderPen, new Point(right, top), new Point(right, bottom));
                break;
            case Dock.Left:
                dc.DrawLine(borderPen, new Point(left, top), new Point(right, top));
                dc.DrawLine(borderPen, new Point(right, top), new Point(right, bottom));
                dc.DrawLine(borderPen, new Point(left, bottom), new Point(right, bottom));
                break;
            case Dock.Right:
                dc.DrawLine(borderPen, new Point(left, top), new Point(right, top));
                dc.DrawLine(borderPen, new Point(left, top), new Point(left, bottom));
                dc.DrawLine(borderPen, new Point(left, bottom), new Point(right, bottom));
                break;
            default:
                dc.DrawLine(borderPen, new Point(left, top), new Point(left, bottom));
                dc.DrawLine(borderPen, new Point(left, bottom), new Point(right, bottom));
                dc.DrawLine(borderPen, new Point(right, top), new Point(right, bottom));
                break;
        }
    }

    private Brush ResolvePanelBackgroundBrush()
    {
        if (HasLocalValue(Control.BackgroundProperty) && Background != null)
            return Background;

        return ResolveBrush("OneBackgroundPrimary", "DockContentBackground", s_fallbackPanelBackgroundBrush);
    }

    private Brush ResolveTabStripBackgroundBrush()
    {
        if (HasLocalValue(TabStripBackgroundProperty) && TabStripBackground != null)
            return TabStripBackground;

        return ResolveBrush("OneTabBackground", "DockTabStripBackground", s_fallbackTabStripBrush);
    }

    private Brush ResolveTabStripBorderBrush()
    {
        if (HasLocalValue(TabStripBorderBrushProperty) && TabStripBorderBrush != null)
            return TabStripBorderBrush;

        return ResolveBrush("OneBorderDefault", "DockTabStripBorder", s_fallbackBorderBrush);
    }

    private Brush ResolveAccentBrush()
    {
        return ResolveBrush("OneTabActiveBorder", "AccentBrush", s_fallbackAccentBrush);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}

/// <summary>
/// Event args for <see cref="DockTabPanel.ItemClosed"/>.
/// </summary>
public sealed class DockItemClosedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the closed item.
    /// </summary>
    public DockItem Item { get; }

    /// <summary>
    /// Gets the index of the item before closing.
    /// </summary>
    public int Index { get; }

    public DockItemClosedEventArgs(DockItem item, int index)
    {
        Item = item;
        Index = index;
    }
}

