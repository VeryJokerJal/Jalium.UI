using Jalium.UI.Media;
using System.Collections;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.Ribbon;

[TemplatePart(Name = "PART_InlineGroups", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_PopupGroups", Type = typeof(ItemsControl))]
[TemplatePart(Name = "PART_DropDown", Type = typeof(Popup))]
[TemplateVisualState(Name = "Expanded", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Minimized", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Collapsed", GroupName = "DisplayStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public partial class Ribbon
{
    private static readonly DependencyPropertyKey SelectedTabPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedTab), typeof(RibbonTab), typeof(Ribbon), new PropertyMetadata(null));
    public static readonly DependencyProperty SelectedTabProperty = SelectedTabPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey InlineGroupsSourcePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(InlineGroupsSource), typeof(IEnumerable), typeof(Ribbon), new PropertyMetadata(null));
    public static readonly DependencyProperty InlineGroupsSourceProperty = InlineGroupsSourcePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PopupGroupsSourcePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PopupGroupsSource), typeof(IEnumerable), typeof(Ribbon), new PropertyMetadata(null));
    public static readonly DependencyProperty PopupGroupsSourceProperty = PopupGroupsSourcePropertyKey.DependencyProperty;
    public RibbonTab? SelectedTab => (RibbonTab?)GetValue(SelectedTabProperty);
    public IEnumerable? InlineGroupsSource => (IEnumerable?)GetValue(InlineGroupsSourceProperty);
    public IEnumerable? PopupGroupsSource => (IEnumerable?)GetValue(PopupGroupsSourceProperty);

    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(
        nameof(CompactWidth), typeof(double), typeof(Ribbon), new PropertyMetadata(600d), value => value is double width && double.IsFinite(width) && width >= 0);
    private static readonly DependencyPropertyKey QuickAccessRowPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(QuickAccessRow), typeof(int), typeof(Ribbon), new PropertyMetadata(0));
    public static readonly DependencyProperty QuickAccessRowProperty = QuickAccessRowPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey QuickAccessColumnPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(QuickAccessColumn), typeof(int), typeof(Ribbon), new PropertyMetadata(1));
    public static readonly DependencyProperty QuickAccessColumnProperty = QuickAccessColumnPropertyKey.DependencyProperty;
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty,value); }
    public int QuickAccessRow => (int)GetValue(QuickAccessRowProperty)!;
    public int QuickAccessColumn => (int)GetValue(QuickAccessColumnProperty)!;

    private ItemsControl? _inlineGroups;
    private ItemsControl? _popupGroups;
    private Popup? _dropDown;
    private readonly List<ContentPresenter> _ownedPresenters = new();
    private bool _updatingPresentation;
    private bool _initializedSelection;

    static Ribbon() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Ribbon), new PropertyMetadata(typeof(Ribbon)));
    public Ribbon() { Unloaded += OnRibbonUnloaded; SizeChanged += (_,_) => RefreshPresentation(); }

    public override void OnApplyTemplate()
    {
        ReleasePresentation();
        base.OnApplyTemplate();
        _inlineGroups = GetTemplateChild("PART_InlineGroups") as ItemsControl;
        _popupGroups = GetTemplateChild("PART_PopupGroups") as ItemsControl;
        _dropDown = GetTemplateChild("PART_DropDown") as Popup;
        foreach (var name in new[] { "PART_Title", "PART_QuickAccess", "PART_ApplicationMenu", "PART_HelpPane" })
            if (GetTemplateChild(name) is ContentPresenter presenter) _ownedPresenters.Add(presenter);
        InitializeSelectionIfNeeded();
        RefreshPresentation();
    }

    private void InitializeSelectionIfNeeded()
    {
        if (_initializedSelection || GetItemCount() == 0) return;
        _initializedSelection = true;
        if (SelectedIndex < 0 && !HasLocalValue(SelectedIndexProperty) && !HasLocalValue(SelectedItemProperty) &&
            GetBindingExpression(SelectedIndexProperty) is null && GetBindingExpression(SelectedItemProperty) is null)
            SetValue(SelectedIndexProperty, 0);
    }

    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (TemplateRootInternal is not null) InitializeSelectionIfNeeded();
        RefreshPresentation();
    }

    protected override void OnTemplateChanging()
    {
        ReleasePresentation();
        base.OnTemplateChanging();
    }

    private void ReleasePresentation()
    {
        var hadParts = _dropDown is not null || _inlineGroups is not null || _popupGroups is not null;
        if (_dropDown is not null)
        {
            _dropDown.ClearBinding(Popup.IsOpenProperty);
            _dropDown.IsOpen = false;
        }
        foreach (var host in new[] { _inlineGroups, _popupGroups })
            if (host is not null)
            {
                host.ClearBinding(ItemsSourceProperty);
                host.ClearValue(ItemsSourceProperty);
            }
        foreach (var presenter in _ownedPresenters)
        {
            presenter.ClearBinding(ContentPresenter.ContentProperty);
            presenter.ClearValue(ContentPresenter.ContentProperty);
        }
        _ownedPresenters.Clear();
        _inlineGroups = _popupGroups = null;
        _dropDown = null;
        SetValue(InlineGroupsSourcePropertyKey, null);
        SetValue(PopupGroupsSourcePropertyKey, null);
        if (hadParts) SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnRibbonUnloaded(object? sender, RoutedEventArgs e) => SetCurrentValue(IsDropDownOpenProperty, false);

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMinimizedProperty || e.Property == IsCollapsedProperty || e.Property == IsDropDownOpenProperty ||
            e.Property == ShowQuickAccessToolBarOnTopProperty || e.Property == CompactWidthProperty || e.Property == IsEnabledProperty || e.Property == TitleProperty)
            RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (_updatingPresentation) return;
        _updatingPresentation = true;
        try
        {
            var selected = GetRibbonTabAt(SelectedIndex);
            SetValue(SelectedTabPropertyKey, selected);
            if ((!IsMinimized || IsCollapsed || !IsEnabled) && IsDropDownOpen)
                SetCurrentValue(IsDropDownOpenProperty, false);
            var groups = selected?.Items;
            // Detach the inactive host first; a group can have only one visual parent.
            if (IsMinimized)
            {
                SetValue(InlineGroupsSourcePropertyKey, null);
                SetValue(PopupGroupsSourcePropertyKey, IsDropDownOpen && !IsCollapsed && IsEnabled ? groups : null);
            }
            else
            {
                SetValue(PopupGroupsSourcePropertyKey, null);
                SetValue(InlineGroupsSourcePropertyKey, !IsCollapsed ? groups : null);
            }
            var quickAccessOnTop = ShowQuickAccessToolBarOnTop && (ActualWidth <= 0 || ActualWidth >= CompactWidth);
            SetValue(QuickAccessRowPropertyKey, quickAccessOnTop ? 0 : 3);
            SetValue(QuickAccessColumnPropertyKey, quickAccessOnTop ? 1 : 0);
            if (TemplateRootInternal is null) return;
            VisualStateManager.GoToState(this, IsCollapsed ? "Collapsed" : IsMinimized ? "Minimized" : "Expanded", false);
            VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
            VisualStateManager.GoToState(this, quickAccessOnTop ? "QuickAccessTop" : "QuickAccessBottom", false);
            VisualStateManager.GoToState(this, Title is null ? "TitleHidden" : "TitleVisible", false);
        }
        finally { _updatingPresentation = false; }
    }

    internal void ActivateHeader(RibbonTab tab)
    {
        var item = ItemContainerGenerator.ItemFromContainer(tab);
        SetCurrentValue(SelectedItemProperty, ReferenceEquals(item, DependencyProperty.UnsetValue) ? tab : item);
        if (IsMinimized) SetCurrentValue(IsDropDownOpenProperty, true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        HandleHeaderKey(e);
        base.OnKeyDown(e);
    }

    internal void HandleHeaderKey(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            SelectedTab?.FocusHeader();
            e.Handled = true;
        }
        if (!e.Handled && IsEnabled && HeaderHasFocus())
        {
            var count = GetItemCount();
            var direction = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            if (FlowDirection == FlowDirection.RightToLeft) direction = -direction;
            if (count > 0 && (direction != 0 || e.Key is Key.Home or Key.End))
            {
                var index = e.Key == Key.Home ? 0 : e.Key == Key.End ? count - 1 : (SelectedIndex + direction + count) % count;
                for (var attempt = 0; attempt < count; attempt++)
                {
                    if (GetRibbonTabAt(index) is { IsEnabled: true, Visibility: Visibility.Visible } tab)
                    {
                        ActivateHeader(tab); tab.FocusHeader(); break;
                    }
                    index = (index + (direction == 0 ? e.Key == Key.End ? -1 : 1 : direction) + count) % count;
                }
                e.Handled = true;
            }
        }
    }

    private bool HeaderHasFocus()
    {
        if (IsKeyboardFocused) return true;
        for (var visual = Keyboard.FocusedElement as Visual; visual is not null && !ReferenceEquals(visual, this); visual = visual.VisualParent)
            if (visual is RibbonTab tab && ReferenceEquals(tab.Ribbon, this)) return true;
        return false;
    }
}

