using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>Dependency-property presentation data for a resource-chain row.</summary>
public class DevToolsResourceItem : DependencyObject
{
    private static DependencyProperty Property(string name, Type type, object? value) =>
        DependencyProperty.Register(name, type, typeof(DevToolsResourceItem), new PropertyMetadata(value),
            type.IsEnum ? candidate => candidate != null && Enum.IsDefined(type, candidate) : null);
    public static readonly DependencyProperty RowKindProperty = Property(nameof(RowKind), typeof(DevToolsResourceRowKind), DevToolsResourceRowKind.Resource);
    public DevToolsResourceRowKind RowKind { get => (DevToolsResourceRowKind)GetValue(RowKindProperty)!; set => SetValue(RowKindProperty, value); }
    public static readonly DependencyProperty KeyTextProperty = Property(nameof(KeyText), typeof(string), string.Empty);
    public string KeyText { get => (string?)GetValue(KeyTextProperty) ?? string.Empty; set => SetValue(KeyTextProperty, value); }
    public static readonly DependencyProperty ValueTextProperty = Property(nameof(ValueText), typeof(string), string.Empty);
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public static readonly DependencyProperty DetailTextProperty = Property(nameof(DetailText), typeof(string), string.Empty);
    public string DetailText { get => (string?)GetValue(DetailTextProperty) ?? string.Empty; set => SetValue(DetailTextProperty, value); }
    public static readonly DependencyProperty ValueKindProperty = Property(nameof(ValueKind), typeof(DevToolsResourceValueKind), DevToolsResourceValueKind.Object);
    public DevToolsResourceValueKind ValueKind { get => (DevToolsResourceValueKind)GetValue(ValueKindProperty)!; set => SetValue(ValueKindProperty, value); }
    public static readonly DependencyProperty TypeLabelProperty = Property(nameof(TypeLabel), typeof(string), string.Empty);
    public string TypeLabel { get => (string?)GetValue(TypeLabelProperty) ?? string.Empty; set => SetValue(TypeLabelProperty, value); }
    public static readonly DependencyProperty SwatchBrushProperty = Property(nameof(SwatchBrush), typeof(Brush), null);
    public Brush? SwatchBrush { get => (Brush?)GetValue(SwatchBrushProperty)!; set => SetValue(SwatchBrushProperty, value); }
    public static readonly DependencyProperty IsWinnerProperty = Property(nameof(IsWinner), typeof(bool), false);
    public bool IsWinner { get => (bool)GetValue(IsWinnerProperty)!; set => SetValue(IsWinnerProperty, value); }
    public static readonly DependencyProperty IsInactiveProperty = Property(nameof(IsInactive), typeof(bool), false);
    public bool IsInactive { get => (bool)GetValue(IsInactiveProperty)!; set => SetValue(IsInactiveProperty, value); }
    public static readonly DependencyProperty IndentProperty = Property(nameof(Indent), typeof(Thickness), default(Thickness));
    public Thickness Indent { get => (Thickness)GetValue(IndentProperty)!; set => SetValue(IndentProperty, value); }

    private static readonly DependencyPropertyKey AutomationNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(AutomationName), typeof(string), typeof(DevToolsResourceItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty AutomationNameProperty = AutomationNamePropertyKey.DependencyProperty;
    public string AutomationName => (string)GetValue(AutomationNameProperty)!;
    internal DevToolsResourceRowIdentity Identity { get; private set; }
    internal void Update(DevToolsResourceRowData data)
    {
        Identity = data.Identity;
        RowKind = data.Kind; KeyText = data.KeyText; ValueText = data.ValueText; DetailText = data.DetailText;
        ValueKind = data.ValueKind; TypeLabel = data.TypeLabel;
        if (SwatchBrush is not SolidColorBrush current || data.Swatch is not SolidColorBrush next || current.Color != next.Color)
            SwatchBrush = data.Swatch;
        IsWinner = data.IsWinner; IsInactive = data.IsInactive;
        Indent = new Thickness(Math.Min(data.Depth, 6) * 8, 0, 0, 0);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == AutomationNameProperty) return;
        SetValue(AutomationNamePropertyKey, RowKind == DevToolsResourceRowKind.Resource
            ? $"{KeyText} = {ValueText} · {(IsInactive ? "inactive theme" : IsWinner ? "winner" : "shadowed")}" + (DetailText.Length == 0 ? "" : " · " + DetailText)
            : KeyText + (DetailText.Length == 0 ? "" : " · " + DetailText));
    }
}

