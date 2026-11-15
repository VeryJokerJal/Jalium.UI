using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls.Charts;
[TemplatePart(Name="PART_PlotView",Type=typeof(SparklinePlotView))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Empty",GroupName="DataStates")]
[TemplateVisualState(Name="Populated",GroupName="DataStates")]
public partial class Sparkline
{
    private SparklinePlotView? _sparklineView;
    private bool _refreshingSparkline;
    private (Size,Thickness,CornerRadius)? _sparklineClipStamp;
    private static readonly DependencyPropertyKey HasDataPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HasData),typeof(bool),typeof(Sparkline),new PropertyMetadata(false));
    public static readonly DependencyProperty HasDataProperty=HasDataPropertyKey.DependencyProperty;
    public bool HasData=>(bool)GetValue(HasDataProperty)!;
    private static readonly DependencyPropertyKey SparklineContentClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SparklineContentClip),typeof(Geometry),typeof(Sparkline),new PropertyMetadata(null));
    public static readonly DependencyProperty SparklineContentClipProperty=SparklineContentClipPropertyKey.DependencyProperty;
    public Geometry? SparklineContentClip=>(Geometry?)GetValue(SparklineContentClipProperty);
    static Sparkline()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(Sparkline),new PropertyMetadata(typeof(Sparkline)));
    public override void OnApplyTemplate(){ReleaseSparkline();base.OnApplyTemplate();_sparklineView=GetTemplateChild("PART_PlotView") as SparklinePlotView;_sparklineView?.SetCurrentValue(SparklinePlotView.OwnerProperty,this);RefreshSparklinePresentation();}
    protected override void OnTemplateChanging(){ReleaseSparkline();base.OnTemplateChanging();}
    private void ReleaseSparkline(){if(_sparklineView is { } view){view.ClearBinding(SparklinePlotView.OwnerProperty);view.SetCurrentValue(SparklinePlotView.OwnerProperty,null);}_sparklineView=null;}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(!_refreshingSparkline&&TemplateRootInternal is not null)RefreshSparklinePresentation();_sparklineView?.InvalidateVisual();}
    protected internal override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);RefreshSparklinePresentation();}
    private void RefreshSparklinePresentation()
    {
        if(_refreshingSparkline)return;_refreshingSparkline=true;
        try
        {
            SetValue(HasDataPropertyKey,Values?.Count>0);
            if(RenderSize.Width>0&&RenderSize.Height>0&&_sparklineClipStamp!=(RenderSize,BorderThickness,CornerRadius))
            {
                _sparklineClipStamp=(RenderSize,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(SparklineContentClipPropertyKey,clip);
            }
            if(TemplateRootInternal is not null){VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,HasData?"Populated":"Empty",false);}
        }
        finally{_refreshingSparkline=false;}
    }
}
public sealed class SparklinePlotView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(Sparkline),typeof(SparklinePlotView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public Sparkline? Owner{get=>(Sparkline?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public SparklinePlotView()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderSparkline(dc);
}
