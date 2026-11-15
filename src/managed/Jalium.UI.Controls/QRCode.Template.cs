using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls;
[TemplatePart(Name="PART_SymbolView",Type=typeof(QRCodeView))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Empty",GroupName="ContentStates")]
[TemplateVisualState(Name="Ready",GroupName="ContentStates")]
[TemplateVisualState(Name="Error",GroupName="ContentStates")]
public partial class QRCode
{
    private static readonly DependencyPropertyKey GenerationErrorTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(GenerationErrorText),typeof(string),typeof(QRCode),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GenerationErrorTextProperty=GenerationErrorTextPropertyKey.DependencyProperty;
    public string GenerationErrorText=>(string)GetValue(GenerationErrorTextProperty)!;
    private QRCodeView? _symbolView;
    private (Size,Thickness,CornerRadius)? _symbolClipStamp;
    private bool _refreshingSymbolPresentation;
    private static readonly DependencyPropertyKey SymbolClipPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SymbolClip),typeof(Geometry),typeof(QRCode),new PropertyMetadata(null));
    public static readonly DependencyProperty SymbolClipProperty=SymbolClipPropertyKey.DependencyProperty;
    public Geometry? SymbolClip=>(Geometry?)GetValue(SymbolClipProperty);
    static QRCode()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(QRCode),new PropertyMetadata(typeof(QRCode)));
    public override void OnApplyTemplate()
    {
        ReleaseSymbolView();base.OnApplyTemplate();_symbolView=GetTemplateChild("PART_SymbolView") as QRCodeView;
        _symbolView?.SetCurrentValue(QRCodeView.OwnerProperty,this);RefreshSymbolPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseSymbolView();base.OnTemplateChanging();}
    private void ReleaseSymbolView(){if(_symbolView is { } view){view.ClearBinding(QRCodeView.OwnerProperty);view.SetCurrentValue(QRCodeView.OwnerProperty,null);}_symbolView=null;}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);_symbolView?.InvalidateVisual();if(!_refreshingSymbolPresentation&&TemplateRootInternal is not null)RefreshSymbolPresentation();
    }
    protected internal override void OnRenderSizeChanged(SizeChangedInfo sizeInfo){base.OnRenderSizeChanged(sizeInfo);RefreshSymbolPresentation();}
    private void RefreshSymbolPresentation()
    {
        if(_refreshingSymbolPresentation)return;_refreshingSymbolPresentation=true;
        try
        {
            EnsureSymbol();SetValue(GenerationErrorTextPropertyKey,_generationError??string.Empty);
            if(RenderSize.Width>0&&RenderSize.Height>0&&_symbolClipStamp!=(RenderSize,BorderThickness,CornerRadius))
            {
                _symbolClipStamp=(RenderSize,BorderThickness,CornerRadius);var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();SetValue(SymbolClipPropertyKey,clip);
            }
            if(TemplateRootInternal is not null){VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);VisualStateManager.GoToState(this,_symbol is not null?"Ready":_generationError is not null?"Error":"Empty",false);}
        }
        finally{_refreshingSymbolPresentation=false;}
    }
    private void InvalidateSymbolView(){RefreshSymbolPresentation();base.InvalidateVisual();_symbolView?.InvalidateVisual();}
}
/// <summary>Renders QR modules and quiet-zone pixels; QRCode templates own the surrounding frame.</summary>
public sealed class QRCodeView : FrameworkElement
{
    public static readonly DependencyProperty OwnerProperty=DependencyProperty.Register(nameof(Owner),typeof(QRCode),typeof(QRCodeView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public QRCode? Owner{get=>(QRCode?)GetValue(OwnerProperty);set=>SetValue(OwnerProperty,value);}
    public QRCodeView()=>Focusable=false;
    protected override bool ParticipatesInRenderCache=>false;
    protected override Size MeasureOverride(Size size)=>new(double.IsFinite(size.Width)?size.Width:0,double.IsFinite(size.Height)?size.Height:0);
    protected override void OnRender(DrawingContext dc)=>Owner?.RenderSymbol(dc);
}
