using System.Collections;
using System.Reflection;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays hierarchical data in a tree structure.
/// </summary>
public class TreeView : ItemsControl
{
    private TreeViewItem? _selectedItem;

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TreeView),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the SelectedValue dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(TreeView),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the value of the selected item.
    /// </summary>
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Identifies the SelectedItemChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedItemChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedItemChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<object>), typeof(TreeView));

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<object?>? SelectedItemChanged
    {
        add => AddHandler(SelectedItemChangedEvent, value);
        remove => RemoveHandler(SelectedItemChangedEvent, value);
    }

    #endregion

    public TreeView()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
    }

    #endregion

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new VirtualizingStackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return WrapItemAsTreeViewItem(item) ?? new TreeViewItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is TreeViewItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not TreeViewItem tvi)
        {
            return;
        }

        tvi.ParentTreeView = this;
        tvi.Level = 0;

        if (item is TreeViewItem)
        {
            return;
        }

        tvi.Header = item;
        tvi.DataContext = item;

        if (ItemTemplate is HierarchicalDataTemplate hdt)
        {
            TreeViewItem.ApplyHierarchicalDataTemplate(tvi, item, hdt);
        }
        else if (ItemTemplate != null)
        {
            tvi.HeaderTemplate = ItemTemplate;
        }
    }

    /// <summary>
    /// Wraps a data item as a TreeViewItem if it isn't one already.
    /// Applies HierarchicalDataTemplate to configure child items.
    /// </summary>
    private TreeViewItem? WrapItemAsTreeViewItem(object item)
    {
        if (item is TreeViewItem tvi)
            return tvi;

        // Create a container for the data item
        tvi = new TreeViewItem { Header = item, DataContext = item };

        // Apply ItemTemplate
        if (ItemTemplate is HierarchicalDataTemplate hdt)
        {
            TreeViewItem.ApplyHierarchicalDataTemplate(tvi, item, hdt);
        }
        else if (ItemTemplate != null)
        {
            tvi.HeaderTemplate = ItemTemplate;
        }

        return tvi;
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeView treeView)
        {
            var oldItem = e.OldValue as TreeViewItem;
            var newItem = e.NewValue as TreeViewItem;

            // Update selection state
            if (oldItem != null)
            {
                oldItem.IsSelected = false;
            }

            if (newItem != null)
            {
                newItem.IsSelected = true;
                treeView._selectedItem = newItem;
            }
            else
            {
                treeView._selectedItem = null;
            }

            // Raise event
            var args = new RoutedPropertyChangedEventArgs<object?>(
                e.OldValue, e.NewValue, SelectedItemChangedEvent);
            treeView.RaiseEvent(args);
        }
    }

    internal void SelectItem(TreeViewItem? item)
    {
        if (_selectedItem != item)
        {
            SelectedItem = item;
        }
    }
}

