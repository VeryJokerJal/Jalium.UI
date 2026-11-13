using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCompositingStateTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PositionedZIndexAutoDiffersFromAnExplicitZero(bool recorded)
    {
        var root = new Canvas { Width=60, Height=40, Background=Brushes.Red };
        var group = new Canvas { Width=40, Height=30 };
        var source = new Border { Width=40, Height=30, Background=Brushes.Blue };
        Css.SetStyle(source,"mix-blend-mode:multiply");group.Children.Add(source);root.Children.Add(group);
        Css.SetStyle(group,"position:relative;z-index:0");Check(root,recorded,0,0,255);
        Css.SetStyle(group,"position:relative;z-index:auto");Check(root,recorded,0,0,0);
        Css.SetStyle(group,"position:relative;z-index:1");Check(root,recorded,0,0,255);
        Css.SetStyle(group,"position:relative;z-index:unset");Check(root,recorded,0,0,0);
    }

    [Theory]
    [InlineData("mix-blend-mode","SCREEN","screen")]
    [InlineData("background-blend-mode","MULTIPLY, screen","multiply,screen")]
    [InlineData("isolation","ISOLATE","isolate")]
    [InlineData("z-index","0","0")]
    [InlineData("z-index","auto","initial")]
    public void StyleQueriesUseComputedCompositingValues(string name,string value,string equivalent)
    {
        var owner = new Border();Css.SetStyle(owner,name+":"+value);
        owner.Measure(new(60,40));owner.Arrange(new(0,0,60,40));owner.UpdateLayout();
        var node=CssNode.Get(owner);var lengths=CssEngine.BuildLengthContext(node);
        Assert.True(CssOrdinaryStyleQuery.IsSupported(name));
        Assert.True(CssOrdinaryStyleQuery.Matches(name,equivalent,node,lengths));
    }

    [Fact]
    public void BackgroundShorthandDoesNotResetItsIndependentBlendLonghand()
    {
        var root = new Border { Width=60, Height=40 };
        Css.SetStyle(root,"background-blend-mode:multiply;background:linear-gradient(blue,blue) red");
        Check(root,false,0,0,0);
        Css.SetStyle(root,"background:linear-gradient(blue,blue) red;background-blend-mode:initial");
        Check(root,false,0,0,255);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ClipPathCreatesAnIsolatedCssStackingContext(bool recorded)
    {
        var root = new Canvas { Width=60, Height=40, Background=Brushes.Red };
        var group = new Canvas { Width=40, Height=30 };
        var source = new Border { Width=40, Height=30, Background=Brushes.Blue };
        Css.SetStyle(source,"mix-blend-mode:multiply");group.Children.Add(source);root.Children.Add(group);
        Css.SetStyle(group,"clip-path:inset(0)");Check(root,recorded,0,0,255);
        Css.SetStyle(group,"clip-path:none");Check(root,recorded,0,0,0);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void MaskAndBlendApplyToTheCompletedOverlappingGroup(bool recorded)
    {
        var root = new Canvas { Width=60, Height=40, Background=Brushes.Red };
        var group = new Canvas { Width=40, Height=30 };
        group.Children.Add(new Border { Width=30, Height=30, Background=Brushes.Blue });
        group.Children.Add(new Border { Width=40, Height=30, Background=Brushes.Blue });
        Css.SetStyle(group,"mix-blend-mode:multiply;mask:image(rgb(0 0 0 / .5))");root.Children.Add(group);
        Check(root,recorded,127,0,0);
        Css.SetStyle(group,"mix-blend-mode:screen;mask:image(rgb(0 0 0 / .5))");Check(root,recorded,255,0,128);
    }

    [Fact]
    public void BoundsDiscoveryRetainsDeepOverflowWithoutRecursing()
    {
        var source = new Border { Width=40, Height=30 };
        source.Measure(new(60,40));source.Arrange(new(0,0,40,30));
        Visual root=source;
        for(var i=0;i<600;i++)root=new BoundsVisual(root);
        Assert.Equal(new Rect(0,0,40,30),CssMaskPainter.InkBounds(root));
    }

    [Fact]
    public void BlendDiscoveryCachesDescendantsInOneTraversal()
    {
        var leaf=new Border();Css.SetStyle(leaf,"mix-blend-mode:multiply");
        var nodes=new List<CountingVisual>();Visual root=leaf;
        for(var i=0;i<600;i++){var node=new CountingVisual(root);nodes.Add(node);root=node;}
        Assert.True(CssCompositingProperties.ContainsBlend(root));
        foreach(var node in nodes)Assert.True(CssCompositingProperties.ContainsBlend(node));
        Assert.InRange(nodes.Sum(node=>node.Reads),1,nodes.Count*3);
        Css.SetStyle(leaf,"");
        Assert.False(CssCompositingProperties.ContainsBlend(root));
    }
    private sealed class CountingVisual(Visual child):Visual
    {
        internal int Reads;
        protected override int VisualChildrenCount=>1;
        protected override Visual GetVisualChild(int index){Reads++;return index==0?child:throw new ArgumentOutOfRangeException(nameof(index));}
    }

    private sealed class BoundsVisual(Visual child):Visual
    {
        protected override int VisualChildrenCount=>1;
        protected override Visual GetVisualChild(int index)=>index==0?child:throw new ArgumentOutOfRangeException(nameof(index));
    }

    [Fact]
    public void AnAbortedChildRestoresOffsetTransformOpacityAndBothCaptures()
    {
        var root = new Canvas { Width=60,Height=40 };
        var source=new ThrowingElement { Width=30,Height=20,Opacity=.5,RenderTransform=new TranslateTransform(2,1) };
        Canvas.SetLeft(source,8);Canvas.SetTop(source,6);Css.SetStyle(source,"mix-blend-mode:multiply;mask:image(white)");root.Children.Add(source);
        root.Measure(new(60,40));root.Arrange(new(0,0,60,40));
        var image=new RenderTargetBitmap(60,40,96,96,PixelFormats.Bgra32);var drawing=new SoftwareDrawingContext(image);
        drawing.DrawRectangle(Brushes.White,null,new(0,0,60,40));
        Assert.Throws<InvalidOperationException>(()=>root.Render(drawing));
        Assert.Equal(default(Point),((IOffsetDrawingContext)drawing).Offset);
        drawing.DrawRectangle(Brushes.Blue,null,new(0,0,60,40));drawing.Close();
        CssCompositingPaintTests.Pixel(image.GetPixelBuffer(),4,4,0,0,255,255);
        CssCompositingPaintTests.Pixel(image.GetPixelBuffer(),50,35,0,0,255,255);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReplayAbortsInterleavedMaskAndBlendCapturesInReverseOrder(bool outerBlend)
    {
        var recorder = new DrawingRecorder();recorder.BindWholeFrame();
        var blend=(IBlendDrawingContext)recorder;var mask=(IAlphaMaskDrawingContext)recorder;
        if(outerBlend){blend.BeginBlendCapture(new(0,0,10,10));mask.BeginAlphaMaskCapture(new(0,0,10,10));}
        else{mask.BeginAlphaMaskCapture(new(0,0,10,10));blend.BeginBlendCapture(new(0,0,10,10));}
        ((IEffectDrawingContext)recorder).BeginEffectCapture(0,0,10,10);
        recorder.DrawRectangle(Brushes.Red,null,new(0,0,10,10));
        var target=new ThrowingDestination();Assert.Throws<InvalidOperationException>(()=>DrawingReplayer.Replay(recorder.Commit(),target));
        Assert.Empty(target.Scopes);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AClonedBackgroundPreservesItsModeAndCompositeOpacity(bool recorded)
    {
        var owner = new Border { Width=60, Height=40 };
        Css.SetStyle(owner,"background:linear-gradient(blue,blue) red;background-blend-mode:multiply");
        CssCompositingPaintTests.Render(owner);
        var brush = owner.Background!.CloneCurrentValue(); brush.Opacity = .5;
        Css.SetStyle(owner,"background:linear-gradient(blue,blue) red;background-blend-mode:screen");
        owner.Background = brush;
        CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(owner,recorded),10,10,0,0,0,128);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ATemplateUsesTheBackgroundOwnersBlendList(bool recorded)
    {
        var parent=new Border { Width=60, Height=40 };
        Css.SetStyle(parent,"background:linear-gradient(blue,blue) red;background-blend-mode:multiply");
        CssCompositingPaintTests.Render(parent);
        var owner=new Border { Width=60, Height=40 };owner.SetTemplatedParent(parent);
        owner.Measure(new(60,40));owner.Arrange(new(0,0,60,40));
        var bitmap=new RenderTargetBitmap(60,40,96,96,PixelFormats.Bgra32);
        var target=new SoftwareDrawingContext(bitmap);var recorder=new DrawingRecorder();recorder.BindWholeFrame();
        DrawingContext drawing=recorded?recorder:target;
        Assert.True(CssBackgroundPainter.TryDraw(owner,Border.BackgroundProperty,parent.Background!,drawing,
            new Rect(0,0,60,40),default,default,default,brush=>drawing.DrawRectangle(brush,null,new Rect(0,0,60,40))));
        if(recorded)DrawingReplayer.Replay(recorder.Commit(),target);
        target.Close();CssCompositingPaintTests.Pixel(bitmap.GetPixelBuffer(),10,10,0,0,0,255);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SvgImageRootBlendingDoesNotEscapeItsImageBoundary(bool recorded)
    {
        using var svg=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40' style='mix-blend-mode:multiply'><rect width='60' height='40' fill='blue'/></svg>");
        var bitmap=new RenderTargetBitmap(60,40,96,96,PixelFormats.Bgra32);var target=new SoftwareDrawingContext(bitmap);
        target.DrawRectangle(Brushes.Red,null,new(0,0,60,40));
        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();svg.Drawing!.RenderTo(recorder);DrawingReplayer.Replay(recorder.Commit(),target);}
        else svg.Drawing!.RenderTo(target);
        target.Close();CssCompositingPaintTests.Pixel(bitmap.GetPixelBuffer(),10,10,0,0,255,255);
    }

    [Fact]
    public void AnOrdinarySvgDoesNotAllocateABlendCapture()
    {
        using var svg=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40'><rect width='60' height='40' fill='blue'/></svg>");
        var target=new ThrowingDestination();svg.Drawing!.RenderTo(target);
        Assert.Equal(0,target.BlendStarts);Assert.Equal(1,target.Geometries);
    }

    private static void Check(FrameworkElement view,bool recorded,int r,int g,int b)
        => CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(view,recorded),10,10,r,g,b,255);
    private sealed class ThrowingElement:FrameworkElement
    {
        protected override void OnRender(DrawingContext context)
        {context.DrawRectangle(Brushes.Red,null,new Rect(RenderSize));throw new InvalidOperationException("Fixture render failure");}
    }
    private sealed class ThrowingDestination:DrawingContextAdapter,IBlendDrawingContext,IAlphaMaskDrawingContext,IEffectDrawingContext,IOffsetDrawingContext
    {
        internal readonly Stack<string> Scopes=new();
        internal int BlendStarts,Geometries;
        public Point Offset { get; set; }
        public bool SupportsBlendModes=>true;public bool SupportsAlphaMasks=>true;
        public void BeginEffectCapture(float x,float y,float w,float h)=>Scopes.Push("effect");
        public void EndEffectCapture()=>Assert.Equal("effect",Scopes.Pop());
        public void ApplyElementEffect(IEffect effect,float x,float y,float w,float h,float captureOriginX=0,float captureOriginY=0,float cornerTL=0,float cornerTR=0,float cornerBR=0,float cornerBL=0) { }
        public void BeginBlendCapture(Rect bounds){BlendStarts++;Scopes.Push("blend");}
        public void EndBlendCapture()=>Assert.Equal("blend",Scopes.Pop());
        public void CompositeBlendCapture(CssBlendMode mode,Rect bounds) { }
        public void BeginAlphaMaskCapture(Rect bounds)=>Scopes.Push("mask");
        public void EndAlphaMaskCapture()=>Assert.Equal("mask",Scopes.Pop());
        public void ApplyAlphaMask(byte[] coverage,int width,int height,Rect mask,Rect capture) { }
        public override void DrawRectangle(Brush? brush,Pen? pen,Rect rect)=>throw new InvalidOperationException("Fixture destination failure");
        public override void DrawLine(Pen pen,Point a,Point b) { }
        public override void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double x,double y) { }
        public override void DrawEllipse(Brush? brush,Pen? pen,Point center,double x,double y) { }
        public override void DrawGeometry(Brush? brush,Pen? pen,Geometry geometry)=>Geometries++;
        public override void DrawImage(ImageSource source,Rect rect) { }
        public override void DrawBackdropEffect(Rect rect,IBackdropEffect effect,CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
