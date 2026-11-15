using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>A reusable header for an automation-tree data item.</summary>
[TemplateVisualState(Name = "Peer", GroupName = "PeerStates")]
[TemplateVisualState(Name = "Issue", GroupName = "PeerStates")]
public class DevToolsAutomationPeerRow : Control
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(DevToolsAutomationItem), typeof(DevToolsAutomationPeerRow),
        new PropertyMetadata(null, (d, e) => ((DevToolsAutomationPeerRow)d).AttachItem(e.OldValue as DevToolsAutomationItem)));
    private System.ComponentModel.PropertyChangedEventHandler? _itemChanged;
    static DevToolsAutomationPeerRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsAutomationPeerRow), new PropertyMetadata(typeof(DevToolsAutomationPeerRow)));
    public DevToolsAutomationItem? Item { get => (DevToolsAutomationItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    private void AttachItem(DevToolsAutomationItem? old)
    {
        if (old != null && _itemChanged != null) old.PropertyChanged -= _itemChanged;
        _itemChanged = null;
        if (Item is { } item)
        {
            var weak = new WeakReference<DevToolsAutomationPeerRow>(this);
            System.ComponentModel.PropertyChangedEventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var row)) row.UpdateState(); else if (sender is DevToolsAutomationItem retired) retired.PropertyChanged -= handler; };
            item.PropertyChanged += handler; _itemChanged = handler;
        }
        UpdateState();
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    private void UpdateState() => VisualStateManager.GoToState(this, Item?.IsIssue == true ? "Issue" : "Peer", false);
    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);
    private sealed class RowPeer(DevToolsAutomationPeerRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsAutomationPeerRow);
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : owner.Item?.AutomationName ?? "Automation peer";
    }
}