/// <summary>A replaceable resource row with semantic value and resolution states.</summary>
[TemplateVisualState(Name = "Scope", GroupName = "RowStates")]
[TemplateVisualState(Name = "Dictionary", GroupName = "RowStates")]
[TemplateVisualState(Name = "Resource", GroupName = "RowStates")]
[TemplateVisualState(Name = "Note", GroupName = "RowStates")]
[TemplateVisualState(Name = "Winner", GroupName = "ResolutionStates")]
[TemplateVisualState(Name = "Shadowed", GroupName = "ResolutionStates")]
[TemplateVisualState(Name = "Inactive", GroupName = "ResolutionStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Object", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Null", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Color", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Brush", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Style", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Template", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Thickness", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Number", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Boolean", GroupName = "ValueStates")]
[TemplateVisualState(Name = "String", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Enum", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Reference", GroupName = "ValueStates")]
[TemplateVisualState(Name = "Typed", GroupName = "PreviewStates")]
[TemplateVisualState(Name = "Swatch", GroupName = "PreviewStates")]
[TemplateVisualState(Name = "Detailed", GroupName = "DetailStates")]
[TemplateVisualState(Name = "Plain", GroupName = "DetailStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsResourceRow : Control
{
    private double _layoutWidth;
    static DevToolsResourceRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsResourceRow), new PropertyMetadata(typeof(DevToolsResourceRow)));
    private static DependencyProperty Property(string name, Type type, object? value, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsResourceRow), new PropertyMetadata(value, changed),
            type.IsEnum ? candidate => candidate != null && Enum.IsDefined(type, candidate) : null);
    public static readonly DependencyProperty RowKindProperty = Property(nameof(RowKind), typeof(DevToolsResourceRowKind), DevToolsResourceRowKind.Resource, OnStateChanged);
    public DevToolsResourceRowKind RowKind { get => (DevToolsResourceRowKind)GetValue(RowKindProperty)!; set => SetValue(RowKindProperty, value); }
    public static readonly DependencyProperty KeyTextProperty = Property(nameof(KeyText), typeof(string), string.Empty);
    public string KeyText { get => (string?)GetValue(KeyTextProperty) ?? string.Empty; set => SetValue(KeyTextProperty, value); }
    public static readonly DependencyProperty ValueTextProperty = Property(nameof(ValueText), typeof(string), string.Empty);
    public string ValueText { get => (string?)GetValue(ValueTextProperty) ?? string.Empty; set => SetValue(ValueTextProperty, value); }
    public static readonly DependencyProperty DetailTextProperty = Property(nameof(DetailText), typeof(string), string.Empty, OnStateChanged);
    public string DetailText { get => (string?)GetValue(DetailTextProperty) ?? string.Empty; set => SetValue(DetailTextProperty, value); }
    public static readonly DependencyProperty ValueKindProperty = Property(nameof(ValueKind), typeof(DevToolsResourceValueKind), DevToolsResourceValueKind.Object, OnStateChanged);
    public DevToolsResourceValueKind ValueKind { get => (DevToolsResourceValueKind)GetValue(ValueKindProperty)!; set => SetValue(ValueKindProperty, value); }
    public static readonly DependencyProperty TypeLabelProperty = Property(nameof(TypeLabel), typeof(string), string.Empty);
    public string TypeLabel { get => (string?)GetValue(TypeLabelProperty) ?? string.Empty; set => SetValue(TypeLabelProperty, value); }
    public static readonly DependencyProperty SwatchBrushProperty = Property(nameof(SwatchBrush), typeof(Brush), null, OnStateChanged);
    public Brush? SwatchBrush { get => (Brush?)GetValue(SwatchBrushProperty)!; set => SetValue(SwatchBrushProperty, value); }
    public static readonly DependencyProperty IsWinnerProperty = Property(nameof(IsWinner), typeof(bool), false, OnStateChanged);
    public bool IsWinner { get => (bool)GetValue(IsWinnerProperty)!; set => SetValue(IsWinnerProperty, value); }
    public static readonly DependencyProperty IsInactiveProperty = Property(nameof(IsInactive), typeof(bool), false, OnStateChanged);
    public bool IsInactive { get => (bool)GetValue(IsInactiveProperty)!; set => SetValue(IsInactiveProperty, value); }

    public static readonly DependencyProperty ShowDetailsProperty = Property(nameof(ShowDetails), typeof(bool), false, OnStateChanged);
    public bool ShowDetails { get => (bool)GetValue(ShowDetailsProperty)!; set => SetValue(ShowDetailsProperty, value); }
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsResourceRow),
        new PropertyMetadata(540d, OnStateChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public DevToolsResourceRow() => SizeChanged += (_, e) => { _layoutWidth = e.NewSize.Width; UpdateStates(); };
    protected override Size MeasureOverride(Size availableSize)
    {
        _layoutWidth = double.IsFinite(availableSize.Width) ? availableSize.Width : ActualWidth;
        UpdateStates();
        return base.MeasureOverride(availableSize);
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateStates(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsResourceRow)d).UpdateStates();
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, RowKind.ToString(), false);
        VisualStateManager.GoToState(this, IsInactive ? "Inactive" : IsWinner ? "Winner" : "Shadowed", false);
        VisualStateManager.GoToState(this, ValueKind.ToString(), false);
        VisualStateManager.GoToState(this, SwatchBrush != null ? "Swatch" : "Typed", false);
        VisualStateManager.GoToState(this, DetailText.Length > 0 && (ShowDetails || ValueKind == DevToolsResourceValueKind.Style) ? "Detailed" : "Plain", false);
        var width = _layoutWidth > 0 ? _layoutWidth : ActualWidth;
        VisualStateManager.GoToState(this, width > 0 && width <= CompactWidth ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ResourceRowPeer(this);
    private sealed class ResourceRowPeer(DevToolsResourceRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsResourceRow);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name :
            owner.KeyText + (owner.RowKind == DevToolsResourceRowKind.Resource ? " = " + owner.ValueText : "") +
            (owner.IsInactive ? " · inactive theme" : owner.RowKind == DevToolsResourceRowKind.Resource ? owner.IsWinner ? " · winner" : " · shadowed" : "");
    }
}

