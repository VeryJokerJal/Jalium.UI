using Jalium.UI.Interop;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Editor.LanguageServer.Protocol;
using Jalium.UI.Input;
using Jalium.UI.Media;
namespace Jalium.UI.Controls;

public partial class EditControl
{
    private EditorCompletionList? _completionPresenter;
    private EditorTextHint? _hoverPresenter;
    private EditorTextHint? _signaturePresenter;
    private EditorTextHint? _previewPresenter;
    private CompletionList? _completionListSnapshot;
    private CompletionItem[]? _completionItemsSnapshot;
    private EditorCompletionRows? _completionRows;
    private int _overlayGeneration;
    private (string Label,string? Active)? _signatureStamp;
    private bool _suspendEditorOverlays;
    private bool _releasingEditorParts;
    private static readonly DependencyPropertyKey CompletionItemsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CompletionItems),typeof(System.Collections.IEnumerable),typeof(EditControl),new PropertyMetadata(null));
    public static readonly DependencyProperty CompletionItemsProperty=CompletionItemsPropertyKey.DependencyProperty;
    public System.Collections.IEnumerable? CompletionItems=>(System.Collections.IEnumerable?)GetValue(CompletionItemsProperty)!;
    private static readonly DependencyPropertyKey CompletionIndexPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CompletionIndex),typeof(int),typeof(EditControl),new PropertyMetadata(-1));
    public static readonly DependencyProperty CompletionIndexProperty=CompletionIndexPropertyKey.DependencyProperty;
    public int CompletionIndex=>(int)GetValue(CompletionIndexProperty)!;
    private static readonly DependencyPropertyKey CompletionBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CompletionBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty CompletionBoundsProperty=CompletionBoundsPropertyKey.DependencyProperty;
    public Rect CompletionBounds=>(Rect)GetValue(CompletionBoundsProperty)!;
    private static readonly DependencyPropertyKey CompletionDisplayPropertyKey=DependencyProperty.RegisterReadOnly(nameof(CompletionDisplay),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty CompletionDisplayProperty=CompletionDisplayPropertyKey.DependencyProperty;
    public Visibility CompletionDisplay=>(Visibility)GetValue(CompletionDisplayProperty)!;
    private static readonly DependencyPropertyKey HoverTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HoverText),typeof(string),typeof(EditControl),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HoverTextProperty=HoverTextPropertyKey.DependencyProperty;
    public string HoverText=>(string)GetValue(HoverTextProperty)!;
    private static readonly DependencyPropertyKey HoverBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HoverBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty HoverBoundsProperty=HoverBoundsPropertyKey.DependencyProperty;
    public Rect HoverBounds=>(Rect)GetValue(HoverBoundsProperty)!;
    private static readonly DependencyPropertyKey HoverDisplayPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HoverDisplay),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty HoverDisplayProperty=HoverDisplayPropertyKey.DependencyProperty;
    public Visibility HoverDisplay=>(Visibility)GetValue(HoverDisplayProperty)!;
    private static readonly DependencyPropertyKey SignatureTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SignatureText),typeof(string),typeof(EditControl),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SignatureTextProperty=SignatureTextPropertyKey.DependencyProperty;
    public string SignatureText=>(string)GetValue(SignatureTextProperty)!;
    private static readonly DependencyPropertyKey SignatureSegmentsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SignatureSegments),typeof(System.Collections.IEnumerable),typeof(EditControl),new PropertyMetadata(null));
    public static readonly DependencyProperty SignatureSegmentsProperty=SignatureSegmentsPropertyKey.DependencyProperty;
    public System.Collections.IEnumerable? SignatureSegments=>(System.Collections.IEnumerable?)GetValue(SignatureSegmentsProperty)!;
    private static readonly DependencyPropertyKey SignatureCounterPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SignatureCounter),typeof(string),typeof(EditControl),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SignatureCounterProperty=SignatureCounterPropertyKey.DependencyProperty;
    public string SignatureCounter=>(string)GetValue(SignatureCounterProperty)!;
    private static readonly DependencyPropertyKey SignatureBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SignatureBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty SignatureBoundsProperty=SignatureBoundsPropertyKey.DependencyProperty;
    public Rect SignatureBounds=>(Rect)GetValue(SignatureBoundsProperty)!;
    private static readonly DependencyPropertyKey SignatureDisplayPropertyKey=DependencyProperty.RegisterReadOnly(nameof(SignatureDisplay),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty SignatureDisplayProperty=SignatureDisplayPropertyKey.DependencyProperty;
    public Visibility SignatureDisplay=>(Visibility)GetValue(SignatureDisplayProperty)!;

    private static readonly DependencyPropertyKey PreviewTextPropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewText),typeof(string),typeof(EditControl),new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty PreviewTextProperty=PreviewTextPropertyKey.DependencyProperty;
    public string PreviewText=>(string)GetValue(PreviewTextProperty)!;
    private static readonly DependencyPropertyKey PreviewLinesPropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewLines),typeof(IReadOnlyList<EditorPreviewLine>),typeof(EditControl),new PropertyMetadata(null));
    public static readonly DependencyProperty PreviewLinesProperty=PreviewLinesPropertyKey.DependencyProperty;
    public IReadOnlyList<EditorPreviewLine>? PreviewLines=>(IReadOnlyList<EditorPreviewLine>?)GetValue(PreviewLinesProperty)!;
    private static readonly DependencyPropertyKey PreviewBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty PreviewBoundsProperty=PreviewBoundsPropertyKey.DependencyProperty;
    public Rect PreviewBounds=>(Rect)GetValue(PreviewBoundsProperty)!;
    private static readonly DependencyPropertyKey PreviewDisplayPropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewDisplay),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty PreviewDisplayProperty=PreviewDisplayPropertyKey.DependencyProperty;
    public Visibility PreviewDisplay=>(Visibility)GetValue(PreviewDisplayProperty)!;
    private static readonly DependencyPropertyKey MinimapPreviewLinesPropertyKey=DependencyProperty.RegisterReadOnly(nameof(MinimapPreviewLines),typeof(IReadOnlyList<EditorPreviewLine>),typeof(EditControl),new PropertyMetadata(null));
    public static readonly DependencyProperty MinimapPreviewLinesProperty=MinimapPreviewLinesPropertyKey.DependencyProperty;
    public IReadOnlyList<EditorPreviewLine>? MinimapPreviewLines=>(IReadOnlyList<EditorPreviewLine>?)GetValue(MinimapPreviewLinesProperty)!;
    private static readonly DependencyPropertyKey MinimapPreviewSizePropertyKey=DependencyProperty.RegisterReadOnly(nameof(MinimapPreviewSize),typeof(Size),typeof(EditControl),new PropertyMetadata(default(Size)));
    public static readonly DependencyProperty MinimapPreviewSizeProperty=MinimapPreviewSizePropertyKey.DependencyProperty;
    public Size MinimapPreviewSize=>(Size)GetValue(MinimapPreviewSizeProperty)!;
    private static readonly DependencyPropertyKey MinimapPreviewFontSizePropertyKey=DependencyProperty.RegisterReadOnly(nameof(MinimapPreviewFontSize),typeof(double),typeof(EditControl),new PropertyMetadata(12.0));
    public static readonly DependencyProperty MinimapPreviewFontSizeProperty=MinimapPreviewFontSizePropertyKey.DependencyProperty;
    public double MinimapPreviewFontSize=>(double)GetValue(MinimapPreviewFontSizeProperty)!;
    private static readonly DependencyPropertyKey MinimapPreviewLineHeightPropertyKey=DependencyProperty.RegisterReadOnly(nameof(MinimapPreviewLineHeight),typeof(double),typeof(EditControl),new PropertyMetadata(16.0));
    public static readonly DependencyProperty MinimapPreviewLineHeightProperty=MinimapPreviewLineHeightPropertyKey.DependencyProperty;
    public double MinimapPreviewLineHeight=>(double)GetValue(MinimapPreviewLineHeightProperty)!;
    private static readonly DependencyPropertyKey IsMinimapPreviewOpenPropertyKey=DependencyProperty.RegisterReadOnly(nameof(IsMinimapPreviewOpen),typeof(bool),typeof(EditControl),new PropertyMetadata(false));
    public static readonly DependencyProperty IsMinimapPreviewOpenProperty=IsMinimapPreviewOpenPropertyKey.DependencyProperty;
    public bool IsMinimapPreviewOpen=>(bool)GetValue(IsMinimapPreviewOpenProperty)!;

    private static readonly DependencyPropertyKey PreviewFontSizePropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewFontSize),typeof(double),typeof(EditControl),new PropertyMetadata(12.0));
    public static readonly DependencyProperty PreviewFontSizeProperty=PreviewFontSizePropertyKey.DependencyProperty;
    public double PreviewFontSize=>(double)GetValue(PreviewFontSizeProperty)!;
    private static readonly DependencyPropertyKey PreviewLineHeightPropertyKey=DependencyProperty.RegisterReadOnly(nameof(PreviewLineHeight),typeof(double),typeof(EditControl),new PropertyMetadata(16.0));
    public static readonly DependencyProperty PreviewLineHeightProperty=PreviewLineHeightPropertyKey.DependencyProperty;
    public double PreviewLineHeight=>(double)GetValue(PreviewLineHeightProperty)!;
    private void AttachEditorOverlayParts()
    {
        _completionPresenter=GetTemplateChild("PART_CompletionList") as EditorCompletionList;
        _hoverPresenter=GetTemplateChild("PART_HoverHint") as EditorTextHint;
        _signaturePresenter=GetTemplateChild("PART_SignatureHint") as EditorTextHint;
        _previewPresenter=GetTemplateChild("PART_PreviewHint") as EditorTextHint;
        _minimapTooltipPopup=GetTemplateChild("PART_MinimapPopup") as Popup;
        _minimapTooltipPresenter=GetTemplateChild("PART_MinimapHint") as EditorTextHint;
        if(_minimapTooltipPopup is not null)_minimapTooltipPopup.SetCurrentValue(Popup.PlacementTargetProperty,this);
    }
    private void ReleaseEditorOverlayParts()
    {
        var hadParts=_completionPresenter is not null||_hoverPresenter is not null||_signaturePresenter is not null;
        _overlayGeneration++;
        CloseMinimapHoverTooltipPopup();
        if(_minimapTooltipPopup is { } popup){popup.ClearBinding(Popup.PlacementTargetProperty);popup.SetCurrentValue(Popup.PlacementTargetProperty,null);popup.ClearBinding(Popup.IsOpenProperty);popup.SetCurrentValue(Popup.IsOpenProperty,false);}
        _minimapTooltipPopup=null;
        if(_completionPresenter is { } list)
        {
            list.ClearBinding(ItemsControl.ItemsSourceProperty);list.ClearValue(ItemsControl.ItemsSourceProperty);
            list.ClearBinding(Jalium.UI.Controls.Primitives.Selector.SelectedIndexProperty);
        }
        foreach(var hint in new[]{_hoverPresenter,_signaturePresenter,_previewPresenter,_minimapTooltipPresenter})
            if(hint is not null)
            {
                hint.ClearBinding(EditorTextHint.PreviewLinesProperty);hint.ClearValue(EditorTextHint.PreviewLinesProperty);
                hint.ClearBinding(EditorTextHint.SyntaxOwnerProperty);hint.ClearValue(EditorTextHint.SyntaxOwnerProperty);
                hint.ClearBinding(EditorTextHint.TextProperty);hint.ClearValue(EditorTextHint.TextProperty);
                hint.ClearBinding(EditorTextHint.SegmentsProperty);hint.ClearValue(EditorTextHint.SegmentsProperty);
                hint.ClearBinding(EditorTextHint.CounterProperty);hint.ClearValue(EditorTextHint.CounterProperty);
            }
        _completionPresenter=null;_hoverPresenter=null;_signaturePresenter=null;_previewPresenter=null;_minimapTooltipPresenter=null;
        _completionRows=null;_completionListSnapshot=null;_completionItemsSnapshot=null;
        if(hadParts)CloseTransientEditorOverlays();
        SetValue(CompletionItemsPropertyKey,null);SetValue(CompletionDisplayPropertyKey,Visibility.Collapsed);
        SetValue(HoverDisplayPropertyKey,Visibility.Collapsed);SetValue(SignatureDisplayPropertyKey,Visibility.Collapsed);SetValue(PreviewDisplayPropertyKey,Visibility.Collapsed);SetValue(PreviewLinesPropertyKey,null);SetValue(MinimapPreviewLinesPropertyKey,null);
    }
    private void CloseTransientEditorOverlays()
    {
        CloseMinimapHoverTooltipPopup();
        _lspIntegration?.DismissCompletion();_lspIntegration?.DismissHover();_lspIntegration?.DismissSignatureHelp();
    }
    private bool IsTemplateEditorOverlaySource(object? source)
    {
        for(var visual=source as Visual;visual is not null&&!ReferenceEquals(visual,this);visual=visual.VisualParent)
            if(ReferenceEquals(visual,_completionPresenter)||ReferenceEquals(visual,_hoverPresenter)||ReferenceEquals(visual,_signaturePresenter)||ReferenceEquals(visual,_previewPresenter))return true;
        return false;
    }
    private Rect FitEditorOverlay(double x,double y,double width,double height)
    {
        var right=GetContentRenderWidth(RenderSize.Width);var bottom=GetContentRenderHeight(RenderSize.Height);
        var left=Math.Min(Math.Max(2,_view.GutterWidth),Math.Max(2,right-2));
        width=Math.Clamp(width,0,Math.Max(0,right-left-2));height=Math.Clamp(height,0,Math.Max(0,bottom-4));
        return new Rect(Math.Clamp(x,left,Math.Max(left,right-width-2)),Math.Clamp(y,2,Math.Max(2,bottom-height-2)),width,height);
    }
    private void UpdateFoldAndScopePreview()
    {
        if(!IsEnabled||_suspendEditorOverlays||!_hasPointerPosition||_hoveredFoldedHintSection is null&&_hoveredScopeGuideSection is null)
        {SetValue(PreviewDisplayPropertyKey,Visibility.Collapsed);SetValue(PreviewLinesPropertyKey,null);return;}
        IReadOnlyList<EditorPreviewLine>? lines=_hoveredFoldedHintSection is { } folded?BuildFoldedSectionPreviewLines(folded):null;
        var text=lines is not null?BuildSyntaxPreviewText(lines):GetScopeGuideTooltipText(_hoveredScopeGuideSection!);
        if(string.IsNullOrWhiteSpace(text)){SetValue(PreviewDisplayPropertyKey,Visibility.Collapsed);return;}
        var font=Math.Max(11,FontSize-1);var layout=new FormattedText(text,FontFamily?.GetRenderingSource(this)??"monospace",font){MaxTextWidth=Math.Max(24,GetContentRenderWidth(RenderSize.Width)-20),Trimming=TextTrimming.None};TextMeasurement.MeasureText(layout);
        SetValue(PreviewFontSizePropertyKey,font);SetValue(PreviewLineHeightPropertyKey,GetEditorPreviewLineHeight(layout,lines?.Count??text.Split('\n').Length));
        var width=Math.Ceiling(layout.Width+ScopeGuideTooltipPaddingX*2);var height=Math.Ceiling(layout.Height+ScopeGuideTooltipPaddingY*2);
        var y=lines is null?_lastPointerPosition.Y-height-ScopeGuideTooltipOffsetY:_lastPointerPosition.Y+ScopeGuideTooltipOffsetY;
        if(y+height>GetContentRenderHeight(RenderSize.Height))y=_lastPointerPosition.Y-height-ScopeGuideTooltipOffsetY;
        if(y<2)y=_lastPointerPosition.Y+ScopeGuideTooltipOffsetY;
        SetValue(PreviewTextPropertyKey,text);
        if(!EqualsPreviewLines(PreviewLines,lines))SetValue(PreviewLinesPropertyKey,lines);
        SetValue(PreviewBoundsPropertyKey,FitEditorOverlay(_lastPointerPosition.X+ScopeGuideTooltipOffsetX,y,width,height));SetValue(PreviewDisplayPropertyKey,Visibility.Visible);
    }
    private static bool EqualsPreviewLines(IReadOnlyList<EditorPreviewLine>? first,IReadOnlyList<EditorPreviewLine>? second)=>ReferenceEquals(first,second)||first is not null&&second is not null&&first.SequenceEqual(second);
    private void UpdateEditorOverlayPresentation()
    {
        UpdateFoldAndScopePreview();
        var integration=_lspIntegration;
        var enabled=!_suspendEditorOverlays&&IsEnabled;
        var list=enabled&&!IsReadOnly&&integration?.IsCompletionActive==true?integration.ActiveCompletionList:null;
        if(!ReferenceEquals(list,_completionListSnapshot)||!ReferenceEquals(list?.Items,_completionItemsSnapshot))
        {
            _completionListSnapshot=list;_completionItemsSnapshot=list?.Items;
            var items=list?.Items;var generation=_overlayGeneration;
            _completionRows=items is null?null:new EditorCompletionRows(items.Length,index=>
            {
                var item=items[index];
                return new EditorCompletionRow(item.Label,item.Detail,GetCompletionKindPrefix(item.Kind),new RelayCommand(_=>
                {
                    if(generation!=_overlayGeneration||!IsEnabled||IsReadOnly||!ReferenceEquals(_lspIntegration?.ActiveCompletionList,list)||_completionPresenter is null)return;
                    _lspIntegration.ActiveCompletionIndex=index;_lspIntegration.ApplyCompletion(item);Focus();InvalidateEditorView();
                },_=>generation==_overlayGeneration&&IsEnabled&&!IsReadOnly&&ReferenceEquals(_lspIntegration?.ActiveCompletionList,list)));
            });
            SetValue(CompletionItemsPropertyKey,_completionRows);
        }
        SetValue(CompletionIndexPropertyKey,list is null?-1:Math.Clamp(integration!.ActiveCompletionIndex,-1,list.Items.Length-1));
        var completionVisible=list?.Items.Length>0;
        SetValue(CompletionDisplayPropertyKey,completionVisible?Visibility.Visible:Visibility.Collapsed);
        var (line,column)=_caret.GetLineColumn(_document);var caretY=_view.GetLineTop(line);var caretX=_view.GutterWidth+column*_view.CharWidth-_view.HorizontalOffset;
        if(completionVisible)
        {
            var height=Math.Min(list!.Items.Length,12)*Math.Max(28,_view.LineHeight+8)+8;var y=caretY+_view.LineHeight+2;
            if(y+height>GetContentRenderHeight(RenderSize.Height))y=caretY-height-2;
            SetValue(CompletionBoundsPropertyKey,FitEditorOverlay(caretX,y,360,height));
        }
        var hover=enabled&&!completionVisible?integration?.ActiveHover:null;
        var hoverText=hover is null?string.Empty:StripMarkdownFences(hover.Contents.Value??string.Empty);
        SetValue(HoverTextPropertyKey,hoverText);SetValue(HoverDisplayPropertyKey,string.IsNullOrWhiteSpace(hoverText)?Visibility.Collapsed:Visibility.Visible);
        if(!string.IsNullOrWhiteSpace(hoverText))
        {
            var lines=hoverText.Split('\n');var width=Math.Max(120,Math.Min(100,lines.Max(text=>text.Length))*_view.CharWidth+16);
            var height=lines.Length*Math.Max(16,FontSize+3)+16;
            var x=hover!.Range is { } range?_view.GutterWidth+range.Start.Character*_view.CharWidth-_view.HorizontalOffset:_hasPointerPosition?_lastPointerPosition.X:_view.GutterWidth;
            var y=hover.Range is { } source?_view.GetLineTop(source.Start.Line+1)-height-4:_hasPointerPosition?_lastPointerPosition.Y-height-4:caretY-height-4;
            if(y<2)y=caretY+_view.LineHeight+4;
            SetValue(HoverBoundsPropertyKey,FitEditorOverlay(x,y,width,height));
        }
        var signature=enabled&&!completionVisible?integration?.ActiveSignatureHelp:null;
        if(signature?.Signatures.Length>0)
        {
            var index=Math.Clamp(signature.ActiveSignature??0,0,signature.Signatures.Length-1);var current=signature.Signatures[index];var label=current.Label??string.Empty;
            var parameter=signature.ActiveParameter??current.ActiveParameter??0;var active=current.Parameters is { } parameters&&parameter>=0&&parameter<parameters.Length?parameters[parameter].Label:null;
            var start=string.IsNullOrEmpty(active)?-1:label.IndexOf(active,StringComparison.Ordinal);
            SetValue(SignatureTextPropertyKey,label);
            if(_signatureStamp!=(label,active))
            {
                _signatureStamp=(label,active);
                var segments=start<0?new[]{new EditorSignatureSegment(label,false)}:new[]{new EditorSignatureSegment(label[..start],false),new EditorSignatureSegment(active!,true),new EditorSignatureSegment(label[(start+active!.Length)..],false)};
                SetValue(SignatureSegmentsPropertyKey,segments);
            }
            SetValue(SignatureCounterPropertyKey,signature.Signatures.Length>1?$"{index+1}/{signature.Signatures.Length}":string.Empty);
            var width=Math.Max(200,label.Length*_view.CharWidth+24);var height=Math.Max(28,_view.LineHeight+16);var y=caretY-height-2;if(y<2)y=caretY+_view.LineHeight+2;
            SetValue(SignatureBoundsPropertyKey,FitEditorOverlay(caretX,y,width,height));SetValue(SignatureDisplayPropertyKey,Visibility.Visible);
        }
        else
        {
            _signatureStamp=null;SetValue(SignatureTextPropertyKey,string.Empty);SetValue(SignatureSegmentsPropertyKey,null);SetValue(SignatureCounterPropertyKey,string.Empty);SetValue(SignatureDisplayPropertyKey,Visibility.Collapsed);
        }
    }
}
