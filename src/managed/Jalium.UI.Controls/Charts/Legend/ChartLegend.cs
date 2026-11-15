using System.Collections;

namespace Jalium.UI.Controls.Charts;

/// <summary>Template-driven chart legend with replaceable item and layout templates.</summary>
[TemplatePart(Name="PART_Items",Type=typeof(ItemsControl))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Horizontal",GroupName="LayoutStates")]
[TemplateVisualState(Name="Vertical",GroupName="LayoutStates")]
public class ChartLegend : Control
{
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this,Jalium.UI.Automation.Peers.AutomationControlType.Pane);
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ChartLegend),
            new PropertyMetadata(Orientation.Horizontal, OnPresentationChanged));

    /// <summary>
    /// Identifies the Items dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(ChartLegend),
            new PropertyMetadata(null, OnPresentationChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of legend item layout.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the legend items to display.
    /// </summary>
    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    #endregion

    public static readonly DependencyProperty ItemTemplateProperty=DependencyProperty.Register(nameof(ItemTemplate),typeof(DataTemplate),typeof(ChartLegend),new PropertyMetadata(null));
    public static readonly DependencyProperty ItemContainerStyleProperty=DependencyProperty.Register(nameof(ItemContainerStyle),typeof(Style),typeof(ChartLegend),new PropertyMetadata(null));
    public static readonly DependencyProperty ItemsPanelProperty=DependencyProperty.Register(nameof(ItemsPanel),typeof(ItemsPanelTemplate),typeof(ChartLegend),new PropertyMetadata(null));
    public DataTemplate? ItemTemplate { get=>(DataTemplate?)GetValue(ItemTemplateProperty); set=>SetValue(ItemTemplateProperty,value); }
    public Style? ItemContainerStyle { get=>(Style?)GetValue(ItemContainerStyleProperty); set=>SetValue(ItemContainerStyleProperty,value); }
    public ItemsPanelTemplate? ItemsPanel { get=>(ItemsPanelTemplate?)GetValue(ItemsPanelProperty); set=>SetValue(ItemsPanelProperty,value); }
    private ItemsControl? _items;
    static ChartLegend()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(ChartLegend),new PropertyMetadata(typeof(ChartLegend)));
    public override void OnApplyTemplate() { ReleaseItems(); base.OnApplyTemplate(); _items=GetTemplateChild("PART_Items") as ItemsControl; UpdateStates(); }
    protected override void OnTemplateChanging() { ReleaseItems(); base.OnTemplateChanging(); }
    private void ReleaseItems() { if(_items is not null) { _items.ClearBinding(ItemsControl.ItemsSourceProperty); _items.ClearValue(ItemsControl.ItemsSourceProperty); } _items=null; }
    protected override void OnIsEnabledChanged(bool oldValue,bool newValue) { base.OnIsEnabledChanged(oldValue,newValue); UpdateStates(); }
    private static void OnPresentationChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) { var legend=(ChartLegend)d; legend.InvalidateMeasure(); legend.UpdateStates(); }
    private void UpdateStates() { if(TemplateRootInternal is null)return; VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false); VisualStateManager.GoToState(this,Orientation.ToString(),false); }
}
