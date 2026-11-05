using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Controls;

[TemplatePart(Name="PART_EditorView",Type=typeof(EditControlView))]
[TemplatePart(Name="PART_VerticalScrollBar",Type=typeof(ScrollBar))]
[TemplatePart(Name="PART_HorizontalScrollBar",Type=typeof(ScrollBar))]
[TemplatePart(Name="PART_CompletionList",Type=typeof(EditorCompletionList))]
[TemplatePart(Name="PART_HoverHint",Type=typeof(EditorTextHint))]
[TemplatePart(Name="PART_SignatureHint",Type=typeof(EditorTextHint))]
[TemplatePart(Name="PART_PreviewHint",Type=typeof(EditorTextHint))]
[TemplatePart(Name="PART_MinimapPopup",Type=typeof(Popup))]
[TemplatePart(Name="PART_MinimapHint",Type=typeof(EditorTextHint))]
[TemplateVisualState(Name="Normal",GroupName="CommonStates")]
[TemplateVisualState(Name="Disabled",GroupName="CommonStates")]
[TemplateVisualState(Name="Focused",GroupName="FocusStates")]
[TemplateVisualState(Name="Unfocused",GroupName="FocusStates")]
[TemplateVisualState(Name="Editable",GroupName="EditingStates")]
[TemplateVisualState(Name="ReadOnly",GroupName="EditingStates")]
public partial class EditControl
{
    private EditControlView? _editorTextView;
    private ScrollBar? _templateVerticalScrollBar;
    private ScrollBar? _templateHorizontalScrollBar;
    private bool _refreshingEditorPresentation;
    private (Size Size, Thickness Border, CornerRadius Corners)? _editorClipStamp;
    private static readonly DependencyPropertyKey EditorContentClipPropertyKey=DependencyProperty.RegisterReadOnly(
        nameof(EditorContentClip),typeof(Geometry),typeof(EditControl),new PropertyMetadata(null));
    public static readonly DependencyProperty EditorContentClipProperty=EditorContentClipPropertyKey.DependencyProperty;
    public Geometry? EditorContentClip=>(Geometry?)GetValue(EditorContentClipProperty);
    static EditControl()=>DefaultStyleKeyProperty.OverrideMetadata(typeof(EditControl),new PropertyMetadata(typeof(EditControl)));

