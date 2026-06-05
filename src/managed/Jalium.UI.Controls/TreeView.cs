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
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TreeViewAutomationPeer(this);
    }

    private TreeViewItem? _selectedItem;
    internal Style? _cachedTreeViewItemStyle;

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TreeView),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the SelectedValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(TreeView),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the value of the selected item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
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
        add { if (value is not null) AddHandler(SelectedItemChangedEvent, value); }
        remove { if (value is not null) RemoveHandler(SelectedItemChangedEvent, value); }
    }

    /// <summary>
    /// Identifies the <see cref="ItemDoubleClicked"/> routed event.
    /// </summary>
    public static readonly RoutedEvent ItemDoubleClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(ItemDoubleClicked), RoutingStrategy.Bubble,
            typeof(EventHandler<TreeViewItemDoubleClickedEventArgs>), typeof(TreeView));

    /// <summary>
    /// 当 <see cref="TreeViewItem"/> 被左键双击时触发。事件参数携带容器和数据上下文，
    /// 调用方无需再用 <see cref="SelectedItem"/> 取值。
    /// </summary>
    public event EventHandler<TreeViewItemDoubleClickedEventArgs>? ItemDoubleClicked
    {
        add { if (value is not null) AddHandler(ItemDoubleClickedEvent, value); }
        remove { if (value is not null) RemoveHandler(ItemDoubleClickedEvent, value); }
    }

    /// <summary>
    /// 由 <see cref="TreeViewItem.OnMouseDoubleClick"/> 调用 — 用容器和 DataContext
    /// 构造高层事件参数并 raise <see cref="ItemDoubleClickedEvent"/>。
    /// </summary>
    internal void RaiseItemDoubleClicked(TreeViewItem container, MouseButtonEventArgs source)
    {
        if (container == null) return;

        var args = new TreeViewItemDoubleClickedEventArgs(
            ItemDoubleClickedEvent, this, container, container.DataContext, source);
        RaiseEvent(args);
        if (args.Handled) source.Handled = true;
    }

    /// <summary>
    /// Identifies the <see cref="ItemContextMenuRequested"/> routed event.
    /// </summary>
    public static readonly RoutedEvent ItemContextMenuRequestedEvent =
        EventManager.RegisterRoutedEvent(nameof(ItemContextMenuRequested), RoutingStrategy.Bubble,
            typeof(EventHandler<TreeViewItemContextMenuRequestedEventArgs>), typeof(TreeView));

    /// <summary>
    /// 当 <see cref="TreeViewItem"/> 被右键点击（请求上下文菜单）时触发。事件参数携带容器、
    /// 数据上下文和原始鼠标参数，订阅方据此构建并弹出右键菜单。
    /// </summary>
    public event EventHandler<TreeViewItemContextMenuRequestedEventArgs>? ItemContextMenuRequested
    {
        add { if (value is not null) AddHandler(ItemContextMenuRequestedEvent, value); }
        remove { if (value is not null) RemoveHandler(ItemContextMenuRequestedEvent, value); }
    }

    /// <summary>
    /// 由 <see cref="TreeViewItem.OnMouseRightButtonUp"/> 调用 — 用容器和 DataContext
    /// 构造高层事件参数并在 TreeView 上 raise <see cref="ItemContextMenuRequestedEvent"/>。<br/>
    /// 与 <see cref="RaiseItemDoubleClicked"/> 同理：TreeViewItem 的鼠标事件冒泡在
    /// <see cref="VirtualizingStackPanel"/> 处断裂、无法自然到达 TreeView，故采用"容器直接
    /// 回调 ParentTreeView"的转发方式，而非依赖路由冒泡。
    /// </summary>
    internal void RaiseItemContextMenuRequested(TreeViewItem container, MouseButtonEventArgs source)
    {
        if (container == null) return;

        var args = new TreeViewItemContextMenuRequestedEventArgs(
            ItemContextMenuRequestedEvent, this, container, container.DataContext, source);
        RaiseEvent(args);
        if (args.Handled) source.Handled = true;
    }

    #endregion

    public TreeView()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        Focusable = true;

        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }

        AddHandler(KeyDownEvent, new KeyEventHandler(OnTreeViewKeyDown));
    }

    private ScrollViewer? _scrollViewer;

    /// <summary>
    /// 内部使用 — TreeViewItem 在 wheel 事件触发时调用此方法把事件转发给 TreeView 内部
    /// 的 ScrollViewer。这是为了绕开 visual tree 在 VirtualizingStackPanel 处断裂的
    /// 框架 bug —— 正常 wheel event 应该沿 visual tree bubble 到 ScrollViewer，
    /// 但 VSP._parent = null 让 bubble 链断在 VSP，事件无法到达 ScrollViewer。
    /// </summary>
    internal void ForwardMouseWheel(MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        if (_scrollViewer == null) return;

        // 用 RaiseEvent 把事件重新派发到 ScrollViewer。
        // RoutedEvent 的 OriginalSource 保留指向真实点击源，与正常 bubble 等效。
        _scrollViewer.RaiseEvent(e);
    }

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
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
        // 仅创建容器；HierarchicalDataTemplate 的应用统一交给 PrepareContainerForItem，
        // 避免双重 apply 导致 BridgeDataItemBoolStateToTreeViewItem 重复订阅、
        // _childrenRealized 状态机被重置以及 EnsureChildrenRealized 跑两遍生成重复子项。
        return item is TreeViewItem tvi ? tvi : new TreeViewItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is TreeViewItem;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "ApplyHierarchicalDataTemplate is reached only when a consumer assigns a HierarchicalDataTemplate to ItemTemplate and binds TreeView to user-defined data items. Its RUC contract — \"HierarchicalDataTemplate ItemsSource may resolve via PropertyAccessorRegistry reflection on data items\" — is the documented prerequisite for that opt-in feature; consumers binding reflective data types must keep their child/state properties preserved (or register typed accessors via PropertyAccessorRegistry), not a defect of this container-preparation site.")]
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not TreeViewItem tvi)
        {
            return;
        }

        // Pre-apply cached TreeViewItem style to avoid expensive implicit style lookup
        // when the container enters the visual tree. The first container resolves the
        // style; subsequent containers reuse the cached result.
        if (tvi.Style == null)
        {
            _cachedTreeViewItemStyle ??= tvi.TryFindResource(typeof(TreeViewItem)) as Style;
            if (_cachedTreeViewItemStyle != null)
            {
                tvi.Style = _cachedTreeViewItemStyle;
            }
        }

        tvi.ParentTreeView = this;
        tvi.ParentItem = null;
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

    internal bool FocusAdjacentVisibleItem(TreeViewItem currentItem, int direction)
    {
        var visibleItems = GetVisibleItems();
        var currentIndex = visibleItems.IndexOf(currentItem);
        if (currentIndex < 0)
        {
            return false;
        }

        var nextIndex = currentIndex + direction;
        if (nextIndex < 0 || nextIndex >= visibleItems.Count)
        {
            return false;
        }

        return visibleItems[nextIndex].Focus();
    }

    internal bool FocusBoundaryVisibleItem(bool last)
    {
        var visibleItems = GetVisibleItems();
        if (visibleItems.Count == 0)
        {
            return false;
        }

        return (last ? visibleItems[^1] : visibleItems[0]).Focus();
    }

    private void OnTreeViewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (Keyboard.FocusedElement is TreeViewItem)
        {
            return;
        }

        var handled = e.Key switch
        {
            Key.Down or Key.Right or Key.Home => FocusBoundaryVisibleItem(last: false),
            Key.Up or Key.Left or Key.End => FocusBoundaryVisibleItem(last: true),
            _ => false
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    private List<TreeViewItem> GetVisibleItems()
    {
        var items = new List<TreeViewItem>();
        if (ItemsHost == null)
        {
            return items;
        }

        CollectVisibleItems(ItemsHost, items);
        return items;
    }

    private static void CollectVisibleItems(Panel panel, List<TreeViewItem> items)
    {
        foreach (var child in panel.Children)
        {
            if (child is not TreeViewItem treeViewItem || treeViewItem.Visibility != Visibility.Visible)
            {
                continue;
            }

            items.Add(treeViewItem);

            if (!treeViewItem.IsExpanded)
            {
                continue;
            }

            var childHost = treeViewItem.GetItemsHostPanel();
            if (childHost != null)
            {
                CollectVisibleItems(childHost, items);
            }
        }
    }
}