/// <summary>A themeable automation hierarchy, property readouts and supported patterns.</summary>
[TemplatePart(Name = "PART_PeerTree", Type = typeof(TreeView))]
[TemplatePart(Name = "PART_TreeRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_GapRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_DetailsRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_TreeColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_GapColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_DetailsColumn", Type = typeof(ColumnDefinition))]
[TemplateVisualState(Name = "PeersAvailable", GroupName = "DataStates")]
[TemplateVisualState(Name = "PeersEmpty", GroupName = "DataStates")]
[TemplateVisualState(Name = "SelectionAvailable", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "SelectionEmpty", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Building", GroupName = "BuildStates")]
[TemplateVisualState(Name = "Ready", GroupName = "BuildStates")]
[TemplateVisualState(Name = "PatternsAvailable", GroupName = "PatternStates")]
[TemplateVisualState(Name = "PatternsEmpty", GroupName = "PatternStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsAutomationView : Control
{
    private TreeView? _tree;
    private NotifyCollectionChangedEventHandler? _sourceChanged, _patternsChanged;
    private bool _synchronizing;
    private bool _needsSelectionFocus, _selectionFocusQueued;
    private double _layoutWidth;
    private bool? _compactLayout;
    private RowDefinition[] _layoutRows = [];
    private ColumnDefinition[] _layoutColumns = [];
    static DevToolsAutomationView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsAutomationView), new PropertyMetadata(typeof(DevToolsAutomationView)));
    private static DependencyProperty Property(string name, Type type, object? value, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsAutomationView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty ItemsSourceProperty = Property(nameof(ItemsSource), typeof(IEnumerable<DevToolsAutomationItem>), null, OnSourceChanged);
    public static readonly DependencyProperty NodeTemplateProperty = Property(nameof(NodeTemplate), typeof(DataTemplate), null);
    public static readonly DependencyProperty PropertyTemplateProperty = Property(nameof(PropertyTemplate), typeof(DataTemplate), null);
    public static readonly DependencyProperty PatternTemplateProperty = Property(nameof(PatternTemplate), typeof(DataTemplate), null);
    public static readonly DependencyProperty RefreshCommandProperty = Property(nameof(RefreshCommand), typeof(ICommand), null);
    public static readonly DependencyProperty NodeCountProperty = Property(nameof(NodeCount), typeof(int), 0);
    public static readonly DependencyProperty IssueCountProperty = Property(nameof(IssueCount), typeof(int), 0);
    public static readonly DependencyProperty StatusTextProperty = Property(nameof(StatusText), typeof(string), "Ready");
    public static readonly DependencyProperty IsBuildingProperty = Property(nameof(IsBuilding), typeof(bool), false, OnStateChanged);
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(DevToolsAutomationItem), typeof(DevToolsAutomationView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsAutomationView),
        new PropertyMetadata(680d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey HasItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasItems), typeof(bool), typeof(DevToolsAutomationView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasItemsProperty = HasItemsPropertyKey.DependencyProperty;
    public static readonly RoutedEvent SelectedItemChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectedItemChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsAutomationView));

    public IEnumerable<DevToolsAutomationItem>? ItemsSource { get => (IEnumerable<DevToolsAutomationItem>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? NodeTemplate { get => (DataTemplate?)GetValue(NodeTemplateProperty); set => SetValue(NodeTemplateProperty, value); }
    public DataTemplate? PropertyTemplate { get => (DataTemplate?)GetValue(PropertyTemplateProperty); set => SetValue(PropertyTemplateProperty, value); }
    public DataTemplate? PatternTemplate { get => (DataTemplate?)GetValue(PatternTemplateProperty); set => SetValue(PatternTemplateProperty, value); }
    public ICommand? RefreshCommand { get => (ICommand?)GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
    public int NodeCount { get => (int)GetValue(NodeCountProperty)!; set => SetValue(NodeCountProperty, value); }
    public int IssueCount { get => (int)GetValue(IssueCountProperty)!; set => SetValue(IssueCountProperty, value); }
    public string StatusText { get => (string?)GetValue(StatusTextProperty) ?? string.Empty; set => SetValue(StatusTextProperty, value); }
    public bool IsBuilding { get => (bool)GetValue(IsBuildingProperty)!; set => SetValue(IsBuildingProperty, value); }
    public DevToolsAutomationItem? SelectedItem { get => (DevToolsAutomationItem?)GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public bool HasItems => (bool)GetValue(HasItemsProperty)!;
    public event RoutedEventHandler SelectedItemChanged { add => AddHandler(SelectedItemChangedEvent, value); remove => RemoveHandler(SelectedItemChangedEvent, value); }
    public DevToolsAutomationView() => SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdateStates(); };
    protected override Size MeasureOverride(Size availableSize)
    {
        _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth;
        UpdateStates(); return base.MeasureOverride(availableSize);
    }
    public override void OnApplyTemplate()
    {
        DetachTree(); base.OnApplyTemplate();
        _layoutRows = new[] { GetTemplateChild("PART_TreeRow"), GetTemplateChild("PART_GapRow"), GetTemplateChild("PART_DetailsRow") }.OfType<RowDefinition>().ToArray();
        _layoutColumns = new[] { GetTemplateChild("PART_TreeColumn"), GetTemplateChild("PART_GapColumn"), GetTemplateChild("PART_DetailsColumn") }.OfType<ColumnDefinition>().ToArray();
        _tree = GetTemplateChild("PART_PeerTree") as TreeView;
        if (_tree != null) _tree.SelectedItemChanged += OnTreeSelectionChanged;
        SynchronizeSelection(null); UpdateDataState();
    }
    protected override void OnTemplateChanging() { DetachTree(); base.OnTemplateChanging(); }
    private void DetachTree()
    {
        if (_tree != null)
        {
            _tree.SelectedItemChanged -= OnTreeSelectionChanged;
            BindingOperations.ClearBinding(_tree, ItemsControl.ItemsSourceProperty);
            _tree.ItemsSource = null;
        }
        _tree = null;
        _layoutRows = []; _layoutColumns = []; _compactLayout = null;
    }
    private void OnTreeSelectionChanged(object? sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (_synchronizing || !IsEnabled) return;
        var item = e.NewValue as DevToolsAutomationItem ?? (e.NewValue as TreeViewItem)?.Header as DevToolsAutomationItem;
        SetCurrentValue(SelectedItemProperty, item);
    }
    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsAutomationView)d;
        view._needsSelectionFocus |= view._tree?.IsKeyboardFocusWithin == true;
        var old = e.OldValue as DevToolsAutomationItem;
        if (old != null && view._patternsChanged != null) old.Patterns.CollectionChanged -= view._patternsChanged;
        view._patternsChanged = null;
        if (view.SelectedItem is { } item)
        {
            var weak = new WeakReference<DevToolsAutomationView>(view);
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateStates(); else if (sender is INotifyCollectionChanged retired) retired.CollectionChanged -= handler; };
            item.Patterns.CollectionChanged += handler; view._patternsChanged = handler;
        }
        view.SynchronizeSelection(old); view.UpdateStates();
        view.RaiseEvent(new RoutedEventArgs(SelectedItemChangedEvent, view));
    }
    private void SynchronizeSelection(DevToolsAutomationItem? old)
    {
        if (_synchronizing) return;
        _synchronizing = true;
        try
        {
            if (old != null && !ReferenceEquals(old, SelectedItem)) old.IsSelected = false;
            if (SelectedItem is not { } selected) return;
            var parents = new Dictionary<DevToolsAutomationItem, DevToolsAutomationItem?>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<DevToolsAutomationItem>();
            foreach (var root in ItemsSource ?? []) if (parents.TryAdd(root, null)) queue.Enqueue(root);
            while (queue.TryDequeue(out var node))
            {
                if (ReferenceEquals(node, selected))
                {
                    for (var parent = parents[node]; parent != null; parent = parents[parent]) parent.IsExpanded = true;
                    selected.IsSelected = true; return;
                }
                foreach (var child in node.Children) if (parents.TryAdd(child, node)) queue.Enqueue(child);
            }
        }
        finally { _synchronizing = false; }
    }
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsAutomationView)d;
        if (e.OldValue is INotifyCollectionChanged old && view._sourceChanged != null) old.CollectionChanged -= view._sourceChanged;
        view._sourceChanged = null;
        if (e.NewValue is INotifyCollectionChanged collection)
        {
            var weak = new WeakReference<DevToolsAutomationView>(view);
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateDataState(); else if (sender is INotifyCollectionChanged retired) retired.CollectionChanged -= handler; };
            collection.CollectionChanged += handler; view._sourceChanged = handler;
        }
        view.UpdateDataState();
    }
    private void UpdateDataState()
    {
        if (SelectedItem is { } previous && !ContainsItem(previous) && _tree?.IsKeyboardFocusWithin == true)
        {
            _needsSelectionFocus = true;
            _tree.Focus();
        }
        SetValue(HasItemsPropertyKey, ItemsSource?.Any() == true);
        if (SelectedItem is { } selected && !ContainsItem(selected)) SetCurrentValue(SelectedItemProperty, null);
        UpdateStates();
    }
    private bool ContainsItem(DevToolsAutomationItem item)
    {
        var pending = new Stack<DevToolsAutomationItem>(ItemsSource ?? []);
        var visited = new HashSet<DevToolsAutomationItem>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (ReferenceEquals(node, item)) return true;
            foreach (var child in node.Children) pending.Push(child);
        }
        return false;
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsAutomationView)d).UpdateStates();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == IsEnabledProperty) UpdateStates(); }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, HasItems ? "PeersAvailable" : "PeersEmpty", false);
        VisualStateManager.GoToState(this, SelectedItem != null ? "SelectionAvailable" : "SelectionEmpty", false);
        VisualStateManager.GoToState(this, IsBuilding ? "Building" : "Ready", false);
        VisualStateManager.GoToState(this, SelectedItem?.Patterns.Count > 0 ? "PatternsAvailable" : "PatternsEmpty", false);
        var width = _layoutWidth > 0 ? _layoutWidth : ActualWidth;
        bool compact = width > 0 && width <= CompactWidth;
        if (_compactLayout is { } previous && previous != compact)
        {
            // Splitters write local pixel sizes. Release them at the orientation
            // boundary so the template's layout states can own the new axis again.
            _compactLayout = compact;
            foreach (var row in _layoutRows) row.ClearValue(RowDefinition.HeightProperty);
            foreach (var column in _layoutColumns) column.ClearValue(ColumnDefinition.WidthProperty);
        }
        _compactLayout = compact;
        VisualStateManager.GoToState(this, compact ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        QueueSelectionFocus();
    }
    private void QueueSelectionFocus()
    {
        if (!_needsSelectionFocus || _selectionFocusQueued || IsBuilding || SelectedItem == null || _tree == null) return;
        _selectionFocusQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _selectionFocusQueued = false;
            if (_tree?.IsKeyboardFocusWithin != true) { _needsSelectionFocus = false; return; }
            if (IsBuilding) return;
            UpdateLayout();
            _needsSelectionFocus = false;
            var pending = new Stack<ItemsControl>(); pending.Push(_tree);
            while (pending.TryPop(out var owner))
            {
                foreach (var data in owner.Items)
                {
                    var container = data as TreeViewItem ?? owner.ItemContainerGenerator.ContainerFromItem(data) as TreeViewItem;
                    if (container == null) continue;
                    if (ReferenceEquals(container.Header, SelectedItem)) { container.Focus(); return; }
                    if (container.IsExpanded) pending.Push(container);
                }
            }
        });
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ViewPeer(this);
    private sealed class ViewPeer(DevToolsAutomationView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(DevToolsAutomationView);
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Automation hierarchy";
    }
}
