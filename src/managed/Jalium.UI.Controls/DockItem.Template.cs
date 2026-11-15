using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

[TemplatePart(Name = "PART_CloseButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Header", Type = typeof(ContentPresenter))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Unselected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "IdleBackground", GroupName = "BackgroundStates")]
[TemplateVisualState(Name = "PointerBackground", GroupName = "BackgroundStates")]
[TemplateVisualState(Name = "SelectedBackground", GroupName = "BackgroundStates")]
[TemplateVisualState(Name = "CloseVisible", GroupName = "CloseStates")]
[TemplateVisualState(Name = "CloseHidden", GroupName = "CloseStates")]
[TemplateVisualState(Name = "NotClosable", GroupName = "CloseStates")]
[TemplateVisualState(Name = "TextHeader", GroupName = "HeaderStates")]
[TemplateVisualState(Name = "CustomHeader", GroupName = "HeaderStates")]
[TemplateVisualState(Name = "Top", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Bottom", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Left", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Right", GroupName = "PlacementStates")]
[TemplateVisualState(Name = "Reordering", GroupName = "DragStates")]
[TemplateVisualState(Name = "NotReordering", GroupName = "DragStates")]
public partial class DockItem
{
    /// <summary>Identifies the optional close button style used by the default template.</summary>
    public static readonly DependencyProperty CloseButtonStyleProperty = DependencyProperty.Register(nameof(CloseButtonStyle), typeof(Style), typeof(DockItem), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey ChromeCornerRadiusPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ChromeCornerRadius), typeof(CornerRadius), typeof(DockItem), new PropertyMetadata(default(CornerRadius)));
    private static readonly DependencyPropertyKey TabStripPlacementPropertyKey = DependencyProperty.RegisterReadOnly(nameof(TabStripPlacement), typeof(Dock), typeof(DockItem), new PropertyMetadata(Dock.Top));
    private static readonly DependencyPropertyKey IsReorderingPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsReordering), typeof(bool), typeof(DockItem), new PropertyMetadata(false));
    public static readonly DependencyProperty ChromeCornerRadiusProperty = ChromeCornerRadiusPropertyKey.DependencyProperty;
    public static readonly DependencyProperty TabStripPlacementProperty = TabStripPlacementPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsReorderingProperty = IsReorderingPropertyKey.DependencyProperty;
    /// <summary>Gets or sets the close button style.</summary>
    public Style? CloseButtonStyle { get => (Style?)GetValue(CloseButtonStyleProperty); set => SetValue(CloseButtonStyleProperty, value); }
    /// <summary>Gets the corner radii derived from the strip placement and actual tab dimensions.</summary>
    public CornerRadius ChromeCornerRadius => (CornerRadius)GetValue(ChromeCornerRadiusProperty)!;
    /// <summary>Gets the placement of the owning tab strip.</summary>
    public Dock TabStripPlacement => (Dock)GetValue(TabStripPlacementProperty)!;
    /// <summary>Gets whether the item is currently previewing a reorder operation.</summary>
    public bool IsReordering => (bool)GetValue(IsReorderingProperty)!;
    private ButtonBase? _templateCloseButton;
    private ContentPresenter? _templateHeader;
    static DockItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DockItem), new PropertyMetadata(typeof(DockItem)));
    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        ReleaseTemplateParts(); base.OnApplyTemplate();
        _templateCloseButton = GetTemplateChild("PART_CloseButton") as ButtonBase;
        if (_templateCloseButton is not null) _templateCloseButton.Click += OnTemplateClose;
        _templateHeader = GetTemplateChild("PART_Header") as ContentPresenter;
        UpdateTemplatePresentation();
    }
    /// <inheritdoc />
    protected override void OnTemplateChanging() { if (_isReorderDragging) CancelReorderDrag(); ReleaseTemplateParts(); base.OnTemplateChanging(); }
    private void ReleaseTemplateParts()
    {
        if (_templateCloseButton is not null) _templateCloseButton.Click -= OnTemplateClose;
        if (_templateHeader is not null) { _templateHeader.ClearBinding(ContentPresenter.ContentProperty); _templateHeader.ClearValue(ContentPresenter.ContentProperty); }
        _templateCloseButton = null; _templateHeader = null;
    }
    private void OnTemplateClose(object sender, RoutedEventArgs e) => TryClose();
    /// <summary>Closes the tab through its owning panel when enabled and closable.</summary>
    public bool TryClose()
    {
        if (!IsEnabled || !CanClose || OwnerPanel is not { } panel || !panel.Items.Contains(this)) return false;
        panel.CloseItem(this); return true;
    }
    /// <inheritdoc />
    protected override void OnHeaderChanged(object? oldHeader, object? newHeader) { base.OnHeaderChanged(oldHeader, newHeader); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnHeaderTemplateChanged(DataTemplate? oldHeaderTemplate, DataTemplate? newHeaderTemplate) { base.OnHeaderTemplateChanged(oldHeaderTemplate, newHeaderTemplate); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnHeaderTemplateSelectorChanged(DataTemplateSelector? oldSelector, DataTemplateSelector? newSelector) { base.OnHeaderTemplateSelectorChanged(oldSelector, newSelector); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnHeaderStringFormatChanged(string? oldValue, string? newValue) { base.OnHeaderStringFormatChanged(oldValue, newValue); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue) { base.OnIsMouseOverChanged(oldValue, newValue); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateTemplatePresentation(); }
    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) { base.OnPropertyChanged(e); if (e.Property == CornerRadiusProperty) UpdateTemplatePresentation(); }
    internal void RefreshDockPlacement() { UpdateTemplatePresentation(); InvalidateMeasure(); InvalidateArrange(); }
    private void UpdateTemplatePresentation(Size? arrangedSize = null)
    {
        var placement = OwnerPanel?.TabStripPlacement ?? Dock.Top;
        SetValue(TabStripPlacementPropertyKey, placement); SetValue(IsReorderingPropertyKey, _isReorderDragging);
        var size = arrangedSize ?? RenderSize; var radius = ResolveTabCornerRadius(size.Width, size.Height + 1);
        SetValue(ChromeCornerRadiusPropertyKey, placement switch { Dock.Bottom => new CornerRadius(0,0,radius,radius), Dock.Left => new CornerRadius(radius,0,0,radius), Dock.Right => new CornerRadius(0,radius,radius,0), _ => new CornerRadius(radius,radius,0,0) });
        if (TemplateRootInternal is null) return;
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
        VisualStateManager.GoToState(this, IsSelected ? "SelectedBackground" : IsMouseOver ? "PointerBackground" : "IdleBackground", false);
        VisualStateManager.GoToState(this, !CanClose ? "NotClosable" : IsSelected || IsMouseOver ? "CloseVisible" : "CloseHidden", false);
        VisualStateManager.GoToState(this, Header is UIElement || HeaderTemplate is not null || HeaderTemplateSelector is not null || HeaderStringFormat is not null ? "CustomHeader" : "TextHeader", false);
        VisualStateManager.GoToState(this, placement.ToString(), false);
        VisualStateManager.GoToState(this, _isReorderDragging ? "Reordering" : "NotReordering", false);
    }
}