/// <summary>A virtualized resource resolution view with source, template, filter and command contracts.</summary>
[TemplatePart(Name = "PART_FilterTextBox", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_ResourceList", Type = typeof(ListBox))]
[TemplateVisualState(Name = "RowsAvailable", GroupName = "DataStates")]
[TemplateVisualState(Name = "RowsEmpty", GroupName = "DataStates")]
[TemplateVisualState(Name = "AllKeys", GroupName = "FilterStates")]
[TemplateVisualState(Name = "Filtered", GroupName = "FilterStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DevToolsResourcesView : Control
{
    private TextBox? _filterBox;
    private NotifyCollectionChangedEventHandler? _sourceChanged;
    static DevToolsResourcesView() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DevToolsResourcesView), new PropertyMetadata(typeof(DevToolsResourcesView)));
    private static DependencyProperty Property(string name, Type type, object? value, PropertyChangedCallback? changed = null) =>
        DependencyProperty.Register(name, type, typeof(DevToolsResourcesView), new PropertyMetadata(value, changed));
    public static readonly DependencyProperty ItemsSourceProperty = Property(nameof(ItemsSource), typeof(IEnumerable), null, OnSourceChanged);
    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty)!; set => SetValue(ItemsSourceProperty, value); }
    public static readonly DependencyProperty ItemTemplateProperty = Property(nameof(ItemTemplate), typeof(DataTemplate), null);
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty)!; set => SetValue(ItemTemplateProperty, value); }
    public static readonly DependencyProperty ScopeCountProperty = Property(nameof(ScopeCount), typeof(int), 0);
    public int ScopeCount { get => (int)GetValue(ScopeCountProperty)!; set => SetValue(ScopeCountProperty, value); }
    public static readonly DependencyProperty DeclarationCountProperty = Property(nameof(DeclarationCount), typeof(int), 0);
    public int DeclarationCount { get => (int)GetValue(DeclarationCountProperty)!; set => SetValue(DeclarationCountProperty, value); }
    public static readonly DependencyProperty WinnerCountProperty = Property(nameof(WinnerCount), typeof(int), 0);
    public int WinnerCount { get => (int)GetValue(WinnerCountProperty)!; set => SetValue(WinnerCountProperty, value); }
    public static readonly DependencyProperty ShowDetailsProperty = Property(nameof(ShowDetails), typeof(bool), false);
    public bool ShowDetails { get => (bool)GetValue(ShowDetailsProperty)!; set => SetValue(ShowDetailsProperty, value); }
    public static readonly DependencyProperty RefreshCommandProperty = Property(nameof(RefreshCommand), typeof(ICommand), null);
    public ICommand? RefreshCommand { get => (ICommand?)GetValue(RefreshCommandProperty)!; set => SetValue(RefreshCommandProperty, value); }
    public static readonly DependencyProperty ToggleWinnersOnlyCommandProperty = Property(nameof(ToggleWinnersOnlyCommand), typeof(ICommand), null);
    public ICommand? ToggleWinnersOnlyCommand { get => (ICommand?)GetValue(ToggleWinnersOnlyCommandProperty)!; set => SetValue(ToggleWinnersOnlyCommandProperty, value); }

    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(nameof(FilterText), typeof(string), typeof(DevToolsResourcesView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFilterChanged));
    public string FilterText { get => (string?)GetValue(FilterTextProperty) ?? string.Empty; set => SetValue(FilterTextProperty, value); }
    public static readonly DependencyProperty IsWinnersOnlyProperty = DependencyProperty.Register(nameof(IsWinnersOnly), typeof(bool), typeof(DevToolsResourcesView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFilterChanged));
    public bool IsWinnersOnly { get => (bool)GetValue(IsWinnersOnlyProperty)!; set => SetValue(IsWinnersOnlyProperty, value); }
    private static readonly DependencyPropertyKey HasRowsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasRows), typeof(bool), typeof(DevToolsResourcesView), new PropertyMetadata(false));
    public static readonly DependencyProperty HasRowsProperty = HasRowsPropertyKey.DependencyProperty;
    public bool HasRows => (bool)GetValue(HasRowsProperty)!;
    public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(nameof(CompactWidth), typeof(double), typeof(DevToolsResourcesView),
        new PropertyMetadata(420d, OnLayoutChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public double CompactWidth { get => (double)GetValue(CompactWidthProperty)!; set => SetValue(CompactWidthProperty, value); }
    public DevToolsResourcesView() => SizeChanged += (_, _) => UpdateStates();
    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((DevToolsResourcesView)d).UpdateStates();
    public static readonly RoutedEvent FilterChangedEvent = EventManager.RegisterRoutedEvent(nameof(FilterChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsResourcesView));
    public event RoutedEventHandler FilterChanged { add => AddHandler(FilterChangedEvent, value); remove => RemoveHandler(FilterChangedEvent, value); }
    private static void OnFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsResourcesView)d;
        view.UpdateStates(); view.RaiseEvent(new RoutedEventArgs(FilterChangedEvent, view));
    }
    public override void OnApplyTemplate()
    {
        if (_filterBox != null) { _filterBox.PreviewKeyDown -= OnFilterKeyDown; _filterBox.LostFocus -= OnFilterLostFocus; }
        base.OnApplyTemplate();
        _filterBox = GetTemplateChild("PART_FilterTextBox") as TextBox;
        if (_filterBox != null) { _filterBox.PreviewKeyDown += OnFilterKeyDown; _filterBox.LostFocus += OnFilterLostFocus; }
        UpdateDataState();
    }
    private bool CommitFilter()
    {
        if (!IsEnabled || TemplateRootInternal == null) return false;
        bool currentPart = false;
        for (Visual? current = _filterBox; current != null; current = current.VisualParent)
            if (ReferenceEquals(current, this)) { currentPart = true; break; }
        if (!currentPart || RefreshCommand?.CanExecute(FilterText) != true) return false;
        RefreshCommand.Execute(FilterText); return true;
    }
    private void OnFilterKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && CommitFilter()) e.Handled = true; }
    private void OnFilterLostFocus(object sender, RoutedEventArgs e) => CommitFilter();
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateStates();
    }
    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DevToolsResourcesView)d;
        if (e.OldValue is INotifyCollectionChanged old && view._sourceChanged != null) old.CollectionChanged -= view._sourceChanged;
        view._sourceChanged = null;
        if (e.NewValue is INotifyCollectionChanged collection)
        {
            var weak = new WeakReference<DevToolsResourcesView>(view);
            NotifyCollectionChangedEventHandler? handler = null;
            handler = (sender, _) => { if (weak.TryGetTarget(out var owner)) owner.UpdateDataState(); else if (sender is INotifyCollectionChanged retired) retired.CollectionChanged -= handler; };
            collection.CollectionChanged += handler; view._sourceChanged = handler;
        }
        view.UpdateDataState();
    }
    private void UpdateDataState()
    {
        bool any = false;
        if (ItemsSource is ICollection collection) any = collection.Count > 0;
        else if (ItemsSource is { } source)
        {
            var iterator = source.GetEnumerator();
            try { any = iterator.MoveNext(); } finally { (iterator as IDisposable)?.Dispose(); }
        }
        SetValue(HasRowsPropertyKey, any); UpdateStates();
    }
    private void UpdateStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, HasRows ? "RowsAvailable" : "RowsEmpty", false);
        VisualStateManager.GoToState(this, IsWinnersOnly || !string.IsNullOrWhiteSpace(FilterText) ? "Filtered" : "AllKeys", false);
        VisualStateManager.GoToState(this, ActualWidth > 0 && ActualWidth <= CompactWidth ? "Compact" : "Wide", false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new ResourcesPeer(this);
    private sealed class ResourcesPeer(DevToolsResourcesView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(DevToolsResourcesView);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : "Resource resolution";
    }
}