/// <summary>
/// Represents an item in a TreeView control.
/// </summary>
public class TreeViewItem : HeaderedItemsControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TreeViewItemAutomationPeer(this);
    }

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
    internal TreeViewItem? ParentItem { get; set; }

    private int _level;
    private bool _isHeaderMouseOver;

    // Deferred child loading: store data source + template, create children only on expand
    private object? _deferredItemsSource;
    private HierarchicalDataTemplate? _deferredTemplate;
    private bool _childrenRealized;

    // 桥接的 dataItem（HierarchicalDataTemplate 模式下背后的 ViewModel 节点）。
    // IsExpanded / IsSelected 的双向同步通过 DP PropertyChanged 回调直接写回 dataItem，
    // 而不再依赖 ExpandedEvent / CollapsedEvent — 因为这两个事件是 Bubble 路由策略，
    // 父节点的桥接 handler 会被子节点冒泡的事件错误触发，导致整条父链连锁收起。
    private object? _bridgedDataItem;
    private System.ComponentModel.PropertyChangedEventHandler? _bridgedINPCHandler;

    #region Template Parts

    private Border? _headerBorder;
    private Border? _indentSpacer;
    private Border? _expanderBorder;
    private Shapes.Path? _expanderArrow;
    private FrameworkElement? _itemsHost; // ItemsPresenter from template (controls visibility/clipping)
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether this item is expanded.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets whether this item has child items (including deferred/not-yet-realized children).
    /// </summary>
    public bool HasItems => Items.Count > 0 || _deferredItemsSource != null;

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
        Focusable = true;
        Items.CollectionChanged += OnChildItemsChanged;
        ResourcesChanged += OnResourcesChangedHandler;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));

        // 把 wheel 事件转发给 ParentTreeView 内部的 ScrollViewer。
        // 必须显式转发因为 framework 的 visual tree 在 VirtualizingStackPanel 处断裂
        // (VSP.VisualParent = null), wheel 事件从 _headerBorder 起 bubble 到 VSP 就停了，
        // 永远不会自然到达 ScrollViewer.OnMouseWheel。
        // 订阅 MouseWheelEvent (bubble) 而非 PreviewMouseWheelEvent (tunnel) —
        // ScrollViewer 注册的 handler 是 MouseWheelEvent，事件类型必须一致才能转发命中。
        AddHandler(MouseWheelEvent, new Input.MouseWheelEventHandler((s, e) =>
        {
            ParentTreeView?.ForwardMouseWheel(e);
        }), handledEventsToo: true);
    }

    /// <summary>
    /// Defer template application when this TreeViewItem is inside a collapsed
    /// parent (i.e. it hasn't been measured yet). This prevents cascading template
    /// instantiation through entire collapsed subtrees during initial load.
    /// The template will be applied on the first MeasureOverride instead.
    /// </summary>
    internal override bool ShouldDeferTemplateApplication()
    {
        // If we don't have a visual parent yet (not in visual tree), defer.
        // If our parent's items host is collapsed, defer.
        // Otherwise apply eagerly (e.g. root items, items being expanded).
        if (VisualParent == null)
        {
            return true;
        }

        // Walk up to find the items host container (ItemsPresenter) and check its visibility.
        for (var ancestor = VisualParent; ancestor != null; ancestor = ancestor.VisualParent)
        {
            if (ancestor is FrameworkElement fe && fe.Visibility == Visibility.Collapsed)
            {
                return true;
            }

            // Stop at the parent TreeViewItem — no need to walk further
            if (ancestor is TreeViewItem)
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Like WPF, only populate the items panel when the node is expanded.
    /// Collapsed nodes defer panel population until first expand, preventing
    /// cascading container creation through the entire tree.
    /// </summary>
    protected override void RefreshItems()
    {
        if (!IsExpanded)
        {
            return; // Defer population until expand
        }

        base.RefreshItems();
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return item is TreeViewItem ? (FrameworkElement)item : new TreeViewItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is TreeViewItem;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "ApplyHierarchicalDataTemplate is reached only when a consumer assigns a HierarchicalDataTemplate (directly or inherited from the parent TreeView) and binds child nodes to user-defined data items. Its RUC contract — \"HierarchicalDataTemplate ItemsSource may resolve via PropertyAccessorRegistry reflection on data items\" — is the documented prerequisite for that opt-in feature; consumers binding reflective data types must keep their child/state properties preserved (or register typed accessors via PropertyAccessorRegistry), not a defect of this container-preparation site.")]
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not TreeViewItem childTvi)
        {
            return;
        }

        // Set hierarchy metadata (like WPF's PrepareItemContainer)
        childTvi.ParentTreeView = ParentTreeView;
        childTvi.ParentItem = this;
        childTvi.Level = Level + 1;

        // Pre-apply cached style to avoid per-item implicit style lookup
        if (childTvi.Style == null && ParentTreeView?._cachedTreeViewItemStyle != null)
        {
            childTvi.Style = ParentTreeView._cachedTreeViewItemStyle;
        }

        if (item is TreeViewItem)
        {
            return;
        }

        childTvi.Header = item;
        childTvi.DataContext = item;

        if (ItemTemplate is HierarchicalDataTemplate hdt)
        {
            TreeViewItem.ApplyHierarchicalDataTemplate(childTvi, item, hdt);
        }
        else if (ParentTreeView?.ItemTemplate is HierarchicalDataTemplate parentHdt)
        {
            var childTemplate = parentHdt.ItemTemplate as HierarchicalDataTemplate ?? parentHdt;
            TreeViewItem.ApplyHierarchicalDataTemplate(childTvi, item, childTemplate);
        }
        else if (ItemTemplate != null)
        {
            childTvi.HeaderTemplate = ItemTemplate;
        }
    }

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        StopExpandAnimation();

        if (_headerBorder != null)
        {
            _headerBorder.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
            _headerBorder.RemoveHandler(MouseEnterEvent, new MouseEventHandler(OnHeaderMouseEnter));
            _headerBorder.RemoveHandler(MouseLeaveEvent, new MouseEventHandler(OnHeaderMouseLeave));
            _headerBorder.RemoveHandler(TouchDownEvent, new RoutedEventHandler(OnHeaderTouchDown));
            _headerBorder.RemoveHandler(TouchMoveEvent, new RoutedEventHandler(OnHeaderTouchMove));
            _headerBorder.RemoveHandler(TouchUpEvent, new RoutedEventHandler(OnHeaderTouchUp));
        }

        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as Border;
        _expanderBorder = GetTemplateChild("PART_ExpanderBorder") as Border;
        _expanderArrow = GetTemplateChild("PART_ExpanderArrow") as Shapes.Path;
        _itemsHost = GetTemplateChild("PART_ItemsHost") as FrameworkElement;

        // Attach click handler to header border only (not the whole item)
        // so child item clicks don't bubble up to parent items
        if (_headerBorder != null)
        {
            _headerBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), true);
            _headerBorder.AddHandler(MouseEnterEvent, new MouseEventHandler(OnHeaderMouseEnter), true);
            _headerBorder.AddHandler(MouseLeaveEvent, new MouseEventHandler(OnHeaderMouseLeave), true);
            _headerBorder.AddHandler(TouchDownEvent, new RoutedEventHandler(OnHeaderTouchDown), true);
            _headerBorder.AddHandler(TouchMoveEvent, new RoutedEventHandler(OnHeaderTouchMove), true);
            _headerBorder.AddHandler(TouchUpEvent, new RoutedEventHandler(OnHeaderTouchUp), true);
            TouchHelper.SetIsRippleEnabled(_headerBorder, true);
        }

        // Sync initial state
        UpdateIndent();
        UpdateExpanderVisibility();

        // Sync expanded visuals (IsExpanded may have been set before template was applied)
        SyncExpandedVisualState();

        // If expanded, populate children through the standard pipeline.
        // ItemsPresenter (PART_ItemsHost) will call SetItemsPresenter which
        // triggers RefreshItems — but only if IsExpanded (our override).
        // For collapsed items, children remain deferred until first expand.

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

    // 注：原本这里有一段 HitTestCore override 试图过滤"layout 拉伸到 header / items 之外
    // 区域内的击中"，但实现有两个 bug：
    //   1. 坐标系混淆 — HitTestCore 的 point 是当前元素局部坐标，
    //      element.VisualBounds 却是相对父级坐标，IsPointWithinElementBounds
    //      Contains 比对得出错误结果。
    //   2. 过滤过激 — 当 result.VisualHit == this 时把不在两个 PART 区域里的点击
    //      一律 return null，吞掉了 ScrollBar 拖拽 / Hover Enter-Leave / 子内容空隙
    //      所有"非精确命中 PART"事件。叠加后整个 TreeView 子树鼠标交互不响应。
    // 移除该 override 让 TreeViewItem 走默认 HitTestCore：默认 TreeViewItem.Background
    // 是 Transparent (=不命中)，所以原 override 想防止的"空白处吞 click"问题
    // 在标准链路下本来就不会发生。

    #endregion

    private void OnChildItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_suppressChildItemsChanged)
        {
            return;
        }

        // Clean up removed items' hierarchy metadata
        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is TreeViewItem childTvi)
                {
                    childTvi.ParentTreeView = null;
                    childTvi.ParentItem = null;
                }
            }
        }

        // Panel child management is handled by the standard ItemsControl pipeline
        // (base.OnItemsCollectionChanged → RefreshItems / AddItemToPanel).
        // We only need to maintain metadata here.
        UpdateExpanderVisibility();
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            UpdateDescendantLevels();
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

        // Use AddRange to fire a single Reset notification instead of N Add notifications.
        _suppressChildItemsChanged = true;
        try
        {
            var boxed = new List<object>(bufferedItems.Count);
            foreach (var childItem in bufferedItems)
            {
                boxed.Add(childItem);
            }
            Items.AddRange(boxed);
        }
        finally
        {
            _suppressChildItemsChanged = false;
        }

        // Set parent/level for all children
        foreach (var childItem in bufferedItems)
        {
            childItem.ParentTreeView = ParentTreeView;
            childItem.ParentItem = this;
            childItem.Level = Level + 1;
        }

        // Standard pipeline handles panel population via RefreshItems
        // (which only runs when expanded)
        UpdateExpanderVisibility();
        if (IsExpanded)
        {
            RefreshItems();
        }
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
            childTvi.ParentItem = this;
            childTvi.Level = _level + 1;
        }
    }

    /// <summary>
    /// TreeViewItem 双击 — 转发为 TreeView 的 <see cref="TreeView.ItemDoubleClicked"/>
    /// 高层事件，携带数据上下文。注意展开箭头 / 内部交互元素的双击会被
    /// <see cref="OnMouseDownHandler"/> 提前 Handled，不会进到这里。
    ///
    /// 末尾设 <c>e.Handled = true</c>：MouseDoubleClickEvent 是 Bubble，沿 Bubble 链
    /// 每层 TreeViewItem (祖先) 的 class handler 都会调一次自己的 OnMouseDoubleClick。
    /// 不 Handled 会让每个祖先都触发 ItemDoubleClicked，业务侧重复打开（如双击叶子节点
    /// 同时打开父项目 .csproj）。最深层 TVI 处理后 Handled，祖先 class handler 不再调用
    /// （class handler 默认 handledEventsToo:false）。
    /// </summary>
    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Handled) return;

        ParentTreeView?.RaiseItemDoubleClicked(this, e);
        e.Handled = true;
    }

    /// <summary>
    /// TreeViewItem 右键抬起 — 转发为 TreeView 的 <see cref="TreeView.ItemContextMenuRequested"/>
    /// 高层事件，携带容器与数据上下文，供上层弹出右键菜单。<br/>
    ///
    /// 右键按下时 <see cref="OnMouseDownHandler"/> 已把本节点选中（它对所有按键都执行
    /// SelectItem），因此菜单针对的就是当前选中项。<br/>
    ///
    /// 末尾设 <c>e.Handled = true</c> 的原因同 <see cref="OnMouseDoubleClick"/>：
    /// OnMouseRightButtonUp 由 MouseUp 冒泡链上的 thunk 逐级 re-raise 触发，祖先 TreeViewItem
    /// 也会收到；handled 经 thunk 回写 source 后，外层 thunk 的 <c>if (e.Handled) return</c>
    /// 会跳过 re-raise，避免右键叶子节点时祖先节点重复 raise 菜单事件。
    /// </summary>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (e.Handled) return;

        ParentTreeView?.RaiseItemContextMenuRequested(this, e);
        e.Handled = true;
    }

    // Panning gate state — TreeView usually lives inside a ScrollViewer, so
    // we defer selection to TouchUp and bail out if the contact drifts past
    // the threshold (handing pan control to the ancestor ScrollViewer).
    private const double HeaderTouchPanCancelThresholdDips = 8.0;
    private int _headerActiveTouchId = -1;
    private Point _headerActiveTouchDownPos;
    private bool _headerTouchClickCandidate;
    private bool _headerTouchExpanderHit;

    /// <summary>
    /// Touch tap on the header. Differs from mouse on purpose: touch taps
    /// also toggle expansion when the item has children, because a separate
    /// expander-arrow target is too small for finger interaction. Mouse
    /// still requires the user to click the arrow.
    /// </summary>
    private void OnHeaderTouchDown(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;

        // Pass-through if the touch hit a focusable child (text box, button)
        // inside the header — they get to consume the event themselves.
        if (touchArgs.OriginalSource is DependencyObject source && IsInsideInteractiveHeaderElement(source))
        {
            return;
        }

        _headerActiveTouchId = touchArgs.TouchDevice.Id;
        _headerActiveTouchDownPos = touchArgs.GetTouchPoint(this).Position;
        _headerTouchClickCandidate = true;
        _headerTouchExpanderHit = HasItems
            && _expanderBorder is { Visibility: Visibility.Visible } expander
            && touchArgs.OriginalSource is DependencyObject originalSrc
            && IsVisualDescendantOf(originalSrc, expander);
        // Suppress mouse synthesis so OnMouseDownHandler does not select the
        // item instantly. PointerDown is still raised by the dispatcher
        // unconditionally and bubbles to an ancestor ScrollViewer.
        e.Handled = true;
    }

    private void OnHeaderTouchMove(object sender, RoutedEventArgs e)
    {
        if (!_headerTouchClickCandidate || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _headerActiveTouchId) return;
        var current = touchArgs.GetTouchPoint(this).Position;
        double dx = current.X - _headerActiveTouchDownPos.X;
        double dy = current.Y - _headerActiveTouchDownPos.Y;
        if (dx * dx + dy * dy > HeaderTouchPanCancelThresholdDips * HeaderTouchPanCancelThresholdDips)
        {
            _headerTouchClickCandidate = false;
            _headerActiveTouchId = -1;
        }
    }

    private void OnHeaderTouchUp(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _headerActiveTouchId) return;
        bool wasCandidate = _headerTouchClickCandidate;
        bool wasExpanderHit = _headerTouchExpanderHit;
        _headerActiveTouchId = -1;
        _headerTouchClickCandidate = false;
        _headerTouchExpanderHit = false;
        if (!wasCandidate) return;

        if (wasExpanderHit)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
            return;
        }

        Focus();
        ParentTreeView?.SelectItem(this);
        // Tap-to-expand: on touch, single tap on the header expands/collapses an item
        // that has children. Matches the WinUI / mobile convention.
        if (HasItems)
        {
            IsExpanded = !IsExpanded;
        }
        e.Handled = true;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        // Check if clicked on expander area.
        // 注：不能用 e.GetPosition(expander) 做坐标范围判断 — Jalium.UI 当前的
        // MouseEventArgs.GetPosition 实现在嵌套 visual tree 中返回的是窗口绝对坐标
        // 而非相对元素的局部坐标，导致坐标永远超出 expander 16×24 范围。
        // 改用 OriginalSource visual tree 关系判断：如果点击命中的元素是 expander
        // 或其后代（PART_ExpanderArrow），就是点了展开箭头。
        if (HasItems && _expanderBorder is { Visibility: Visibility.Visible } expander &&
            e.OriginalSource is DependencyObject originalSrc &&
            IsVisualDescendantOf(originalSrc, expander))
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
            return;
        }

        // Let focusable controls inside the header (for example buttons or text boxes)
        // receive the click instead of treating the whole header as a selection surface.
        if (e.OriginalSource is DependencyObject source && IsInsideInteractiveHeaderElement(source))
        {
            return;
        }

        Focus();

        // Select this item
        ParentTreeView?.SelectItem(this);

        // 选中已通过 SelectItem 完成，事件继续 bubble — 不再设 e.Handled = true。
        // Handled 会截断 Bubble 链，导致 Control 类处理器（订阅 MouseDownEvent
        // 检测双击）在外层 TreeView 那一层永远不触发，业务侧
        // SolutionTree.ItemDoubleClicked / MouseDoubleClick 收不到事件。
        // 上层若需特殊吞 click，自行在外层 handler 设 Handled。
    }

    /// <summary>
    /// 沿 visual tree 向上判断 <paramref name="child"/> 是否是 <paramref name="ancestor"/>
    /// 自身或后代。用于鼠标事件 OriginalSource 检测，避免依赖坐标转换。
    /// </summary>
    private static bool IsVisualDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void OnHeaderMouseEnter(object sender, MouseEventArgs e)
    {
        if (_isHeaderMouseOver)
        {
            return;
        }

        _isHeaderMouseOver = true;
        UpdateHeaderVisualState();
    }

    private void OnHeaderMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isHeaderMouseOver)
        {
            return;
        }

        _isHeaderMouseOver = false;
        UpdateHeaderVisualState();
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        var handled = e.Key switch
        {
            Key.Up => ParentTreeView?.FocusAdjacentVisibleItem(this, -1) == true,
            Key.Down => ParentTreeView?.FocusAdjacentVisibleItem(this, 1) == true,
            Key.Home => ParentTreeView?.FocusBoundaryVisibleItem(last: false) == true,
            Key.End => ParentTreeView?.FocusBoundaryVisibleItem(last: true) == true,
            Key.Right => HandleRightArrow(),
            Key.Left => HandleLeftArrow(),
            Key.Enter or Key.Space => HandleSelectionKey(),
            _ => false
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    private bool HandleRightArrow()
    {
        if (HasItems && !IsExpanded)
        {
            IsExpanded = true;
            return true;
        }

        if (!IsExpanded)
        {
            return false;
        }

        foreach (var item in Items)
        {
            if (item is TreeViewItem treeViewItem &&
                treeViewItem.Visibility == Visibility.Visible &&
                treeViewItem.Focus())
            {
                return true;
            }
        }

        var childHost = GetItemsHostPanel();
        if (childHost == null)
        {
            return false;
        }

        foreach (var child in childHost.Children)
        {
            if (child is TreeViewItem treeViewItem && treeViewItem.Visibility == Visibility.Visible && treeViewItem.Focus())
            {
                return true;
            }
        }

        return false;
    }

    private bool HandleLeftArrow()
    {
        if (HasItems && IsExpanded)
        {
            IsExpanded = false;
            return true;
        }

        var parentItem = FindParentTreeViewItem();
        return parentItem != null && parentItem.Focus();
    }

    private bool HandleSelectionKey()
    {
        ParentTreeView?.SelectItem(this);
        return true;
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
            SetExpanderAngle(IsExpanded ? 0 : -90);
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
            SetExpanderAngle(IsExpanded ? 0 : -90);
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
        var targetAngle = expanded ? 0.0 : -90.0;

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
        SetExpanderAngle(_expandAnimationTargetExpanded ? 0 : -90);
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

        return IsExpanded ? 0 : -90;
    }

    private void SetExpanderAngle(double angle)
    {
        if (_expanderArrow == null)
        {
            return;
        }

        // 用绝对像素 CenterX/CenterY 锁定旋转中心 — 避免 RenderTransformOrigin 相对
        // 单位在 Stretch 缩放下计算偏差。Path 的 Width/Height = 8，几何中心 = (4, 4)。
        // 当 ActualWidth/Height 已经布局过则用真实尺寸的一半，更精确。
        var rotateTransform = _expanderArrow.RenderTransform as RotateTransform ?? new RotateTransform();
        var w = _expanderArrow.ActualWidth > 0 ? _expanderArrow.ActualWidth : _expanderArrow.Width;
        var h = _expanderArrow.ActualHeight > 0 ? _expanderArrow.ActualHeight : _expanderArrow.Height;
        rotateTransform.CenterX = (double.IsNaN(w) || w <= 0) ? 4 : w / 2;
        rotateTransform.CenterY = (double.IsNaN(h) || h <= 0) ? 4 : h / 2;
        rotateTransform.Angle = angle;
        _expanderArrow.RenderTransform = rotateTransform;
        _expanderArrow.InvalidateVisual();
    }

    private ClothChild[] CollectClothChildren(double targetHeight)
    {
        var panel = ItemsHost;
        if (panel == null || panel.Children.Count == 0)
        {
            return [];
        }

        var children = new List<UIElement>();
        foreach (var child in panel.Children)
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

    /// <summary>
    /// 资源查找走 _headerBorder 而非 TreeViewItem 自己 —— template 内部的 part
    /// 已 attach 到 visual tree, 它的 VisualParent 链直接经过 TreeView.Resources;
    /// TreeViewItem 在某些时机（比如 ItemContainerGenerator 初次创建容器时）visual tree 可能
    /// 没完全连通, this.TryFindResource 会跳到 Application.Resources 拿到主题默认 fallback
    /// (#15FFFFFF 等), 错过用户在 TreeView.Resources 里的覆盖。
    /// </summary>
    private Brush ResolveResourceBrush(string key, Brush fallback)
    {
        // 优先级 1：直接从 ParentTreeView 的 Resources 拿 — TreeViewItem 持有 ParentTreeView 引用，
        // 不依赖 visual tree（framework 在 ItemContainerGenerator 创建容器时
        // 显式设置该字段），因此即使 visual ancestor 链断开也能正确解析用户在
        // <TreeView.Resources> 里的覆盖。
        if (ParentTreeView is { } tv)
        {
            // 沿 ParentTreeView 链向上 — 处理嵌套 TreeView 场景（虽然现在不嵌套）
            FrameworkElement? cur = tv;
            while (cur != null)
            {
                if (cur.Resources != null && cur.Resources.TryGetValue(key, out var v) && v is Brush direct)
                {
                    return direct;
                }
                cur = cur.VisualParent as FrameworkElement;
            }
        }

        // 优先级 2：_headerBorder 走 visual tree 查找（保险路径）
        if (_headerBorder?.TryFindResource(key) is Brush b1) return b1;

        // 优先级 3：TreeViewItem 自身查找
        if (TryFindResource(key) is Brush b2) return b2;

        return fallback;
    }

    private Brush ResolveHoverBackgroundBrush()
        => ResolveResourceBrush("ControlBackgroundHover", s_fallbackHoverBackgroundBrush);

    private Brush ResolveSelectedBackgroundBrush()
        => ResolveResourceBrush("SelectionBackground", s_fallbackSelectedBackgroundBrush);

    private Brush ResolveSelectedHoverBackgroundBrush()
        => ResolveResourceBrush("AccentBrushPressed", s_fallbackSelectedHoverBackgroundBrush);

    private void OnResourcesChangedHandler(object? sender, EventArgs e)
    {
        UpdateHeaderVisualState();
    }

    internal Panel? GetItemsHostPanel()
    {
        if (ItemsHost == null)
        {
            ApplyTemplate();
        }

        return ItemsHost;
    }

    private TreeViewItem? FindParentTreeViewItem()
    {
        if (ParentItem != null)
        {
            return ParentItem;
        }

        for (Visual? current = VisualParent; current != null; current = current.VisualParent)
        {
            if (current is TreeViewItem treeViewItem)
            {
                return treeViewItem;
            }
        }

        return null;
    }

    private bool IsInsideInteractiveHeaderElement(DependencyObject element)
    {
        for (var current = element; current != null; current = (current as UIElement)?.VisualParent as DependencyObject)
        {
            if (ReferenceEquals(current, this))
            {
                break;
            }

            if (current is UIElement uiElement && uiElement.Focusable
                && current is not TextBlock && current is not Label)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointWithinElementBounds(FrameworkElement? element, Point point)
    {
        if (element == null || element.Visibility != Visibility.Visible)
        {
            return false;
        }

        return element.VisualBounds.Contains(point);
    }

    #endregion

    #region Property Changed Callbacks

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "TryWriteProperty here writes back IsExpanded only when _bridgedDataItem is non-null, i.e. only in the HierarchicalDataTemplate data-item bridging path the consumer opted into. Its RUC contract — \"PropertyAccessorRegistry falls back to Type.GetProperty when no setter is registered. Register typed setters via RegisterSetter() to opt out of reflection.\" — names the documented consumer responsibility; the data type's IsExpanded setter must be preserved (or a typed setter registered), not a defect of this state-sync callback.")]
    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            var expanded = (bool)e.NewValue!;

            // 反向同步: tvi -> dataItem。直接通过 DP 回调写回，不依赖 ExpandedEvent / CollapsedEvent
            // 的冒泡 — 那条路径会让父节点也跟着收起。
            if (tvi._bridgedDataItem != null)
            {
                PropertyAccessorRegistry.TryWriteProperty(tvi._bridgedDataItem, "IsExpanded", expanded);
            }

            // Realize deferred children on first expand
            if (expanded)
            {
                tvi.EnsureChildrenRealized();

                // Populate the items panel through the standard ItemsControl pipeline.
                // RefreshItems was deferred while collapsed (returns early when !IsExpanded).
                tvi.RefreshItems();
            }

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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "TryWriteProperty here writes back IsSelected only when _bridgedDataItem is non-null, i.e. only in the HierarchicalDataTemplate data-item bridging path the consumer opted into. Its RUC contract — \"PropertyAccessorRegistry falls back to Type.GetProperty when no setter is registered. Register typed setters via RegisterSetter() to opt out of reflection.\" — names the documented consumer responsibility; the data type's IsSelected setter must be preserved (or a typed setter registered), not a defect of this state-sync callback.")]
    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            var selected = (bool)e.NewValue!;

            // 反向同步: tvi -> dataItem
            if (tvi._bridgedDataItem != null)
            {
                PropertyAccessorRegistry.TryWriteProperty(tvi._bridgedDataItem, "IsSelected", selected);
            }

            if (selected && tvi.ParentTreeView != null)
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
    /// Applies a HierarchicalDataTemplate to a TreeViewItem, setting up header template.
    /// Child items are deferred until the node is expanded to avoid recursive tree creation.
    ///
    /// <para>ItemsSource resolution accepts three shapes — XAML 解析期对 BindingBase 类型属性
    /// 的处理在不同写法下产物不同，此处显式覆盖三种情形以保证 attribute / property-element /
    /// 直接代码注入都能工作：</para>
    /// <list type="bullet">
    /// <item><c>Binding</c> 实例（属性元素 <c>&lt;Binding Path="X"/&gt;</c> 或代码直接构造）— 反射读 Path。</item>
    /// <item>string 形如 <c>"{Binding X}"</c>（attribute 语法 <c>ItemsSource="{Binding Children}"</c>
    /// 在某些 SG/Reader 路径下产物退化成字符串字面量）— 提取路径表达式重新当作 Binding 处理。</item>
    /// <item><c>IEnumerable</c> 已求值集合 — 当作直接子集合，无需路径反射。</item>
    /// </list>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("HierarchicalDataTemplate ItemsSource may resolve via PropertyAccessorRegistry reflection on data items.")]
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

        // Defer child creation: only resolve whether children exist (for the expander arrow),
        // but don't create TreeViewItem containers until the node is expanded.
        var childItems = ResolveHierarchicalChildItems(hdt.ItemsSource, dataItem);
        if (childItems is IEnumerable enumerable)
        {
            // Check if there are any children without enumerating everything
            var enumerator = enumerable.GetEnumerator();
            try
            {
                if (enumerator.MoveNext())
                {
                    // Has children — store for deferred realization
                    tvi._deferredItemsSource = childItems;
                    tvi._deferredTemplate = hdt.ItemTemplate as HierarchicalDataTemplate ?? hdt;
                    tvi._childrenRealized = false;
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        // 把 dataItem.IsExpanded / IsSelected 桥接到 TreeViewItem 的对应 DP 上 —
        // 这是 HDT 模式里"数据节点 ↔ UI 容器状态"自动双向同步的预期行为。
        BridgeDataItemBoolStateToTreeViewItem(tvi, dataItem);

        // If the item is already expanded, realize children immediately
        if (tvi.IsExpanded)
        {
            tvi.EnsureChildrenRealized();
        }
    }

    /// <summary>
    /// 把 <paramref name="dataItem"/> 上常见的 <c>IsExpanded</c> / <c>IsSelected</c> 布尔属性
    /// 同步到 <see cref="TreeViewItem"/> 的对应 DP 上。如果数据类型实现了 <see cref="INotifyPropertyChanged"/>，
    /// 还订阅 PropertyChanged 让外部修改 dataItem.IsExpanded 时 UI 自动跟随；
    /// TreeViewItem.IsExpanded/IsSelected 反向变化时也回写到 dataItem，实现双向桥接。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("PropertyAccessorRegistry uses reflection fallback for unregistered types.")]
    private static void BridgeDataItemBoolStateToTreeViewItem(TreeViewItem tvi, object dataItem)
    {
        // 容器复用 / dataItem 换绑场景 — 先解绑旧 dataItem 的 INPC，避免泄漏 + 错绑。
        if (tvi._bridgedDataItem is System.ComponentModel.INotifyPropertyChanged oldInpc &&
            tvi._bridgedINPCHandler != null)
        {
            oldInpc.PropertyChanged -= tvi._bridgedINPCHandler;
            tvi._bridgedINPCHandler = null;
        }

        tvi._bridgedDataItem = dataItem;

        // 初始 push：dataItem.IsExpanded / IsSelected → tvi DP
        if (PropertyAccessorRegistry.TryReadProperty(dataItem, "IsExpanded", out var expandedValue) &&
            expandedValue is bool expanded && tvi.IsExpanded != expanded)
        {
            tvi.IsExpanded = expanded;
        }

        if (PropertyAccessorRegistry.TryReadProperty(dataItem, "IsSelected", out var selectedValue) &&
            selectedValue is bool selected && tvi.IsSelected != selected)
        {
            tvi.IsSelected = selected;
        }

        // 单向订阅：dataItem PropertyChanged → tvi DP。
        // 反向 (tvi DP → dataItem) 在 OnIsExpandedChanged / OnIsSelectedChanged 内直接处理，
        // 不再依赖 ExpandedEvent / CollapsedEvent — 那两个事件是 Bubble 路由，
        // 让父节点的桥接 handler 在子节点收起时被错误触发，进而把父 dataItem.IsExpanded 写为 false，
        // 沿父链连锁塌陷整棵子树（即"收起一个 item 所有 item 都收起"的 bug）。
        if (dataItem is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            tvi._bridgedINPCHandler = (_, args) =>
            {
                if (tvi._bridgedDataItem == null) return;

                if (args.PropertyName == "IsExpanded" &&
                    PropertyAccessorRegistry.TryReadProperty(tvi._bridgedDataItem, "IsExpanded", out var v) &&
                    v is bool b && tvi.IsExpanded != b)
                {
                    tvi.IsExpanded = b;
                }
                else if (args.PropertyName == "IsSelected" &&
                    PropertyAccessorRegistry.TryReadProperty(tvi._bridgedDataItem, "IsSelected", out var vs) &&
                    vs is bool bs && tvi.IsSelected != bs)
                {
                    tvi.IsSelected = bs;
                }
            };
            inpc.PropertyChanged += tvi._bridgedINPCHandler;
        }
    }

    /// <summary>
    /// 把 <see cref="HierarchicalDataTemplate.ItemsSource"/> 在三种 XAML 解析产物形态下
    /// 统一解析成"当前节点的子集合"对象。返回 null 表示无法解析（节点视为无子项）。
    /// </summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Resolves PropertyPath against the data item via reflection.")]
    private static object? ResolveHierarchicalChildItems(object? itemsSource, object dataItem)
    {
        if (itemsSource == null)
            return null;

        // 1) 标准 Binding 实例（属性元素语法 <Binding Path="Children" /> 或代码注入）
        if (itemsSource is Jalium.UI.Data.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
        {
            return ResolvePropertyPath(dataItem, binding.Path.Path);
        }

        // 2) attribute 语法 ItemsSource="{Binding Children}" — XAML SourceGenerator
        // 把整个表达式当字符串字面量传给 BindingBase setter，反射 SetValue 直接存了
        // string。这里识别 "{Binding X}" 形态并提取路径作为 fallback。
        if (itemsSource is string text)
        {
            var path = ExtractBindingPathFromMarkup(text);
            if (!string.IsNullOrEmpty(path))
            {
                return ResolvePropertyPath(dataItem, path);
            }
        }

        // 3) 已求值的 IEnumerable — 直接当子集合返回。
        if (itemsSource is IEnumerable directEnumerable)
        {
            return directEnumerable;
        }

        return null;
    }

    /// <summary>
    /// 从 <c>"{Binding X}"</c> / <c>"{Binding Path=X}"</c> 这类字面量里抠出
    /// Path 表达式（"X"）。仅识别简单 Binding 形态，复杂场景（StaticResource / 多参数）返回 null。
    /// </summary>
    private static string? ExtractBindingPathFromMarkup(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}')) return null;

        var content = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (!content.StartsWith("Binding", StringComparison.Ordinal)) return null;

        var rest = content.Substring("Binding".Length).Trim();
        if (rest.Length == 0) return null;

        // 切换式：可能是 "PathExpr" 也可能是 "Path=PathExpr,Mode=..."。
        // 取第一个 ',' 之前的部分；如果含 '=' 取等号右侧；否则整段就是 Path。
        var firstComma = rest.IndexOf(',');
        var head = (firstComma >= 0 ? rest.Substring(0, firstComma) : rest).Trim();
        var eq = head.IndexOf('=');
        if (eq < 0)
        {
            return head; // 位置参数即 Path
        }
        var key = head.Substring(0, eq).Trim();
        if (!string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase))
        {
            return null; // 不是 Path= 形式（可能是 ElementName=、Source= 等），不在此 fallback 范围
        }
        return head.Substring(eq + 1).Trim();
    }

    /// <summary>
    /// Realizes deferred child items. Called when the node is first expanded.
    /// Uses batch operations to minimize notifications, style lookups, and layout invalidations.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "ApplyHierarchicalDataTemplate is invoked here only when a deferred HierarchicalDataTemplate (_deferredTemplate) was stored, i.e. only in the HierarchicalDataTemplate path the consumer opted into. Its RUC contract — \"HierarchicalDataTemplate ItemsSource may resolve via PropertyAccessorRegistry reflection on data items\" — is the documented prerequisite for that opt-in feature; consumers binding reflective data types must keep their child/state properties preserved (or register typed accessors via PropertyAccessorRegistry), not a defect of this deferred-realization site.")]
    internal void EnsureChildrenRealized()
    {
        if (_childrenRealized || _deferredItemsSource == null)
        {
            return;
        }

        _childrenRealized = true;
        var itemsSource = _deferredItemsSource;
        var template = _deferredTemplate;
        _deferredItemsSource = null;
        _deferredTemplate = null;

        if (itemsSource is not IEnumerable enumerable)
        {
            return;
        }

        // Pre-resolve the TreeViewItem style once to avoid per-item resource lookups
        var cachedStyle = ParentTreeView?._cachedTreeViewItemStyle
            ?? TryFindResource(typeof(TreeViewItem)) as Style;
        if (cachedStyle != null && ParentTreeView != null)
        {
            ParentTreeView._cachedTreeViewItemStyle = cachedStyle;
        }

        // Build all child containers first, without adding to any collection
        var childContainers = new List<object>();
        foreach (var childItem in enumerable)
        {
            if (childItem is TreeViewItem childTvi)
            {
                childContainers.Add(childTvi);
            }
            else
            {
                var childContainer = new TreeViewItem
                {
                    Header = childItem,
                    DataContext = childItem
                };

                // Pre-apply cached style to avoid implicit style lookup overhead
                if (cachedStyle != null && childContainer.Style == null)
                {
                    childContainer.Style = cachedStyle;
                }

                if (template != null)
                {
                    ApplyHierarchicalDataTemplate(childContainer, childItem, template);
                }

                childContainers.Add(childContainer);
            }
        }

        if (childContainers.Count == 0)
        {
            return;
        }

        // Batch add to Items collection — fires a single Reset notification
        _suppressChildItemsChanged = true;
        try
        {
            Items.AddRange(childContainers);
        }
        finally
        {
            _suppressChildItemsChanged = false;
        }

        // Set parent/level metadata
        for (int i = 0; i < childContainers.Count; i++)
        {
            if (childContainers[i] is TreeViewItem childTvi)
            {
                childTvi.ParentTreeView = ParentTreeView;
                childTvi.ParentItem = this;
                childTvi.Level = Level + 1;
            }
        }

        // Standard pipeline handles panel population via RefreshItems
        UpdateExpanderVisibility();
        InvalidateMeasure();
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("HierarchicalDataTemplate ItemsSource path is resolved against runtime types via PropertyAccessorRegistry.")]
    private static object? ResolvePropertyPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            if (!PropertyAccessorRegistry.TryReadProperty(current, part, out var next))
                return null;
            current = next;
        }
        return current;
    }

    #endregion
}