[TemplatePart(Name = "PART_HeaderButton", Type = typeof(Button))]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
public partial class RibbonTab
{
    private Button? _headerButton;
    static RibbonTab() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonTab), new PropertyMetadata(typeof(RibbonTab)));
    public override void OnApplyTemplate()
    {
        ReleaseHeader(); base.OnApplyTemplate();
        _headerButton = GetTemplateChild("PART_HeaderButton") as Button;
        if (_headerButton is not null) _headerButton.Click += OnHeaderClick;
        UpdateSelectionState();
    }
    protected override void OnTemplateChanging() { ReleaseHeader(); base.OnTemplateChanging(); }
    private void ReleaseHeader()
    {
        if (_headerButton is not null)
        {
            _headerButton.Click -= OnHeaderClick;
            _headerButton.ClearBinding(ContentControl.ContentProperty);
            _headerButton.ClearValue(ContentControl.ContentProperty);
        }
        _headerButton = null;
    }
    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (Ribbon is { } ribbon) ribbon.ActivateHeader(this);
        else SetCurrentValue(IsSelectedProperty, true);
    }
    internal void FocusHeader() { if (_headerButton is not null) _headerButton.Focus(); else Focus(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Handle header navigation before the template's horizontal ScrollViewer
        // consumes arrow/Home/End keys as scrolling commands.
        Ribbon?.HandleHeaderKey(e);
        base.OnKeyDown(e);
    }
    private void UpdateSelectionState()
    {
        if (TemplateRootInternal is not null)
            VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsSelectedProperty) UpdateSelectionState();
    }
}

public partial class RibbonGroup
{
    static RibbonGroup() => DefaultStyleKeyProperty.OverrideMetadata(typeof(RibbonGroup), new PropertyMetadata(typeof(RibbonGroup)));
}
