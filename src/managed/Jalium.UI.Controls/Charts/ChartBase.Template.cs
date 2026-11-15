using System.Collections;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls.Charts;

public sealed record ChartTooltipInfo(string? SeriesTitle,string? XValue,string? YValue,Brush? SeriesBrush);
[TemplatePart(Name="PART_PlotView",Type=typeof(ChartPlotView))]
[TemplatePart(Name="PART_Title",Type=typeof(TextBlock))]
[TemplatePart(Name="PART_Legend",Type=typeof(ChartLegend))]
[TemplatePart(Name="PART_Tooltip",Type=typeof(ChartTooltip))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="LegendHorizontal",GroupName="LegendStates")]
[TemplateVisualState(Name="LegendVertical",GroupName="LegendStates")]
public abstract partial class ChartBase
{
    static ChartBase()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(ChartBase),new PropertyMetadata(typeof(ChartBase)));
    private ChartPlotView? _plotView;
    private ChartLegend? _legendPresenter;
    private ChartTooltip? _tooltipPresenter;
    private bool _refreshingChartPresentation;
    private ChartTooltipInfo? _tooltipInfo;
    private Point _tooltipAnchor;
    private (Size,Thickness,CornerRadius)? _chartClipStamp;
    private static readonly DependencyPropertyKey ChartContentClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ChartContentClip),typeof(Geometry),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty ChartContentClipProperty=ChartContentClipPropertyKey.DependencyProperty;
    public Geometry? ChartContentClip=>(Geometry?)GetValue(ChartContentClipProperty);
    private static readonly DependencyPropertyKey ChartTitleBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ChartTitleBounds),typeof(Rect),typeof(ChartBase),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty ChartTitleBoundsProperty=ChartTitleBoundsPropertyKey.DependencyProperty;
    public Rect ChartTitleBounds=>(Rect)GetValue(ChartTitleBoundsProperty)!;
    private static readonly DependencyPropertyKey ChartTitleVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ChartTitleVisibility),typeof(Visibility),typeof(ChartBase),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty ChartTitleVisibilityProperty=ChartTitleVisibilityPropertyKey.DependencyProperty;
    public Visibility ChartTitleVisibility=>(Visibility)GetValue(ChartTitleVisibilityProperty)!;
    private static readonly DependencyPropertyKey ResolvedTitleForegroundPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ResolvedTitleForeground),typeof(Brush),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty ResolvedTitleForegroundProperty=ResolvedTitleForegroundPropertyKey.DependencyProperty;
    public Brush? ResolvedTitleForeground=>(Brush?)GetValue(ResolvedTitleForegroundProperty)!;
    private static readonly DependencyPropertyKey LegendItemsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(LegendItems),typeof(IEnumerable),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty LegendItemsProperty=LegendItemsPropertyKey.DependencyProperty;
    public IEnumerable? LegendItems=>(IEnumerable?)GetValue(LegendItemsProperty)!;
    private static readonly DependencyPropertyKey LegendBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(LegendBounds),typeof(Rect),typeof(ChartBase),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty LegendBoundsProperty=LegendBoundsPropertyKey.DependencyProperty;
    public Rect LegendBounds=>(Rect)GetValue(LegendBoundsProperty)!;
    private static readonly DependencyPropertyKey LegendVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(LegendVisibility),typeof(Visibility),typeof(ChartBase),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty LegendVisibilityProperty=LegendVisibilityPropertyKey.DependencyProperty;
    public Visibility LegendVisibility=>(Visibility)GetValue(LegendVisibilityProperty)!;
    private static readonly DependencyPropertyKey LegendOrientationPropertyKey=DependencyProperty.RegisterReadOnly(nameof(LegendOrientation),typeof(Orientation),typeof(ChartBase),new PropertyMetadata(Orientation.Horizontal));
    public static readonly DependencyProperty LegendOrientationProperty=LegendOrientationPropertyKey.DependencyProperty;
    public Orientation LegendOrientation=>(Orientation)GetValue(LegendOrientationProperty)!;
    private static readonly DependencyPropertyKey ActiveTooltipInfoPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ActiveTooltipInfo),typeof(ChartTooltipInfo),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty ActiveTooltipInfoProperty=ActiveTooltipInfoPropertyKey.DependencyProperty;
    public ChartTooltipInfo? ActiveTooltipInfo=>(ChartTooltipInfo?)GetValue(ActiveTooltipInfoProperty)!;
    private static readonly DependencyPropertyKey TooltipBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(TooltipBounds),typeof(Rect),typeof(ChartBase),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty TooltipBoundsProperty=TooltipBoundsPropertyKey.DependencyProperty;
    public Rect TooltipBounds=>(Rect)GetValue(TooltipBoundsProperty)!;
    private static readonly DependencyPropertyKey TooltipVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(TooltipVisibility),typeof(Visibility),typeof(ChartBase),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty TooltipVisibilityProperty=TooltipVisibilityPropertyKey.DependencyProperty;
    public Visibility TooltipVisibility=>(Visibility)GetValue(TooltipVisibilityProperty)!;
    private static readonly DependencyPropertyKey TooltipContentPropertyKey=DependencyProperty.RegisterReadOnly(nameof(TooltipContent),typeof(object),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty TooltipContentProperty=TooltipContentPropertyKey.DependencyProperty;
    public object? TooltipContent=>(object?)GetValue(TooltipContentProperty)!;
    private static readonly DependencyPropertyKey DefaultTooltipInfoPropertyKey=DependencyProperty.RegisterReadOnly(nameof(DefaultTooltipInfo),typeof(ChartTooltipInfo),typeof(ChartBase),new PropertyMetadata(null));
    public static readonly DependencyProperty DefaultTooltipInfoProperty=DefaultTooltipInfoPropertyKey.DependencyProperty;
    public ChartTooltipInfo? DefaultTooltipInfo=>(ChartTooltipInfo?)GetValue(DefaultTooltipInfoProperty)!;
    private static readonly DependencyPropertyKey TooltipMaxWidthPropertyKey=DependencyProperty.RegisterReadOnly(nameof(TooltipMaxWidth),typeof(double),typeof(ChartBase),new PropertyMetadata(280.0));
    public static readonly DependencyProperty TooltipMaxWidthProperty=TooltipMaxWidthPropertyKey.DependencyProperty;
    public double TooltipMaxWidth=>(double)GetValue(TooltipMaxWidthProperty)!;
    private static readonly DependencyPropertyKey TooltipMaxHeightPropertyKey=DependencyProperty.RegisterReadOnly(nameof(TooltipMaxHeight),typeof(double),typeof(ChartBase),new PropertyMetadata(200.0));
    public static readonly DependencyProperty TooltipMaxHeightProperty=TooltipMaxHeightPropertyKey.DependencyProperty;
    public double TooltipMaxHeight=>(double)GetValue(TooltipMaxHeightProperty)!;
    private static readonly DependencyPropertyKey ChartTitleLineHeightPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ChartTitleLineHeight),typeof(double),typeof(ChartBase),new PropertyMetadata(22.5));
    public static readonly DependencyProperty ChartTitleLineHeightProperty=ChartTitleLineHeightPropertyKey.DependencyProperty;
    public double ChartTitleLineHeight=>(double)GetValue(ChartTitleLineHeightProperty)!;
    public override void OnApplyTemplate()
    {
        ReleaseChartParts();base.OnApplyTemplate();_plotView=GetTemplateChild("PART_PlotView") as ChartPlotView;
        _plotView?.SetCurrentValue(ChartPlotView.OwnerProperty,this);
        _legendPresenter=GetTemplateChild("PART_Legend") as ChartLegend;_tooltipPresenter=GetTemplateChild("PART_Tooltip") as ChartTooltip;
        RefreshChartPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseChartParts();base.OnTemplateChanging();}
    private void ReleaseChartParts()
    {
        if(_plotView is { } view){view.ClearBinding(ChartPlotView.OwnerProperty);view.SetCurrentValue(ChartPlotView.OwnerProperty,null);}_plotView=null;
        if(_legendPresenter is { } legend){legend.ClearBinding(ChartLegend.ItemsProperty);legend.SetCurrentValue(ChartLegend.ItemsProperty,null);}_legendPresenter=null;
        if(_tooltipPresenter is { } tip)
            foreach(var property in new[]{ChartTooltip.SeriesTitleProperty,ChartTooltip.XValueProperty,ChartTooltip.YValueProperty,ChartTooltip.SeriesBrushProperty,ContentControl.ContentProperty,ContentControl.ContentTemplateProperty}){tip.ClearBinding(property);tip.ClearValue(property);}
        _tooltipPresenter=null;_isTooltipShown=false;_tooltipInfo=null;
    }
    protected internal override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);RefreshChartPresentation();}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);if(!_refreshingChartPresentation&&TemplateRootInternal is not null)RefreshChartPresentation();_plotView?.InvalidateVisual();
    }
    private void RefreshChartPresentation()
    {
        if(_refreshingChartPresentation||TemplateRootInternal is null||RenderSize.Width<=0||RenderSize.Height<=0)return;
        _refreshingChartPresentation=true;
        try
        {
            var size=RenderSize;var margin=PlotAreaMargin;var title=string.IsNullOrEmpty(Title)?0:Math.Max(0,TitleFontSize+8);
            SetValue(ChartTitleLineHeightPropertyKey,Math.Max(0,TitleFontSize*1.25));
            SetValue(ChartTitleBoundsPropertyKey,new Rect(0,Math.Max(0,margin.Top/2),size.Width,title));
            SetValue(ChartTitleVisibilityPropertyKey,title>0?Visibility.Visible:Visibility.Collapsed);SetValue(ResolvedTitleForegroundPropertyKey,TitleForeground??Foreground??s_defaultTitleForeground);
            var items=CollectLegendItems()?.Select((item,index)=>item.Brush is null?new ChartLegendItem{Label=item.Label,Brush=GetSeriesBrush(index),IsVisible=item.IsVisible}:item).ToArray()??[];
            var previous=LegendItems?.Cast<ChartLegendItem>().ToArray();
            if(previous is null||previous.Length!=items.Length||previous.Where((item,index)=>item.Label!=items[index].Label||!ReferenceEquals(item.Brush,items[index].Brush)||item.IsVisible!=items[index].IsVisible).Any())SetValue(LegendItemsPropertyKey,items);
            var vertical=LegendPosition is LegendPosition.Left or LegendPosition.Right;
            SetValue(LegendOrientationPropertyKey,vertical?Orientation.Vertical:Orientation.Horizontal);
            SetValue(LegendVisibilityPropertyKey,IsLegendVisible&&items.Length>0?Visibility.Visible:Visibility.Collapsed);
            var plot=GetPlotArea();var legendBounds=LegendPosition switch
            {
                LegendPosition.Top=>new Rect(Math.Max(0,margin.Left),Math.Max(0,margin.Top+title),Math.Max(0,size.Width-margin.Left-margin.Right),24),
                LegendPosition.Left=>new Rect(8,Math.Max(0,plot.Top),Math.Max(0,Math.Min(84,size.Width-16)),Math.Max(0,plot.Height)),
                LegendPosition.Right=>new Rect(Math.Max(8,size.Width-92),Math.Max(0,plot.Top),Math.Max(0,Math.Min(84,size.Width-16)),Math.Max(0,plot.Height)),
                _=>new Rect(Math.Max(0,margin.Left),Math.Max(0,size.Height-24),Math.Max(0,size.Width-margin.Left-margin.Right),24)
            };
            SetValue(LegendBoundsPropertyKey,legendBounds);SetValue(ActiveTooltipInfoPropertyKey,_tooltipInfo);
            SetValue(DefaultTooltipInfoPropertyKey,TooltipTemplate is null?_tooltipInfo:null);SetValue(TooltipContentPropertyKey,TooltipTemplate is null?null:_tooltipInfo);
            SetValue(TooltipMaxWidthPropertyKey,Math.Min(280,Math.Max(0,size.Width-4)));SetValue(TooltipMaxHeightPropertyKey,Math.Max(0,size.Height-4));
            var show=IsTooltipEnabled&&_isTooltipShown&&_tooltipInfo is not null;SetValue(TooltipVisibilityPropertyKey,show?Visibility.Visible:Visibility.Collapsed);
            if(show&&_tooltipPresenter is { } tooltip)
            {
                tooltip.Measure(new Size(Math.Min(280,Math.Max(0,size.Width-4)),Math.Max(0,size.Height-4)));
                var width=Math.Clamp(tooltip.DesiredSize.Width,0,Math.Max(0,size.Width-4));var height=Math.Clamp(tooltip.DesiredSize.Height,0,Math.Max(0,size.Height-4));
                SetValue(TooltipBoundsPropertyKey,new Rect(Math.Clamp(_tooltipAnchor.X+12,2,Math.Max(2,size.Width-width-2)),Math.Clamp(_tooltipAnchor.Y+12,2,Math.Max(2,size.Height-height-2)),width,height));
            }
            if(_chartClipStamp!=(size,BorderThickness,CornerRadius))
            {
                _chartClipStamp=(size,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(size),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(size).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(ChartContentClipPropertyKey,clip);
            }
            VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,vertical?"LegendVertical":"LegendHorizontal",false);
        }
        finally{_refreshingChartPresentation=false;}
    }
    internal void RenderPlotSurface(DrawingContext dc){var plot=GetPlotArea();if(plot.Width>0&&plot.Height>0)RenderChart(dc,plot);}
}
/// <summary>Renders chart data and axes; templates own chart headers, legends, tooltips and frames.</summary>
public sealed class ChartPlotView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(ChartBase),typeof(ChartPlotView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public ChartBase? Owner{get=>(ChartBase?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public ChartPlotView()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderPlotSurface(dc);
}
