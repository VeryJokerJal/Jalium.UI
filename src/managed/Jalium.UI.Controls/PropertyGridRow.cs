using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>A selectable, responsive inspector row whose visuals live entirely in its theme.</summary>
[TemplatePart(Name = "PART_Editor", Type = typeof(PropertyGridEditor))]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Wide", GroupName = "LayoutStates")]
[TemplateVisualState(Name = "Compact", GroupName = "LayoutStates")]
public class PropertyGridRow : Control
{
    static PropertyGridRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PropertyGridRow), new PropertyMetadata(typeof(PropertyGridRow)));
        FocusableProperty.OverrideMetadata(typeof(PropertyGridRow), new PropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(PropertyGridRow), new PropertyMetadata(false));
    }
    public static readonly DependencyProperty PropertyProperty = DependencyProperty.Register(nameof(Property), typeof(PropertyItem), typeof(PropertyGridRow), new PropertyMetadata(null, OnPropertyChanged));
    public PropertyItem? Property { get => (PropertyItem?)GetValue(PropertyProperty)!; set => SetValue(PropertyProperty, value); }
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(nameof(Owner), typeof(PropertyGrid), typeof(PropertyGridRow), new PropertyMetadata(null, OnOwnerChanged));
    public PropertyGrid? Owner { get => (PropertyGrid?)GetValue(OwnerProperty)!; set => SetValue(OwnerProperty, value); }
    public static readonly DependencyProperty SelectedPropertyProperty = DependencyProperty.Register(nameof(SelectedProperty), typeof(PropertyItem), typeof(PropertyGridRow), new PropertyMetadata(null, OnSelectionChanged));
    public PropertyItem? SelectedProperty { get => (PropertyItem?)GetValue(SelectedPropertyProperty)!; set => SetValue(SelectedPropertyProperty, value); }
    public static readonly DependencyProperty NameColumnWidthProperty = DependencyProperty.Register(nameof(NameColumnWidth), typeof(double), typeof(PropertyGridRow), new PropertyMetadata(150.0, OnLayoutChanged), value => value is double width && double.IsFinite(width) && width >= 0);
    public double NameColumnWidth { get => (double)GetValue(NameColumnWidthProperty)!; set => SetValue(NameColumnWidthProperty, value); }
    public static readonly DependencyProperty NameForegroundProperty = DependencyProperty.Register(nameof(NameForeground), typeof(Brush), typeof(PropertyGridRow), new PropertyMetadata(null));
    public Brush? NameForeground { get => (Brush?)GetValue(NameForegroundProperty)!; set => SetValue(NameForegroundProperty, value); }
    private static readonly DependencyPropertyKey DisplayNamePropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayName), typeof(string), typeof(PropertyGridRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayNameProperty = DisplayNamePropertyKey.DependencyProperty;
    public string DisplayName => (string)GetValue(DisplayNameProperty)!;
    private static readonly DependencyPropertyKey DescriptionPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Description), typeof(string), typeof(PropertyGridRow), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DescriptionPropertyKey.DependencyProperty;
    public string Description => (string)GetValue(DescriptionProperty)!;
    private static readonly DependencyPropertyKey IsSelectedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSelected), typeof(bool), typeof(PropertyGridRow), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectedProperty = IsSelectedPropertyKey.DependencyProperty;
    public bool IsSelected => (bool)GetValue(IsSelectedProperty)!;
    private static readonly DependencyPropertyKey EffectiveNameColumnWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(EffectiveNameColumnWidth), typeof(double), typeof(PropertyGridRow), new PropertyMetadata(150.0));
    public static readonly DependencyProperty EffectiveNameColumnWidthProperty = EffectiveNameColumnWidthPropertyKey.DependencyProperty;
    public double EffectiveNameColumnWidth => (double)GetValue(EffectiveNameColumnWidthProperty)!;
    private PropertyGridEditor? _editor;
    private string? _pendingEdit;
    private bool _hasTemplate;
    private bool _compact;

    public PropertyGridRow()
    {
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnPointerDown), true);
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDown), true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown));
        Loaded += (_, _) => Owner?.RegisterRow(this);
        Unloaded += (_, _) => Owner?.UnregisterRow(this);
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hasTemplate = true;
        _editor = GetTemplateChild("PART_Editor") as PropertyGridEditor;
        Owner?.RegisterRow(this);
        if (_pendingEdit != null && _editor != null) { _editor.RestoreEdit(_pendingEdit); _pendingEdit = null; }
        UpdateStates();
    }
    protected override void OnTemplateChanging()
    {
        _hasTemplate = false;
        if (_editor != null)
        {
            if (_editor.IsEditing) _pendingEdit = _editor.EditText;
            _editor.Retire();
            Owner?.UnregisterEditor(_editor);
        }
        _editor = null;
        Owner?.UnregisterRow(this);
        base.OnTemplateChanging();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateLayoutMode(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == IsMouseOverProperty || e.Property == IsKeyboardFocusWithinProperty)
        {
            if (e.Property == IsKeyboardFocusWithinProperty && IsKeyboardFocusWithin && IsOwnRowSource(Keyboard.FocusedElement)) Select();
            UpdateStates();
        }
    }
    private void Select()
    {
        if (Property != null && IsEnabled) Owner?.SetCurrentValue(PropertyGrid.SelectedPropertyProperty, Property);
    }
    private bool InEditor(object? source)
    {
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, _editor)) return true;
            if (ReferenceEquals(node, this)) break;
        }
        return false;
    }
    private bool IsOwnRowSource(object? source)
    {
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is PropertyGridRow row) return ReferenceEquals(row, this);
        return false;
    }
    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !IsOwnRowSource(e.OriginalSource)) return;
        Select();
        if (!InEditor(e.OriginalSource)) Focus();
    }
    private void OnTouchDown(object sender, RoutedEventArgs e)
    {
        if (!IsOwnRowSource(e.OriginalSource)) return;
        Select();
        if (!InEditor(e.OriginalSource)) Focus();
    }
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || InEditor(e.OriginalSource)) return;
        if (e.Key is Key.Up or Key.Down) e.Handled = Owner?.MoveSelection(this, e.Key == Key.Up ? -1 : 1) == true;
        else if (e.Key is Key.F2 or Key.Enter) e.Handled = _editor?.BeginEdit() == true;
    }
    private void UpdateLayoutMode(double width)
    {
        _compact = double.IsFinite(width) && width < NameColumnWidth + 160;
        SetValue(EffectiveNameColumnWidthPropertyKey, _compact ? double.NaN : NameColumnWidth);
        UpdateStates();
    }
    private void UpdateStates()
    {
        if (!_hasTemplate) return;
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, IsKeyboardFocusWithin ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, _compact ? "Compact" : "Wide", false);
    }
    private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (PropertyGridRow)d;
        // A draft retained across template replacement belongs to that property.
        row._pendingEdit = null;
        row.Owner?.UnregisterRow(row, e.OldValue as PropertyItem);
        row.SetValue(DisplayNamePropertyKey, row.Property?.DisplayName ?? string.Empty);
        row.SetValue(DescriptionPropertyKey, row.Property?.Description ?? string.Empty);
        row.Owner?.RegisterRow(row);
        OnSelectionChanged(d, e);
    }
    private static void OnOwnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (PropertyGridRow)d;
        (e.OldValue as PropertyGrid)?.UnregisterRow(row);
        row.Owner?.RegisterRow(row);
    }
    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (PropertyGridRow)d;
        row.SetValue(IsSelectedPropertyKey, row.Property != null && ReferenceEquals(row.Property, row.SelectedProperty));
        row.UpdateStates();
    }
    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var row = (PropertyGridRow)d;
        row.UpdateLayoutMode(row.ActualWidth > 0 ? row.ActualWidth : double.PositiveInfinity);
        row.InvalidateMeasure();
    }
}
