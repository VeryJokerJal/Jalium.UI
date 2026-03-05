using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A panel that displays multiple <see cref="DockItem"/> children as tabs.
/// Used within <see cref="DockSplitPanel"/> or <see cref="DockLayout"/>.
/// </summary>
[ContentProperty("Items")]
public sealed class DockTabPanel : Selector
{
    private static readonly SolidColorBrush s_fallbackPanelBackgroundBrush = new(Color.FromRgb(0x1E, 0x1E, 0x2E));
    private static readonly SolidColorBrush s_fallbackTabStripBrush = new(Color.FromRgb(0x18, 0x18, 0x26));
    private static readonly SolidColorBrush s_fallbackAccentBrush = new(Color.FromRgb(0x7A, 0xA2, 0xF7));

    /// <summary>
    /// Occurs when a tab is explicitly closed via tab close action.
    /// Reorder operations do not raise this event.
    /// </summary>
    public event EventHandler<DockItemClosedEventArgs>? ItemClosed;

    #region Dependency Properties

    public static readonly DependencyProperty SelectedContentProperty =
        DependencyProperty.Register(nameof(SelectedContent), typeof(object), typeof(DockTabPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TabStripHeightProperty =
        DependencyProperty.Register(nameof(TabStripHeight), typeof(double), typeof(DockTabPanel),
            new PropertyMetadata(28.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty TabStripBackgroundProperty =
        DependencyProperty.Register(nameof(TabStripBackground), typeof(Brush), typeof(DockTabPanel),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty TabStripBorderBrushProperty =
        DependencyProperty.Register(nameof(TabStripBorderBrush), typeof(Brush), typeof(DockTabPanel),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Properties

    public object? SelectedContent
    {
        get => GetValue(SelectedContentProperty);
        set => SetValue(SelectedContentProperty, value);
    }

    public double TabStripHeight
    {
        get => (double)GetValue(TabStripHeightProperty);
        set => SetValue(TabStripHeightProperty, value);
    }

    public Brush? TabStripBackground
    {
        get => (Brush?)GetValue(TabStripBackgroundProperty);
        set => SetValue(TabStripBackgroundProperty, value);
    }

    public Brush? TabStripBorderBrush
    {
        get => (Brush?)GetValue(TabStripBorderBrushProperty);
        set => SetValue(TabStripBorderBrushProperty, value);
    }

    #endregion

    private UIElement? _selectedContentElement;
    private bool _isDockHighlighted;

    /// <summary>
    /// Indicates this panel is inside a floating window (not docked).
    /// Floating panels are excluded from dock-back hit-testing targets.
    /// </summary>
    internal bool IsFloating { get; set; }

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
        var mouseX = mouseInPanel.X;

        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i] is not DockItem item) continue;

            var itemPos = item.TransformToAncestor(this);
            var midX = itemPos.X + item.ActualWidth / 2;

            if (mouseX < midX)
            {
                // Insert before tab i 鈥?skip if no actual change
                if (i == _reorderDragItemIndex || i == _reorderDragItemIndex + 1)
                    return -1;
                return i;
            }
        }

        // After all tabs 鈥?skip if dragged tab is already last
        if (_reorderDragItemIndex == Items.Count - 1)
            return -1;
        return Items.Count;
    }

    #endregion

    public DockTabPanel()
    {
        MinWidth = 100;
        MinHeight = 100;

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
        return new StackPanel { Orientation = Orientation.Horizontal };
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

        InvalidateMeasure();
        InvalidateVisual();
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

        // Always detach old selected content from visual tree first.
        // This is needed because setting SelectedIndex to the same value
        // won't trigger UpdateContainerSelection, leaving stale content parented.
        if (_selectedContentElement != null)
        {
            RemoveVisualChild(_selectedContentElement);
            _selectedContentElement = null;
        }

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
                // SelectedIndex didn't change 鈥?force content update manually
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
        if (_selectedContentElement == element)
        {
            RemoveVisualChild(_selectedContentElement);
            _selectedContentElement = null;
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

        // Remove stale content
        if (_selectedContentElement != null)
        {
            RemoveVisualChild(_selectedContentElement);
            _selectedContentElement = null;
        }

        SetValue(SelectedContentProperty, item.Content);

        if (item.Content is UIElement contentElement)
        {
            // Force-detach from old panel if still attached
            if (contentElement.VisualParent is DockTabPanel oldPanel && oldPanel != this)
                oldPanel.ReleaseContentElement(contentElement);

            if (contentElement.VisualParent == null)
            {
                _selectedContentElement = contentElement;
                AddVisualChild(contentElement);
            }
        }

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

        // Remove old content from visual tree
        if (_selectedContentElement != null)
        {
            RemoveVisualChild(_selectedContentElement);
            _selectedContentElement = null;
        }

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

                    if (contentElement.VisualParent == null)
                    {
                        _selectedContentElement = contentElement;
                        AddVisualChild(contentElement);
                    }
                }
            }
        }
        else
        {
            SetValue(SelectedContentProperty, null);
        }

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
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
            panel.InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (ItemsHost == null)
            RefreshItems();

        var tabStripHeight = TabStripHeight;

        // Measure tab headers
        if (ItemsHost != null)
        {
            ItemsHost.Measure(new Size(availableSize.Width, tabStripHeight));
        }
        else
        {
            // Fallback when ItemsHost has not been created yet.
            // Use unconstrained width so each tab can size to its header text.
            foreach (var item in Items)
            {
                if (item is DockItem dockItem)
                    dockItem.Measure(new Size(double.PositiveInfinity, tabStripHeight));
            }
        }

        // Measure selected content (inset 1px for content border on left/right/bottom)
        var contentHeight = Math.Max(0, availableSize.Height - tabStripHeight - 1);
        var contentWidth = Math.Max(0, availableSize.Width - 2);
        if (_selectedContentElement is UIElement contentElement)
        {
            // Reparent/switch can transiently report an undersized available size for one pass.
            // Prefer current arranged size as fallback to avoid measuring content at near-zero width.
            if (contentWidth <= 0 && ActualWidth > 2)
                contentWidth = Math.Max(0, ActualWidth - 2);
            if (contentHeight <= 0 && ActualHeight > tabStripHeight + 1)
                contentHeight = Math.Max(0, ActualHeight - tabStripHeight - 1);

            contentElement.Measure(new Size(contentWidth, contentHeight));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tabStripHeight = TabStripHeight;

        // Arrange tab strip at top
        if (ItemsHost != null)
            ItemsHost.Arrange(new Rect(0, 0, finalSize.Width, tabStripHeight));

        // Arrange content below (inset 1px for border on left/right/bottom, top seamless to active tab)
        var contentRect = new Rect(1, tabStripHeight, Math.Max(0, finalSize.Width - 2), Math.Max(0, finalSize.Height - tabStripHeight - 1));
        if (_selectedContentElement is UIElement contentElement)
        {
            // Keep measure/arrange paired for switched content so it doesn't render stale geometry
            // until a later unrelated invalidation arrives.
            contentElement.Measure(new Size(contentRect.Width, contentRect.Height));
            contentElement.Arrange(contentRect);
        }

        return finalSize;
    }

    public override int VisualChildrenCount
    {
        get
        {
            int count = 0;
            if (ItemsHost != null) count++;
            if (_selectedContentElement != null) count++;
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
        if (_selectedContentElement != null)
        {
            if (index == current) return _selectedContentElement;
            current++;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var tabStripHeight = TabStripHeight;
        var panelBackground = Background ?? ResolveBrush("OneBackgroundPrimary", "DockContentBackground", s_fallbackPanelBackgroundBrush);
        var tabStripBrush = TabStripBackground ?? ResolveBrush("OneTabBackground", "DockTabStripBackground", s_fallbackTabStripBrush);

        // Paint a full backdrop first so anti-aliased rounded edges do not blend with
        // an uninitialized/parent surface (which can appear as white halos).
        dc.DrawRectangle(panelBackground, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Calculate active tab position
        double activeTabX = 0, activeTabWidth = 0;
        bool hasActiveTab = false;
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            for (int i = 0; i < SelectedIndex; i++)
            {
                if (Items[i] is DockItem item)
                    activeTabX += item.ActualWidth;
            }
            if (Items[SelectedIndex] is DockItem selItem && selItem.ActualWidth > 0)
            {
                activeTabWidth = selItem.ActualWidth;
                hasActiveTab = true;
            }
        }

        // 1. Fill content area with full rounded corners (including top-left/top-right)
        var contentHeight = Math.Max(0, ActualHeight - tabStripHeight);
        if (contentHeight > 0)
        {
            dc.DrawRoundedRectangle(
                panelBackground,
                null,
                new Rect(0, tabStripHeight, ActualWidth, contentHeight),
                new CornerRadius(4, 4, 4, 4));
        }

        // 2. Draw tab strip background, SKIPPING the active tab
        if (hasActiveTab)
        {
            if (activeTabX > 0)
                dc.DrawRectangle(tabStripBrush, null, new Rect(0, 0, activeTabX, tabStripHeight));
            var rightStart = activeTabX + activeTabWidth;
            if (rightStart < ActualWidth)
                dc.DrawRectangle(tabStripBrush, null, new Rect(rightStart, 0, ActualWidth - rightStart, tabStripHeight));
        }
        else
        {
            dc.DrawRectangle(tabStripBrush, null, new Rect(0, 0, ActualWidth, tabStripHeight));
        }

        // Dock highlight border
        if (IsDockHighlighted)
        {
            var highlightPen = new Pen(ResolveBrush("OneTabActiveBorder", "AccentBrush", s_fallbackAccentBrush), 2);
            dc.DrawRoundedRectangle(null, highlightPen, new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 4, 4);
        }

        base.OnRender(drawingContextObj);
    }

    /// <summary>
    /// Draws the complete border AFTER children (so it's on top of tab backgrounds).
    /// </summary>
    protected override void OnPostRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc) return;

        var tabStripHeight = TabStripHeight;
        var contentHeight = Math.Max(0, ActualHeight - tabStripHeight);
        if (contentHeight <= 0) return;

        var accentBrush = ResolveBrush("OneTabActiveBorder", "AccentBrush", s_fallbackAccentBrush);
        var borderPen = new Pen(accentBrush, 1);
        var halfStroke = borderPen.Thickness * 0.5;
        var left = halfStroke;
        var top = halfStroke;
        var right = Math.Max(left, ActualWidth - halfStroke);
        var bottom = Math.Max(top, ActualHeight - halfStroke);

        if (right <= left || bottom <= top)
            return;

        // Recalculate active tab position
        double activeTabX = 0, activeTabWidth = 0;
        bool hasActiveTab = false;
        if (SelectedIndex >= 0 && SelectedIndex < Items.Count)
        {
            for (int i = 0; i < SelectedIndex; i++)
            {
                if (Items[i] is DockItem item)
                    activeTabX += item.ActualWidth;
            }
            if (Items[SelectedIndex] is DockItem selItem && selItem.ActualWidth > 0)
            {
                activeTabWidth = selItem.ActualWidth;
                hasActiveTab = true;
            }
        }

        double selectedTabX = activeTabX;
        double selectedTabWidth = activeTabWidth;
        if (hasActiveTab)
        {
            selectedTabX = Math.Clamp(activeTabX, left, right);
            var selectedTabRight = Math.Clamp(activeTabX + activeTabWidth, left, right);
            selectedTabWidth = Math.Max(0, selectedTabRight - selectedTabX);
            hasActiveTab = selectedTabWidth > 0;
        }

        double topY = Math.Clamp(tabStripHeight, top, bottom);
        double w = right;
        double h = bottom;
        const double contentR = 4;
        const double tabTopR = 4;
        const double tabBottomR = 4;
        const double k = 0.5522847498; // cubic bezier approximation for quarter circle
        var firstTabAtLeftEdge = hasActiveTab && selectedTabX <= left + contentR + 0.5;
        var startY = firstTabAtLeftEdge ? topY : topY + contentR;

        var borderFigure = new PathFigure
        {
            StartPoint = new Point(left, startY),
            IsClosed = false,
            IsFilled = false,
        };

        if (firstTabAtLeftEdge)
        {
            // First selected tab touching the left edge should connect with a square corner.
            borderFigure.Segments.Add(new LineSegment(new Point(left, topY)));
        }
        else
        {
            // Top-left content corner: left edge -> top edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(left, topY + contentR * (1 - k)),
                new Point(left + contentR * (1 - k), topY),
                new Point(left + contentR, topY)));
        }

