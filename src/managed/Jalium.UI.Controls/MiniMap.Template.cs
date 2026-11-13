using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls;

[TemplatePart(Name="PART_OverviewSurface",Type=typeof(MiniMapSurface))]
[TemplatePart(Name="PART_Viewport",Type=typeof(Border))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Idle",GroupName="InteractionStates")]
[TemplateVisualState(Name="Dragging",GroupName="InteractionStates")]
public partial class MiniMap
{
    private MiniMapSurface? _overviewSurface;
    private bool _refreshingMiniMap;
    private (Size,Thickness,CornerRadius)? _overviewClipStamp;
    static MiniMap()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(MiniMap),new PropertyMetadata(typeof(MiniMap)));
        BorderThicknessProperty.OverrideMetadata(typeof(MiniMap),new PropertyMetadata(new Thickness(1)));
        CornerRadiusProperty.OverrideMetadata(typeof(MiniMap),new PropertyMetadata(new CornerRadius(3)));
    }
    private static readonly DependencyPropertyKey OverviewContentClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(OverviewContentClip),typeof(Geometry),typeof(MiniMap),new PropertyMetadata(null));
    public static readonly DependencyProperty OverviewContentClipProperty=OverviewContentClipPropertyKey.DependencyProperty;
    public Geometry? OverviewContentClip=>(Geometry?)GetValue(OverviewContentClipProperty);
    private static readonly DependencyPropertyKey ViewportBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ViewportBounds),typeof(Rect),typeof(MiniMap),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty ViewportBoundsProperty=ViewportBoundsPropertyKey.DependencyProperty;
    public Rect ViewportBounds=>(Rect)GetValue(ViewportBoundsProperty)!;
    private static readonly DependencyPropertyKey ViewportVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ViewportVisibility),typeof(Visibility),typeof(MiniMap),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty ViewportVisibilityProperty=ViewportVisibilityPropertyKey.DependencyProperty;
    public Visibility ViewportVisibility=>(Visibility)GetValue(ViewportVisibilityProperty)!;
    private static readonly DependencyPropertyKey EffectiveViewportBrushPropertyKey=DependencyProperty.RegisterReadOnly(nameof(EffectiveViewportBrush),typeof(Brush),typeof(MiniMap),new PropertyMetadata(null));
    public static readonly DependencyProperty EffectiveViewportBrushProperty=EffectiveViewportBrushPropertyKey.DependencyProperty;
    public Brush? EffectiveViewportBrush=>(Brush?)GetValue(EffectiveViewportBrushProperty);
    private static readonly DependencyPropertyKey EffectiveViewportBorderBrushPropertyKey=DependencyProperty.RegisterReadOnly(nameof(EffectiveViewportBorderBrush),typeof(Brush),typeof(MiniMap),new PropertyMetadata(null));
    public static readonly DependencyProperty EffectiveViewportBorderBrushProperty=EffectiveViewportBorderBrushPropertyKey.DependencyProperty;
    public Brush? EffectiveViewportBorderBrush=>(Brush?)GetValue(EffectiveViewportBorderBrushProperty);
    private static readonly DependencyPropertyKey EffectiveViewportBorderThicknessPropertyKey=DependencyProperty.RegisterReadOnly(nameof(EffectiveViewportBorderThickness),typeof(Thickness),typeof(MiniMap),new PropertyMetadata(default(Thickness)));
    public static readonly DependencyProperty EffectiveViewportBorderThicknessProperty=EffectiveViewportBorderThicknessPropertyKey.DependencyProperty;
    public Thickness EffectiveViewportBorderThickness=>(Thickness)GetValue(EffectiveViewportBorderThicknessProperty)!;
    private void InitializeMiniMapTemplateBehavior()=>SizeChanged+=(_,_)=>RefreshMiniMapPresentation();
    public override void OnApplyTemplate()
    {
        ReleaseMiniMapParts();base.OnApplyTemplate();_overviewSurface=GetTemplateChild("PART_OverviewSurface") as MiniMapSurface;
        _overviewSurface?.SetCurrentValue(MiniMapSurface.OwnerProperty,this);RefreshMiniMapPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseMiniMapParts();base.OnTemplateChanging();}
    private void ReleaseMiniMapParts()
    {
        _isDraggingViewport=false;if(IsMouseCaptured)ReleaseMouseCapture();
        if(_overviewSurface is { } surface){surface.ClearBinding(MiniMapSurface.OwnerProperty);surface.SetCurrentValue(MiniMapSurface.OwnerProperty,null);}_overviewSurface=null;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);if(!_refreshingMiniMap)RefreshMiniMapPresentation();_overviewSurface?.InvalidateVisual();
    }
    private void RefreshMiniMapPresentation()
    {
        if(_refreshingMiniMap)return;_refreshingMiniMap=true;
        try
        {
            var bounds=ComputeViewportRect(ControlRenderGeometry.GetContentRect(new Rect(RenderSize),new Thickness(2)));
            SetValue(ViewportBoundsPropertyKey,bounds.IsEmpty?default(Rect):bounds);SetValue(ViewportVisibilityPropertyKey,bounds.Width>0&&bounds.Height>0?Visibility.Visible:Visibility.Collapsed);
            SetValue(EffectiveViewportBrushPropertyKey,ViewportBrush??s_defaultViewportBrush);SetValue(EffectiveViewportBorderBrushPropertyKey,ViewportBorderBrush??s_defaultViewportBorderBrush);SetValue(EffectiveViewportBorderThicknessPropertyKey,new Thickness(Math.Max(0,ViewportBorderThickness)));
            if(RenderSize.Width>0&&RenderSize.Height>0&&_overviewClipStamp!=(RenderSize,BorderThickness,CornerRadius))
            {
                _overviewClipStamp=(RenderSize,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(OverviewContentClipPropertyKey,clip);
            }
            if(TemplateRootInternal is not null){VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,_isDraggingViewport?"Dragging":"Idle",false);}
        }
        finally{_refreshingMiniMap=false;}
    }
    private void InvalidateMiniMapView(){RefreshMiniMapPresentation();base.InvalidateVisual();_overviewSurface?.InvalidateVisual();}
}
/// <summary>Renders overview content and markers; the MiniMap template owns its frame and viewport.</summary>
public sealed class MiniMapSurface : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(MiniMap),typeof(MiniMapSurface),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public MiniMap? Owner{get=>(MiniMap?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public MiniMapSurface()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderMiniMapSurface(dc);
}
