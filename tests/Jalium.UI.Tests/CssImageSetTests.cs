using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssImageSetTests
{
    [Fact]
    public void ConicBackgroundMetadataDoesNotMutateFrozenOrOtherConsumers()
    {
        var element = new Border();
        var image = Parse("conic-gradient(black 0deg 180deg,transparent 180deg 360deg)");
        var frozen = Assert.IsType<ImageBrush>(image.Border(Context(element), null, []).Brush);
        Assert.True(frozen.IsFrozen);
        Assert.Null(frozen.CssBackgroundLayout);

        var first = Assert.IsType<ImageBrush>(image.Background(Context(element), null, out var resource));
        var second = Assert.IsType<ImageBrush>(image.Background(Context(new Border()), null, out _));
        Assert.Null(resource);
        Assert.NotSame(first, second);
        Assert.NotNull(first.CssGradientLayout);
        Assert.NotNull(first.CssBackgroundLayout);
        Assert.NotSame(first.CssBackgroundLayout, second.CssBackgroundLayout);
        first.CssBackgroundLayout = null;
        Assert.NotNull(second.CssBackgroundLayout);
        Assert.True(frozen.IsFrozen);
        Assert.Null(frozen.CssBackgroundLayout);
        Assert.NotNull(image.Shape(Context(element)).ResolvePixels(new(100, 100)));
    }

    [Theory]
    [InlineData("url(a)")]
    [InlineData("'a'")]
    [InlineData("url('')")]
    [InlineData("url(a) 1x, url(b) 2x")]
    [InlineData("url(a) 96dpi, 'b' 37.79527559055118dpcm")]
    [InlineData("url(a) 1dppx type('image/png')")]
    [InlineData("url(a) type('image/png') 2x")]
    [InlineData("url(a) type('')")]
    [InlineData("url(a) type('not a MIME type')")]
    [InlineData("url(a) 0x")]
    [InlineData("url(a) calc(-1x)")]
    [InlineData("url(a) calc(2x * 3)")]
    [InlineData("url(a) calc(1dppx * sibling-index())")]
    [InlineData("url(a) calc(1dppx * sign(1em - 10px))")]
    [InlineData("linear-gradient(red,blue), radial-gradient(red,blue) 2x")]
    [InlineData("conic-gradient(red,blue) type('image/png')")]
    [InlineData("url(a\\ b.png) /* descriptor */ 2X")]
    public void ImageSetGrammarAndItsWebkitAliasAcceptCompleteOptions(string options)
    {
        foreach (var name in new[] { "image-set", "-webkit-image-set" })
        {
            var reader = new CssTokenReader(name + "(" + options + ")");
            Assert.True(CssImageValue.TryRead(ref reader, CssCompileContext.Default, out _));
            Assert.True(reader.AtEnd);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("url(a),")]
    [InlineData(",url(a)")]
    [InlineData("url(a) -1x")]
    [InlineData("url(a) 2")]
    [InlineData("url(a) 0")]
    [InlineData("url(a) 1px")]
    [InlineData("url(a) 2x 3x")]
    [InlineData("url(a) type('image/png') type('image/png')")]
    [InlineData("url(a) type(image/png)")]
    [InlineData("url(a) type('image/png' 'image/jpeg')")]
    [InlineData("url(a) calc(3 * 4)")]
    [InlineData("url(a) calc(1x + 1)")]
    [InlineData("none 1x")]
    [InlineData("red 1x")]
    [InlineData("image-set(url(a))")]
    [InlineData("-webkit-image-set(url(a))")]
    [InlineData("url(a b.png)")]
    [InlineData("url(a) 1fr")]
    public void InvalidImageSetRejectsTheEntireDeclaration(string options)
    {
        foreach (var name in new[] { "image-set", "-webkit-image-set" })
        {
            var reader = new CssTokenReader(name + "(" + options + ")");
            Assert.False(CssImageValue.TryRead(ref reader, CssCompileContext.Default, out _));
        }
    }

    [Theory]
    [InlineData(.75, 1, "red")]
    [InlineData(1, 1, "red")]
    [InlineData(1.25, 2, "blue")]
    [InlineData(2, 2, "blue")]
    [InlineData(2.5, 3, "green")]
    [InlineData(5, 3, "green")]
    public void UnorderedCandidatesUseTheSmallestSufficientDensityOrLargestAvailable(double dpi, double density, string color)
    {
        var element = new Border(); VisualTreeHelper.SetRootDpi(element, new(dpi, dpi));
        var selection = Parse("image-set(linear-gradient(green,green) 3x,linear-gradient(red,red) 96dpi,linear-gradient(blue,blue) 2dppx)").Select(Context(element));
        Assert.NotNull(selection); Assert.Equal(density, selection.Value.Resolution);
        Assert.Equal(color switch { "red" => Colors.Red, "blue" => Colors.Blue, _ => Colors.Green }, ColorOf(selection.Value, element));
    }

    [Fact]
    public void TypeFilteringPrecedesDuplicateRemovalAndFirstSupportedResolutionWins()
    {
        var element = new Border();
        var selected = Parse("image-set(linear-gradient(red,red) type('image/not-real'),linear-gradient(green,green) type('IMAGE/PNG;version=1'),linear-gradient(blue,blue) 96dpi)").Select(Context(element));
        Assert.NotNull(selected); Assert.Equal(Colors.Green, ColorOf(selected.Value, element));
        Assert.Null(Parse("image-set(url(a) type(''),url(b) type('image/*'),url(c) type('text/html'))").Select(Context(element)));
    }

    [Fact]
    public void ZeroAndNegativeMathAreValidInvalidImagesRatherThanCascadeFallbacks()
    {
        var element = new Border();
        foreach (var density in new[] { "0x", "calc(-1x)", "calc(1x - 2x)" })
        {
            Css.SetStyle(element, $"background-image:linear-gradient(red,red);background-image:image-set(linear-gradient(blue,blue) {density});shape-outside:circle(50%);shape-outside:image-set(url('') {density});");
            Flush(); Assert.Null(element.Background); Assert.Null(element.GetValue(CssShapeOutsideProperties.ValueProperty));
        }
        Css.SetStyle(element, "background-image:linear-gradient(green,green);background-image:image-set(url(a) -1x);");
        Flush(); Assert.NotNull(element.Background);
    }

    [Fact]
    public void DensityMathUsesElementTypographyAndSiblingContext()
    {
        var first = new Border(); var second = new Border(); var root = new StackPanel();root.Children.Add(first);root.Children.Add(second);
        Css.SetStyle(root,"font-size:16px;"); Flush();
        var image = Parse("image-set(linear-gradient(red,red) calc(1x * sibling-index()),linear-gradient(blue,blue) 3x)");
        Assert.Equal(1d,image.Select(Context(first))!.Value.Resolution);Assert.Equal(2d,image.Select(Context(second))!.Value.Resolution);
        var font = Parse("image-set(linear-gradient(red,red) calc(1x * sign(1em - 10px)))");
        Assert.NotNull(font.Select(Context(first)));Css.SetStyle(first,"font-size:8px;");Flush();Assert.Null(font.Select(Context(first)));
    }

    [Fact]
    public void DpiChangesAndVariablesReselectBothBackgroundAndShapeWithoutChangingSourceText()
    {
        var floating = new Border();var body = new TextBlock{IsTextSelectionEnabled=true};var original=new Run("图片候选随屏幕密度切换，原始文字与选择范围保持不变。");body.Inlines.Add(original);
        var root = new StackPanel();root.Children.Add(floating);root.Children.Add(body);
        Css.SetStyle(root,"display:flow-root;--first:red;");Css.SetStyle(floating,"float:left;width:100px;height:100px;background-image:image-set(linear-gradient(var(--first),var(--first)) 1x,linear-gradient(blue,blue) 2x);shape-outside:image-set(linear-gradient(to right,black 25%,transparent 25%) 1x,linear-gradient(to right,black 75%,transparent 75%) 2x);");Css.SetStyle(body,"display:inline;");
        Layout(root);body.Select(2,4);var initial=Shape(floating)!.Band(20,20).Right;Assert.InRange(initial,24,26);
        VisualTreeHelper.SetRootDpi(root,new(2,2));Layout(root);Assert.InRange(Shape(floating)!.Band(20,20).Right,74,76);
        Assert.Equal(Colors.Blue,PaintColor(floating.Background!));Assert.Same(original,body.Inlines[0]);Assert.Equal(original.Text.Substring(2,4),body.SelectedText);
        VisualTreeHelper.SetRootDpi(root,new(1,1));Css.SetStyle(root,"display:flow-root;--first:green;");Layout(root);Assert.Equal(Colors.Green,PaintColor(floating.Background!));Assert.Equal(2,root.Children.Count);
    }

    [Fact]
    public void CurrentColorAndBackgroundShorthandKeepLayerOrderingAndLocalPrecedence()
    {
        var element=new Border();Css.SetStyle(element,"color:red;background:image-set(linear-gradient(currentcolor,currentcolor)) center/20px 10px no-repeat padding-box content-box,linear-gradient(blue,blue) left/50% 100% no-repeat green;");Flush();
        var state=CssBackgroundPaintProperties.Get(element)!;Assert.Equal(2,state.Layers.Length);Assert.Equal(Colors.Red,PaintColor(state.Layers[0].Image!));Assert.Equal(CssBackgroundBox.Content,state.Layers[0].Clip);
        Css.SetStyle(element,"color:purple;background-image:image-set(linear-gradient(currentcolor,currentcolor));");Flush();Assert.Equal(Colors.Purple,PaintColor(element.Background!));
        var local=new SolidColorBrush(Colors.Orange);element.Background=local;Css.SetStyle(element,"background-image:image-set(linear-gradient(blue,blue));");Flush();Assert.Same(local,element.Background);
    }

    [Fact]
    public void OnlyChosenUrlLoadsOnceAndRasterDensityControlsAutoSizeAcrossDpiChanges()
    {
        var resolver=new Images();resolver.Add("low.png",Png(40,20));resolver.Add("high.png",Png(80,40));
        var element=new Border();Attach(element,"Border{background-image:image-set('low.png' 1x,'high.png' 2x);background-repeat:no-repeat;}",resolver);
        Ready(element);var low=Assert.IsType<ImageBrush>(element.Background);Decode(low);Assert.Equal(new Rect(0,0,40,20),low.CssBackgroundLayout!.ImageRect(new(0,0,100,100),low.ImageSource!.Width,low.ImageSource.Height));Assert.Equal(new[]{"low.png"},resolver.Requests);
        VisualTreeHelper.SetRootDpi(element,new(2,2));Flush();Ready(element);var high=Assert.IsType<ImageBrush>(element.Background);Decode(high);Assert.Equal(new Rect(0,0,40,20),high.CssBackgroundLayout!.ImageRect(new(0,0,100,100),high.ImageSource!.Width,high.ImageSource.Height));
        VisualTreeHelper.SetRootDpi(element,new(1,1));Flush();Assert.Same(low.ImageSource,Assert.IsType<ImageBrush>(element.Background).ImageSource);Assert.Equal(new[]{"low.png","high.png"},resolver.Requests);Assert.Equal(2,resolver.Disposed);
    }

    [Fact]
    public void VectorAndGeneratedImagesIgnoreRasterResolutionForNaturalSizing()
    {
        var resolver=new Images();resolver.Add("vector.svg",Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'><rect width='80' height='40' fill='green'/></svg>"));
        var element=new Border();Attach(element,"Border{background-image:image-set('vector.svg' 2x);background-repeat:no-repeat;}",resolver);Ready(element);
        var brush=Assert.IsType<ImageBrush>(element.Background);Assert.IsType<SvgImage>(brush.ImageSource);Assert.Equal(1,brush.CssBackgroundLayout!.NaturalResolution);Assert.Equal(new Rect(0,0,80,40),brush.CssBackgroundLayout.ImageRect(new(0,0,100,100),80,40));
        Css.SetStyle(element,"background-image:image-set(linear-gradient(green,green) 2x);");Flush();Assert.IsType<LinearGradientBrush>(element.Background);
    }

    [Fact]
    public void UnsupportedAndUnchosenUrlsNeverFetchAndFailureDoesNotSelectAnotherCandidate()
    {
        var resolver=new Images();resolver.Add("broken.png",new byte[]{1,2,3});
        var element=new Border();Attach(element,"Border{background-image:image-set('unsupported.png' type('image/not-real'),'broken.png' 1x,'unchosen.png' 2x);}",resolver);Ready(element);
        Assert.Equal(new[]{"broken.png"},resolver.Requests);var image=Assert.IsType<BitmapImage>(Assert.IsType<ImageBrush>(element.Background).ImageSource);
        var decode=image.PreloadAsync(100,100);PumpUntil(()=>decode.IsCompleted);Assert.True(decode.IsFaulted);Assert.Equal(new[]{"broken.png"},resolver.Requests);
    }

    [Fact]
    public void StaleBackgroundCompletionCannotReplaceANewerDeclaration()
    {
        var resolver=new Images(gated:true);resolver.Add("old.png",Png(40,20));var element=new Border();Attach(element,"Border{background-image:image-set('old.png');}",resolver);
        var old=(CssBackgroundImageResource[])element.GetValue(CssBackgroundImageResource.ActiveProperty)!;
        Css.SetStyle(element,"background-image:linear-gradient(blue,blue);");Flush();resolver.Open();PumpUntil(()=>old[0].Ready.IsCompleted);Flush();Assert.Equal(Colors.Blue,PaintColor(element.Background!));Assert.Empty((CssBackgroundImageResource[])element.GetValue(CssBackgroundImageResource.ActiveProperty)!);
    }

    [Fact]
    public void DeferredBackgroundDecodeRequestsRepaintAndStopsAfterItsDeclarationIsReplaced()
    {
        var resolver=new Images();resolver.Add("background.png",Png(40,20));var element=new Border();Attach(element,"Border{background-image:image-set('background.png');}",resolver);Ready(element);
        var brush=Assert.IsType<ImageBrush>(element.Background);var image=Assert.IsType<BitmapImage>(brush.ImageSource);element.ClearRenderDirty();
        var task=image.PreloadAsync(100,100);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully);PumpUntil(()=>{BitmapDecodeNotifier.FlushPendingForTests();return element.IsRenderDirty;});
        Css.SetStyle(element,"background-image:linear-gradient(blue,blue);");Flush();element.ClearRenderDirty();
        var version=(long)typeof(BitmapImage).GetField("_deferredSourceVersion",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(image)!;
        image.RaiseDecodeNotification(version,true,null);Flush();Assert.False(element.IsRenderDirty);
    }

    [Fact]
    public void AnimatedBackgroundFramesUseTheNormalBitmapSubstituteAndRequestRepaint()
    {
        var resolver=new Images();resolver.Add("animation.gif",Gif());var element=new Border();Attach(element,"Border{background-image:image-set('animation.gif');}",resolver);Ready(element);
        var image=Assert.IsType<BitmapImage>(Assert.IsType<ImageBrush>(element.Background).ImageSource);var task=image.PreloadAsync(20,20);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully);
        PumpUntil(()=>{BitmapDecodeNotifier.FlushPendingForTests();return image.AnimatedSubstitute is {IsPlaying:true};});Flush();var animation=Assert.IsType<AnimatedBitmap>(image.AnimatedSubstitute);animation.Stop();
        try {element.ClearRenderDirty();animation.CurrentFrameIndex=1;Flush();Assert.True(element.IsRenderDirty);}
        finally {animation.Stop();}
    }

    [Theory]
    [InlineData("image/png",true)] [InlineData("image/jpeg",true)] [InlineData("image/svg+xml",true)]
    [InlineData("IMAGE/PNG; charset=utf-8",true)] [InlineData("image/*",false)] [InlineData("text/html",false)]
    [InlineData("image/png trailing",false)] [InlineData("image/not-real",false)]
    public void MimeSelectionUsesActualDecoderCapabilitiesAndValidMimeEssence(string mime,bool supported)
        => Assert.Equal(supported,CssImageTypeSupport.IsSupported(mime));

    [Theory]
    [InlineData("image/webp")] [InlineData("image/tiff")] [InlineData("image/vnd.microsoft.icon")]
    [InlineData("image/heic")] [InlineData("image/avif")]
    public void OptionalCodecProbesContainRealDecodableImages(string mime)
    {
        if(OperatingSystem.IsMacOS())Assert.True(ImageMimeTypes.ProbeOptional(mime));
    }

    [Fact]
    public void SharedCompiledImagesAreSafeOnDifferentDispatchers()
    {
        var sheet=CssStyleSheet.Parse("Border{color:green;background-image:image-set(linear-gradient(currentcolor,currentcolor) 1x,linear-gradient(blue,blue) 2x);}");
        Exception? failure=null;var colors=new List<Color>();
        foreach(var scale in new[]{1d,2d})
        {
            var thread=new Thread(()=>{try{var e=new Border();VisualTreeHelper.SetRootDpi(e,new(scale,scale));Css.GetStyleSheets(e).Add(sheet);Flush();colors.Add(PaintColor(e.Background!));}catch(Exception error){failure=error;}});thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(failure);Assert.Equal(new[]{Colors.Green,Colors.Blue},colors);
    }

    [Fact]
    public void DpiCandidateReflowRetainsTabFocusAccessibleTextSelectionAndPointOwnership()
    {
        Keyboard.Initialize();Keyboard.ClearFocus();var a=new TextBlock{Text="屏幕密度切换后，原始文字仍可以选择和通过键盘访问。",IsTextSelectionEnabled=true,Focusable=true};var b=new TextBlock{Text="下一个焦点",Focusable=true};KeyboardNavigation.SetIsTabStop(a,true);KeyboardNavigation.SetIsTabStop(b,true);
        var floating=new Border();var root=new StackPanel();root.Children.Add(floating);root.Children.Add(a);root.Children.Add(b);Css.SetStyle(root,"display:flow-root;");Css.SetStyle(floating,"float:left;width:100px;height:100px;shape-outside:image-set(linear-gradient(to right,black 25%,transparent 25%) 1x,linear-gradient(to right,black 75%,transparent 75%) 2x);");Css.SetStyle(a,"display:inline;");
        var window=new DisplayedTestWindow{Content=root,Width=260,Height=500,TitleBarStyle=WindowTitleBarStyle.Native};
        try
        {
            Layout(window);a.Select(2,4);Assert.True(a.Focus());
            // A Window supplies DPI through IWindowHost, so simulate that host
            // transition rather than changing only a nested visual's DPI field.
            typeof(Window).GetField("_dpiScale",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,2d);
            window.NotifyDpiChangedRecursive(new(1,1),new(2,2));Layout(window);Assert.InRange(Shape(floating)!.Band(20,20).Right,74,76);
            Assert.Same(a,Keyboard.FocusedElement);Assert.Equal(a.Text.Substring(2,4),a.SelectedText);Assert.Equal(a.Text,a.GetAutomationPeer()!.GetName());Assert.True(KeyboardNavigation.MoveFocus(a));Assert.Same(b,Keyboard.FocusedElement);Assert.True(KeyboardNavigation.MoveFocus(b,reverse:true));Assert.Same(a,Keyboard.FocusedElement);
            var line=a.GetSourceRangeSegments(0,a.Text.Length)[0];var offset=a.TransformToAncestor(root);Assert.Same(a,root.InputHitTest(new(offset.X+(line.StartX+line.EndX)/2,offset.Y+line.Baseline-2)));
        }
        finally {Keyboard.ClearFocus();window.Close();}
    }

    private static CssImageValue Parse(string css)
    {var reader=new CssTokenReader(css);Assert.True(CssImageValue.TryRead(ref reader,CssCompileContext.Default,out var image));Assert.True(reader.AtEnd);return image!;}
    private static CssApplyContext Context(FrameworkElement element)
    {var node=CssNode.Get(element);return new(node,CssEngine.BuildLengthContext(node),new());}
    private static Color ColorOf(CssImageValue.Selection selection,FrameworkElement element)=>PaintColor(selection.Image.Background(Context(element),selection.Resolution,out _)!);
    private static Color PaintColor(Brush brush)
    {var bitmap=new RenderTargetBitmap(4,4,96,96,PixelFormats.Bgra32);var drawing=new SoftwareDrawingContext(bitmap);drawing.DrawRectangle(brush,null,new(0,0,4,4));drawing.Close();var bytes=bitmap.GetPixelBuffer();return Color.FromArgb(bytes[3],bytes[2],bytes[1],bytes[0]);}
    private static void Attach(FrameworkElement root,string css,Images resolver)
    {var sheet=CssStyleSheet.Parse(css,"image-set",new Uri("file:///assets/css/"),resolver);Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(root).Add(sheet);Flush();}
    private static void Ready(FrameworkElement element)
    {var resources=(CssBackgroundImageResource[])element.GetValue(CssBackgroundImageResource.ActiveProperty)!;PumpUntil(()=>resources.All(r=>r.Ready.IsCompleted));Assert.All(resources,r=>Assert.True(r.Ready.IsCompletedSuccessfully,r.Ready.Exception?.ToString()));Flush();}
    private static void Decode(ImageBrush brush)
    {if(brush.ImageSource is BitmapImage bitmap){var task=bitmap.PreloadAsync(100,100);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully,task.Exception?.ToString());}}
    private static void PumpUntil(Func<bool> done)
    {var stop=DateTime.UtcNow.AddSeconds(10);while(!done()&&DateTime.UtcNow<stop){Flush();Thread.Sleep(1);}Assert.True(done(),"Image operation timed out");}
    private static void Flush(){Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);}
    private static void Layout(FrameworkElement root){for(var i=0;i<4;i++){Flush();root.Measure(new(260,500));root.Arrange(new(0,0,260,500));root.UpdateLayout();}}
    private static CssFloatShape? Shape(FrameworkElement element)=>CssShapeOutsideProperties.Resolve(element,new(100,100),default,260);
    private static byte[] Png(int width,int height)
    {var pixels=new byte[width*height*4];for(var i=0;i<pixels.Length;i+=4){pixels[i+1]=128;pixels[i+3]=255;}var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
    private static byte[] Gif()
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});
        foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}}){writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);}writer.Write((byte)0x3b);return output.ToArray();
    }
    private sealed class Images(bool gated=false):ICssResourceResolver
    {
        private readonly Dictionary<string,byte[]> _images=[];private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Requests=[];internal int Disposed;
        internal void Add(string name,byte[] bytes)=>_images[name]=bytes;
        internal void Open()=>_gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);if(gated)await _gate.Task;return new(uri,new Tracked(_images[name],()=>Interlocked.Increment(ref Disposed)));}
    }
    private sealed class Tracked(byte[] bytes,Action disposed):MemoryStream(bytes,writable:false)
    {protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)disposed();}}
}
