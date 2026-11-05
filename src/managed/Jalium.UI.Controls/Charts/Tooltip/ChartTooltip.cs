using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>Template-driven series data tooltip with optional consumer content.</summary>
[TemplatePart(Name="PART_Content",Type=typeof(ContentPresenter))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="HasTitle",GroupName="TitleStates")]
[TemplateVisualState(Name="NoTitle",GroupName="TitleStates")]
[TemplateVisualState(Name="HasXValue",GroupName="XValueStates")]
[TemplateVisualState(Name="NoXValue",GroupName="XValueStates")]
[TemplateVisualState(Name="HasYValue",GroupName="YValueStates")]
[TemplateVisualState(Name="NoYValue",GroupName="YValueStates")]
[TemplateVisualState(Name="HasSeriesBrush",GroupName="MarkerStates")]
[TemplateVisualState(Name="NoSeriesBrush",GroupName="MarkerStates")]
public class ChartTooltip : ContentControl
{
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()=>new Jalium.UI.Automation.Peers.GenericAutomationPeer(this,Jalium.UI.Automation.Peers.AutomationControlType.ToolTip);
    #region Dependency Properties

    /// <summary>
    /// Identifies the SeriesTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesTitleProperty =
        DependencyProperty.Register(nameof(SeriesTitle), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnPresentationChanged));

    /// <summary>
    /// Identifies the XValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty XValueProperty =
        DependencyProperty.Register(nameof(XValue), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnPresentationChanged));

    /// <summary>
    /// Identifies the YValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty YValueProperty =
        DependencyProperty.Register(nameof(YValue), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnPresentationChanged));

    /// <summary>
    /// Identifies the SeriesBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeriesBrushProperty =
        DependencyProperty.Register(nameof(SeriesBrush), typeof(Brush), typeof(ChartTooltip),
            new PropertyMetadata(null, OnPresentationChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the series title to display.
    /// </summary>
    public string? SeriesTitle
    {
        get => (string?)GetValue(SeriesTitleProperty);
        set => SetValue(SeriesTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the formatted X value to display.
    /// </summary>
    public string? XValue
    {
        get => (string?)GetValue(XValueProperty);
        set => SetValue(XValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the formatted Y value to display.
    /// </summary>
    public string? YValue
    {
        get => (string?)GetValue(YValueProperty);
        set => SetValue(YValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the series color brush.
    /// </summary>
    public Brush? SeriesBrush
    {
        get => (Brush?)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    #endregion

    private ContentPresenter? _content;
    static ChartTooltip()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(ChartTooltip),new PropertyMetadata(typeof(ChartTooltip)));
    public override void OnApplyTemplate() { ReleaseContent(); base.OnApplyTemplate(); _content=GetTemplateChild("PART_Content") as ContentPresenter; UpdateStates(); }
    protected override void OnTemplateChanging() { ReleaseContent(); base.OnTemplateChanging(); }
    private void ReleaseContent() { if(_content is not null) { _content.ClearBinding(ContentPresenter.ContentProperty); _content.ClearValue(ContentPresenter.ContentProperty); } _content=null; }
    protected override void OnIsEnabledChanged(bool oldValue,bool newValue) { base.OnIsEnabledChanged(oldValue,newValue); UpdateStates(); }
    private static void OnPresentationChanged(DependencyObject d,DependencyPropertyChangedEventArgs e) { var tooltip=(ChartTooltip)d; tooltip.InvalidateMeasure(); tooltip.UpdateStates(); }
    private void UpdateStates()
    {
        if(TemplateRootInternal is null)return;
        VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);
        VisualStateManager.GoToState(this,string.IsNullOrEmpty(SeriesTitle)?"NoTitle":"HasTitle",false);
        VisualStateManager.GoToState(this,string.IsNullOrEmpty(XValue)?"NoXValue":"HasXValue",false);
        VisualStateManager.GoToState(this,string.IsNullOrEmpty(YValue)?"NoYValue":"HasYValue",false);
        VisualStateManager.GoToState(this,SeriesBrush is null?"NoSeriesBrush":"HasSeriesBrush",false);
    }
}
