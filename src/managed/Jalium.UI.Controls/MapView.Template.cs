using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls;

[TemplatePart(Name="PART_MapSurface",Type=typeof(MapViewSurface))]
[TemplatePart(Name="PART_ZoomInButton",Type=typeof(Button))]
[TemplatePart(Name="PART_ZoomOutButton",Type=typeof(Button))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Focused",GroupName="FocusStates")]
[TemplateVisualState(Name="Unfocused",GroupName="FocusStates")]
[TemplateVisualState(Name="Idle",GroupName="InteractionStates")]
[TemplateVisualState(Name="Dragging",GroupName="InteractionStates")]
public partial class MapView
{
    private MapViewSurface? _mapSurface;
    private Button? _zoomInButton;
    private Button? _zoomOutButton;
    private bool _refreshingMap;
    private (Size,Thickness,CornerRadius)? _mapClipStamp;
    static MapView()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(MapView),new PropertyMetadata(typeof(MapView)));
    private static readonly DependencyPropertyKey MapContentClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(MapContentClip),typeof(Geometry),typeof(MapView),new PropertyMetadata(null));
    public static readonly DependencyProperty MapContentClipProperty=MapContentClipPropertyKey.DependencyProperty;
    public Geometry? MapContentClip=>(Geometry?)GetValue(MapContentClipProperty);
    private static readonly DependencyPropertyKey ZoomInCommandPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ZoomInCommand),typeof(ICommand),typeof(MapView),new PropertyMetadata(null));
    public static readonly DependencyProperty ZoomInCommandProperty=ZoomInCommandPropertyKey.DependencyProperty;
    public ICommand ZoomInCommand=>(ICommand)GetValue(ZoomInCommandProperty)!;
    private static readonly DependencyPropertyKey ZoomOutCommandPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ZoomOutCommand),typeof(ICommand),typeof(MapView),new PropertyMetadata(null));
    public static readonly DependencyProperty ZoomOutCommandProperty=ZoomOutCommandPropertyKey.DependencyProperty;
    public ICommand ZoomOutCommand=>(ICommand)GetValue(ZoomOutCommandProperty)!;
    private static readonly DependencyPropertyKey CanZoomInPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CanZoomIn),typeof(bool),typeof(MapView),new PropertyMetadata(false));
    public static readonly DependencyProperty CanZoomInProperty=CanZoomInPropertyKey.DependencyProperty;
    public bool CanZoomIn=>(bool)GetValue(CanZoomInProperty)!;
    private static readonly DependencyPropertyKey CanZoomOutPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CanZoomOut),typeof(bool),typeof(MapView),new PropertyMetadata(false));
    public static readonly DependencyProperty CanZoomOutProperty=CanZoomOutPropertyKey.DependencyProperty;
    public bool CanZoomOut=>(bool)GetValue(CanZoomOutProperty)!;
    private static readonly DependencyPropertyKey ZoomControlsVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ZoomControlsVisibility),typeof(Visibility),typeof(MapView),new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty ZoomControlsVisibilityProperty=ZoomControlsVisibilityPropertyKey.DependencyProperty;
    public Visibility ZoomControlsVisibility=>(Visibility)GetValue(ZoomControlsVisibilityProperty)!;
    private static readonly DependencyPropertyKey ScaleBarVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ScaleBarVisibility),typeof(Visibility),typeof(MapView),new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty ScaleBarVisibilityProperty=ScaleBarVisibilityPropertyKey.DependencyProperty;
    public Visibility ScaleBarVisibility=>(Visibility)GetValue(ScaleBarVisibilityProperty)!;
    private static readonly DependencyPropertyKey AttributionVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(AttributionVisibility),typeof(Visibility),typeof(MapView),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty AttributionVisibilityProperty=AttributionVisibilityPropertyKey.DependencyProperty;
    public Visibility AttributionVisibility=>(Visibility)GetValue(AttributionVisibilityProperty)!;
    private static readonly DependencyPropertyKey ScaleBarLabelPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ScaleBarLabel),typeof(string),typeof(MapView),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ScaleBarLabelProperty=ScaleBarLabelPropertyKey.DependencyProperty;
    public string ScaleBarLabel=>(string)GetValue(ScaleBarLabelProperty)!;
    private static readonly DependencyPropertyKey ScaleBarWidthPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ScaleBarWidth),typeof(double),typeof(MapView),new PropertyMetadata(0.0));
    public static readonly DependencyProperty ScaleBarWidthProperty=ScaleBarWidthPropertyKey.DependencyProperty;
    public double ScaleBarWidth=>(double)GetValue(ScaleBarWidthProperty)!;
    private static readonly DependencyPropertyKey AttributionTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(AttributionText),typeof(string),typeof(MapView),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty AttributionTextProperty=AttributionTextPropertyKey.DependencyProperty;
    public string AttributionText=>(string)GetValue(AttributionTextProperty)!;
    private void InitializeMapTemplateBehavior()
    {
        SetValue(ZoomInCommandPropertyKey,new RelayCommand(_=>{if(CanExecuteZoom(true))ZoomIn();},_=>CanExecuteZoom(true)));
        SetValue(ZoomOutCommandPropertyKey,new RelayCommand(_=>{if(CanExecuteZoom(false))ZoomOut();},_=>CanExecuteZoom(false)));
        SizeChanged+=(_,_)=>RefreshMapPresentation();
    }
    private bool CanExecuteZoom(bool zoomIn)=>IsEnabled&&IsZoomEnabled&&(zoomIn?ZoomLevel<MaxZoomLevel:ZoomLevel>MinZoomLevel);
    public override void OnApplyTemplate()
    {
        ReleaseMapParts();base.OnApplyTemplate();_mapSurface=GetTemplateChild("PART_MapSurface") as MapViewSurface;
        _mapSurface?.SetCurrentValue(MapViewSurface.OwnerProperty,this);
        _zoomInButton=GetTemplateChild("PART_ZoomInButton") as Button;_zoomOutButton=GetTemplateChild("PART_ZoomOutButton") as Button;
        RefreshMapPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseMapParts();base.OnTemplateChanging();}
    private void ReleaseMapParts()
    {
        _isDragging=false;if(IsMouseCaptured)ReleaseMouseCapture();
        if(_mapSurface is { } surface){surface.ClearBinding(MapViewSurface.OwnerProperty);surface.SetCurrentValue(MapViewSurface.OwnerProperty,null);}_mapSurface=null;
        foreach(var button in new[]{_zoomInButton,_zoomOutButton})if(button is not null){button.ClearBinding(Button.CommandProperty);button.SetCurrentValue(Button.CommandProperty,null);}
        _zoomInButton=_zoomOutButton=null;
    }
    private bool IsMapOverlaySource(object? source)
    {
        for(var node=source as Visual;node is not null&&!ReferenceEquals(node,this);node=node.VisualParent)
            if(ReferenceEquals(node,_zoomInButton)||ReferenceEquals(node,_zoomOutButton))return true;
        return false;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);if(!_refreshingMap)RefreshMapPresentation();_mapSurface?.InvalidateVisual();
    }
    private void RefreshMapPresentation()
    {
        if(_refreshingMap)return;_refreshingMap=true;
        try
        {
            SetValue(CanZoomInPropertyKey,CanExecuteZoom(true));SetValue(CanZoomOutPropertyKey,CanExecuteZoom(false));
            SetValue(ZoomControlsVisibilityPropertyKey,ShowZoomControls?Visibility.Visible:Visibility.Collapsed);
            SetValue(ScaleBarVisibilityPropertyKey,ShowScaleBar?Visibility.Visible:Visibility.Collapsed);
            var attribution=TileSource?.Attribution??string.Empty;SetValue(AttributionTextPropertyKey,attribution);SetValue(AttributionVisibilityPropertyKey,ShowAttribution&&!string.IsNullOrWhiteSpace(attribution)?Visibility.Visible:Visibility.Collapsed);
            var resolution=MercatorProjection.GroundResolution(Center.Latitude,ZoomLevel);var distance=GetNiceScaleDistance(120*resolution);
            SetValue(ScaleBarWidthPropertyKey,double.IsFinite(distance/resolution)?Math.Clamp(distance/resolution,0,120):0);
            SetValue(ScaleBarLabelPropertyKey,distance>=1000?$"{distance/1000:F0} km":$"{distance:F0} m");
            if(RenderSize.Width>0&&RenderSize.Height>0&&_mapClipStamp!=(RenderSize,BorderThickness,CornerRadius))
            {
                _mapClipStamp=(RenderSize,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(MapContentClipPropertyKey,clip);
            }
            if(TemplateRootInternal is not null){VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,IsKeyboardFocusWithin?"Focused":"Unfocused",false);VisualStateManager.GoToState(this,_isDragging?"Dragging":"Idle",false);}
        }
        finally{_refreshingMap=false;}
    }
    private void InvalidateMapView(){RefreshMapPresentation();base.InvalidateVisual();_mapSurface?.InvalidateVisual();}
}

/// <summary>Intrinsic map rendering; MapView templates own the frame and overlays.</summary>
public sealed class MapViewSurface : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(MapView),typeof(MapViewSurface),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public MapView? Owner{get=>(MapView?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public MapViewSurface()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderMapSurface(dc);
}

/// <summary>A reusable scale legend for geographic views.</summary>
public sealed class MapScaleBar : Control
{
    public static readonly DependencyProperty LabelProperty=DependencyProperty.Register(nameof(Label),typeof(string),typeof(MapScaleBar),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ScaleWidthProperty=DependencyProperty.Register(nameof(ScaleWidth),typeof(double),typeof(MapScaleBar),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public string Label{get=>(string)GetValue(LabelProperty)!;set=>SetValue(LabelProperty,value);}
    public double ScaleWidth{get=>(double)GetValue(ScaleWidthProperty)!;set=>SetValue(ScaleWidthProperty,value);}
    static MapScaleBar()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(MapScaleBar),new PropertyMetadata(typeof(MapScaleBar)));
}
