using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The tree projection displayed by a developer-tools Inspector.</summary>
public enum DevToolsInspectorView { Visual, Logical, Flat }

/// <summary>A reusable Inspector shell with replaceable tree and detail content.</summary>
[TemplatePart(Name = "PART_SearchBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_ViewSelector", Type = typeof(DevToolsSegmentedSelector))]
[TemplatePart(Name = "PART_DetailsScrollViewer", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_TreeColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_DetailsColumn", Type = typeof(ColumnDefinition))]
[TemplatePart(Name = "PART_TreeRow", Type = typeof(RowDefinition))]
[TemplatePart(Name = "PART_DetailsRow", Type = typeof(RowDefinition))]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "DetailsAvailable", GroupName = "DataStates")]
[TemplateVisualState(Name = "DetailsEmpty", GroupName = "DataStates")]
public class DevToolsInspector : Control
{
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
        nameof(SearchText), typeof(string), typeof(DevToolsInspector), new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((DevToolsInspector)d).RaiseEvent(new RoutedEventArgs(SearchTextChangedEvent, d))));
    public static readonly DependencyProperty SelectedViewProperty = DependencyProperty.Register(
        nameof(SelectedView), typeof(DevToolsInspectorView), typeof(DevToolsInspector), new FrameworkPropertyMetadata(DevToolsInspectorView.Visual,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedViewChanged), v => v is DevToolsInspectorView view && Enum.IsDefined(view));
    public static readonly DependencyProperty TreeContentProperty = Property(nameof(TreeContent), typeof(object));
    public static readonly DependencyProperty DetailsContentProperty = Property(nameof(DetailsContent), typeof(object));
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsInspector), new PropertyMetadata(760d, OnPresentationChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public static readonly DependencyProperty IsDetailsEmptyProperty = DependencyProperty.Register(nameof(IsDetailsEmpty), typeof(bool), typeof(DevToolsInspector), new PropertyMetadata(false, OnPresentationChanged));
    public static readonly DependencyProperty EmptyTextProperty = Property(nameof(EmptyText), typeof(string), "Select an element to inspect");
    public static readonly DependencyProperty KeepFocusedDetailsInViewProperty = Property(nameof(KeepFocusedDetailsInView), typeof(bool), true);
    public static readonly DependencyProperty DetailsHorizontalScrollBarVisibilityProperty = DependencyProperty.Register(nameof(DetailsHorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(DevToolsInspector), new PropertyMetadata(ScrollBarVisibility.Disabled), value => value is ScrollBarVisibility visibility && Enum.IsDefined(visibility));
    public static readonly DependencyProperty DetailsVerticalScrollBarVisibilityProperty = DependencyProperty.Register(nameof(DetailsVerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(DevToolsInspector), new PropertyMetadata(ScrollBarVisibility.Auto), value => value is ScrollBarVisibility visibility && Enum.IsDefined(visibility));
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsCompact), typeof(bool), typeof(DevToolsInspector), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsPickingProperty = Property(nameof(IsPicking), typeof(bool), false);
    public static readonly DependencyProperty CanDeleteProperty = Property(nameof(CanDelete), typeof(bool), false);
    public static readonly DependencyProperty CanUndoProperty = Property(nameof(CanUndo), typeof(bool), false);
    public static readonly DependencyProperty DeleteToolTipProperty = Property(nameof(DeleteToolTip), typeof(string), "Select a removable element");
    public static readonly DependencyProperty UndoToolTipProperty = Property(nameof(UndoToolTip), typeof(string), "Nothing to restore");
    public static readonly DependencyProperty RefreshCommandProperty = Property(nameof(RefreshCommand), typeof(ICommand));
    public static readonly DependencyProperty PickCommandProperty = Property(nameof(PickCommand), typeof(ICommand));
    public static readonly DependencyProperty ExpandCommandProperty = Property(nameof(ExpandCommand), typeof(ICommand));
    public static readonly DependencyProperty CollapseCommandProperty = Property(nameof(CollapseCommand), typeof(ICommand));
    public static readonly DependencyProperty CopyCommandProperty = Property(nameof(CopyCommand), typeof(ICommand));
    public static readonly DependencyProperty DeleteCommandProperty = Property(nameof(DeleteCommand), typeof(ICommand));
    public static readonly DependencyProperty UndoCommandProperty = Property(nameof(UndoCommand), typeof(ICommand));
    public static readonly RoutedEvent SearchTextChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SearchTextChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsInspector));
    public static readonly RoutedEvent SelectedViewChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedViewChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsInspector));

    private TextBox? _searchBox;
    private DevToolsSegmentedSelector? _viewSelector;
    private bool _synchronizing;
    private double _layoutWidth = double.PositiveInfinity;
    private ColumnDefinition? _treeColumn, _detailsColumn;
    private RowDefinition? _treeRow, _detailsRow;
    private GridLength? _wideTreeLength, _wideDetailsLength, _compactTreeLength, _compactDetailsLength;
    private bool? _appliedCompact;
    private bool _updatingPresentation;
    private ScrollViewer? _detailsScrollViewer;
    private DispatcherOperation? _pendingFocusReveal;
    static DevToolsInspector() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsInspector), new PropertyMetadata(typeof(DevToolsInspector)));
    private static DependencyProperty Property(string name, Type type, object? value = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsInspector), new PropertyMetadata(value));
    public string SearchText { get => (string?)GetValue(SearchTextProperty) ?? string.Empty; set => SetValue(SearchTextProperty, value); }
    public DevToolsInspectorView SelectedView { get => (DevToolsInspectorView)GetValue(SelectedViewProperty)!; set => SetValue(SelectedViewProperty, value); }
    public object? TreeContent { get => GetValue(TreeContentProperty); set => SetValue(TreeContentProperty, value); }
    public object? DetailsContent { get => GetValue(DetailsContentProperty); set => SetValue(DetailsContentProperty, value); }
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public bool IsCompact => (bool)GetValue(IsCompactProperty)!;
    public bool IsDetailsEmpty { get => (bool)GetValue(IsDetailsEmptyProperty)!; set => SetValue(IsDetailsEmptyProperty, value); }
    public string EmptyText { get => (string?)GetValue(EmptyTextProperty) ?? string.Empty; set => SetValue(EmptyTextProperty, value); }
    public bool KeepFocusedDetailsInView { get => (bool)GetValue(KeepFocusedDetailsInViewProperty)!; set => SetValue(KeepFocusedDetailsInViewProperty, value); }
    public ScrollBarVisibility DetailsHorizontalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(DetailsHorizontalScrollBarVisibilityProperty)!; set => SetValue(DetailsHorizontalScrollBarVisibilityProperty, value); }
    public ScrollBarVisibility DetailsVerticalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(DetailsVerticalScrollBarVisibilityProperty)!; set => SetValue(DetailsVerticalScrollBarVisibilityProperty, value); }
    public bool IsPicking { get => (bool)GetValue(IsPickingProperty)!; set => SetValue(IsPickingProperty, value); }
    public bool CanDelete { get => (bool)GetValue(CanDeleteProperty)!; set => SetValue(CanDeleteProperty, value); }
    public bool CanUndo { get => (bool)GetValue(CanUndoProperty)!; set => SetValue(CanUndoProperty, value); }
    public string? DeleteToolTip { get => (string?)GetValue(DeleteToolTipProperty); set => SetValue(DeleteToolTipProperty, value); }
    public string? UndoToolTip { get => (string?)GetValue(UndoToolTipProperty); set => SetValue(UndoToolTipProperty, value); }
    public ICommand? RefreshCommand { get => (ICommand?)GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
    public ICommand? PickCommand { get => (ICommand?)GetValue(PickCommandProperty); set => SetValue(PickCommandProperty, value); }
    public ICommand? ExpandCommand { get => (ICommand?)GetValue(ExpandCommandProperty); set => SetValue(ExpandCommandProperty, value); }
    public ICommand? CollapseCommand { get => (ICommand?)GetValue(CollapseCommandProperty); set => SetValue(CollapseCommandProperty, value); }
    public ICommand? CopyCommand { get => (ICommand?)GetValue(CopyCommandProperty); set => SetValue(CopyCommandProperty, value); }
    public ICommand? DeleteCommand { get => (ICommand?)GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }
    public ICommand? UndoCommand { get => (ICommand?)GetValue(UndoCommandProperty); set => SetValue(UndoCommandProperty, value); }
    public event RoutedEventHandler SearchTextChanged { add => AddHandler(SearchTextChangedEvent, value); remove => RemoveHandler(SearchTextChangedEvent, value); }
    public event RoutedEventHandler SelectedViewChanged { add => AddHandler(SelectedViewChangedEvent, value); remove => RemoveHandler(SelectedViewChangedEvent, value); }

    public DevToolsInspector()
    {
        SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdatePresentation(); QueueFocusedDetailsReveal(); };
        Unloaded += (_, _) => CancelFocusReveal();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth > 0 ? ActualWidth : double.PositiveInfinity;
        UpdatePresentation();
        return base.MeasureOverride(availableSize);
    }
    private static void OnPresentationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsInspector)d).UpdatePresentation();
    private void UpdatePresentation()
    {
        if (_updatingPresentation) return;
        _updatingPresentation = true;
        try
        {
            SetValue(IsCompactPropertyKey, _layoutWidth < CompactWidth);
            if (TemplateRootInternal == null) return;
            bool changing = _appliedCompact != IsCompact;
            if (changing && _appliedCompact is bool previous)
            {
                CaptureSplitSizing(previous);
                // GridSplitter writes local star sizes. Retire only the axis
                // that is becoming inactive so it cannot keep empty tracks open.
                if (IsCompact) { _treeColumn?.ClearValue(ColumnDefinition.WidthProperty); _detailsColumn?.ClearValue(ColumnDefinition.WidthProperty); }
                else { _treeRow?.ClearValue(RowDefinition.HeightProperty); _detailsRow?.ClearValue(RowDefinition.HeightProperty); }
            }
            VisualStateManager.GoToState(this, IsCompact ? "Compact" : "Wide", false);
            VisualStateManager.GoToState(this, IsDetailsEmpty ? "DetailsEmpty" : "DetailsAvailable", false);
            if (changing)
            {
                if (IsCompact) { Restore(_treeRow, RowDefinition.HeightProperty, _compactTreeLength); Restore(_detailsRow, RowDefinition.HeightProperty, _compactDetailsLength); }
                else { Restore(_treeColumn, ColumnDefinition.WidthProperty, _wideTreeLength); Restore(_detailsColumn, ColumnDefinition.WidthProperty, _wideDetailsLength); }
                _appliedCompact = IsCompact;
            }
        }
        finally { _updatingPresentation = false; }
    }
    private void CaptureSplitSizing(bool compact)
    {
        if (compact)
        {
            if (_treeRow != null) _compactTreeLength = LocalLength(_treeRow, RowDefinition.HeightProperty);
            if (_detailsRow != null) _compactDetailsLength = LocalLength(_detailsRow, RowDefinition.HeightProperty);
        }
        else
        {
            if (_treeColumn != null) _wideTreeLength = LocalLength(_treeColumn, ColumnDefinition.WidthProperty);
            if (_detailsColumn != null) _wideDetailsLength = LocalLength(_detailsColumn, ColumnDefinition.WidthProperty);
        }
    }
    private static GridLength? LocalLength(DependencyObject part, DependencyProperty property) => part.ReadLocalValue(property) is GridLength length ? length : null;
    private static void Restore(DependencyObject? part, DependencyProperty property, GridLength? length) { if (length is GridLength value) part?.SetValue(property, value); }
    private void OnDetailsViewportSizeChanged(object sender, SizeChangedEventArgs e) => QueueFocusedDetailsReveal();
    private void QueueFocusedDetailsReveal()
    {
        CancelFocusReveal();
        if (!KeepFocusedDetailsInView || Keyboard.FocusedElement is not FrameworkElement focused || !IsInCurrentDetails(focused)) return;
        _pendingFocusReveal = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _pendingFocusReveal = null;
            if (KeepFocusedDetailsInView && ReferenceEquals(Keyboard.FocusedElement, focused) && focused.IsVisible && IsInCurrentDetails(focused)) focused.BringIntoView();
        });
    }
    private bool IsInCurrentDetails(Visual visual)
    {
        if (_detailsScrollViewer == null) return false;
        for (Visual? current = visual; current != null; current = current.VisualParent) if (ReferenceEquals(current, _detailsScrollViewer)) return true;
        return false;
    }
    private void CancelFocusReveal() { _pendingFocusReveal?.Abort(); _pendingFocusReveal = null; }

    public override void OnApplyTemplate()
    {
        DetachParts();
        base.OnApplyTemplate();
        _searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
        _viewSelector = GetTemplateChild("PART_ViewSelector") as DevToolsSegmentedSelector;
        _treeColumn = FindName("PART_TreeColumn") as ColumnDefinition;
        _detailsColumn = FindName("PART_DetailsColumn") as ColumnDefinition;
        _treeRow = FindName("PART_TreeRow") as RowDefinition;
        _detailsRow = FindName("PART_DetailsRow") as RowDefinition;
        _detailsScrollViewer = GetTemplateChild("PART_DetailsScrollViewer") as ScrollViewer;
        if (_detailsScrollViewer != null) _detailsScrollViewer.SizeChanged += OnDetailsViewportSizeChanged;
        SynchronizeView();
        if (_viewSelector != null) _viewSelector.SelectionChanged += OnViewSelectionChanged;
        UpdatePresentation();
    }
    protected override void OnTemplateChanging() { DetachParts(); base.OnTemplateChanging(); }
    private void DetachParts()
    {
        CancelFocusReveal();
        if (_detailsScrollViewer != null) _detailsScrollViewer.SizeChanged -= OnDetailsViewportSizeChanged;
        _detailsScrollViewer = null;
        if (_appliedCompact is bool previous) CaptureSplitSizing(previous);
        _treeColumn = null; _detailsColumn = null; _treeRow = null; _detailsRow = null; _appliedCompact = null;
        if (_viewSelector != null) _viewSelector.SelectionChanged -= OnViewSelectionChanged;
        _viewSelector = null;
        _searchBox = null;
    }
    /// <summary>Focuses the current search part, returning false if the consumer omitted it.</summary>
    public bool FocusSearch() { ApplyTemplate(); return _searchBox?.Focus() == true; }
    private static void OnSelectedViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var inspector = (DevToolsInspector)d;
        inspector.SynchronizeView();
        inspector.RaiseEvent(new RoutedEventArgs(SelectedViewChangedEvent, inspector));
    }
    private void SynchronizeView()
    {
        if (_viewSelector == null) return;
        _synchronizing = true;
        try { _viewSelector.SetSelectedSilent(SelectedView switch { DevToolsInspectorView.Flat => 0, DevToolsInspectorView.Visual => 1, _ => 2 }); }
        finally { _synchronizing = false; }
    }
    private void OnViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_synchronizing || !ReferenceEquals(e.OriginalSource, _viewSelector) || _viewSelector is not { SelectedIndex: >= 0 and <= 2 }) return;
        SetCurrentValue(SelectedViewProperty, _viewSelector.SelectedIndex switch { 0 => DevToolsInspectorView.Flat, 1 => DevToolsInspectorView.Visual, _ => DevToolsInspectorView.Logical });
    }
}