/// <summary>
/// Represents an item in a TreeView control.
/// </summary>
public class TreeViewItem : HeaderedItemsControl
{
    private const double IndentSize = 16;
    private const double ExpanderSize = 16;
    private const double ExpandAnimationDurationMs = 260;
    private const double CollapseAnimationDurationMs = 180;
    private const double ClothStaggerProgress = 0.09;
    private static readonly BackEase s_expandHeightEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.85 };
    private static readonly CubicEase s_arrowExpandEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase s_collapseEase = new() { EasingMode = EasingMode.EaseInOut };
    private static readonly BackEase s_clothEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 1.05 };
    private static readonly SolidColorBrush s_fallbackHoverBackgroundBrush = new(Themes.ThemeColors.ControlBackgroundHover);
    private static readonly SolidColorBrush s_fallbackSelectedBackgroundBrush = new(Themes.ThemeColors.SelectionBackground);
    private static readonly SolidColorBrush s_fallbackSelectedHoverBackgroundBrush = new(Themes.ThemeColors.AccentPressed);

    internal TreeView? ParentTreeView { get; set; }

    private int _level;
    private bool _isHeaderMouseOver;

    #region Template Parts

    private Border? _headerBorder;
    private Border? _indentSpacer;
    private Border? _expanderBorder;
    private Shapes.Path? _expanderArrow;
    private StackPanel? _itemsHost;
    private Threading.DispatcherTimer? _expandAnimTimer;
    private bool _suppressChildItemsChanged;
    private long _expandAnimationStartTick;
    private bool _expandAnimationTargetExpanded;
    private double _expandAnimationFromHeight;
    private double _expandAnimationToHeight;
    private double _expandAnimationFromAngle;
    private double _expandAnimationToAngle;
    private ClothChild[] _expandAnimationChildren = [];

    #endregion

    private readonly struct ClothChild
    {
        public ClothChild(UIElement element, double initialY, double progressDelay)
        {
            Element = element;
            InitialY = initialY;
            ProgressDelay = progressDelay;
        }

        public UIElement Element { get; }
        public double InitialY { get; }
        public double ProgressDelay { get; }
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsExpanded dependency property.
    /// </summary>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether this item is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets whether this item has child items.
    /// </summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// Gets or sets the indentation level.
    /// </summary>
    internal int Level
    {
        get => _level;
        set
        {
            if (_level == value)
            {
                return;
            }

            _level = value;
            UpdateIndent();
            UpdateDescendantLevels();
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Identifies the Expanded routed event.
    /// </summary>
    public static readonly RoutedEvent ExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(Expanded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeViewItem));

    /// <summary>
    /// Identifies the Collapsed routed event.
    /// </summary>
    public static readonly RoutedEvent CollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(Collapsed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeViewItem));

    /// <summary>
    /// Occurs when the item is expanded.
    /// </summary>
    public event RoutedEventHandler Expanded
    {
        add => AddHandler(ExpandedEvent, value);
        remove => RemoveHandler(ExpandedEvent, value);
    }

    /// <summary>
    /// Occurs when the item is collapsed.
    /// </summary>
    public event RoutedEventHandler Collapsed
    {
        add => AddHandler(CollapsedEvent, value);
        remove => RemoveHandler(CollapsedEvent, value);
    }

    #endregion

    public TreeViewItem()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        Items.CollectionChanged += OnChildItemsChanged;
        ResourcesChanged += OnResourcesChangedHandler;
    }

    /// <summary>
    /// Override to prevent ItemsControl's fallback panel from interfering.
    /// TreeViewItem manages children via PART_ItemsHost in the template.
    /// </summary>
    protected override void RefreshItems()
    {
        // No-op: TreeViewItem manages children via _itemsHost (template part)
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StopExpandAnimation();

        if (_headerBorder != null)
        {
            _headerBorder.RemoveHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
            _headerBorder.RemoveHandler(MouseEnterEvent, new RoutedEventHandler(OnHeaderMouseEnter));
            _headerBorder.RemoveHandler(MouseLeaveEvent, new RoutedEventHandler(OnHeaderMouseLeave));
        }

        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as Border;
        _expanderBorder = GetTemplateChild("PART_ExpanderBorder") as Border;
        _expanderArrow = GetTemplateChild("PART_ExpanderArrow") as Shapes.Path;
        _itemsHost = GetTemplateChild("PART_ItemsHost") as StackPanel;

        // Attach click handler to header border only (not the whole item)
        // so child item clicks don't bubble up to parent items
        if (_headerBorder != null)
        {
            _headerBorder.AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler), true);
            _headerBorder.AddHandler(MouseEnterEvent, new RoutedEventHandler(OnHeaderMouseEnter), true);
            _headerBorder.AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnHeaderMouseLeave), true);
        }

        // Sync initial state
        UpdateIndent();
        UpdateExpanderVisibility();

        // Add existing items to the items host panel
        if (_itemsHost != null)
        {
            foreach (var item in Items)
            {
                if (item is TreeViewItem childTvi)
                {
                    childTvi.ParentTreeView = ParentTreeView;
                    childTvi.Level = Level + 1;
                    _itemsHost.Children.Add(childTvi);
                }
            }

            // Sync expanded visuals (IsExpanded may have been set before template was applied)
            SyncExpandedVisualState();
        }
        else
        {
            SyncExpandedVisualState();
        }

        UpdateHeaderVisualState();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // IsExpanded may be set before the item is attached to a window.
        // Re-sync once attached so layout/render is guaranteed to update.
        if (VisualParent != null)
        {
            SyncExpandedVisualState();
            InvalidateMeasure();
            InvalidateVisual();
        }
        else
        {
            StopExpandAnimation();
        }
    }

    #endregion

    private void OnChildItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_suppressChildItemsChanged)
        {
            return;
        }

        try
        {
            var needsDescendantRefresh = e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset;

            // Handle removed items
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        _itemsHost?.Children.Remove(childTvi);
                        childTvi.ParentTreeView = null;
                    }
                }
            }

            // Handle added items
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        childTvi.ParentTreeView = ParentTreeView;
                        childTvi.Level = Level + 1;
                        _itemsHost?.Children.Add(childTvi);
                    }
                }
            }

            // Handle reset
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _itemsHost?.Children.Clear();
                foreach (var item in Items)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        childTvi.ParentTreeView = ParentTreeView;
                        childTvi.Level = Level + 1;
                        _itemsHost?.Children.Add(childTvi);
                    }
                }
            }

            UpdateExpanderVisibility();
            if (needsDescendantRefresh)
            {
                UpdateDescendantLevels();
            }

            InvalidateMeasure();
        }
        catch
        {
            // Ignored
        }
    }

    internal void AddChildItems(IEnumerable<TreeViewItem> childItems)
    {
        var bufferedItems = new List<TreeViewItem>();
        foreach (var childItem in childItems)
        {
            bufferedItems.Add(childItem);
        }

        if (bufferedItems.Count == 0)
        {
            return;
        }

        _suppressChildItemsChanged = true;
        try
        {
            foreach (var childItem in bufferedItems)
            {
                Items.Add(childItem);
            }
        }
        finally
        {
            _suppressChildItemsChanged = false;
        }

        foreach (var childItem in bufferedItems)
        {
            childItem.ParentTreeView = ParentTreeView;
            childItem.Level = Level + 1;
            _itemsHost?.Children.Add(childItem);
        }

        UpdateExpanderVisibility();
        InvalidateMeasure();
    }

    private void UpdateDescendantLevels()
    {
        foreach (var item in Items)
        {
            if (item is not TreeViewItem childTvi)
            {
                continue;
            }

            childTvi.ParentTreeView = ParentTreeView;
            childTvi.Level = _level + 1;
        }
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs)
        {
            // Check if clicked on expander area
            if (HasItems && _expanderBorder is { Visibility: Visibility.Visible } expander)
            {
                var pos = mouseArgs.GetPosition(expander);
                if (pos.X >= 0 && pos.X <= expander.ActualWidth &&
                    pos.Y >= 0 && pos.Y <= expander.ActualHeight)
                {
                    IsExpanded = !IsExpanded;
                    e.Handled = true;
                    return;
                }
            }

            // Select this item
            ParentTreeView?.SelectItem(this);
            e.Handled = true;
        }
    }

    private void OnHeaderMouseEnter(object sender, RoutedEventArgs e)
    {
        if (_isHeaderMouseOver)
        {
            return;
        }

        _isHeaderMouseOver = true;
        UpdateHeaderVisualState();
    }

    private void OnHeaderMouseLeave(object sender, RoutedEventArgs e)
    {
        if (!_isHeaderMouseOver)
        {
            return;
        }

        _isHeaderMouseOver = false;
        UpdateHeaderVisualState();
    }

    #region State Updates

    private void UpdateIndent()
    {
        if (_indentSpacer != null)
        {
            _indentSpacer.Width = _level * IndentSize;
        }
    }

    private void UpdateExpanderVisibility()
    {
        if (_expanderBorder != null)
        {
            _expanderBorder.Visibility = HasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        // When children are realized after template application, keep glyph direction
        // in sync with the current expanded state.
        if (_expanderArrow != null && _expandAnimTimer == null)
        {
            SetExpanderAngle(IsExpanded ? 90 : 0);
        }
    }

    private void SyncExpandedVisualState()
    {
        StopExpandAnimation();

        if (_itemsHost != null)
        {
            _itemsHost.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
            _itemsHost.MaxHeight = double.PositiveInfinity;
            _itemsHost.ClipToBounds = false;
        }

        if (_expanderArrow != null)
        {
            SetExpanderAngle(IsExpanded ? 90 : 0);
        }
    }

    private bool ShouldAnimateExpandedStateChange() =>
        _itemsHost != null
        && HasItems
        && VisualParent != null;

    private void BeginExpandedStateAnimation(bool expanded)
    {
        if (_itemsHost == null)
        {
            return;
        }

        var startHeight = GetCurrentItemsHostHeight();
        var targetHeight = expanded ? MeasureItemsHostNaturalHeight() : 0.0;
        var startAngle = GetCurrentExpanderAngle();
        var targetAngle = expanded ? 90.0 : 0.0;

        if (Math.Abs(startHeight - targetHeight) < 0.5 && Math.Abs(startAngle - targetAngle) < 0.5)
        {
            SyncExpandedVisualState();
            return;
        }

        StopExpandAnimation();

        _expandAnimationTargetExpanded = expanded;
        _expandAnimationStartTick = Environment.TickCount64;
        _expandAnimationFromHeight = startHeight;
        _expandAnimationToHeight = targetHeight;
        _expandAnimationFromAngle = startAngle;
        _expandAnimationToAngle = targetAngle;
        _expandAnimationChildren = expanded
            ? CollectClothChildren(targetHeight)
            : [];

        _itemsHost.Visibility = Visibility.Visible;
        _itemsHost.ClipToBounds = true;
        _itemsHost.MaxHeight = Math.Max(0, startHeight);

        _expandAnimTimer = new Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _expandAnimTimer.Tick += OnExpandAnimationTick;
        _expandAnimTimer.Start();

        ApplyExpandAnimationFrame(0);
    }

    private void OnExpandAnimationTick(object? sender, EventArgs e)
    {
        var durationMs = _expandAnimationTargetExpanded
            ? ExpandAnimationDurationMs
            : CollapseAnimationDurationMs;
        var elapsedMs = Math.Max(0, Environment.TickCount64 - _expandAnimationStartTick);
        var progress = durationMs <= 0
            ? 1.0
            : Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

        ApplyExpandAnimationFrame(progress);

        if (progress >= 1.0)
        {
            CompleteExpandAnimation();
        }
    }

    private void ApplyExpandAnimationFrame(double progress)
    {
        if (_itemsHost == null)
        {
            return;
        }

        var easedProgress = _expandAnimationTargetExpanded
            ? s_expandHeightEase.Ease(progress)
            : s_collapseEase.Ease(progress);
        var arrowProgress = _expandAnimationTargetExpanded
            ? s_arrowExpandEase.Ease(progress)
            : s_collapseEase.Ease(progress);

        _itemsHost.MaxHeight = Math.Max(0, Lerp(_expandAnimationFromHeight, _expandAnimationToHeight, easedProgress));
        SetExpanderAngle(Lerp(_expandAnimationFromAngle, _expandAnimationToAngle, arrowProgress));

        if (_expandAnimationTargetExpanded)
        {
            ApplyClothOffsets(progress);
        }
        else
        {
            ClearChildOffsets();
        }

        InvalidateMeasure();
        ParentTreeView?.InvalidateMeasure();
    }

    private void CompleteExpandAnimation()
    {
        StopExpandAnimation();

        if (_itemsHost != null)
        {
            _itemsHost.Visibility = _expandAnimationTargetExpanded ? Visibility.Visible : Visibility.Collapsed;
            _itemsHost.MaxHeight = double.PositiveInfinity;
            _itemsHost.ClipToBounds = false;
        }

        ClearChildOffsets();
        SetExpanderAngle(_expandAnimationTargetExpanded ? 90 : 0);
        InvalidateMeasure();
        ParentTreeView?.InvalidateMeasure();
    }

    private void StopExpandAnimation()
    {
        if (_expandAnimTimer == null && _expandAnimationChildren.Length == 0)
        {
            return;
        }

        if (_expandAnimTimer != null)
        {
            _expandAnimTimer.Stop();
            _expandAnimTimer.Tick -= OnExpandAnimationTick;
            _expandAnimTimer = null;
        }

        ClearChildOffsets();
        _expandAnimationChildren = [];
    }

    private double GetCurrentItemsHostHeight()
    {
        if (_itemsHost == null || _itemsHost.Visibility != Visibility.Visible)
        {
            return 0;
        }

        if (_itemsHost.ActualHeight > 0)
        {
            return _itemsHost.ActualHeight;
        }

        if (!double.IsInfinity(_itemsHost.MaxHeight))
        {
            return Math.Max(0, _itemsHost.MaxHeight);
        }

        return MeasureItemsHostNaturalHeight();
    }

    private double MeasureItemsHostNaturalHeight()
    {
        if (_itemsHost == null)
        {
            return 0;
        }

        var previousVisibility = _itemsHost.Visibility;
        var previousMaxHeight = _itemsHost.MaxHeight;
        var previousClipToBounds = _itemsHost.ClipToBounds;

        var availableWidth = _itemsHost.ActualWidth > 0
            ? _itemsHost.ActualWidth
            : (ActualWidth > 0 ? ActualWidth : double.PositiveInfinity);

        _itemsHost.Visibility = Visibility.Visible;
        _itemsHost.MaxHeight = double.PositiveInfinity;
        _itemsHost.ClipToBounds = false;
        _itemsHost.Measure(new Size(availableWidth, double.PositiveInfinity));
        var desiredHeight = _itemsHost.DesiredSize.Height;

        _itemsHost.Visibility = previousVisibility;
        _itemsHost.MaxHeight = previousMaxHeight;
        _itemsHost.ClipToBounds = previousClipToBounds;

        return Math.Max(0, desiredHeight);
    }

    private double GetCurrentExpanderAngle()
    {
        if (_expanderArrow?.RenderTransform is RotateTransform rotateTransform)
        {
            return rotateTransform.Angle;
        }

        return IsExpanded ? 90 : 0;
    }

    private void SetExpanderAngle(double angle)
    {
        if (_expanderArrow == null)
        {
            return;
        }

        var rotateTransform = _expanderArrow.RenderTransform as RotateTransform ?? new RotateTransform();
        rotateTransform.Angle = angle;
        _expanderArrow.RenderTransform = rotateTransform;
        _expanderArrow.InvalidateVisual();
    }

    private ClothChild[] CollectClothChildren(double targetHeight)
    {
        if (_itemsHost == null || _itemsHost.Children.Count == 0)
        {
            return [];
        }

        var children = new List<UIElement>();
        foreach (var child in _itemsHost.Children)
        {
            if (child is UIElement uiElement && uiElement.Visibility == Visibility.Visible)
            {
                children.Add(uiElement);
            }
        }

        if (children.Count == 0)
        {
            return [];
        }

        var baseOffset = Math.Min(Math.Max(12.0, targetHeight * 0.22), 36.0);
        var result = new ClothChild[children.Count];
        for (int i = 0; i < children.Count; i++)
        {
            var normalizedIndex = (i + 1.0) / children.Count;
            result[i] = new ClothChild(
                children[i],
                -baseOffset * normalizedIndex,
                Math.Min(0.45, i * ClothStaggerProgress));
        }

        return result;
    }

    private void ApplyClothOffsets(double progress)
    {
        if (_expandAnimationChildren.Length == 0)
        {
            return;
        }

        for (int i = 0; i < _expandAnimationChildren.Length; i++)
        {
            var child = _expandAnimationChildren[i];
            var localProgress = child.ProgressDelay >= 1.0
                ? 1.0
                : Math.Clamp((progress - child.ProgressDelay) / (1.0 - child.ProgressDelay), 0.0, 1.0);
            var eased = s_clothEase.Ease(localProgress);
            child.Element.RenderOffset = new Point(0, child.InitialY * (1.0 - eased));
        }
    }

    private void ClearChildOffsets()
    {
        if (_expandAnimationChildren.Length == 0)
        {
            return;
        }

        for (int i = 0; i < _expandAnimationChildren.Length; i++)
        {
            _expandAnimationChildren[i].Element.RenderOffset = default;
        }
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * progress);

    private void UpdateHeaderVisualState()
    {
        if (_headerBorder == null)
        {
            return;
        }

        if (IsSelected && _isHeaderMouseOver)
        {
            _headerBorder.Background = ResolveSelectedHoverBackgroundBrush();
            return;
        }

        if (IsSelected)
        {
            _headerBorder.Background = ResolveSelectedBackgroundBrush();
            return;
        }

        if (_isHeaderMouseOver)
        {
            _headerBorder.Background = ResolveHoverBackgroundBrush();
            return;
        }

        _headerBorder.ClearValue(Border.BackgroundProperty);
    }

    private Brush ResolveHoverBackgroundBrush()
        => TryFindResource("ControlBackgroundHover") as Brush ?? s_fallbackHoverBackgroundBrush;

    private Brush ResolveSelectedBackgroundBrush()
        => TryFindResource("SelectionBackground") as Brush ?? s_fallbackSelectedBackgroundBrush;

    private Brush ResolveSelectedHoverBackgroundBrush()
        => TryFindResource("AccentBrushPressed") as Brush ?? s_fallbackSelectedHoverBackgroundBrush;

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        UpdateHeaderVisualState();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            var expanded = (bool)e.NewValue;

            if (expanded)
                tvi.RaiseEvent(new RoutedEventArgs(ExpandedEvent, tvi));
            else
                tvi.RaiseEvent(new RoutedEventArgs(CollapsedEvent, tvi));

            if (tvi.ShouldAnimateExpandedStateChange())
            {
                tvi.BeginExpandedStateAnimation(expanded);
            }
            else
            {
                tvi.SyncExpandedVisualState();
            }

            tvi.InvalidateMeasure();
            tvi.ParentTreeView?.InvalidateMeasure();
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            if ((bool)e.NewValue && tvi.ParentTreeView != null)
            {
                tvi.ParentTreeView.SelectItem(tvi);
            }

            tvi.UpdateHeaderVisualState();
        }
    }

    #endregion

    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
        // TreeView item visuals are driven by header-local hover state.
        // Avoid invalidating the full expanded subtree whenever the pointer
        // moves across descendants inside the item.
    }

    #region HierarchicalDataTemplate Support

    /// <summary>
    /// Applies a HierarchicalDataTemplate to a TreeViewItem, setting up header template
    /// and populating child items from the ItemsSource binding.
    /// </summary>
    internal static void ApplyHierarchicalDataTemplate(TreeViewItem tvi, object dataItem, HierarchicalDataTemplate hdt)
    {
        // Set the header template
        tvi.Header = dataItem;
        tvi.HeaderTemplate = hdt;

        // Apply ItemTemplate for child items
        if (hdt.ItemTemplate != null)
            tvi.ItemTemplate = hdt.ItemTemplate;
        else
            tvi.ItemTemplate = hdt; // Recursive: same template for children

        // Resolve ItemsSource binding to populate children
        if (hdt.ItemsSource is Jalium.UI.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
        {
            var childItems = ResolvePropertyPath(dataItem, binding.Path.Path);
            if (childItems is IEnumerable enumerable)
            {
                foreach (var childItem in enumerable)
                {
                    if (childItem is TreeViewItem childTvi)
                    {
                        tvi.Items.Add(childTvi);
                    }
                    else
                    {
                        var childContainer = new TreeViewItem
                        {
                            Header = childItem,
                            DataContext = childItem
                        };

                        // Recursively apply the template to children
                        var childTemplate = hdt.ItemTemplate as HierarchicalDataTemplate ?? hdt;
                        ApplyHierarchicalDataTemplate(childContainer, childItem, childTemplate);

                        tvi.Items.Add(childContainer);
                    }
                }
            }
        }
    }

    private static object? ResolvePropertyPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }

    #endregion
}

// Note: HeaderedItemsControl is defined in Menu.cs