    private static readonly DependencyPropertyKey VerticalScrollBarBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollBarBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty VerticalScrollBarBoundsProperty=VerticalScrollBarBoundsPropertyKey.DependencyProperty;
    public Rect VerticalScrollBarBounds=>(Rect)GetValue(VerticalScrollBarBoundsProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollBarBoundsPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollBarBounds),typeof(Rect),typeof(EditControl),new PropertyMetadata(default(Rect)));
    public static readonly DependencyProperty HorizontalScrollBarBoundsProperty=HorizontalScrollBarBoundsPropertyKey.DependencyProperty;
    public Rect HorizontalScrollBarBounds=>(Rect)GetValue(HorizontalScrollBarBoundsProperty)!;

    private static readonly DependencyPropertyKey ComputedVerticalScrollBarVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ComputedVerticalScrollBarVisibility),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty ComputedVerticalScrollBarVisibilityProperty=ComputedVerticalScrollBarVisibilityPropertyKey.DependencyProperty;
    public Visibility ComputedVerticalScrollBarVisibility=>(Visibility)GetValue(ComputedVerticalScrollBarVisibilityProperty)!;

    private static readonly DependencyPropertyKey ComputedHorizontalScrollBarVisibilityPropertyKey=DependencyProperty.RegisterReadOnly(nameof(ComputedHorizontalScrollBarVisibility),typeof(Visibility),typeof(EditControl),new PropertyMetadata(Visibility.Collapsed));
    public static readonly DependencyProperty ComputedHorizontalScrollBarVisibilityProperty=ComputedHorizontalScrollBarVisibilityPropertyKey.DependencyProperty;
    public Visibility ComputedHorizontalScrollBarVisibility=>(Visibility)GetValue(ComputedHorizontalScrollBarVisibilityProperty)!;

    private static readonly DependencyPropertyKey VerticalScrollMaximumPropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollMaximum),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty VerticalScrollMaximumProperty=VerticalScrollMaximumPropertyKey.DependencyProperty;
    public double VerticalScrollMaximum=>(double)GetValue(VerticalScrollMaximumProperty)!;

    private static readonly DependencyPropertyKey VerticalScrollViewportPropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollViewport),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty VerticalScrollViewportProperty=VerticalScrollViewportPropertyKey.DependencyProperty;
    public double VerticalScrollViewport=>(double)GetValue(VerticalScrollViewportProperty)!;

    private static readonly DependencyPropertyKey VerticalScrollOffsetPropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollOffset),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty VerticalScrollOffsetProperty=VerticalScrollOffsetPropertyKey.DependencyProperty;
    public double VerticalScrollOffset=>(double)GetValue(VerticalScrollOffsetProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollMaximumPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollMaximum),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty HorizontalScrollMaximumProperty=HorizontalScrollMaximumPropertyKey.DependencyProperty;
    public double HorizontalScrollMaximum=>(double)GetValue(HorizontalScrollMaximumProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollViewportPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollViewport),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty HorizontalScrollViewportProperty=HorizontalScrollViewportPropertyKey.DependencyProperty;
    public double HorizontalScrollViewport=>(double)GetValue(HorizontalScrollViewportProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollOffsetPropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollOffset),typeof(double),typeof(EditControl),new PropertyMetadata(0.0));
    public static readonly DependencyProperty HorizontalScrollOffsetProperty=HorizontalScrollOffsetPropertyKey.DependencyProperty;
    public double HorizontalScrollOffset=>(double)GetValue(HorizontalScrollOffsetProperty)!;

    private static readonly DependencyPropertyKey VerticalScrollSmallChangePropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollSmallChange),typeof(double),typeof(EditControl),new PropertyMetadata(1.0));
    public static readonly DependencyProperty VerticalScrollSmallChangeProperty=VerticalScrollSmallChangePropertyKey.DependencyProperty;
    public double VerticalScrollSmallChange=>(double)GetValue(VerticalScrollSmallChangeProperty)!;

    private static readonly DependencyPropertyKey VerticalScrollLargeChangePropertyKey=DependencyProperty.RegisterReadOnly(nameof(VerticalScrollLargeChange),typeof(double),typeof(EditControl),new PropertyMetadata(1.0));
    public static readonly DependencyProperty VerticalScrollLargeChangeProperty=VerticalScrollLargeChangePropertyKey.DependencyProperty;
    public double VerticalScrollLargeChange=>(double)GetValue(VerticalScrollLargeChangeProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollSmallChangePropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollSmallChange),typeof(double),typeof(EditControl),new PropertyMetadata(1.0));
    public static readonly DependencyProperty HorizontalScrollSmallChangeProperty=HorizontalScrollSmallChangePropertyKey.DependencyProperty;
    public double HorizontalScrollSmallChange=>(double)GetValue(HorizontalScrollSmallChangeProperty)!;

    private static readonly DependencyPropertyKey HorizontalScrollLargeChangePropertyKey=DependencyProperty.RegisterReadOnly(nameof(HorizontalScrollLargeChange),typeof(double),typeof(EditControl),new PropertyMetadata(1.0));
    public static readonly DependencyProperty HorizontalScrollLargeChangeProperty=HorizontalScrollLargeChangePropertyKey.DependencyProperty;
    public double HorizontalScrollLargeChange=>(double)GetValue(HorizontalScrollLargeChangeProperty)!;

    private void InitializeEditorTemplateBehavior()
    {
        SizeChanged+=(_,_)=>RefreshEditorPresentation();
        Loaded+=(_,_)=>{_suspendEditorOverlays=false;RefreshEditorPresentation();};
        Unloaded+=(_,_)=>{_suspendEditorOverlays=true;CloseTransientEditorOverlays();RefreshEditorPresentation();};
    }
    public override void OnApplyTemplate()
    {
        ReleaseEditorTemplateParts();base.OnApplyTemplate();
        _editorTextView=GetTemplateChild("PART_EditorView") as EditControlView;
        _editorTextView?.SetCurrentValue(EditControlView.OwnerProperty,this);
        _templateVerticalScrollBar=GetTemplateChild("PART_VerticalScrollBar") as ScrollBar;
        _templateHorizontalScrollBar=GetTemplateChild("PART_HorizontalScrollBar") as ScrollBar;
        if(_templateVerticalScrollBar is not null)_templateVerticalScrollBar.ValueChanged+=OnTemplateVerticalScroll;
        if(_templateHorizontalScrollBar is not null)_templateHorizontalScrollBar.ValueChanged+=OnTemplateHorizontalScroll;
        AttachEditorOverlayParts();
        RefreshEditorPresentation();
    }
    protected override void OnTemplateChanging(){ReleaseEditorTemplateParts();base.OnTemplateChanging();}
    private void ReleaseEditorTemplateParts()
    {
        _releasingEditorParts=true;
        try
        {
        ReleaseEditorOverlayParts();
        _scrollBarDragMode=ScrollBarDragMode.None;
        _isDragging=false;
        if(IsMouseCaptured)ReleaseMouseCapture();
        if(_templateVerticalScrollBar is not null)_templateVerticalScrollBar.ValueChanged-=OnTemplateVerticalScroll;
        if(_templateHorizontalScrollBar is not null)_templateHorizontalScrollBar.ValueChanged-=OnTemplateHorizontalScroll;
        foreach(var bar in new[]{_templateVerticalScrollBar,_templateHorizontalScrollBar})
            if(bar is not null){bar.ClearBinding(RangeBase.ValueProperty);bar.ClearBinding(RangeBase.MaximumProperty);bar.ClearBinding(ScrollBar.ViewportSizeProperty);bar.ClearBinding(RangeBase.SmallChangeProperty);bar.ClearBinding(RangeBase.LargeChangeProperty);}
        _templateVerticalScrollBar=_templateHorizontalScrollBar=null;
        if(_editorTextView is { } view&&ReferenceEquals(view.Owner,this)){view.ClearBinding(EditControlView.OwnerProperty);view.SetCurrentValue(EditControlView.OwnerProperty,null);}
        _editorTextView=null;
        }
        finally{_releasingEditorParts=false;}
    }
    private bool IsTemplateScrollBarSource(object? source)
    {
        for(var visual=source as Visual;visual is not null&&!ReferenceEquals(visual,this);visual=visual.VisualParent)
            if(ReferenceEquals(visual,_templateVerticalScrollBar)||ReferenceEquals(visual,_templateHorizontalScrollBar))return true;
        return false;
    }
    private void OnTemplateVerticalScroll(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!_refreshingEditorPresentation&&ReferenceEquals(sender,_templateVerticalScrollBar))SetVerticalOffsetImmediate(e.NewValue,userInitiated:true,cancelAnimation:true);
    }
    private void OnTemplateHorizontalScroll(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!_refreshingEditorPresentation&&ReferenceEquals(sender,_templateHorizontalScrollBar))SetHorizontalOffsetImmediate(e.NewValue,cancelAnimation:true);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);_editorTextView?.InvalidateVisual();
        if(e.Property==IsEnabledProperty&&!IsEnabled)CloseTransientEditorOverlays();
        else if(e.Property==IsReadOnlyProperty&&IsReadOnly)_lspIntegration?.DismissCompletion();
        if(!_refreshingEditorPresentation&&!_releasingEditorParts)RefreshEditorPresentation();
    }
    private void RefreshEditorPresentation()
    {
        if(_refreshingEditorPresentation||_releasingEditorParts||_view is null||_document is null||RenderSize.Width<=0||RenderSize.Height<=0)return;
        _refreshingEditorPresentation=true;
        try
        {
            UpdateViewLayoutForRenderableSize();
            if (_hasPointerPosition && !_isDragging && _scrollBarDragMode == ScrollBarDragMode.None)
            {
                UpdateHoveredScopeGuide(_lastPointerPosition, GetContentRenderWidth(RenderSize.Width), GetContentRenderHeight(RenderSize.Height));
                UpdateHoveredFoldedHint(_lastPointerPosition, GetContentRenderWidth(RenderSize.Width), GetContentRenderHeight(RenderSize.Height));
            }
            else
            {
                _hoveredScopeGuideSection = null;
                _hoveredFoldedHintSection = null;
            }

            var stamp=(RenderSize,BorderThickness,CornerRadius);
            if(_editorClipStamp!=stamp)
            {
                _editorClipStamp=stamp;
                var rect=ControlRenderGeometry.GetContentRect(new Rect(RenderSize),BorderThickness);
                var radii=CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize).Inset(BorderThickness).Normalize(rect.Size);
                var clip=new PathGeometry();clip.Figures.Add(CssRoundedRectangleGeometry.Figure(rect,radii));clip.Freeze();
                SetValue(EditorContentClipPropertyKey,clip);
            }
            SetValue(VerticalScrollBarBoundsPropertyKey,_verticalScrollTrackRect.IsEmpty?default(Rect):_verticalScrollTrackRect);
            SetValue(HorizontalScrollBarBoundsPropertyKey,_horizontalScrollTrackRect.IsEmpty?default(Rect):_horizontalScrollTrackRect);
            SetValue(ComputedVerticalScrollBarVisibilityPropertyKey,_verticalScrollTrackRect.IsEmpty?Visibility.Collapsed:Visibility.Visible);
            SetValue(ComputedHorizontalScrollBarVisibilityPropertyKey,_horizontalScrollTrackRect.IsEmpty?Visibility.Collapsed:Visibility.Visible);
            SetValue(VerticalScrollSmallChangePropertyKey,Math.Max(1,_view.LineHeight));
            SetValue(HorizontalScrollSmallChangePropertyKey,Math.Max(1,_view.CharWidth));
            SetValue(VerticalScrollLargeChangePropertyKey,Math.Max(_effectiveViewportHeight*0.9,Math.Max(1,_view.LineHeight)));
            SetValue(HorizontalScrollLargeChangePropertyKey,Math.Max(_effectiveTextViewportWidth*0.9,Math.Max(1,_view.CharWidth)));
            SetValue(VerticalScrollMaximumPropertyKey,GetMaxVerticalOffset());
            SetValue(HorizontalScrollMaximumPropertyKey,GetMaxHorizontalOffset());
            SetValue(VerticalScrollViewportPropertyKey,Math.Max(0,_effectiveViewportHeight));
            SetValue(HorizontalScrollViewportPropertyKey,Math.Max(0,_effectiveTextViewportWidth));
            SetValue(VerticalScrollOffsetPropertyKey,_view.VerticalOffset);
            SetValue(HorizontalScrollOffsetPropertyKey,_view.HorizontalOffset);
            UpdateEditorOverlayPresentation();
            if(TemplateRootInternal is not null)
            {
                VisualStateManager.GoToState(this,IsEnabled?"Normal":"Disabled",false);
                VisualStateManager.GoToState(this,IsKeyboardFocused?"Focused":"Unfocused",false);
                VisualStateManager.GoToState(this,IsReadOnly?"ReadOnly":"Editable",false);
            }
        }
        finally{_refreshingEditorPresentation=false;}
    }
    private void InvalidateEditorView()
    {
        RefreshEditorPresentation();base.InvalidateVisual();_editorTextView?.InvalidateVisual();
    }
    private void InvalidateEditorView(Rect dirty)
    {
        RefreshEditorPresentation();base.InvalidateVisual(dirty);_editorTextView?.InvalidateVisual(dirty);
    }
}
