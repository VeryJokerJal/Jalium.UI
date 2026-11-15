using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// A panel that displays multiple <see cref="DockItem"/> children as tabs.
/// Used within <see cref="DockSplitPanel"/> or <see cref="DockLayout"/>.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Items")]
public partial class DockTabPanel : Selector
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Tab);

    private static readonly SolidColorBrush s_fallbackPanelBackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly SolidColorBrush s_fallbackTabStripBrush = new(Color.FromRgb(0x18, 0x18, 0x26));
    private static readonly SolidColorBrush s_fallbackBorderBrush = new(Color.FromRgb(0x3C, 0x3C, 0x4D));
    private static readonly SolidColorBrush s_fallbackAccentBrush = new(Color.FromRgb(0x7A, 0xA2, 0xF7));
    private const double TabStripScrollBarThickness = 10.0;
    private const double DefaultDockCornerRadius = 8.0;
    private const double DefaultDockTabJoinRadius = 6.0;

    // Cached pens for OnRender/OnPostRender to avoid per-frame allocation
    private Pen? _separatorPen;
    private Brush? _separatorPenBrush;
    private Pen? _highlightPen;
    private Brush? _highlightPenBrush;
    private Pen? _topBorderPen;
    private Brush? _topBorderPenBrush;
    private RenderPenCache _contentBorderPen;

    /// <summary>
    /// Occurs when a tab is explicitly closed via tab close action.
    /// Reorder operations do not raise this event.
    /// </summary>
    public event EventHandler<DockItemClosedEventArgs>? ItemClosed;

    #region Dependency Properties

    private static readonly DependencyPropertyKey IsPanelFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsPanelFocused), typeof(bool), typeof(DockTabPanel),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsPanelFocusedProperty = IsPanelFocusedPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets whether this panel or any of its content currently has focus.
    /// Used to switch between focused (accent) and unfocused (neutral) visual states.
    /// </summary>
    public bool IsPanelFocused => (bool)GetValue(IsPanelFocusedProperty)!;

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
        get => (double)GetValue(TabStripHeightProperty)!;
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

    private ContentControl? _selectedContentHost;
    private bool _isDockHighlighted;
    private ScrollBar? _tabStripScrollBar;
    private double _tabStripScrollOffset;
    private double _tabStripExtent;
    private double _tabStripViewport;
    private bool _isTabStripScrollable;
    private bool _ensureSelectedTabVisible;
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

    internal Rect TabHeadersViewportRect => ToOwnerRect(_tabHeaderViewportRect);

    internal double TabStripScrollOffsetForTesting => _tabStripScrollOffset;

    internal bool IsTabStripScrollableForTesting => _tabStripScrollBar?.Visibility == Visibility.Visible;

    internal Rect TabStripScrollBarRectForTesting => ToOwnerRect(_tabScrollBarRect);

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
        set => _isDockHighlighted = value;
        // NOTE: no InvalidateVisual() — invalidating a large panel triggers expensive
        // re-rendering of all child controls. DockIndicatorWindow provides visual feedback.
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
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        DefaultStyleKey = typeof(DockTabPanel);
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));

        ((System.Collections.Specialized.INotifyCollectionChanged)Items).CollectionChanged += OnDockItemsChanged;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnPanelMouseDown), true);

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

    protected override bool IsItemItsOwnContainerOverride(object item) => item is DockItem;

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
        InvalidateDockMeasure();
        InvalidateDockVisual();
    }

    internal Rect GetTabStripInteractionRect() => ToOwnerRect(GetLocalTabStripRect());

    private Rect GetLocalTabStripRect()
    {
        if (_tabHeaderViewportRect.Width > 0 && _tabHeaderViewportRect.Height > 0)
            return _tabHeaderViewportRect;
        var size = DockAreaSize;
        var stripThickness = GetEffectiveTabStripThickness(size);
        return TabStripPlacement switch
        {
            Dock.Bottom => new Rect(0, Math.Max(0, size.Height - stripThickness), size.Width, stripThickness),
            Dock.Left => new Rect(0, 0, stripThickness, size.Height),
            Dock.Right => new Rect(Math.Max(0, size.Width - stripThickness), 0, stripThickness, size.Height),
            _ => new Rect(0, 0, size.Width, stripThickness),
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
        SetHostedContent(null);

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
        if (ReferenceEquals(_selectedContentHost?.Content, element))
        {
            SetHostedContent(null);
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

        if (item.Content is UIElement contentElement)
            DetachContentFromDockPanel(contentElement, this);
        SetValue(SelectedContentProperty, item.Content);

        SetHostedContent(item.Content);

        InvalidateDockMeasure();
        InvalidateDockArrange();
        InvalidateDockVisual();
    }

    protected override void UpdateContainerSelection()
    {
        // Update IsSelected on all DockItems
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is DockItem dockItem)
                dockItem.IsSelected = (i == SelectedIndex);
        }

        SetHostedContent(null);

        // Update selected content
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            var selectedItem = Items[SelectedIndex];
            if (selectedItem is DockItem dockItem)
            {
                if (dockItem.Content is UIElement contentElement)
                    DetachContentFromDockPanel(contentElement, this);
                SetValue(SelectedContentProperty, dockItem.Content);

                SetHostedContent(dockItem.Content);
            }
        }
        else
        {
            SetValue(SelectedContentProperty, null);
            SetHostedContent(null);
        }

        _ensureSelectedTabVisible = true;
        EnsureSelectedTabVisibleCore();
        InvalidateDockMeasure();
        InvalidateDockArrange();
        InvalidateDockVisual();
    }

    private static void OnTabStripPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            panel._tabStripScrollOffset = 0;
            panel._ensureSelectedTabVisible = true;
            foreach (var item in panel.Items.OfType<DockItem>().Concat(panel.ItemsHost?.Children.OfType<DockItem>() ?? Enumerable.Empty<DockItem>()).Distinct())
                item.RefreshDockPlacement();
            panel.EnsureItemsHostOrientation();
            panel.InvalidateDockMeasure();
            panel.InvalidateDockArrange();
            panel.InvalidateDockVisual();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
            panel.InvalidateDockVisual();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            panel.InvalidateDockMeasure();
            panel.InvalidateDockArrange();
            panel.InvalidateDockVisual();
        }
    }

    private static void OnSelectedContentTransitionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DockTabPanel panel)
        {
            if (panel._selectedContentHost is TransitioningContentControl host)
                host.SetCurrentValue(TransitioningContentControl.TransitionModeProperty, e.NewValue);
            panel.InvalidateDockVisual();
        }
    }

    private void OnTabStripScroll(object? sender, ScrollEventArgs e)
    {
        if (ReferenceEquals(sender, _tabStripScrollBar) && _tabStripScrollBar is { } bar)
            SetTabStripScrollOffsetInternal(bar.Value, invalidateLayout: true);
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (_tabStripScrollBar is not { Visibility: Visibility.Visible } bar)
            return;

        var pos = e.GetPosition(this);
        if (!GetTabStripInteractionRect().Contains(pos))
            return;

        var delta = e.Delta > 0
            ? -Math.Max(12, bar.SmallChange * 2)
            : Math.Max(12, bar.SmallChange * 2);

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

        _tabStripScrollBar?.SetCurrentValue(ScrollBar.OrientationProperty, IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal);
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
        var previousViewport = _tabStripViewport;
        var selectedWasVisible = IsSelectedTabFullyVisible();
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
        if (_ensureSelectedTabVisible || (selectedWasVisible && Math.Abs(previousViewport - _tabStripViewport) > 0.5))
            EnsureSelectedTabVisibleCore();
        _ensureSelectedTabVisible = false;
    }

    private bool IsSelectedTabFullyVisible()
    {
        if (_tabStripViewport <= 0 || SelectedIndex < 0 || SelectedIndex >= Items.Count ||
            Items[SelectedIndex] is not DockItem selectedItem) return false;
        double start = 0;
        for (var i = 0; i < SelectedIndex; i++)
            if (Items[i] is DockItem item) start += GetTabPrimaryLength(item);
        return start >= _tabStripScrollOffset - 0.5 &&
            start + GetTabPrimaryLength(selectedItem) <= _tabStripScrollOffset + _tabStripViewport + 0.5;
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
            InvalidateDockArrange();
            InvalidateDockVisual();
        }
    }

    private void SyncTabStripScrollBarValue()
    {
        if (_tabStripScrollBar is { } bar && Math.Abs(bar.Value - _tabStripScrollOffset) > 0.1)
            bar.SetCurrentValue(RangeBase.ValueProperty, _tabStripScrollOffset);
    }

    private void UpdateTabStripScrollBarMetrics()
    {
        var maxOffset = Math.Max(0, _tabStripExtent - _tabStripViewport);
        var show = _isTabStripScrollable && _tabStripViewport > 0 && maxOffset > 0.5;
        if (!show) _tabStripScrollOffset = 0;
        if (_tabStripScrollBar is { } bar)
        {
            bar.SetCurrentValue(ScrollBar.OrientationProperty, IsVerticalTabStrip ? Orientation.Vertical : Orientation.Horizontal);
            bar.SetCurrentValue(RangeBase.MinimumProperty, 0d);
            bar.SetCurrentValue(RangeBase.MaximumProperty, maxOffset);
            bar.SetCurrentValue(ScrollBar.ViewportSizeProperty, Math.Max(0, _tabStripViewport));
            var largeChange = Math.Max(1, Math.Min(_tabStripViewport, maxOffset));
            bar.SetCurrentValue(RangeBase.LargeChangeProperty, largeChange);
            bar.SetCurrentValue(RangeBase.SmallChangeProperty, Math.Max(8, largeChange / 8));
            bar.SetCurrentValue(VisibilityProperty, show ? Visibility.Visible : Visibility.Collapsed);
        }
        SyncTabStripScrollBarValue();
        UpdateDockVisualStates();
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

        if (_tabStripScrollBar?.Visibility == Visibility.Visible)
        {
            if (IsVerticalTabStrip)
            {
                var width = Math.Min(GetTabScrollBarThickness(), stripRect.Width);
                if (width > 0)
                {
                    scrollBarRect = TabStripPlacement == Dock.Left
                        ? new Rect(stripRect.Right - width, stripRect.Y, width, stripRect.Height)
                        : new Rect(stripRect.X, stripRect.Y, width, stripRect.Height);
                }
            }
            else
            {
                var height = Math.Min(GetTabScrollBarThickness(), stripRect.Height);
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

    internal Size MeasureDockArea(Size availableSize)
    {
        // Materialize the template-owned items panel before reading header extents.
        var stripThickness = GetEffectiveTabStripThickness(availableSize);
        var headerSize = IsVerticalTabStrip
            ? new Size(stripThickness, double.PositiveInfinity)
            : new Size(double.PositiveInfinity, stripThickness);
        _headerPresenter?.Measure(headerSize);
        EnsureItemsHostOrientation();
        MeasureTabHeaders(stripThickness);
        var stripViewport = IsVerticalTabStrip
            ? new Size(stripThickness, availableSize.Height)
            : new Size(availableSize.Width, stripThickness);
        RefreshTabStripScrollMetrics(stripViewport);
        if (_tabStripScrollBar is { } bar)
            bar.Measure(bar.Visibility == Visibility.Visible
                ? IsVerticalTabStrip
                    ? new Size(Math.Min(stripThickness, TabStripScrollBarThickness), availableSize.Height)
                    : new Size(availableSize.Width, Math.Min(stripThickness, TabStripScrollBarThickness))
                : default);
        var contentWidth = Math.Max(0, availableSize.Width - (IsVerticalTabStrip ? stripThickness + 1 : 2));
        var contentHeight = Math.Max(0, availableSize.Height - (IsVerticalTabStrip ? 2 : stripThickness + 1));
        _selectedContentHost?.Measure(new Size(contentWidth, contentHeight));
        _backgroundChrome?.Measure(availableSize);
        _overlayChrome?.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(ItemsHost?.DesiredSize.Width ?? 0, _selectedContentHost?.DesiredSize.Width ?? 0),
            double.IsFinite(availableSize.Height) ? availableSize.Height : stripThickness + (_selectedContentHost?.DesiredSize.Height ?? 0));
    }

    internal Size ArrangeDockArea(Size finalSize)
    {
        EnsureItemsHostOrientation();
        var stripThickness = GetEffectiveTabStripThickness(finalSize);
        RefreshTabStripScrollMetrics(IsVerticalTabStrip
            ? new Size(stripThickness, finalSize.Height)
            : new Size(finalSize.Width, stripThickness));
        var (stripRect, contentRect, headerViewportRect, scrollBarRect) = ComputeLayoutRects(finalSize);
        _tabStripRect = stripRect; _contentRect = contentRect;
        _tabHeaderViewportRect = headerViewportRect; _tabScrollBarRect = scrollBarRect;
        if (_headerPresenter is { } presenter)
        {
            var hostRect = IsVerticalTabStrip
                ? new Rect(headerViewportRect.X, headerViewportRect.Y - _tabStripScrollOffset,
                    headerViewportRect.Width, Math.Max(_tabStripExtent, headerViewportRect.Height))
                : new Rect(headerViewportRect.X - _tabStripScrollOffset, headerViewportRect.Y,
                    Math.Max(_tabStripExtent, headerViewportRect.Width), headerViewportRect.Height);
            presenter.Arrange(hostRect);
        }
        _tabStripScrollBar?.Arrange(_tabStripScrollBar.Visibility == Visibility.Visible
            ? scrollBarRect : default);
        _selectedContentHost?.Measure(contentRect.Size);
        _selectedContentHost?.Arrange(contentRect);
        var fullRect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        _backgroundChrome?.Arrange(fullRect); _overlayChrome?.Arrange(fullRect);
        InvalidateDockVisual();
        return finalSize;
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

        var pos = selectedItem.TransformToAncestor((FrameworkElement?)_dockLayoutPanel ?? this);
        selectedTabX = pos.X;
        selectedTabWidth = selectedItem.ActualWidth;
        return true;
    }

    private DockItem? GetSelectedDockItem()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return null;

        return Items[SelectedIndex] as DockItem;
    }

    private double ResolveChromeCornerRadius(double width, double height)
    {
        var radius = Math.Max(
            Math.Max(CornerRadius.TopLeft, CornerRadius.TopRight),
            Math.Max(CornerRadius.BottomRight, CornerRadius.BottomLeft));

        if (radius <= 0)
            radius = DefaultDockCornerRadius;

        var maxAllowed = Math.Max(0, Math.Min(width, height) / 2.0);
        return Math.Min(radius, maxAllowed);
    }

    private static double ResolveTabCornerRadius(DockItem? item, double width, double height)
    {
        var itemCornerRadius = item?.CornerRadius ?? default;
        var radius = Math.Max(
            Math.Max(itemCornerRadius.TopLeft, itemCornerRadius.TopRight),
            Math.Max(itemCornerRadius.BottomRight, itemCornerRadius.BottomLeft));

        if (radius <= 0)
            radius = DefaultDockCornerRadius;

        var maxAllowed = Math.Max(0, Math.Min(width, height) / 2.0);
        return Math.Min(radius, maxAllowed);
    }

    private static double ResolveJoinRadius(double desiredRadius, double availableSpan, double availableHeight)
    {
        if (desiredRadius <= 0 || availableSpan <= 0 || availableHeight <= 0)
            return 0;

        return Math.Min(desiredRadius, Math.Min(availableSpan, availableHeight));
    }

    private bool TryGetSelectedTabOutlineMetrics(
        double leftEdge,
        double rightEdge,
        double topEdge,
        double topY,
        double contentCornerRadius,
        out double selectedTabX,
        out double selectedTabWidth,
        out double tabTopLeftRadius,
        out double tabTopRightRadius,
        out double leftJoinRadius,
        out double rightJoinRadius,
        out double gapLeft,
        out double gapRight,
        out bool isLeftEdgeTab)
    {
        selectedTabX = 0;
        selectedTabWidth = 0;
        tabTopLeftRadius = 0;
        tabTopRightRadius = 0;
        leftJoinRadius = 0;
        rightJoinRadius = 0;
        gapLeft = 0;
        gapRight = 0;
        isLeftEdgeTab = false;

        if (!TryGetSelectedTabBounds(out selectedTabX, out selectedTabWidth))
            return false;

        var clampedLeft = Math.Clamp(selectedTabX, leftEdge, rightEdge);
        var clampedRight = Math.Clamp(selectedTabX + selectedTabWidth, leftEdge, rightEdge);
        selectedTabX = clampedLeft;
        selectedTabWidth = Math.Max(0, clampedRight - clampedLeft);
        if (selectedTabWidth <= 0)
            return false;

        var baseTabTopRadius = ResolveTabCornerRadius(GetSelectedDockItem(), selectedTabWidth, Math.Max(0, topY - topEdge));
        var desiredTabJoinRadius = Math.Max(DefaultDockTabJoinRadius, baseTabTopRadius - 2);

        isLeftEdgeTab = selectedTabX <= leftEdge + contentCornerRadius + 0.5;
        if (isLeftEdgeTab)
        {
            var tabRight = selectedTabX + selectedTabWidth;
            selectedTabX = leftEdge;
            selectedTabWidth = Math.Max(0, Math.Min(tabRight, rightEdge) - selectedTabX);
            if (selectedTabWidth <= 0)
                return false;
        }

        tabTopLeftRadius = baseTabTopRadius;
        tabTopRightRadius = baseTabTopRadius;

        var availableLeftJoin = isLeftEdgeTab
            ? 0
            : Math.Max(0, selectedTabX - (leftEdge + contentCornerRadius));
        leftJoinRadius = ResolveJoinRadius(desiredTabJoinRadius, availableLeftJoin, Math.Max(0, topY - topEdge));

        var availableRightJoin = Math.Max(0, (rightEdge - contentCornerRadius) - (selectedTabX + selectedTabWidth));
        rightJoinRadius = ResolveJoinRadius(desiredTabJoinRadius, availableRightJoin, Math.Max(0, topY - topEdge));

        gapLeft = isLeftEdgeTab ? selectedTabX : selectedTabX - leftJoinRadius;
        gapRight = selectedTabX + selectedTabWidth + rightJoinRadius;
        return gapRight > gapLeft;
    }

    internal void RenderDockBackground(DrawingContext drawingContextObj)
    {
        var dc = drawingContextObj;

        var panelBackground = ResolvePanelBackgroundBrush();
        var tabStripBrush = ResolveTabStripBackgroundBrush();
        var borderBrush = ResolveTabStripBorderBrush();

        // Paint a full backdrop first so anti-aliased rounded edges do not blend with
        // an uninitialized/parent surface (which can appear as white halos).
        dc.DrawRectangle(panelBackground, null, new Rect(0, 0, DockAreaSize.Width, DockAreaSize.Height));

        var stripRect = _tabStripRect.Width > 0 && _tabStripRect.Height > 0
            ? _tabStripRect
            : GetLocalTabStripRect();

        if (TabStripPlacement == Dock.Top)
        {
            var topY = Math.Clamp(stripRect.Bottom, 0, DockAreaSize.Height);
            var contentHeight = Math.Max(0, DockAreaSize.Height - topY);
            if (contentHeight > 0)
            {
                var contentRect = _contentRect.Width > 0 && _contentRect.Height > 0
                    ? _contentRect
                    : new Rect(0, topY, DockAreaSize.Width, contentHeight);
                var contentCornerRadius = ResolveChromeCornerRadius(contentRect.Width, contentRect.Height);
                dc.DrawRoundedRectangle(
                    panelBackground,
                    null,
                    contentRect,
                    new CornerRadius(contentCornerRadius));
            }

            // Join curve fills are drawn in OnPostRender (after children) so
            // they appear on top of the tab items.
        }
        else
        {
            if (stripRect.Width > 0 && stripRect.Height > 0)
                dc.DrawRectangle(tabStripBrush, null, stripRect);

            if (_contentRect.Width > 0 && _contentRect.Height > 0)
                dc.DrawRectangle(panelBackground, null, _contentRect);

            if (_separatorPen == null || _separatorPenBrush != borderBrush)
            {
                _separatorPenBrush = borderBrush;
                _separatorPen = new Pen(borderBrush, 1);
            }
            var separatorPen = _separatorPen;
            switch (TabStripPlacement)
            {
                case Dock.Bottom:
                    dc.DrawLine(separatorPen, new Point(0, stripRect.Y), new Point(DockAreaSize.Width, stripRect.Y));
                    break;
                case Dock.Left:
                    dc.DrawLine(separatorPen, new Point(stripRect.Right, 0), new Point(stripRect.Right, DockAreaSize.Height));
                    break;
                case Dock.Right:
                    dc.DrawLine(separatorPen, new Point(stripRect.X, 0), new Point(stripRect.X, DockAreaSize.Height));
                    break;
                default:
                    dc.DrawLine(separatorPen, new Point(0, stripRect.Bottom), new Point(DockAreaSize.Width, stripRect.Bottom));
                    break;
            }
        }

        if (IsDockHighlighted)
        {
            var accentBrush = ResolveAccentBrush();
            if (_highlightPen == null || _highlightPenBrush != accentBrush)
            {
                _highlightPenBrush = accentBrush;
                _highlightPen = new Pen(accentBrush, 2);
            }
            var highlightPen = _highlightPen;
            var highlightRect = new Rect(1, 1, Math.Max(0, DockAreaSize.Width - 2), Math.Max(0, DockAreaSize.Height - 2));
            var highlightCornerRadius = ResolveChromeCornerRadius(highlightRect.Width, highlightRect.Height);
            dc.DrawRoundedRectangle(null, highlightPen, highlightRect, highlightCornerRadius, highlightCornerRadius);
        }


    }

    internal void RenderDockOverlay(DrawingContext drawingContextObj)
    {
        var dc = drawingContextObj;
        if (TabStripPlacement == Dock.Top)
        {
            var stripRect = _tabStripRect.Width > 0 && _tabStripRect.Height > 0
                ? _tabStripRect
                : GetLocalTabStripRect();
            var topY = Math.Clamp(stripRect.Bottom, 0, DockAreaSize.Height);
            if (Math.Max(0, DockAreaSize.Height - topY) <= 0)
                return;

            var topBorderBrush = ResolveTabStripBorderBrush();
            if (_topBorderPen == null || _topBorderPenBrush != topBorderBrush)
            {
                _topBorderPenBrush = topBorderBrush;
                _topBorderPen = new Pen(topBorderBrush, 1)
                {
                    LineJoin = PenLineJoin.Round,
                };
            }
            var topBorderPen = _topBorderPen;
            var halfStroke = topBorderPen.Thickness * 0.5;
            var leftEdge = halfStroke;
            var topEdge = halfStroke;
            var rightEdge = Math.Max(leftEdge, DockAreaSize.Width - halfStroke);
            var bottomEdge = Math.Max(topEdge, DockAreaSize.Height - halfStroke);
            var contentRightEdge = rightEdge;
            var contentBottomEdge = bottomEdge;
            if (_contentRect.Width > 0 && _contentRect.Height > 0)
            {
                contentRightEdge = Math.Clamp(_contentRect.Right - halfStroke, leftEdge, rightEdge);
                contentBottomEdge = Math.Clamp(_contentRect.Bottom - halfStroke, topEdge, bottomEdge);
            }

            if (contentRightEdge <= leftEdge || contentBottomEdge <= topEdge)
                return;

            topY = Math.Clamp(topY, topEdge, contentBottomEdge);
            double w = contentRightEdge;
            double h = contentBottomEdge;
            var contentR = ResolveChromeCornerRadius(contentRightEdge - leftEdge, contentBottomEdge - topY);
            var outerBorderRect = new Rect(leftEdge, topY, Math.Max(0, w - leftEdge), Math.Max(0, h - topY));
            const double k = 0.5522847498; // cubic bezier approximation for quarter circle

            if (!TryGetSelectedTabOutlineMetrics(
                    leftEdge,
                    w,
                    topEdge,
                    topY,
                    contentR,
                    out var selectedTabX,
                    out var selectedTabWidth,
                    out var tabTopLeftR,
                    out var tabTopRightR,
                    out var leftJoinR,
                    out var rightJoinR,
                    out var gapLeft,
                    out var gapRight,
                    out var isLeftEdgeTab))
            {
                // No selected tab — use the crisp SDF rounded-rect for content border.
                dc.DrawRoundedRectangle(null, topBorderPen, outerBorderRect, contentR, contentR);
                return;
            }

            // Fill the concave join curve pockets with the selected tab's background
            // so the tab visually flows into the content area.  This runs in OnPostRender
            // (after child rendering) so the fill paints on top of the tab items.
            var selectedItem = GetSelectedDockItem();
            var joinFillBrush = selectedItem?.ResolveSelectedBackgroundBrush()
                                ?? ResolvePanelBackgroundBrush();
            if (!isLeftEdgeTab && leftJoinR > 0)
            {
                var leftCurve = new PathFigure
                {
                    StartPoint = new Point(gapLeft, topY),
                    IsClosed = true,
                    IsFilled = true,
                };
                leftCurve.Segments.Add(new BezierSegment(
                    new Point(selectedTabX - leftJoinR * (1 - k), topY),
                    new Point(selectedTabX, topY - leftJoinR * (1 - k)),
                    new Point(selectedTabX, topY - leftJoinR)));
                leftCurve.Segments.Add(new LineSegment(new Point(selectedTabX, topY)));
                var leftGeo = new PathGeometry();
                leftGeo.Figures.Add(leftCurve);
                dc.DrawGeometry(joinFillBrush, null, leftGeo);
            }
            if (rightJoinR > 0)
            {
                var rightCurve = new PathFigure
                {
                    StartPoint = new Point(selectedTabX + selectedTabWidth, topY),
                    IsClosed = true,
                    IsFilled = true,
                };
                rightCurve.Segments.Add(new LineSegment(new Point(selectedTabX + selectedTabWidth, topY - rightJoinR)));
                rightCurve.Segments.Add(new BezierSegment(
                    new Point(selectedTabX + selectedTabWidth, topY - rightJoinR * (1 - k)),
                    new Point(selectedTabX + selectedTabWidth + rightJoinR * (1 - k), topY),
                    new Point(gapRight, topY)));
                var rightGeo = new PathGeometry();
                rightGeo.Figures.Add(rightCurve);
                dc.DrawGeometry(joinFillBrush, null, rightGeo);
            }

            // Draw the entire content + tab outline as a single unified path so the
            // stroke thickness and anti-aliasing are perfectly consistent everywhere.
            double tx = selectedTabX;
            double tw = selectedTabWidth;

            var borderFigure = new PathFigure
            {
                StartPoint = new Point(gapRight, topY),
                IsClosed = true,
                IsFilled = false,
            };

            // Content top edge — right of tab gap to top-right corner
            borderFigure.Segments.Add(new LineSegment(new Point(w - contentR, topY)));
            // Content top-right corner
            borderFigure.Segments.Add(new BezierSegment(
                new Point(w - contentR * (1 - k), topY),
                new Point(w, topY + contentR * (1 - k)),
                new Point(w, topY + contentR)));
            // Content right side
            borderFigure.Segments.Add(new LineSegment(new Point(w, h - contentR)));
            // Content bottom-right corner
            borderFigure.Segments.Add(new BezierSegment(
                new Point(w, h - contentR * (1 - k)),
                new Point(w - contentR * (1 - k), h),
                new Point(w - contentR, h)));
            // Content bottom edge
            borderFigure.Segments.Add(new LineSegment(new Point(leftEdge + contentR, h)));
            // Content bottom-left corner
            borderFigure.Segments.Add(new BezierSegment(
                new Point(leftEdge + contentR * (1 - k), h),
                new Point(leftEdge, h - contentR * (1 - k)),
                new Point(leftEdge, h - contentR)));

            if (isLeftEdgeTab)
            {
                // Left side goes straight up into the tab — no content top-left corner
                borderFigure.Segments.Add(new LineSegment(new Point(tx, topEdge + tabTopLeftR)));
            }
            else
            {
                // Content left side up to content top-left corner
                borderFigure.Segments.Add(new LineSegment(new Point(leftEdge, topY + contentR)));
                // Content top-left corner
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(leftEdge, topY + contentR * (1 - k)),
                    new Point(leftEdge + contentR * (1 - k), topY),
                    new Point(leftEdge + contentR, topY)));
                // Content top edge — left of tab gap
                borderFigure.Segments.Add(new LineSegment(new Point(gapLeft, topY)));
                // Left join curve (content top edge into tab left side)
                if (leftJoinR > 0)
                {
                    borderFigure.Segments.Add(new BezierSegment(
                        new Point(tx - leftJoinR * (1 - k), topY),
                        new Point(tx, topY - leftJoinR * (1 - k)),
                        new Point(tx, topY - leftJoinR)));
                }
                borderFigure.Segments.Add(new LineSegment(new Point(tx, topEdge + tabTopLeftR)));
            }

            // Tab top-left corner
            if (tabTopLeftR > 0)
            {
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(tx, topEdge + tabTopLeftR * (1 - k)),
                    new Point(tx + tabTopLeftR * (1 - k), topEdge),
                    new Point(tx + tabTopLeftR, topEdge)));
            }
            else
            {
                borderFigure.Segments.Add(new LineSegment(new Point(tx, topEdge)));
            }

            // Tab top edge
            borderFigure.Segments.Add(new LineSegment(new Point(tx + tw - tabTopRightR, topEdge)));
            // Tab top-right corner
            borderFigure.Segments.Add(new BezierSegment(
                new Point(tx + tw - tabTopRightR * (1 - k), topEdge),
                new Point(tx + tw, topEdge + tabTopRightR * (1 - k)),
                new Point(tx + tw, topEdge + tabTopRightR)));
            // Tab right side down to join
            borderFigure.Segments.Add(new LineSegment(new Point(tx + tw, topY - rightJoinR)));
            // Right join curve (tab right side back to content top edge)
            if (rightJoinR > 0)
            {
                borderFigure.Segments.Add(new BezierSegment(
                    new Point(tx + tw, topY - rightJoinR * (1 - k)),
                    new Point(tx + tw + rightJoinR * (1 - k), topY),
                    new Point(gapRight, topY)));
            }

            var borderGeometry = new PathGeometry();
            borderGeometry.Figures.Add(borderFigure);
            dc.DrawGeometry(null, topBorderPen, borderGeometry);
            return;
        }

        if (_contentRect.Width <= 0 || _contentRect.Height <= 0) return;
        var borderBrush = ResolveTabStripBorderBrush();
        var borderPen = _contentBorderPen.Get(borderBrush, 1);
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
        if (IsPanelFocused)
            return ResolveBrush("OneBorderFocused", "DockTabStripBorderFocused", s_fallbackAccentBrush);

        if (HasLocalValue(TabStripBorderBrushProperty) && TabStripBorderBrush != null)
            return TabStripBorderBrush;

        return ResolveBrush("OneBorderDefault", "DockTabStripBorder", s_fallbackBorderBrush);
    }

    internal void SetPanelFocusedInternal(bool focused)
    {
        if (IsPanelFocused == focused) return;
        SetValue(IsPanelFocusedPropertyKey, focused);
        _topBorderPen = null;
        _topBorderPenBrush = null;
        InvalidateDockVisual();
        foreach (var child in Items)
        {
            if (child is DockItem item)
                item.InvalidateVisual();
        }
    }

    private void OnPanelMouseDown(object sender, MouseButtonEventArgs e)
    {
        DockManager.SetActivePanel(this);
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