        if (hasActiveTab)
        {
            double tx = selectedTabX;
            double tw = selectedTabWidth;

            // First tab touching left edge should use a square bottom-left corner.
            var isLeftEdgeTab = tx <= left + contentR + 0.5;
            var canUseLeftOutward = !isLeftEdgeTab && tx - tabBottomR >= left + contentR;
            var leftJoinX = canUseLeftOutward
                ? tx - tabBottomR
                : (isLeftEdgeTab ? tx : Math.Clamp(tx + tabBottomR, left + contentR, w - contentR));
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
            borderFigure.Segments.Add(new LineSegment(new Point(tx, top + tabTopR)));

            // Selected tab top-left corner: left edge -> top edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(tx, top + tabTopR * (1 - k)),
                new Point(tx + tabTopR * (1 - k), top),
                new Point(tx + tabTopR, top)));

            // Selected tab top edge
            borderFigure.Segments.Add(new LineSegment(new Point(tx + tw - tabTopR, top)));

            // Selected tab top-right corner: top edge -> right edge
            borderFigure.Segments.Add(new BezierSegment(
                new Point(tx + tw - tabTopR * (1 - k), top),
                new Point(tx + tw, top + tabTopR * (1 - k)),
                new Point(tx + tw, top + tabTopR)));

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
        borderFigure.Segments.Add(new LineSegment(new Point(left + contentR, h)));

        // Bottom-left content corner: bottom edge -> left edge
        borderFigure.Segments.Add(new BezierSegment(
            new Point(left + contentR * (1 - k), h),
            new Point(left, h - contentR * (1 - k)),
            new Point(left, h - contentR)));

        // Left edge back to start
        borderFigure.Segments.Add(new LineSegment(new Point(left, startY)));

        var borderGeometry = new PathGeometry();
        borderGeometry.Figures.Add(borderFigure);
        dc.DrawGeometry(null, borderPen, borderGeometry);
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
