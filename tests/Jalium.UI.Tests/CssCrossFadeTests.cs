using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCrossFadeTests
{
    [Theory]
    [InlineData("image(red)")]
    [InlineData("image(currentcolor)")]
    [InlineData("image('a')")]
    [InlineData("image(url('a'),red)")]
    [InlineData("image(color-mix(in srgb,currentcolor,white))")]
    [InlineData("cross-fade(red)")]
    [InlineData("cross-fade(red,blue,green)")]
    [InlineData("cross-fade(20% red,blue 80%)")]
    [InlineData("cross-fade(red calc(20% + 30%),blue)")]
    [InlineData("cross-fade(red calc(-10%),blue)")]
    [InlineData("cross-fade(image(red),linear-gradient(green,blue))")]
    [InlineData("cross-fade(url('a') 0%,image-set('b' 1x,'c' 2x) 100%)")]
    [InlineData("cross-fade(red 10%,cross-fade(blue 30%,white 70%) 90%)")]
    [InlineData("image-set(cross-fade(red,blue) 1x,image(green) 2x)")]
    public void CurrentImages4GrammarIsAvailableToAllImageConsumers(string value)
    {
        foreach(var property in new[]{"background-image","content","list-style-image","shape-outside"})
            Assert.True(CssAuthoring.IsValueSupported(property,value),property+": "+value);
    }

    [Theory]
    [InlineData("image()")]
    [InlineData("image(red,blue)")]
    [InlineData("cross-fade()")]
    [InlineData("cross-fade(red,)")]
    [InlineData("cross-fade(red green)")]
    [InlineData("cross-fade('a',blue)")]
    [InlineData("cross-fade(red -1%,blue)")]
    [InlineData("cross-fade(red 101%,blue)")]
    [InlineData("cross-fade(red 0,blue)")]
    [InlineData("cross-fade(red 1px,blue)")]
    [InlineData("cross-fade(red 20% 30%,blue)")]
    [InlineData("cross-fade(20% red 30%,blue)")]
    [InlineData("image-set(cross-fade(image-set('a'),red))")]
    public void InvalidGrammarRejectsTheWholeImage(string value)
    {var reader=new CssTokenReader(value);Assert.False(CssImageValue.TryRead(ref reader,CssCompileContext.Default,out _));}

    [Fact]
    public void ExcessiveImageNestingIsRejectedWithoutRecursionFailure()
    {var value=string.Concat(Enumerable.Repeat("cross-fade(red,",40))+"blue"+new string(')',40);var reader=new CssTokenReader(value);Assert.False(CssImageValue.TryRead(ref reader,CssCompileContext.Default,out _));}

    [Theory]
    [InlineData("image(red)",255,0,0,255)]
    [InlineData("cross-fade(red,blue)",128,0,128,255)]
    [InlineData("cross-fade(red 25%,blue 75%)",64,0,191,255)]
    [InlineData("cross-fade(25% red,75% blue)",64,0,191,255)]
    [InlineData("cross-fade(red 80%,blue 80%)",128,0,128,255)]
    [InlineData("cross-fade(red 25%,blue 25%)",128,0,128,128)]
    [InlineData("cross-fade(white 50%,transparent 50%)",255,255,255,128)]
    [InlineData("cross-fade(red 20%,green,blue)",51,51,102,255)]
    [InlineData("cross-fade(red 0%,blue 0%)",0,0,0,0)]
    [InlineData("cross-fade(red calc(-10%),blue)",0,0,255,255)]
    [InlineData("cross-fade(red calc(150%),blue)",255,0,0,255)]
    [InlineData("cross-fade(red calc(20% + 30%),blue)",128,0,128,255)]
    [InlineData("cross-fade(rgb(255 0 0 / 1) 40%,rgb(0 255 0 / .5) 20%,rgb(0 0 255 / 0) 40%)",204,51,0,128)]
    [InlineData("cross-fade(red 10%,cross-fade(blue 30%,white 70%) 90%)",186,161,230,255)]
    public void PaintingUsesNormalizedPremultipliedSrgbAndLeftoverTransparency(string value,int red,int green,int blue,int alpha)
    {var brush=Parse(value).Background(Context(new Border()),null,out _);Assert.NotNull(brush);var color=Paint(brush!);Near(red,color.R);Near(green,color.G);Near(blue,color.B);Near(alpha,color.A);}

    [Fact]
    public void CrossFadeNaturalSizeAveragesOnlyDimensionedImagesAndFlattensNestedMixes()
    {
        var resolver=new Images();resolver.Add("small.png",Png(40,20,Colors.Green));resolver.Add("large.png",Png(80,40,Colors.Blue));
        var owner=new TextBlock{Text="原文"};Attach(owner,".owner{content:cross-fade(url('small.png') 10%,cross-fade(url('large.png') 20%,red 80%) 90%);}",resolver);
        // Stream disposal can precede asynchronous image-header decoding. The
        // 300px intrinsic fallback must not satisfy this readiness condition.
        Layout(owner);PumpUntil(()=>resolver.Disposed==2&&Math.Abs(Image(owner).NaturalSize.Width-65.714286)<.0001&&Math.Abs(Image(owner).NaturalSize.Height-32.857143)<.0001);Layout(owner);
        Assert.Equal(65.714286,owner.DesiredSize.Width,4);Assert.Equal(32.857143,owner.DesiredSize.Height,4);Assert.Equal("原文",owner.Text);
    }

    [Fact]
    public void DuplicateImagesLoadOnceZeroWeightImagesNeverLoadAndColorDoesNotShrinkNaturalSize()
    {
        var resolver=new Images();resolver.Add("same.png",Png(40,20,Colors.Green));var owner=new TextBlock{Text="原文"};
        Attach(owner,".owner{content:cross-fade(url('same.png') 25%,url('same.png') 25%,url('never.png') 0%,red 50%);}",resolver);
        Layout(owner);PumpUntil(()=>Image(owner).NaturalSize.Width==40);Decode(owner);Layout(owner);
        Assert.Equal(new Size(40,20),owner.DesiredSize);Assert.Equal(new[]{"same.png"},resolver.Requests);Assert.Equal(1,resolver.Disposed);
        var color=PaintOwner(owner);Near(128,color.R);Near(64,color.G);Near(0,color.B);Near(255,color.A);
    }

    [Fact]
    public void ImageSetInsideCrossFadeReselectsAndPreservesRasterCssDimensions()
    {
        var resolver=new Images();resolver.Add("low.png",Png(40,20,Colors.Green));resolver.Add("high.png",Png(80,40,Colors.Blue));
        var owner=new TextBlock{Text="原文"};Attach(owner,".owner{content:cross-fade(image-set('low.png' 1x,'high.png' 2x) 50%,red 50%);}",resolver);
        Layout(owner);PumpUntil(()=>Image(owner).NaturalSize.Width==40);Layout(owner);Assert.Equal(new Size(40,20),owner.DesiredSize);
        VisualTreeHelper.SetRootDpi(owner,new(2,2));Layout(owner);PumpUntil(()=>resolver.Requests.Count==2&&Image(owner).NaturalSize.Width==40);Decode(owner);Layout(owner);
        Assert.Equal(new Size(40,20),owner.DesiredSize);var color=PaintOwner(owner);Near(128,color.R);Near(0,color.G);Near(128,color.B);
        VisualTreeHelper.SetRootDpi(owner,new(1,1));Layout(owner);Assert.Equal(new Size(40,20),owner.DesiredSize);Assert.Equal(new[]{"low.png","high.png"},resolver.Requests);
    }

    [Fact]
    public void BackgroundAutoUsesMixedNaturalSizeAndImageColorsHonorSizedRepeatingTiles()
    {
        var resolver=new Images();resolver.Add("small.png",Png(40,20,Colors.Green));var owner=new Border();
        Attach(owner,".owner{width:150px;height:70px;background-image:cross-fade(url('small.png') 50%,red 50%);background-repeat:no-repeat;}",resolver);
        Layout(owner);PumpUntil(()=>resolver.Disposed==1);ReadyBackground(owner);Layout(owner);
        var bytes=Render(owner,150,70);Assert.Equal(255,bytes[(10*150+10)*4+3]);Assert.Equal(0,bytes[(10*150+50)*4+3]);
        Css.SetStyle(owner,"width:150px;height:70px;background-image:image(red);background-size:20px 10px;background-repeat:space;");Layout(owner);bytes=Render(owner,150,70);
        Assert.Equal(255,bytes[5*4+3]);Assert.Equal(0,bytes[20*4+3]);Assert.Equal(255,bytes[27*4+3]);
    }

    [Fact]
    public void CurrentColorMixesObserveMutableInheritedForeground()
    {
        var foreground=new SolidColorBrush(Colors.Green);var owner=new TextBlock{Text="原文",Foreground=foreground};
        Css.SetStyle(owner,"content:cross-fade(currentcolor,white);width:40px;height:20px;");Layout(owner);
        var color=PaintOwner(owner);Near(128,color.R);Near(192,color.G);Near(128,color.B);
        foreground.Color=Colors.Blue;Layout(owner);color=PaintOwner(owner);Near(128,color.R);Near(128,color.G);Near(255,color.B);
    }

    [Fact]
    public void SolidImageAndCrossFadeMarkersUseOneEmWithoutNaturalDimensions()
    {
        var root=new StackPanel();var row=new TextBlock{Text="列表正文"};root.Children.Add(row);Css.SetClass(row,"owner");
        Attach(root,".owner{display:list-item;list-style-type:none;list-style-image:cross-fade(image(red),blue);font-size:20px;}",new());Layout(root);
        var marker=CssGeneratedContent.Get(row)!.Marker!;Assert.Equal(new Size(20,20),Image(marker).DesiredSize);Assert.Equal(" ",marker.Text);Assert.Equal("列表正文",row.Text);
    }

    [Fact]
    public void CrossFadeShapeUsesMixedAlphaAndStrictThresholds()
    {
        var owner=new Border();Css.SetStyle(owner,"float:left;width:100px;height:100px;shape-outside:cross-fade(linear-gradient(to right,black 50%,transparent 50%) 50%,transparent 50%);");Layout(owner);
        var value=(CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;
        var shape=value.Resolve(owner,new(100,100),default,100,0,.4)!;Assert.InRange(shape.Band(20,20).Right,49,51);
        Assert.False(value.Resolve(owner,new(100,100),default,100,0,.5)!.Band(20,20).Active);
    }

    [Fact]
    public void ShapeResourcesReflowAfterLoadingAndEnforceOriginPixelApproval()
    {
        foreach(var allowed in new[]{true,false})
        {
            var resolver=new Images(gated:true,allowed:allowed);resolver.Add("mask.png",Png(100,100,Colors.Black));var owner=new Border();
            Attach(owner,".owner{float:left;width:100px;height:100px;shape-outside:cross-fade(url('mask.png') 50%,black 50%);}",resolver,new Uri("https://cdn.example/"));Layout(owner);
            var value=(CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;Assert.False(value.Resolve(owner,new(100,100),default,100,0,.75)!.Band(20,20).Active);
            resolver.Open();PumpUntil(()=>value.Resources.All(resource=>resource.Ready.IsCompleted));Layout(owner);
            var band=value.Resolve(owner,new(100,100),default,100,0,.75)!.Band(20,20);Assert.Equal(allowed,band.Active);if(allowed)Assert.Equal(100,band.Right);
        }
    }

    [Fact]
    public void LateMixedImageCompletionCannotReplaceANewerDeclaration()
    {
        var resolver=new Images(gated:true);resolver.Add("old.png",Png(40,20,Colors.Green));var owner=new Border();
        Attach(owner,".owner{width:60px;height:30px;background-image:cross-fade(url('old.png'),red);}",resolver);Layout(owner);
        var resources=(CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!;
        Css.SetStyle(owner,"width:60px;height:30px;background-image:image(blue);");Layout(owner);resolver.Open();PumpUntil(()=>resources.All(resource=>resource.Ready.IsCompleted));Layout(owner);
        Assert.Equal(Colors.Blue,PaintOwner(owner));Assert.Empty((CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!);
    }

    [Theory]
    [InlineData("content")] [InlineData("background-image")] [InlineData("shape-outside")]
    public void WeightEditsReuseResidentUrls(string property)
    {
        var resolver=new Images();resolver.Add("same.png",Png(40,20,Colors.Green));var owner=new Border();
        Attach(owner,$".owner{{--weight:25%;width:80px;height:40px;float:left;{property}:cross-fade(url('same.png') var(--weight),red);}}",resolver);Layout(owner);
        void Ready()
        {
            if(property=="content"){PumpUntil(()=>Image(owner).NaturalSize.Width==40);Decode(owner);}
            else if(property=="background-image")ReadyBackground(owner);
            else{var value=(CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;PumpUntil(()=>value.Resources.All(r=>r.Ready.IsCompleted));Assert.NotNull(value.Resolve(owner,new(80,40),default,80,0,.5));}
            Layout(owner);
        }
        Ready();for(var i=0;i<3;i++){Css.SetStyle(owner,$"--weight:{(i%2==0?75:25)}%;");Layout(owner);Ready();}
        Assert.Equal(new[]{"same.png"},resolver.Requests);Assert.Equal(1,resolver.Disposed);
    }

    [Fact]
    public void ElementContextPercentagesStayReactiveWhenSiblingCountChanges()
    {
        var owner=new Border();Css.SetClass(owner,"owner");var root=new StackPanel();root.Children.Add(owner);root.Children.Add(new Border());
        Attach(root,".owner{width:40px;height:20px;background-image:cross-fade(red calc(10% * sibling-count()),blue);}",new());Layout(root);
        var color=PaintOwner(owner);Near(51,color.R);Near(204,color.B);
        root.Children.Add(new Border());Layout(root);color=PaintOwner(owner);Near(77,color.R);Near(178,color.B);
    }

    [Fact]
    public void AnimationFramesInvalidateMixedPaintButKeepTheShapeAtFrameZero()
    {
        var resolver=new Images();resolver.Add("animated.gif",Gif());var owner=new Border();
        Attach(owner,".owner{width:40px;height:10px;float:left;background-image:cross-fade(url('animated.gif'),red);shape-outside:cross-fade(url('animated.gif'),transparent);}",resolver);Layout(owner);ReadyBackground(owner);
        var shape=(CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;PumpUntil(()=>shape.Resources.All(resource=>resource.Ready.IsCompleted));
        var resource=Assert.Single((CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!);var bitmap=Assert.IsType<BitmapImage>(resource.Get(CssNode.Get(owner)));
        PumpUntil(()=>bitmap.AnimatedSubstitute is{IsPlaying:true});var animation=Assert.IsType<AnimatedBitmap>(bitmap.AnimatedSubstitute);animation.Stop();
        try
        {
            animation.CurrentFrameIndex=0;Flush();var before=ColorAt(Render(owner,40,10),40,8,5);owner.ClearRenderDirty();animation.CurrentFrameIndex=1;Flush();Assert.True(owner.IsRenderDirty);
            var after=ColorAt(Render(owner,40,10),40,8,5);Assert.NotEqual(before,after);Near(128,after.A);Near(255,after.R);
            Assert.InRange(shape.Resolve(owner,new(40,10),default,40,0,.4)!.Band(0,10).Right,19.9,20.1);
            Css.SetStyle(owner,"background-image:image(blue);shape-outside:none;");Layout(owner);owner.ClearRenderDirty();animation.CurrentFrameIndex=0;Flush();Assert.False(owner.IsRenderDirty);
        }
        finally{animation.Stop();}
    }

    [Fact]
    public void SharedCompiledImagesKeepContextColorsSeparateAcrossDispatchers()
    {
        var sheet=CssStyleSheet.Parse(".owner{content:cross-fade(currentcolor,white);width:40px;height:20px;}");var colors=new List<Color>();Exception? failure=null;
        foreach(var color in new[]{Colors.Green,Colors.Blue})
        {
            var thread=new Thread(()=>{try{var owner=new TextBlock{Text="原文",Foreground=new SolidColorBrush(color)};Css.SetClass(owner,"owner");Css.GetStyleSheets(owner).Add(sheet);Layout(owner);colors.Add(PaintOwner(owner));}catch(Exception error){failure=error;}});thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(failure);Near(192,colors[0].G);Near(128,colors[0].B);Near(128,colors[1].G);Near(255,colors[1].B);
    }

    [Fact]
    public void DimensionlessSolidMixesDoNotAllocateViewportSizedRasterBuffers()
    {
        var brush=Parse("cross-fade(image(red),image(blue))").Background(Context(new Border()),null,out _)!;
        var layout=brush.CssImageLayout!;var before=GC.GetAllocatedBytesForCurrentThread();Assert.NotNull(layout.Resolve(8000,8000));
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-before,0,1_000_000);var color=Paint(brush);Near(128,color.R);Near(128,color.B);
    }

    [Theory]
    [InlineData("content")] [InlineData("background-image")] [InlineData("shape-outside")]
    public void EquivalentResolvedUrisShareOneReadWithinTheMixedImage(string property)
    {
        var resolver=new Images();resolver.Add("same.png",Png(40,20,Colors.Green));var owner=new Border();
        Attach(owner,$".owner{{width:80px;height:40px;float:left;{property}:cross-fade(url('same.png'),url('./same.png'));}}",resolver);Layout(owner);
        if(property=="content"){PumpUntil(()=>Image(owner).NaturalSize.Width==40);Decode(owner);}
        else if(property=="background-image")ReadyBackground(owner);
        else{var value=(CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;PumpUntil(()=>value.Resources.All(r=>r.Ready.IsCompleted));}
        Assert.Equal(new[]{"same.png"},resolver.Requests);Assert.Equal(1,resolver.Disposed);
    }

    [Fact]
    public void VectorImagesParticipateInMixedPaintingAndKeepTheirNaturalSizeAtExplicitDensity()
    {
        var resolver=new Images();resolver.Add("vector.svg",Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='10'><rect width='20' height='10' fill='blue'/></svg>"));
        var owner=new TextBlock{Text="原文"};VisualTreeHelper.SetRootDpi(owner,new(2,2));Attach(owner,".owner{content:cross-fade(image-set('vector.svg' 2x) 25%,red 75%);}",resolver);Layout(owner);PumpUntil(()=>Image(owner).NaturalSize.Width==20);Layout(owner);
        Assert.Equal(new Size(20,10),owner.DesiredSize);var color=PaintOwner(owner);Near(191,color.R);Near(0,color.G);Near(64,color.B);Near(255,color.A);
    }

    private static CssApplyContext Context(FrameworkElement owner){var node=CssNode.Get(owner);return new(node,CssEngine.BuildLengthContext(node),new());}
    private static CssImageValue Parse(string value){var reader=new CssTokenReader(value);Assert.True(CssImageValue.TryRead(ref reader,CssCompileContext.Default,out var image),value);Assert.True(reader.AtEnd);return image!;}
    private static void Near(int expected,int actual)=>Assert.InRange(actual,Math.Max(0,expected-1),Math.Min(255,expected+1));
    private static Color Paint(Brush brush){var target=new RenderTargetBitmap(4,4,96,96,PixelFormats.Bgra32);var dc=new SoftwareDrawingContext(target);dc.DrawRectangle(brush,null,new(0,0,4,4));dc.Close();return ColorAt(target.GetPixelBuffer(),4,2,2);}
    private static byte[] Render(FrameworkElement owner,int width,int height){var target=new RenderTargetBitmap(width,height,96,96,PixelFormats.Bgra32);var dc=new SoftwareDrawingContext(target);owner.Render(dc);dc.Close();return target.GetPixelBuffer();}
    private static Color PaintOwner(FrameworkElement owner){var w=(int)Math.Max(1,owner.ActualWidth);var h=(int)Math.Max(1,owner.ActualHeight);return ColorAt(Render(owner,w,h),w,w/2,h/2);}
    private static Color ColorAt(byte[] data,int width,int x,int y){var i=(y*width+x)*4;return Color.FromArgb(data[i+3],data[i+2],data[i+1],data[i]);}
    private static CssGeneratedImage Image(FrameworkElement owner)=>Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(owner)).Select(i=>VisualTreeHelper.GetChild(owner,i)).OfType<CssGeneratedImage>().Single();
    private static void Decode(FrameworkElement owner)
    {foreach(var resource in ((IEnumerable<CssBackgroundImageResource>)typeof(CssGeneratedImage).GetField("_resources",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(Image(owner))!))if(resource.Get(CssNode.Get(Image(owner)),true) is BitmapImage bitmap){var task=bitmap.PreloadAsync(160,80);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully);}Flush();}
    private static void ReadyBackground(FrameworkElement owner)
    {var resources=(CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!;PumpUntil(()=>resources.All(resource=>resource.Ready.IsCompleted));foreach(var resource in resources)if(resource.Get(CssNode.Get(owner)) is BitmapImage bitmap){var task=bitmap.PreloadAsync(160,80);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully);}Flush();}
    private static void Attach(FrameworkElement owner,string source,Images resolver,Uri? baseUri=null)
    {if(Css.GetClass(owner).Length==0)Css.SetClass(owner,"owner");var sheet=CssStyleSheet.Parse(source,"cross-fade",baseUri??new Uri("file:///images/"),resolver);Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(owner).Add(sheet);Flush();}
    private static void Layout(FrameworkElement root){for(var i=0;i<5;i++){Flush();root.Measure(new(260,1000));root.Arrange(new(0,0,root.DesiredSize.Width,root.DesiredSize.Height));root.UpdateLayout();}}
    private static void Flush(){Dispatcher.CurrentDispatcher.ProcessQueue();BitmapDecodeNotifier.FlushPendingForTests();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);}
    private static void PumpUntil(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(10);while(!condition()&&DateTime.UtcNow<end){Flush();Thread.Sleep(1);}Assert.True(condition(),"CSS image operation timed out");Flush();}
    private static byte[] Png(int width,int height,Color color){var pixels=new byte[width*height*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;pixels[i+3]=color.A;}var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();}
    private static byte[] Gif(){using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}}){writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);}writer.Write((byte)0x3b);return output.ToArray();}
    private sealed class Images(bool gated=false,bool? allowed=null):ICssResourceResolver
    {
        internal readonly List<string> Requests=[];private readonly Dictionary<string,byte[]> _bytes=[];private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);internal int Disposed;
        internal void Open()=>_gate.TrySetResult();internal void Add(string name,byte[] bytes)=>_bytes[name]=bytes;
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default){var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);if(gated)await _gate.Task;return new(uri,new Closed(_bytes.GetValueOrDefault(name,new byte[]{1,2,3}),()=>Interlocked.Increment(ref Disposed))){ImagePixelAccessAllowed=allowed};}
    }
    private sealed class Closed(byte[] bytes,Action disposed):MemoryStream(bytes,writable:false){protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)disposed();}}
}