/// <summary>
/// 提供 <see cref="TreeView.ItemDoubleClicked"/> 事件的数据：携带被双击的容器
/// （<see cref="TreeViewItem"/>）和它对应的数据 <see cref="DataContext"/>，
/// 让订阅方不必再 <c>tree.SelectedItem</c> 取值。
/// </summary>
public sealed class TreeViewItemDoubleClickedEventArgs : RoutedEventArgs
{
    public TreeViewItemDoubleClickedEventArgs(
        RoutedEvent routedEvent,
        object? source,
        TreeViewItem container,
        object? item,
        MouseButtonEventArgs sourceArgs)
        : base(routedEvent, source)
    {
        Container = container;
        Item = item;
        SourceArgs = sourceArgs;
    }

    /// <summary>被双击的 <see cref="TreeViewItem"/> 容器。</summary>
    public TreeViewItem Container { get; }

    /// <summary>容器对应的数据（DataContext）；若 <see cref="ItemsControl.Items"/>
    /// 直接放 TreeViewItem 实例则与 <see cref="Container"/> 相同。</summary>
    public object? Item { get; }

    /// <summary>原始 <see cref="MouseButtonEventArgs"/> — 提供修饰键、坐标、点击数等信息。</summary>
    public MouseButtonEventArgs SourceArgs { get; }

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<TreeViewItemDoubleClickedEventArgs> typed)
            typed(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

/// <summary>
/// 提供 <see cref="TreeView.ItemContextMenuRequested"/> 事件的数据：携带被右键点击的容器
/// （<see cref="TreeViewItem"/>）、它对应的数据 <see cref="FrameworkElement.DataContext"/>
/// 以及原始鼠标参数，让订阅方据此构建并弹出右键菜单。
/// </summary>
public sealed class TreeViewItemContextMenuRequestedEventArgs : RoutedEventArgs
{
    public TreeViewItemContextMenuRequestedEventArgs(
        RoutedEvent routedEvent,
        object? source,
        TreeViewItem container,
        object? item,
        MouseButtonEventArgs sourceArgs)
        : base(routedEvent, source)
    {
        Container = container;
        Item = item;
        SourceArgs = sourceArgs;
    }

    /// <summary>被右键点击的 <see cref="TreeViewItem"/> 容器。</summary>
    public TreeViewItem Container { get; }

    /// <summary>容器对应的数据（DataContext）；若 <see cref="ItemsControl.Items"/>
    /// 直接放 TreeViewItem 实例则与 <see cref="Container"/> 相同。</summary>
    public object? Item { get; }

    /// <summary>原始 <see cref="MouseButtonEventArgs"/> — 提供修饰键、坐标、点击数等信息。</summary>
    public MouseButtonEventArgs SourceArgs { get; }

    /// <inheritdoc />
    internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is EventHandler<TreeViewItemContextMenuRequestedEventArgs> typed)
            typed(target, this);
        else
            base.InvokeEventHandler(handler, target);
    }
}

// Note: HeaderedItemsControl is defined in Menu.cs
