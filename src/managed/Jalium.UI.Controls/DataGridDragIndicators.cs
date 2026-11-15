namespace Jalium.UI.Controls;

/// <summary>A replaceable column-drag preview shared by DataGrid and TreeDataGrid.</summary>
[TemplatePart(Name = "PART_Surface", Type = typeof(Border))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class DataGridColumnDragIndicator : ContentControl
{
    static DataGridColumnDragIndicator()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DataGridColumnDragIndicator), new PropertyMetadata(typeof(DataGridColumnDragIndicator)));
        FocusableProperty.OverrideMetadata(typeof(DataGridColumnDragIndicator), new PropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(DataGridColumnDragIndicator), new PropertyMetadata(false));
    }
    public DataGridColumnDragIndicator() => UseTemplateContentManagement();

    public static readonly DependencyProperty SurfaceStyleProperty = DependencyProperty.Register(
        nameof(SurfaceStyle), typeof(Style), typeof(DataGridColumnDragIndicator), new PropertyMetadata(null));
    /// <summary>Optional style for the default surface, including existing Border indicator styles.</summary>
    public Style? SurfaceStyle { get => (Style?)GetValue(SurfaceStyleProperty); set => SetValue(SurfaceStyleProperty, value); }

    private Style? _previousControlStyle;
    private Style? _previousSurfaceStyle;
    private Style? _appliedControlStyle;
    private Style? _appliedSurfaceStyle;

    internal void ApplyOwnerStyle(Style? style)
    {
        ClearOwnerStyle();
        if (style == null) return;
        if (style.TargetType?.IsAssignableFrom(typeof(Border)) == true && !style.TargetType.IsAssignableFrom(GetType()))
        {
            _previousSurfaceStyle = SurfaceStyle;
            _appliedSurfaceStyle = style;
            SetCurrentValue(SurfaceStyleProperty, style);
        }
        else
        {
            _previousControlStyle = Style;
            _appliedControlStyle = style;
            SetCurrentValue(StyleProperty, style);
        }
    }
    internal void ClearOwnerStyle()
    {
        // A style changed externally during a drag wins over restoring its old value.
        if (_appliedControlStyle != null && ReferenceEquals(Style, _appliedControlStyle))
            SetCurrentValue(StyleProperty, _previousControlStyle);
        if (_appliedSurfaceStyle != null && ReferenceEquals(SurfaceStyle, _appliedSurfaceStyle))
            SetCurrentValue(SurfaceStyleProperty, _previousSurfaceStyle);
        _previousControlStyle = _previousSurfaceStyle = _appliedControlStyle = _appliedSurfaceStyle = null;
    }
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty) UpdateState();
    }
    private void UpdateState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}

/// <summary>A templated insertion marker for column reordering.</summary>
public class DataGridDropLocationIndicator : DataGridColumnDragIndicator
{
    static DataGridDropLocationIndicator() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DataGridDropLocationIndicator), new PropertyMetadata(typeof(DataGridDropLocationIndicator)));
}
