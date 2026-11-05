using System.Collections;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssImageSetChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);if(!context.IsValid||context.Backend!=RenderBackend.Metal)throw new InvalidOperationException("Metal unavailable");var passed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{375,720})
        {
            const int height=940;var resolver=new Images();
            resolver.Bytes["low.png"]=Png(144,56,(x,y)=>Color.FromRgb((byte)(x%24<12?20:114),(byte)(x%24<12?99:180),(byte)(x%24<12?66:149)));
            resolver.Bytes["high.png"]=Png(288,112,(x,y)=>Color.FromRgb((byte)(x%48<24?24:124),(byte)(x%48<24?75:163),(byte)(x%48<24?126:198)));
            resolver.Bytes["mask-low.png"]=Png(100,100,(x,y)=>x<35?Color.FromRgb(114,180,149):Colors.Transparent);
            resolver.Bytes["mask-high.png"]=Png(200,200,(x,y)=>x<140?Color.FromRgb(114,180,149):Colors.Transparent);
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");
            var natural=Section("光栅候选的自然尺寸");var swatch=new Border();Css.SetClass(swatch,"swatch");natural.Children.Add(swatch);natural.Children.Add(Text("候选图片的像素数量翻倍，显示宽度仍为 144，图案随屏幕密度切换。"));root.Children.Add(natural);
            var shape=Section("同一候选参与绘制与文字环绕");var floating=new Border();Css.SetClass(floating,"float");shape.Children.Add(floating);var body=new TextBlock{IsTextSelectionEnabled=true};Css.SetClass(body,"body");var original=new Run("图片候选依据屏幕密度选择，透明区域决定文字经过的位置。切换密度以后，原始文字、选择范围和点命中保持一致。");body.Inlines.Add(original);body.Inlines.Add(new Run("原始 Run 保留"){Foreground=new SolidColorBrush(Color.FromRgb(24,75,126)),FontWeight=FontWeights.Bold});shape.Children.Add(body);root.Children.Add(shape);
            var gradient=Section("格式筛选与生成图片");var generated=new Border();Css.SetClass(generated,"generated");gradient.Children.Add(generated);gradient.Children.Add(Text("不支持的格式先被排除，同分辨率保留第一个可用候选；渐变使用当前文字颜色。"));root.Children.Add(gradient);
            var sheet=CssStyleSheet.Parse("""
                .root {display:block;padding:22px;color:#233c32;font-size:16px;line-height:1.65;--tone:#146342;}
                .root::before {content:attr(Tag);display:block;font-size:24px;color:#146342;margin-bottom:18px;}
                .section {display:flow-root;padding:14px;margin-bottom:16px;border:1px solid #abcdbd;background:#f4f8f5;white-space:normal;line-height:1.65;}
                .section::before {content:attr(Tag);display:block;font-size:18px;color:#146342;margin-bottom:12px;}
                .swatch {display:block;height:72px;margin-bottom:12px;background-image:image-set('low.png' 1x,'high.png' 2x);background-repeat:no-repeat;}
                .float {float:var(--side,left);width:100px;height:100px;margin:0 12px 8px 0;background-image:image-set('mask-low.png' 1x,'mask-high.png' 2x);background-size:100% 100%;background-repeat:no-repeat;shape-outside:image-set('mask-low.png' 1x,'mask-high.png' 2x);shape-margin:6px;}
                .body {display:inline;}
                .generated {display:block;height:56px;margin-bottom:12px;color:var(--tone);background-image:image-set(url('never-fetch.png') type('image/not-real'),linear-gradient(to right,currentcolor,#72b495) 1x,radial-gradient(circle at 0% 50%,currentcolor,#7ca3c6) 2x);}
                ""","native-image-set",new Uri("file:///css-image-set/"),resolver);if(sheet.Diagnostics.Count>0)throw new InvalidOperationException("CSS diagnostics");Css.GetStyleSheets(root).Add(sheet);
            foreach(var phase in new[]{"1x","2x","updated-1x"})
            {
                var scale=phase=="2x"?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));root.Tag=phase=="1x"?"CSS 图片候选 · 1x":phase=="2x"?"CSS 图片候选 · 2x":"CSS 图片候选 · 更新与返回";
                if(phase=="updated-1x"){Css.SetStyle(root,"--tone:#184b7e;--side:right;");Css.SetStyle(floating,"margin:0 0 8px 12px;");original.Text="更新以后，"+original.Text;}
                body.Select(2,6);Layout(root,width,height);Ready(swatch);Ready(floating);Ready(generated);ShapeReady(floating);Layout(root,width,height);
                foreach(var element in new[]{swatch,floating})DecodeBackground(element);Layout(root,width,height);
                if(root.Children.Count!=3||shape.Children.Count!=2||!ReferenceEquals(body.Inlines[0],original)||body.SelectedText!=body.Text.Substring(2,6))throw new InvalidOperationException("Original source tree/selection changed");
                var lines=LineRects(body);foreach(var line in lines){if(line.Left<0||line.Right>width+.01||line.Bottom>height)throw new InvalidOperationException("Text overflow");if(!ReferenceEquals(root.InputHitTest(new(line.X+line.Width*.5,line.Y+line.Height*.65)),body))throw new InvalidOperationException("Source hit changed");}
                using var view=new NSView(new CGRect(0,0,width,height));using var target=context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle),width*scale,height*scale);target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Requested renderer unavailable");using var drawing=new RenderTargetDrawingContext(target,context);
                if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Readback request failed");target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*height*scale*scale*4];if(target.FetchReadback(pixels,(uint)(width*scale*4),out var w,out var h)!=JaliumResult.Ok||w!=width*scale||h!=height*scale)throw new InvalidOperationException("Readback failed");
                var sourceBounds=Bounds(swatch);var sample=Pixel(sourceBounds.X+10,sourceBounds.Y+10);var expected=scale==2?Color.FromRgb(24,75,126):Color.FromRgb(20,99,66);if(!Close(sample,expected))throw new InvalidOperationException("Wrong density candidate pixel "+sample);
                if(!Close(Pixel(sourceBounds.X+146,sourceBounds.Y+10),Color.FromRgb(244,248,245)))throw new InvalidOperationException("Raster natural density did not preserve 144 CSS px width");
                foreach(var line in lines){var count=0;for(var y=(int)Math.Ceiling(line.Top*scale);y<Math.Min(h,line.Bottom*scale);y++)for(var x=(int)Math.Ceiling(line.Left*scale);x<Math.Min(w,line.Right*scale);x++){var i=(y*(int)w+x)*4;if(pixels[i]<180&&pixels[i+1]<150&&pixels[i+2]<150)count++;if(Math.Abs(pixels[i]-149)<3&&Math.Abs(pixels[i+1]-180)<3&&Math.Abs(pixels[i+2]-114)<3)throw new InvalidOperationException("Opaque candidate intersects text");}if(count<3)throw new InvalidOperationException("Native glyph pixels missing");}
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_IMAGE_SET_EVIDENCE_DIR") is {Length:>0} path){Directory.CreateDirectory(path);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w,(int)h,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,(int)w*4)));using var output=File.Create(Path.Combine(path,$"image-set-{engine}-{width}-{phase}.png"));png.Save(output);}
                Color Pixel(double x,double y){var i=((int)Math.Floor(y*scale)*(int)w+(int)Math.Floor(x*scale))*4;return Color.FromArgb(pixels[i+3],pixels[i+2],pixels[i+1],pixels[i]);}
                passed++;
            }
            if(resolver.Requests.Contains("never-fetch.png")||resolver.Requests.Count!=6)throw new InvalidOperationException("Unselected or repeated image fetch: "+string.Join(',',resolver.Requests));
        }
        Console.WriteLine($"macOS CSS image-set GPU checks: {passed}/12 passed");return passed==12?0:1;
    }
    private static bool Close(Color a,Color b)=>Math.Abs(a.R-b.R)<3&&Math.Abs(a.G-b.G)<3&&Math.Abs(a.B-b.B)<3;
    private static StackPanel Section(string title){var row=new StackPanel{Tag=title};Css.SetClass(row,"section");return row;}
    private static TextBlock Text(string value)=>new(){Text=value};
    private static void Layout(FrameworkElement root,int width,int height){for(var i=0;i<5;i++){Pump();root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
    private static void Pump()=>typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);
    private static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<end){Pump();Thread.Sleep(1);}if(!task.IsCompletedSuccessfully)throw new InvalidOperationException("Image operation failed",task.Exception);Pump();}
    private static void Ready(FrameworkElement element){var property=(DependencyProperty)typeof(FrameworkElement).Assembly.GetType("Jalium.UI.Styling.CssBackgroundImageResource")!.GetField("ActiveProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;foreach(var resource in (Array)element.GetValue(property)!)Wait((Task)resource!.GetType().GetProperty("Ready",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(resource)!);}
    private static void ShapeReady(FrameworkElement element){var property=(DependencyProperty)typeof(FrameworkElement).Assembly.GetType("Jalium.UI.Styling.CssShapeOutsideProperties")!.GetField("ValueProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;var value=element.GetValue(property)!;var resource=value.GetType().GetProperty("Resource",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;Wait((Task)resource.GetType().GetProperty("Ready",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(resource)!);}
    private static void DecodeBackground(Border element){if(element.Background is ImageBrush{ImageSource:BitmapImage image})Wait(image.PreloadAsync(300,300));}
    private static byte[] Png(int width,int height,Func<int,int,Color> color){var pixels=new byte[width*height*4];for(var y=0;y<height;y++)for(var x=0;x<width;x++){var c=color(x,y);var i=(y*width+x)*4;pixels[i]=c.B;pixels[i+1]=c.G;pixels[i+2]=c.R;pixels[i+3]=c.A;}var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));using var stream=new MemoryStream();png.Save(stream);return stream.ToArray();}
    private sealed class Images:ICssResourceResolver{internal readonly Dictionary<string,byte[]> Bytes=[];internal readonly List<string> Requests=[];public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default){var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);return ValueTask.FromResult(new CssResource(uri,new MemoryStream(Bytes[name],writable:false)));}}
    private static Rect Bounds(FrameworkElement e){var x=e.VisualBounds.X;var y=e.VisualBounds.Y;for(var p=VisualTreeHelper.GetParent(e) as FrameworkElement;p is not null;p=VisualTreeHelper.GetParent(p) as FrameworkElement){x+=p.VisualBounds.X;y+=p.VisualBounds.Y;}return new(x,y,e.ActualWidth,e.ActualHeight);}
    private static IReadOnlyList<Rect> LineRects(TextBlock body)
    {
        var segments=(IReadOnlyList<(double StartX,double EndX,double Baseline)>)typeof(TextBlock).GetMethod("GetSourceRangeSegments",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(body,[0,body.Text.Length])!;var lines=((IEnumerable)typeof(TextBlock).GetField("_layoutLines",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(body)!).Cast<object>().Where(line=>(int)line.GetType().GetProperty("Length")!.GetValue(line)!>0).ToArray();if(lines.Length!=segments.Count)throw new InvalidOperationException("Source fragment coverage changed");var origin=Bounds(body);var result=new List<Rect>();for(var i=0;i<lines.Length;i++){var baseline=(double)lines[i].GetType().GetProperty("Baseline")!.GetValue(lines[i])!;var height=(double)lines[i].GetType().GetProperty("Height")!.GetValue(lines[i])!;result.Add(new(origin.X+segments[i].StartX,origin.Y+segments[i].Baseline-baseline,segments[i].EndX-segments[i].StartX,height));}return result;
    }
}
