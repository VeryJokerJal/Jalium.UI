using System.Net;
using System.Net.Sockets;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssImageShapeTests
{
    private const string LongText="图片透明区域参与文字环绕，保持原始文字和选择范围，加载完成和阈值更新以后继续按照实际轮廓换行。";

    [Fact]
    public void AuthoringPublishesRuntimeImageValuesAndTheirCssDefaults()
    {
        Assert.Equal("none",CssAuthoring.GetProperty("shape-outside").InitialValue);Assert.Equal("0",CssAuthoring.GetProperty("shape-image-threshold").InitialValue);Assert.Equal("0",CssAuthoring.GetProperty("shape-margin").InitialValue);
        Assert.Contains("url(\"image.png\")",CssAuthoring.GetValues("shape-outside"));Assert.Contains("conic-gradient(black, transparent)",CssAuthoring.GetValues("shape-outside"));Assert.Contains("calc(25% + .25)",CssAuthoring.GetValues("shape-image-threshold"));Assert.False(CssAuthoring.GetProperty("shape-image-threshold").IsInherited);
    }

    [Theory]
    [InlineData("0",0)] [InlineData(".5",.5)] [InlineData("50%",.5)]
    [InlineData("-1",0)] [InlineData("120%",1)] [InlineData("2",1)]
    [InlineData("calc(25% + .25)",.5)] [InlineData("clamp(0,.4 + 20%,1)",.6)]
    [InlineData("calc(1 - 200%)",0)]
    public void ThresholdIsANumberPercentageClampedAtComputedValue(string css,double expected)
    { var element=new Border();Css.SetStyle(element,"shape-image-threshold:"+css+";");Flush();Assert.Equal(expected,(double)element.GetValue(CssShapeOutsideProperties.ThresholdProperty)!,6); }

    [Theory]
    [InlineData("10px")] [InlineData("auto")] [InlineData(".5 .6")] [InlineData("calc(1px + 1%)")]
    public void InvalidThresholdsDoNotInstallAValue(string css)
    { var element=new Border();Css.SetStyle(element,"shape-image-threshold:"+css+";");Flush();Assert.Equal(0d,element.GetValue(CssShapeOutsideProperties.ThresholdProperty)); }

    [Fact]
    public void ThresholdUsesStrictGreaterThanAndRetainsHighPrecisionAlpha()
    {
        var pixels=new byte[]{0,0,0,0,0,0,0,127,0,0,0,128,0,0,0,255};
        var image=CssShapeImagePixels.FromBitmap(BitmapSource.Create(4,1,96,96,PixelFormats.Bgra32,null,pixels,16));
        Assert.Equal(new[]{1,4},image.Runs(0)[0]);Assert.Equal(new[]{2,4},image.Runs(127/255d)[0]);Assert.Equal(new[]{3,4},image.Runs(128/255d)[0]);Assert.Empty(image.Runs(1)[0]);
        var precise=BitmapSource.Create(2,1,96,96,PixelFormats.Rgba128Float,null,new float[]{0,0,0,.5001f,0,0,0,.4999f},32);
        var mask=CssShapeImagePixels.FromBitmap(precise);Assert.Equal(new[]{0,1},mask.Runs(.5)[0]);Assert.Empty(mask.Runs(.501)[0]);
    }

    [Fact]
    public void PixelFormatsWithoutAlphaRemainOpaqueAndIndexedTransparencyIsRespected()
    {
        var rgb=BitmapSource.Create(2,1,96,96,PixelFormats.Bgr24,null,new byte[6],6);Assert.Equal(new[]{0,2},CssShapeImagePixels.FromBitmap(rgb).Runs(.99)[0]);
        var palette=new BitmapPalette(new[]{Colors.Transparent,Colors.Green});var indexed=BitmapSource.Create(2,1,96,96,PixelFormats.Indexed8,palette,new byte[]{0,1},2);
        Assert.Equal(new[]{1,2},CssShapeImagePixels.FromBitmap(indexed).Runs(0)[0]);
    }

    [Fact]
    public void ImageHolesAreRetainedWhenOnlyTheirTransparentInteriorIntersectsTheMarginBox()
    {
        var pixels=Pixels(10,1,(x,y)=>x<2||x>=8?(byte)255:(byte)0);
        Assert.False(CssFloatShape.FromPixels(pixels,0,new(-50,0,100,100),new(20,100),0).Band(20,20).Active);
        var expanded=CssFloatShape.FromPixels(pixels,0,new(-50,0,100,100),new(20,100),15).Band(20,20);
        Assert.Equal((15d,20d,true),expanded);
    }

    [Fact]
    public void ImageShapeMarginUsesCircularDistanceAndIsClippedToMarginBox()
    {
        var pixels=Pixels(4,4,(x,y)=>x==1&&y==1?(byte)255:(byte)0);var shape=CssFloatShape.FromPixels(pixels,0,new(0,0,100,100),new(100,100),10);
        Assert.False(shape.Band(0,15).Active);Assert.Equal(50+Math.Sqrt(75),shape.Band(15,5).Right,6);
        Assert.Equal((0d,100d,true),CssFloatShape.FromPixels(pixels,0,new(0,0,100,100),new(100,100),1e9).Band(0,100));
    }

    [Fact]
    public void RasterCoordinatesAndEqualitySurviveRemeasureAndFindEarliestWordFit()
    {
        var pixels=Pixels(10,10,(x,y)=>x<10-y?(byte)255:(byte)0);var a=CssFloatShape.FromPixels(pixels,0,new(0,0,100,100),new(100,100),0);var b=CssFloatShape.FromPixels(pixels,0,new(0,0,100,100),new(100,100),0);
        Assert.Equal(a,b);Assert.Equal(a.GetHashCode(),b.GetHashCode());var space=new CssFloatSpace(CssFloatEnvironment.Empty);space.Add(new(CssFloatSide.Left,0,0,100,100,a));
        Assert.Equal(40,space.NextFit(150,0,20,90),4);Assert.Equal(100,space.ClearBottom(CssClearSide.Both),4);
    }

    [Theory]
    [InlineData("linear-gradient(to right,black 50%,transparent 50%)",50)]
    [InlineData("linear-gradient(to right,transparent,black)",100)]
    [InlineData("repeating-linear-gradient(to right,black 0 10px,transparent 10px 20px)",90)]
    [InlineData("radial-gradient(circle at 0% 50%,black 30px,transparent 30px)",30)]
    [InlineData("conic-gradient(black 0deg 180deg,transparent 180deg 360deg)",100)]
    public void GradientImagesUseTheExistingUsedSizeGradientRenderer(string gradient,double expected)
    {
        var element=new Border();Css.SetStyle(element,$"shape-outside:{gradient};");Flush();var shape=Resolve(element,100,100);Assert.NotNull(shape);
        Assert.InRange(shape!.Band(40,20).Right,expected-1.1,expected+.1);
    }

    [Fact]
    public void CustomPropertyCanSwitchBackgroundAndFloatShapeToAConicGradient()
    {
        var element = new Border();
        var sheet = CssStyleSheet.Parse("""
            Border {
                background-image: var(--paint, radial-gradient(black, transparent));
                shape-outside: var(--paint, radial-gradient(black, transparent));
            }
            """);
        Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(element).Add(sheet);
        Flush();
        Assert.IsType<RadialGradientBrush>(element.Background);

        Css.SetStyle(element, "--paint:conic-gradient(black 0deg 180deg,transparent 180deg 360deg);");
        Flush();
        var background = Assert.IsType<ImageBrush>(element.Background);
        Assert.NotNull(background.CssGradientLayout);
        Assert.NotNull(background.CssBackgroundLayout);
        Assert.InRange(Resolve(element, 100, 100)!.Band(40, 20).Right, 99, 100.1);

        Css.SetStyle(element, "--paint:linear-gradient(to right,black 50%,transparent 50%);");
        Flush();
        Assert.IsType<LinearGradientBrush>(element.Background);
        Assert.InRange(Resolve(element, 100, 100)!.Band(40, 20).Right, 49, 50.1);
        Assert.NotNull(background.CssGradientLayout);
    }

    [Fact]
    public void GradientAlphaThresholdCurrentColorAndVariableUpdatesReflowAtTheSameWidth()
    {
        var floating=Floating();Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:linear-gradient(to right,currentcolor,currentcolor);color:rgb(0 0 0 / 20%);shape-image-threshold:50%;");
        var text=Text();var root=Root(floating,text);Layout(root,180);Assert.Equal(0,Lines(text,root)[0].StartX,4);
        Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:linear-gradient(to right,currentcolor,currentcolor);color:rgb(0 0 0 / 80%);shape-image-threshold:50%;");Layout(root,180);Assert.Equal(100,Lines(text,root)[0].StartX,4);
    }

    [Fact]
    public void UrlLoadsAsynchronouslyThroughTheStylesheetResolverAndReflowsItsConsumers()
    {
        var resolver=new Resolver();var floating=Floating();var text=Text();var root=Root(floating,text);
        Attach(root,".float {shape-outside:url('../assets/mask.png');shape-image-threshold:0;}",resolver,new Uri("https://app.example/styles/main.css"));Layout(root,180);Assert.Equal(100,Lines(text,root)[0].StartX,4);
        var value=Value(floating);resolver.Complete(Png(10,10,(x,y)=>x<5?(byte)255:(byte)0),new Uri("https://cdn.example/mask.png"),true);Wait(value);Assert.False(root.IsMeasureValid);
        Layout(root,180);Assert.Equal(50,Lines(text,root)[0].StartX,4);Assert.Equal(new Uri("https://app.example/assets/mask.png"),resolver.Requested);Assert.Equal(1,resolver.Calls);Assert.True(resolver.Disposed);
        Layout(root,250);Layout(root,180);Assert.Equal(50,Lines(text,root)[0].StartX,4);Assert.Equal(1,resolver.Calls);
    }

    [Fact]
    public void FullyTransparentAndThresholdOneProduceAnEmptyFloatAreaInsteadOfNone()
    {
        foreach(var opaque in new[]{false,true})
        {
            var floating=Floating();var root=Root(floating,Text());var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');shape-image-threshold:"+(opaque?"1":"0")+";}",resolver);
            Layout(root,180);resolver.Complete(Png(4,4,(_,_)=>opaque?(byte)255:(byte)0));Wait(Value(floating));Layout(root,180);Assert.False(Resolve(floating,100,100)!.Band(0,100).Active);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InvalidOrDeniedImageFallsBackToTheFullRectangularFloat(bool denied)
    {
        var floating=Floating();var text=Text();var root=Root(floating,text);var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');}",resolver);Layout(root,180);
        resolver.Complete(denied?Png(4,4,(_,_)=>0):new byte[]{1,2,3,4},new Uri("https://cdn.example/mask.png"),denied?false:true);Wait(Value(floating));Layout(root,180);
        Assert.Null(Resolve(floating,100,100));Assert.Equal(100,Lines(text,root)[0].StartX,4);
    }

    [Fact]
    public void ARemoteCustomResolverMustExplicitlyApprovePixelsEvenForSameStylesheetHost()
    {
        var floating=Floating();var root=Root(floating,Text());var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');}",resolver,new Uri("https://app.example/main.css"));Layout(root,180);
        resolver.Complete(Png(4,4,(_,_)=>0),new Uri("https://app.example/mask.png"));Wait(Value(floating));Assert.Null(Resolve(floating,100,100));
    }

    [Fact]
    public void AReplacedResourceCannotInvalidateOrRestoreItsFormerConsumers()
    {
        var floating=Floating();var root=Root(floating,Text());var resolver=new Resolver();Attach(root,".float {shape-outside:url('old.png');}",resolver);Layout(root,180);var old=Value(floating);
        Css.SetStyle(floating,"shape-outside:inset(0 50% 0 0);");Layout(root,180);Assert.True(root.IsMeasureValid);
        resolver.Complete(Png(4,4,(_,_)=>255));Wait(old);Assert.True(root.IsMeasureValid);Assert.Equal(50,Resolve(floating,100,100)!.Band(0,100).Right,4);
    }

    [Fact]
    public void UrlImageUsesContentBoxAndImageObjectFitPositionInvalidateLayout()
    {
        var image=new Image();Css.SetClass(image,"float");Css.SetStyle(image,"float:left;width:100px;height:100px;padding:10px;border:5px solid green;object-fit:contain;");
        var root=Root(image,Text());var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');}",resolver);Layout(root,180);
        resolver.Complete(Png(4,2,(_,_)=>255));Wait(Value(image));Layout(root,180);var shape=Resolve(image,100,100)!;
        Assert.False(shape.Band(0,20).Active);Assert.Equal(85,shape.Band(40,20).Right,4);
        Css.SetStyle(image,"float:left;width:100px;height:100px;padding:10px;border:5px solid green;object-fit:contain;object-position:left top;");Layout(root,180);Assert.True(Resolve(image,100,100)!.Band(15,10).Active);
        Assert.Equal(85,Resolve(image,100,100)!.Band(15,10).Right,4);
    }

    [Fact]
    public void DataUriAndSvgUseTheSamePixelPipelineAndDoNotImportSvgSubresources()
    {
        foreach(var svg in new[]{false,true})
        {
            var bytes=svg?System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><rect width='50' height='100' fill='green'/></svg>"):Png(10,10,(x,y)=>x<5?(byte)255:(byte)0);
            var floating=Floating();Css.SetStyle(floating,$"float:left;width:100px;height:100px;shape-outside:url('data:image/{(svg?"svg+xml":"png")};base64,{Convert.ToBase64String(bytes)}');");Flush();Wait(Value(floating));Assert.InRange(Resolve(floating,100,100)!.Band(0,100).Right,49.9,50.1);
        }
        var resolver=new Resolver();var bad=Floating();var root=Root(bad);Attach(root,".float {shape-outside:url('unsafe.svg');}",resolver);Layout(root,100);
        resolver.Complete(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><image href='https://external.example/private.png'/></svg>"));Wait(Value(bad));Assert.False(Resolve(bad,100,100)!.Band(0,100).Active);Assert.Equal(1,resolver.Calls);
    }

    [Fact]
    public void ImageThresholdReflowKeepsOriginalRunSelectionHitsTabFocusAndAccessibleText()
    {
        Keyboard.Initialize();Keyboard.ClearFocus();var floating=Floating();var a=Text();var b=Text();a.Focusable=b.Focusable=true;KeyboardNavigation.SetIsTabStop(a,true);KeyboardNavigation.SetIsTabStop(b,true);
        a.Text="";var run=new Run(LongText);a.Inlines.Add(run);var root=Root(floating,a,b);var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');shape-image-threshold:var(--cutoff,0);}",resolver);
        var window=new DisplayedTestWindow{Content=root,Width=180,Height=500,TitleBarStyle=WindowTitleBarStyle.Native};
        try
        {
            Layout(window,180);resolver.Complete(Png(10,10,(x,y)=>x<4?(byte)255:(byte)100));Wait(Value(floating));Layout(window,180);a.Select(2,6);Assert.True(a.Focus());
            Css.SetStyle(floating,"float:left;width:100px;height:100px;--cutoff:.5;");Layout(window,180);Assert.Equal(40,Lines(a,root)[0].StartX,4);Assert.Same(run,a.Inlines[0]);Assert.Equal(LongText.Substring(2,6),a.SelectedText);Assert.Same(a,Keyboard.FocusedElement);Assert.Equal(a.Text,a.GetAutomationPeer()!.GetName());
            foreach(var line in Lines(a,root))Assert.Same(a,root.InputHitTest(new((line.StartX+line.EndX)/2,line.Baseline-2)));
            Assert.True(KeyboardNavigation.MoveFocus(a));Assert.Same(b,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(b,reverse:true));Assert.Same(a,Keyboard.FocusedElement);Assert.Equal(1,resolver.Calls);
        }
        finally{Keyboard.ClearFocus();window.Close();}
    }

    [Fact]
    public void ReplacedImageBorderAndRoundedContentUseTheSameContentBoxAsItsShape()
    {
        var image=new Image {Source=Bitmap(2,2,(_,_)=>255)};Css.SetStyle(image,"width:100px;height:100px;border:5px solid red;padding:10px;border-radius:30px;object-fit:fill;");Layout(image,100);
        var target=new RenderTargetBitmap(100,100,96,96,PixelFormats.Bgra32);target.Render(image);var bytes=new byte[40000];target.CopyPixels(bytes,400,0);
        Assert.Equal(255,bytes[(50*100+2)*4+2]);Assert.Equal(0,bytes[(50*100+10)*4+3]);Assert.Equal(120,bytes[(50*100+50)*4+1]);Assert.Equal(0,bytes[(15*100+15)*4+3]);
        Assert.Equal(new Thickness(15),CssBoxMetrics.ContentInsets(image,100));
    }

    [Fact]
    public void DenseRasterWordFitUsesExactPixelIntervalsAndCircularMargins()
    {
        var pixels=Pixels(512,512,(x,y)=>x<512-y?(byte)255:(byte)0);var shape=CssFloatShape.FromPixels(pixels,0,new(0,0,100,100),new(100,100),8);
        var space=new CssFloatSpace(CssFloatEnvironment.Empty);space.Add(new(CssFloatSide.Left,0,0,100,100,shape));var timer=System.Diagnostics.Stopwatch.StartNew();var next=space.NextFit(150,0,20,90);timer.Stop();
        var fit=space.Band(150,next,20,useShapes:true);Assert.True(fit.Right-fit.Left>=89.999999);var before=space.Band(150,next-.002,20,useShapes:true);Assert.True(before.Right-before.Left<90);Assert.InRange(next,50,54);
        Console.WriteLine($"512px raster contour word fit: {timer.Elapsed.TotalMilliseconds:F2}ms, y={next:F6}");
    }

    [Fact]
    public void OptimizedRasterBandsMatchAllOpaquePixelRectanglesAcrossClippedHoles()
    {
        for(var offset=-35;offset<=25;offset+=30)
        foreach(var margin in new[]{0d,5d})
        {
            var bitmap=Bitmap(8,8,(x,y)=>(x+y*3)%5<2?(byte)255:(byte)0);var pixels=CssShapeImagePixels.FromBitmap(bitmap);var image=new Rect(offset,0,64,64);
            var raster=CssFloatShape.FromPixels(pixels,0,image,new(40,64),margin);var path=new PathGeometry {FillRule=FillRule.Nonzero};
            for(var y=0;y<8;y++)for(var x=0;x<8;x++)if((x+y*3)%5<2){var rect=new Rect(offset+x*8,y*8,8,8);var f=new PathFigure{StartPoint=rect.TopLeft,IsClosed=true,IsFilled=true};f.Segments.Add(new LineSegment(rect.TopRight,true));f.Segments.Add(new LineSegment(rect.BottomRight,true));f.Segments.Add(new LineSegment(rect.BottomLeft,true));path.Figures.Add(f);}
            var reference=CssFloatShape.Create(path,0,0,40,64,margin);
            for(var y=0;y<64;y+=7){var a=raster.Band(y,5);var b=reference.Band(y,5);Assert.Equal(b.Active,a.Active);if(a.Active){Assert.InRange(a.Left,b.Left-.03,b.Left+.03);Assert.InRange(a.Right,b.Right-.03,b.Right+.03);}}
        }
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    public void BinaryImageSignaturesAreNotMisclassifiedAsSvg(string kind)
    {
        var data=kind=="jpeg"?Convert.FromBase64String("/9j/4AAQSkZJRgABAQAASABIAAD/4QBMRXhpZgAATU0AKgAAAAgAAYdpAAQAAAABAAAAGgAAAAAAA6ABAAMAAAABAAEAAKACAAQAAAABAAAABKADAAQAAAABAAAAAgAAAAD/7QA4UGhvdG9zaG9wIDMuMAA4QklNBAQAAAAAAAA4QklNBCUAAAAAABDUHYzZjwCyBOmACZjs+EJ+/8AAEQgAAgAEAwEiAAIRAQMRAf/EAB8AAAEFAQEBAQEBAAAAAAAAAAABAgMEBQYHCAkKC//EALUQAAIBAwMCBAMFBQQEAAABfQECAwAEEQUSITFBBhNRYQcicRQygZGhCCNCscEVUtHwJDNicoIJChYXGBkaJSYnKCkqNDU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6g4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2drh4uPk5ebn6Onq8fLz9PX29/j5+v/EAB8BAAMBAQEBAQEBAQEAAAAAAAABAgMEBQYHCAkKC//EALURAAIBAgQEAwQHBQQEAAECdwABAgMRBAUhMQYSQVEHYXETIjKBCBRCkaGxwQkjM1LwFWJy0QoWJDThJfEXGBkaJicoKSo1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoKDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uLj5OXm5+jp6vLz9PX29/j5+v/bAEMAAgICAgICAwICAwUDAwMFBgUFBQUGCAYGBgYGCAoICAgICAgKCgoKCgoKCgwMDAwMDA4ODg4ODw8PDw8PDw8PD//bAEMBAgICBAQEBwQEBxALCQsQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEP/dAAQAAf/aAAwDAQACEQMRAD8A+F6KKK/l8/jc/9k=") : Bmp();
        var floating=Floating();var root=Root(floating);var resolver=new Resolver();Attach(root,".float {shape-outside:url('binary.image');}",resolver);Layout(root,100);resolver.Complete(data);Wait(Value(floating));Assert.Equal((0d,100d,true),Resolve(floating,100,100)!.Band(0,100));
    }

    [Fact]
    public void AnimatedRasterUsesOnlyItsFirstFrame()
    {
        var data=Gif();Assert.Equal(2,BitmapImage.ResolveDecoder().ReadFrameCount(data));
        var floating=Floating();Css.SetStyle(floating,$"float:left;width:100px;height:100px;shape-outside:url('data:image/gif;base64,{Convert.ToBase64String(data)}');");Flush();Wait(Value(floating));var shape=Resolve(floating,100,100)!;Assert.Equal(50,shape.Band(0,100).Right,4);
        Dispatcher.CurrentDispatcher.ProcessQueue();Assert.Equal(shape,Resolve(floating,100,100));
    }

    [Fact]
    public void SvgTextAlphaUsesRealGlyphOutlinesInsteadOfHighlightRectangles()
    {
        var resolver=new Resolver();var floating=Floating();var root=Root(floating);Attach(root,".float {shape-outside:url('text.svg');}",resolver);Layout(root,100);
        resolver.Complete(System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><text x='10' y='70' font-family='Arial' font-size='48' fill='green'>O</text></svg>"));Wait(Value(floating));
        var shape=Resolve(floating,100,100);Assert.NotNull(shape);var band=shape!.Band(50,10);Assert.True(band.Active);Assert.InRange(band.Right,35,60);Assert.False(shape.Band(0,10).Active);Assert.Equal(4,Value(floating).Resource!.Resolve(new Size(100,100))!.Runs(0)[50].Length);
    }

    [Theory]
    [InlineData("O",400,0)]
    [InlineData("ffi",700,1)]
    [InlineData("中文",400,0)]
    [InlineData("مرحبا",400,0)]
    public void MacOSOutlineUsesShapedRunsStylesAndFallbackFonts(string text,int weight,int style)
    {
        if(!OperatingSystem.IsMacOS())return;
        Assert.True(Jalium.UI.Interop.NativeTextOutline.TryGetPath(text,"Arial",48,weight,style,out var data,out var width,out var baseline));Assert.True(width>0);Assert.True(baseline>0);
        var geometry=PathMarkupParser.ParseSvgPathData(data);Assert.True(geometry.Bounds.Width>0);Assert.True(geometry.Bounds.Height>0);
        if(text=="O")Assert.False(geometry.FillContains(new Point(geometry.Bounds.X+geometry.Bounds.Width/2,geometry.Bounds.Y+geometry.Bounds.Height/2)));
    }

    [Fact]
    public void ImageExclusionsCrossNormalBlockBoundariesWithoutChangingBlockHeight()
    {
        var floating=Floating();var host=new StackPanel();Css.SetStyle(host,"display:block;");host.Children.Add(floating);var text=Text();var root=Root(host,text);var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');}",resolver);Layout(root,180);
        resolver.Complete(Png(10,10,(x,y)=>x<4?(byte)255:(byte)0));Wait(Value(floating));Layout(root,180);Assert.Equal(0,host.ActualHeight,4);Assert.Equal(40,Lines(text,root)[0].StartX,4);Assert.NotNull(CssDisplayLayout.FlowFor(host).ExportedFloats[0].Shape);Assert.Same(host,floating.VisualParent);
    }

    [Fact]
    public void SvgEmbeddedRasterUsesPixelsWhileExternalImageNodesAreExcluded()
    {
        var inner=Convert.ToBase64String(Png(4,4,(x,y)=>x<2?(byte)255:(byte)0));var svg=$"<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><image width='100' height='100' href='data:image/png;base64,{inner}'/><image width='100' height='100' href='https://external.example/image.png'/></svg>";
        var floating=Floating();var root=Root(floating);var resolver=new Resolver();Attach(root,".float {shape-outside:url('embedded.svg');shape-image-threshold:.5;}",resolver);Layout(root,100);resolver.Complete(System.Text.Encoding.UTF8.GetBytes(svg));Wait(Value(floating));Assert.Equal(50,Resolve(floating,100,100)!.Band(0,100).Right,4);Assert.Equal(1,resolver.Calls);
    }

    [Fact]
    public void NativeStretchPrecedenceMatchesTheDisplayedImageDuringShapeLayout()
    {
        var image=new Image{Source=Bitmap(4,2,(_,_)=>255),Stretch=Stretch.UniformToFill};Css.SetClass(image,"float");Css.SetStyle(image,"float:left;width:100px;height:100px;object-fit:contain;");
        var root=Root(image);var resolver=new Resolver();Attach(root,".float {shape-outside:url('mask.png');}",resolver);Layout(root,100);resolver.Complete(Png(4,2,(_,_)=>255));Wait(Value(image));Layout(root,100);Assert.True(Resolve(image,100,100)!.Band(0,20).Active);
        image.ClearValue(Image.StretchProperty);Layout(root,100);Assert.False(Resolve(image,100,100)!.Band(0,20).Active);
    }

    [Theory]
    [InlineData("*")] [InlineData("null")] [InlineData("https://other.example")] [InlineData("")]
    public async Task DefaultRemoteImageFetchUsesAnonymousCorsAndNoCookies(string allowOrigin)
    {
        using var server=new Server(allowOrigin,requests:2);var resolver=(ICssImageResourceResolver)CssStyleSheet.DefaultResolver;
        var result=await resolver.ResolveImageAsync(server.Uri,new Uri("http://user:secret@example.test/path/private.css?q=token"));
        using(result.Content){Assert.Equal(allowOrigin is "*" or "null",result.ImagePixelAccessAllowed);}
        var second=await resolver.ResolveImageAsync(server.Uri,null);second.Content.Dispose();var request=await server.Request;Assert.Contains("Origin: null",request,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Cookie:",request,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Authorization:",request,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("secret",request);Assert.DoesNotContain("private.css",request);Assert.DoesNotContain("q=token",request);Assert.Contains("Referer: http://example.test/",request,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnonymousCorsValidationIncludesEveryRedirectResponse()
    {
        using var destination=new Server("*");using var redirect=new Server("null",redirect:destination.Uri);var resolver=(ICssImageResourceResolver)CssStyleSheet.DefaultResolver;
        var result=await resolver.ResolveImageAsync(redirect.Uri,null);using(result.Content){Assert.True(result.ImagePixelAccessAllowed);Assert.Equal(destination.Uri,result.Uri);}
        Assert.Contains("Origin: null",await redirect.Request,StringComparison.OrdinalIgnoreCase);Assert.Contains("Origin: null",await destination.Request,StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Bmp()
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write((byte)'B');writer.Write((byte)'M');writer.Write(78);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(4);writer.Write(2);writer.Write((ushort)1);writer.Write((ushort)24);writer.Write(0);writer.Write(24);writer.Write(3780);writer.Write(3780);writer.Write(0);writer.Write(0);for(var i=0;i<8;i++)writer.Write(new byte[]{0,120,0});return stream.ToArray();
    }

    private static byte[] Gif()
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});
        foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}})
        {
            writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});
            uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);
        }
        writer.Write((byte)0x3b);return output.ToArray();
    }

    private static CssShapeImagePixels Pixels(int width,int height,Func<int,int,byte> alpha)=>CssShapeImagePixels.FromBitmap(Bitmap(width,height,alpha));
    private static BitmapSource Bitmap(int width,int height,Func<int,int,byte> alpha)
    {var bytes=new byte[width*height*4];for(var y=0;y<height;y++)for(var x=0;x<width;x++){var i=(y*width+x)*4;bytes[i+1]=120;bytes[i+3]=alpha(x,y);}return BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,bytes,width*4);}
    private static byte[] Png(int width,int height,Func<int,int,byte> alpha)
    {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(Bitmap(width,height,alpha)));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();}
    private static Border Floating(){var element=new Border();Css.SetClass(element,"float");Css.SetStyle(element,"float:left;width:100px;height:100px;");return element;}
    private static TextBlock Text(){var t=new TextBlock{Text=LongText,FontSize=16,LineHeight=20,IsTextSelectionEnabled=true,VerticalAlignment=VerticalAlignment.Top};Css.SetStyle(t,"display:inline;");return t;}
    private static StackPanel Root(params UIElement[] children){var p=new StackPanel();foreach(var c in children)p.Children.Add(c);Css.SetStyle(p,"display:flow-root;font-size:16px;");return p;}
    private static void Attach(FrameworkElement root,string css,Resolver resolver,Uri? baseUri=null){var sheet=CssStyleSheet.Parse(css,"shape-tests",baseUri,resolver);Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(root).Add(sheet);Flush();}
    private static CssShapeImageValue Value(UIElement element)=>(CssShapeImageValue)element.GetValue(CssShapeOutsideProperties.ValueProperty)!;
    private static CssFloatShape? Resolve(FrameworkElement element,double width,double height)=>CssShapeOutsideProperties.Resolve(element,new(width,height),CssBoxMetrics.Margin(element,200),200);
    private static void Wait(CssShapeImageValue value){Assert.True(value.Resource!.Ready.Wait(TimeSpan.FromSeconds(10)));Dispatcher.CurrentDispatcher.ProcessQueue();Flush();}
    private static void Flush()=>CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
    private static void Layout(FrameworkElement root,double width){for(var i=0;i<5;i++){Flush();root.Measure(new(width,1500));root.Arrange(new(0,0,width,Math.Max(1,root.DesiredSize.Height)));root.UpdateLayout();}}
    private static IReadOnlyList<(double StartX,double EndX,double Baseline)> Lines(TextBlock text,FrameworkElement root){var p=text.TransformToAncestor(root);return text.GetSourceRangeSegments(0,text.Text.Length).Select(l=>(l.StartX+p.X,l.EndX+p.X,l.Baseline+p.Y)).ToArray();}

    private sealed class Resolver:ICssResourceResolver
    {
        private readonly TaskCompletionSource<CssResource> _result=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Uri? Requested;internal int Calls;internal bool Disposed;
        public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default){Requested=uri;Interlocked.Increment(ref Calls);return new(_result.Task);}
        internal void Complete(byte[] bytes,Uri? final=null,bool? allowed=null)=>_result.SetResult(new(final??new Uri("file:///test/mask.png"),new Tracked(bytes,()=>Disposed=true)){ImagePixelAccessAllowed=allowed});
    }
    private sealed class Tracked(byte[] bytes,Action closed):MemoryStream(bytes,writable:false){protected override void Dispose(bool disposing){base.Dispose(disposing);closed();}}
    private sealed class Server:IDisposable
    {
        private readonly TcpListener _listener=new(IPAddress.Loopback,0);internal readonly Uri Uri;internal readonly Task<string> Request;
        internal Server(string allow,int requests=1,Uri? redirect=null)
        {
            _listener.Start();Uri=new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/mask.png");
            Request=Task.Run(async()=>{
                var all=new List<string>();
                for(var index=0;index<requests;index++)
                {
                    using var client=await _listener.AcceptTcpClientAsync();await using var stream=client.GetStream();using var reader=new StreamReader(stream,leaveOpen:true);
                    string? line;while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))all.Add(line);
                    var body=redirect is null?Png(1,1,(_,_)=>255):[];
                    var header=$"HTTP/1.1 {(redirect is null?"200 OK":"302 Found")}\r\nConnection:close\r\nContent-Length:{body.Length}\r\nContent-Type:image/png\r\nSet-Cookie:private=value; Path=/\r\n"+(redirect is not null?$"Location:{redirect}\r\n":"")+(allow.Length>0?$"Access-Control-Allow-Origin:{allow}\r\n":"")+"\r\n";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(header));await stream.WriteAsync(body);
                }
                return string.Join("\n",all);
            });
        }
        public void Dispose()=>_listener.Stop();
    }
}
