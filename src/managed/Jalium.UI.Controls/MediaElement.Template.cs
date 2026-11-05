using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls;

[TemplatePart(Name="PART_VideoSurface",Type=typeof(MediaVideoSurface))]
[TemplatePart(Name="PART_Status",Type=typeof(TextBlock))]
[TemplatePart(Name="PART_Subtitle",Type=typeof(TextBlock))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Empty",GroupName="ContentStates")]
[TemplateVisualState(Name="Loading",GroupName="ContentStates")]
[TemplateVisualState(Name="AudioOnly",GroupName="ContentStates")]
[TemplateVisualState(Name="Video",GroupName="ContentStates")]
[TemplateVisualState(Name="Stopped",GroupName="PlaybackStates")]
[TemplateVisualState(Name="Playing",GroupName="PlaybackStates")]
[TemplateVisualState(Name="Paused",GroupName="PlaybackStates")]
public partial class MediaElement
{
    private MediaVideoSurface? _templateVideoSurface;
    private bool _refreshingMediaPresentation;
    private (Size,Thickness,CornerRadius)? _mediaClipStamp;
    private static readonly DependencyPropertyKey MediaContentClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(MediaContentClip),typeof(Geometry),typeof(MediaElement),new PropertyMetadata(null));
    public static readonly DependencyProperty MediaContentClipProperty=MediaContentClipPropertyKey.DependencyProperty;
    public Geometry? MediaContentClip=>(Geometry?)GetValue(MediaContentClipProperty);
    static MediaElement()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(MediaElement),new PropertyMetadata(typeof(MediaElement)));
    public static readonly DependencyProperty AudioOnlyTextProperty=DependencyProperty.Register(nameof(AudioOnlyText),typeof(string),typeof(MediaElement),new PropertyMetadata("Audio Only"));
    public static readonly DependencyProperty LoadingTextProperty=DependencyProperty.Register(nameof(LoadingText),typeof(string),typeof(MediaElement),new PropertyMetadata("Loading..."));
    public static readonly DependencyProperty SubtitleForegroundProperty=DependencyProperty.Register(nameof(SubtitleForeground),typeof(Brush),typeof(MediaElement),new PropertyMetadata(new SolidColorBrush(Color.FromRgb(255,255,255))));
    public static readonly DependencyProperty SubtitleBackgroundProperty=DependencyProperty.Register(nameof(SubtitleBackground),typeof(Brush),typeof(MediaElement),new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180,0,0,0))));
    public static readonly DependencyProperty SubtitleFontSizeProperty=DependencyProperty.Register(nameof(SubtitleFontSize),typeof(double),typeof(MediaElement),new PropertyMetadata(24.0));
    public string AudioOnlyText{get=>(string)GetValue(AudioOnlyTextProperty)!;set=>SetValue(AudioOnlyTextProperty,value);}
    public string LoadingText{get=>(string)GetValue(LoadingTextProperty)!;set=>SetValue(LoadingTextProperty,value);}
    public Brush? SubtitleForeground{get=>(Brush?)GetValue(SubtitleForegroundProperty);set=>SetValue(SubtitleForegroundProperty,value);}
    public Brush? SubtitleBackground{get=>(Brush?)GetValue(SubtitleBackgroundProperty);set=>SetValue(SubtitleBackgroundProperty,value);}
    public double SubtitleFontSize{get=>(double)GetValue(SubtitleFontSizeProperty)!;set=>SetValue(SubtitleFontSizeProperty,value);}
    private static readonly DependencyPropertyKey DisplayStatusTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(DisplayStatusText),typeof(string),typeof(MediaElement),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayStatusTextProperty=DisplayStatusTextPropertyKey.DependencyProperty;
    public string DisplayStatusText=>(string)GetValue(DisplayStatusTextProperty)!;
    private static readonly DependencyPropertyKey DisplayStatusVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(DisplayStatusVisibility),typeof(Visibility),typeof(MediaElement),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty DisplayStatusVisibilityProperty=DisplayStatusVisibilityPropertyKey.DependencyProperty;
    public Visibility DisplayStatusVisibility=>(Visibility)GetValue(DisplayStatusVisibilityProperty)!;
    private static readonly DependencyPropertyKey CurrentSubtitlePropertyKey=DependencyProperty.RegisterReadOnly(nameof(CurrentSubtitle),typeof(string),typeof(MediaElement),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CurrentSubtitleProperty=CurrentSubtitlePropertyKey.DependencyProperty;
    public string CurrentSubtitle=>(string)GetValue(CurrentSubtitleProperty)!;
    private static readonly DependencyPropertyKey SubtitleVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SubtitleVisibility),typeof(Visibility),typeof(MediaElement),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty SubtitleVisibilityProperty=SubtitleVisibilityPropertyKey.DependencyProperty;
    public Visibility SubtitleVisibility=>(Visibility)GetValue(SubtitleVisibilityProperty)!;
    private static readonly DependencyPropertyKey ComputedSubtitleLineHeightPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ComputedSubtitleLineHeight),typeof(double),typeof(MediaElement),new PropertyMetadata(30.0));
    public static readonly DependencyProperty ComputedSubtitleLineHeightProperty=ComputedSubtitleLineHeightPropertyKey.DependencyProperty;
    public double ComputedSubtitleLineHeight=>(double)GetValue(ComputedSubtitleLineHeightProperty)!;
    private static readonly DependencyPropertyKey ComputedSubtitleMaxWidthPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ComputedSubtitleMaxWidth),typeof(double),typeof(MediaElement),new PropertyMetadata(0.0));
    public static readonly DependencyProperty ComputedSubtitleMaxWidthProperty=ComputedSubtitleMaxWidthPropertyKey.DependencyProperty;
    public double ComputedSubtitleMaxWidth=>(double)GetValue(ComputedSubtitleMaxWidthProperty)!;
    private void InitializeMediaTemplateBehavior()=>SizeChanged+=(_,_)=>RefreshMediaPresentation();
    public override void OnApplyTemplate()
    {
        ReleaseMediaParts();base.OnApplyTemplate();_templateVideoSurface=GetTemplateChild("PART_VideoSurface") as MediaVideoSurface;
        _templateVideoSurface?.SetCurrentValue(MediaVideoSurface.OwnerProperty,this);RefreshMediaPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseMediaParts();base.OnTemplateChanging();}
    private void ReleaseMediaParts()
    {
        if(_templateVideoSurface is { } surface){surface.ClearBinding(MediaVideoSurface.OwnerProperty);surface.SetCurrentValue(MediaVideoSurface.OwnerProperty,null);}_templateVideoSurface=null;
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);if(!_refreshingMediaPresentation)RefreshMediaPresentation();_templateVideoSurface?.InvalidateVisual();
    }
    private void RefreshMediaPresentation()
    {
        if(_refreshingMediaPresentation)return;_refreshingMediaPresentation=true;
        try
        {
            if(RenderSize.Width>0&&RenderSize.Height>0&&_mediaClipStamp!=(RenderSize,BorderThickness,CornerRadius))
            {
                _mediaClipStamp=(RenderSize,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(MediaContentClipPropertyKey,clip);
            }
            var hasFrame=_hasVideo&&(_frameBitmap is not null||_d3dImage is {IsFrontBufferAvailable:true} image&&image.NativeHandle!=nint.Zero);
            var state=!_hasVideo&&_hasAudio?"AudioOnly":hasFrame?"Video":_hasVideo||_isOpening?"Loading":"Empty";
            var status=state=="AudioOnly"?AudioOnlyText:state=="Loading"?LoadingText:string.Empty;
            SetValue(DisplayStatusTextPropertyKey,status);SetValue(DisplayStatusVisibilityPropertyKey,string.IsNullOrEmpty(status)?Visibility.Collapsed:Visibility.Visible);
            SetValue(ComputedSubtitleLineHeightPropertyKey,Math.Max(0,SubtitleFontSize*1.3));SetValue(ComputedSubtitleMaxWidthPropertyKey,Math.Max(0,RenderSize.Width-64));
            var subtitle=_subtitleText??string.Empty;SetValue(CurrentSubtitlePropertyKey,subtitle);SetValue(SubtitleVisibilityPropertyKey,!string.IsNullOrWhiteSpace(subtitle)&&RenderSize.Width>40&&RenderSize.Height>20?Visibility.Visible:Visibility.Collapsed);
            if(TemplateRootInternal is not null)
            {
                VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,state,false);
                VisualStateManager.GoToState(this,_isPaused?"Paused":_isPlaying?"Playing":"Stopped",false);
            }
        }
        finally{_refreshingMediaPresentation=false;}
    }
    private void InvalidateMediaView()
    {
        if(!CheckAccess()){Dispatcher.BeginInvoke(InvalidateMediaView);return;}
        RefreshMediaPresentation();base.InvalidateVisual();_templateVideoSurface?.InvalidateVisual();
    }
}
/// <summary>Displays decoded video frames; MediaElement templates provide all overlays.</summary>
public sealed class MediaVideoSurface : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(MediaElement),typeof(MediaVideoSurface),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public MediaElement? Owner{get=>(MediaElement?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public MediaVideoSurface()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderVideoSurface(dc);
}
