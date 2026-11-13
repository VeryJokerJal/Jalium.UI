namespace Jalium.UI.Controls.Primitives;

/// <summary>A templated row header with selection, pointer and content states.</summary>
[TemplatePart(Name = "PART_ContentPresenter", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Pressed", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Content", GroupName = "ContentStates")]
[TemplateVisualState(Name = "Indicator", GroupName = "ContentStates")]
public class DataGridRowHeader : ButtonBase
{
    public DataGridRowHeader() => ClearValue(FocusableProperty);

    static DataGridRowHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DataGridRowHeader), new PropertyMetadata(typeof(DataGridRowHeader)));
        WidthProperty.OverrideMetadata(typeof(DataGridRowHeader), new PropertyMetadata(30.0));
        FocusableProperty.OverrideMetadata(typeof(DataGridRowHeader), new PropertyMetadata(false));
        HorizontalContentAlignmentProperty.OverrideMetadata(typeof(DataGridRowHeader), new PropertyMetadata(HorizontalAlignment.Center));
        VerticalContentAlignmentProperty.OverrideMetadata(typeof(DataGridRowHeader), new PropertyMetadata(VerticalAlignment.Center));
    }

    public static readonly DependencyProperty IsRowSelectedProperty = DependencyProperty.Register(
        nameof(IsRowSelected), typeof(bool), typeof(DataGridRowHeader), new PropertyMetadata(false));
    public static readonly DependencyProperty RowIndexProperty = DependencyProperty.Register(
        nameof(RowIndex), typeof(int), typeof(DataGridRowHeader), new PropertyMetadata(-1));
    public static readonly DependencyProperty SeparatorBrushProperty = DependencyProperty.Register(
        nameof(SeparatorBrush), typeof(Jalium.UI.Media.Brush), typeof(DataGridRowHeader), new PropertyMetadata(null));
    public static readonly DependencyProperty SeparatorVisibilityProperty = DependencyProperty.Register(
        nameof(SeparatorVisibility), typeof(Visibility), typeof(DataGridRowHeader), new PropertyMetadata(Visibility.Visible));
    private static readonly DependencyPropertyKey HasHeaderContentPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasHeaderContent), typeof(bool), typeof(DataGridRowHeader), new PropertyMetadata(false));
    public static readonly DependencyProperty HasHeaderContentProperty = HasHeaderContentPropertyKey.DependencyProperty;

    public bool IsRowSelected { get => (bool)GetValue(IsRowSelectedProperty)!; internal set => SetValue(IsRowSelectedProperty, value); }
    public int RowIndex { get => (int)GetValue(RowIndexProperty)!; set => SetValue(RowIndexProperty, value); }
    public Jalium.UI.Media.Brush? SeparatorBrush { get => (Jalium.UI.Media.Brush?)GetValue(SeparatorBrushProperty); set => SetValue(SeparatorBrushProperty, value); }
    public Visibility SeparatorVisibility { get => (Visibility)GetValue(SeparatorVisibilityProperty)!; set => SetValue(SeparatorVisibilityProperty, value); }
    /// <summary>Whether explicit header content or a header data template is available.</summary>
    public bool HasHeaderContent => (bool)GetValue(HasHeaderContentProperty)!;

    private bool _usesItemContent;
    private bool _settingRowContent;

    // Keep the item available to template selectors without displaying its ToString()
    // as a header when the owning row has no explicit header.
    internal void SetRowContent(object? content, bool usesItemContent)
    {
        _usesItemContent = usesItemContent;
        _settingRowContent = true;
        try { SetCurrentValue(ContentProperty, content); }
        finally { _settingRowContent = false; }
        UpdateContentState();
    }

    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (!_settingRowContent) _usesItemContent = false;
        UpdateContentState();
    }

    protected override void OnContentTemplateChanged(DataTemplate? oldContentTemplate, DataTemplate? newContentTemplate)
    {
        base.OnContentTemplateChanged(oldContentTemplate, newContentTemplate);
        UpdateContentState();
    }

    protected override void OnContentTemplateSelectorChanged(DataTemplateSelector? oldContentTemplateSelector, DataTemplateSelector? newContentTemplateSelector)
    {
        base.OnContentTemplateSelectorChanged(oldContentTemplateSelector, newContentTemplateSelector);
        UpdateContentState();
    }

    private void UpdateContentState()
    {
        SetValue(HasHeaderContentPropertyKey, Content != null &&
            (!_usesItemContent || ContentTemplate != null || ContentTemplateSelector != null));
        UpdateVisualState();
    }

    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateVisualState(); }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == IsMouseOverProperty ||
            e.Property == IsPressedProperty || e.Property == IsRowSelectedProperty)
            UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsPressed ? "Pressed" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsRowSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, HasHeaderContent ? "Content" : "Indicator", false);
    }
}
