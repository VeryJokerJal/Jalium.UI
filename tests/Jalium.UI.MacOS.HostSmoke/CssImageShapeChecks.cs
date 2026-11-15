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
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssImageShapeChecks
{
    private sealed record Row(StackPanel Host,FrameworkElement Float,TextBlock Body,Run Original,string Mode);
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if(!context.IsValid||context.Backend!=RenderBackend.Metal)throw new InvalidOperationException("Metal unavailable");
        var passed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{375,720})
        {
            const int height=1240;
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");var rows=new List<Row>();var resolver=new Images();
            var svg="<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><rect width='96' height='96' rx='24' fill='#72b495'/></svg>";
            var glow=Bitmap((x,y)=>{var d=Math.Sqrt((x-43)*(x-43)+(y-48)*(y-48));return d<31?(byte)255:d<43?(byte)90:(byte)0;});
            var notch=Bitmap((x,y)=>x<(y>=24&&y<72?30:78)?(byte)255:(byte)0);
            var reversed=Bitmap((x,y)=>x>=(y>=24&&y<72?66:18)?(byte)255:(byte)0);
            resolver.Bytes["glow.png"]=Encode(glow);resolver.Bytes["notch.png"]=Encode(notch);resolver.Bytes["reversed.png"]=Encode(reversed);resolver.Bytes["shape.svg"]=System.Text.Encoding.UTF8.GetBytes(svg);
            foreach(var (mode,title) in new[]{("glow","PNG · 半透明轮廓"),("notch","透明缺口 · 更换图片"),("gradient","径向渐变 · 透明度阈值"),("svg","SVG · 内容盒与圆角边框")})
            {
                var row=new StackPanel{Tag=title};Css.SetClass(row,"row");root.Children.Add(row);FrameworkElement floating;
                if(mode=="gradient")floating=new Border();
                else floating=new Image{Source=mode=="glow"?glow:mode=="notch"?notch:SvgImage.FromSvgString(svg)};
                Css.SetClass(floating,"float "+mode);row.Children.Add(floating);
                var body=new TextBlock{IsTextSelectionEnabled=true};Css.SetClass(body,"body");var original=new Run("图片的透明区域决定文字可以经过的位置，半透明边缘通过阈值控制。窄窗口中继续换行，宽窗口中保留舒展的段落。");body.Inlines.Add(original);
                body.Inlines.Add(new Run("原始 Run 与选择范围"){Foreground=new SolidColorBrush(Color.FromRgb(24,75,126)),FontWeight=FontWeights.Bold});body.Inlines.Add(new Run("在异步加载与样式更新后依然保留。"));row.Children.Add(body);rows.Add(new(row,floating,body,original,mode));
            }
            var sheet=CssStyleSheet.Parse("""
                .root {display:block;padding:22px;color:#233c32;font-size:16px;}
                .root::before {content:'CSS 图片透明度环绕';display:block;font-size:24px;color:#146342;margin-bottom:18px;}
                .row {display:flow-root;padding:14px;margin-bottom:16px;border:1px solid #abcdbd;background:#f4f8f5;line-height:1.65;white-space:normal;}
                .row::before {content:attr(Tag);display:block;font-size:18px;color:#146342;margin-bottom:10px;}
                .float {float:var(--side,left);width:96px;height:96px;margin:0 12px 8px 0;object-fit:fill;shape-margin:6px;shape-image-threshold:var(--cutoff,0);}
                .glow {shape-outside:url('glow.png');}
                .notch {shape-outside:var(--mask,url('notch.png'));}
                .gradient {background-image:var(--paint,radial-gradient(circle at 50% 50%,#72b495 34px,transparent 42px));shape-outside:var(--paint,radial-gradient(circle at 50% 50%,#72b495 34px,transparent 42px));}
                .svg {shape-outside:url('shape.svg');shape-margin:12px;border:3px solid #146342;padding:3px;border-radius:30px;}
                .body {display:inline;}
                ""","native-image-shapes",new Uri("file:///css-image-shapes/"),resolver);
            if(sheet.Diagnostics.Count>0)throw new InvalidOperationException("CSS diagnostics");Css.GetStyleSheets(root).Add(sheet);
            using var view=new NSView(new CGRect(0,0,width,height));using var target=context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle),width,height);
            target.SetDpi(96,96);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Requested renderer unavailable");using var drawing=new RenderTargetDrawingContext(target,context);
            foreach(var phase in new[]{"pending","loaded","updated"})
            {
                if(phase=="loaded")resolver.Open();
                foreach(var row in rows)
                {
                    if(phase=="loaded")row.Host.Tag=row.Mode switch{"glow"=>"PNG · 已提取半透明轮廓","notch"=>"透明缺口 · 原始图片","gradient"=>"径向渐变 · 原始透明度",_=>"SVG · 已按内容盒解码"};
                    if(phase=="updated")
                    {
                        row.Original.Text="更新以后，"+row.Original.Text;
                        var side=row.Mode=="gradient"?"left":"right";var style=$"--side:{side};--cutoff:.5;margin:{(side=="right"?"0 0 8px 12px":"0 12px 8px 0")};";
                        if(row.Mode=="notch"){style+="--mask:url('reversed.png');";((Image)row.Float).Source=reversed;}
                        if(row.Mode=="gradient")style+="--paint:conic-gradient(#72b495 0deg 180deg,transparent 180deg 360deg);";
                        Css.SetStyle(row.Float,style);row.Host.Tag=row.Mode switch{"glow"=>"阈值 0.5 · 忽略淡色边缘","notch"=>"图片已更换 · 右侧环绕","gradient"=>"锥形渐变 · 动态更新",_=>"SVG · 右侧内容盒环绕"};
                    }
                    row.Body.Select(2,6);
                }
                for(var i=0;i<3;i++){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
                if(phase!="pending")WaitForImages(rows);
                root.Measure(new Size(0,height));root.Measure(new Size(double.PositiveInfinity,height));root.Measure(new Size(width+97,height));
                for(var i=0;i<5;i++){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
                var ink=new List<Rect>();
                foreach(var row in rows)
                {
                    if(row.Host.Children.Count!=2||!ReferenceEquals(VisualTreeHelper.GetParent(row.Body),row.Host)||row.Body.Inlines.Count!=3||!ReferenceEquals(row.Body.Inlines[0],row.Original))throw new InvalidOperationException("Original source tree changed");
                    if(row.Body.SelectedText!=row.Body.Text.Substring(2,6))throw new InvalidOperationException("Original selection offsets changed");
                    var lines=LineRects(row.Body);var floating=Bounds(row.Float);
                    if(phase=="pending"&&row.Mode!="gradient"&&lines[0].X<floating.Right+11.99)throw new InvalidOperationException("Pending image did not preserve rectangular float behavior");
                    if(phase=="loaded"&&row.Mode=="notch"&&lines[0].X>=floating.Right+11.99)throw new InvalidOperationException("Decoded PNG did not reduce its float area");
                    foreach(var rect in lines)
                    {
                        if(rect.X<0||rect.Y<0||rect.Right>width+.01||rect.Bottom>height+.01)throw new InvalidOperationException("Text overflow: "+rect);
                        var hit=root.InputHitTest(new(rect.X+rect.Width*.3,rect.Y+rect.Height*.65));if(!ReferenceEquals(hit,row.Body))throw new InvalidOperationException("Source point hit changed: "+row.Mode);ink.Add(rect);
                    }
                }
                if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("GPU readback request failed");target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*height*4];if(target.FetchReadback(pixels,(uint)(width*4),out var w,out var h)!=JaliumResult.Ok||w!=width||h!=height)throw new InvalidOperationException("GPU readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_IMAGE_SHAPE_EVIDENCE_DIR") is {Length:>0} path)
                {Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));using var output=File.Create(Path.Combine(path,$"image-shapes-{engine}-{width}-{phase}.png"));encoder.Save(output);}
                foreach(var rect in ink)
                {
                    var count=0;var overlaps=0;for(var y=Math.Max(0,(int)rect.Y);y<Math.Min(height,Math.Ceiling(rect.Bottom));y++)for(var x=Math.Max(0,(int)rect.X);x<Math.Min(width,Math.Ceiling(rect.Right));x++){var p=(y*width+x)*4;if(pixels[p]<180&&pixels[p+1]<150&&pixels[p+2]<150)count++;if(Math.Abs(pixels[p]-149)<3&&Math.Abs(pixels[p+1]-180)<3&&Math.Abs(pixels[p+2]-114)<3)overlaps++;}
                    if(count<3)throw new InvalidOperationException("Native glyph pixels missing");if(overlaps>0)throw new InvalidOperationException("Opaque image pixels intersect source line");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS image shape GPU checks: {passed}/12 passed");return passed==12?0:1;
    }
    private static void WaitForImages(IEnumerable<Row> rows)
    {
        var assembly=typeof(FrameworkElement).Assembly;var property=(DependencyProperty)assembly.GetType("Jalium.UI.Styling.CssShapeOutsideProperties")!.GetField("ValueProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        foreach(var row in rows)
        {
            var value=row.Float.GetValue(property);var resource=value?.GetType().GetProperty("Resource",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(value);if(resource is null)continue;
            var task=(Task)resource.GetType().GetProperty("Ready",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(resource)!;if(!task.Wait(TimeSpan.FromSeconds(10)))throw new InvalidOperationException("Shape decode timeout");
        }
        typeof(Jalium.UI.Threading.Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Jalium.UI.Threading.Dispatcher.CurrentDispatcher,null);
    }
    private static BitmapSource Bitmap(Func<int,int,byte> alpha){var data=new byte[96*96*4];for(var y=0;y<96;y++)for(var x=0;x<96;x++){var i=(y*96+x)*4;data[i]=149;data[i+1]=180;data[i+2]=114;data[i+3]=alpha(x,y);}return BitmapSource.Create(96,96,96,96,PixelFormats.Bgra32,null,data,384);}
    private static byte[] Encode(BitmapSource image){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var output=new MemoryStream();encoder.Save(output);return output.ToArray();}
    private sealed class Images:ICssResourceResolver
    {
        internal readonly Dictionary<string,byte[]> Bytes=[];private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open()=>_gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default){await _gate.Task;return new(uri,new MemoryStream(Bytes[Path.GetFileName(uri.LocalPath)],writable:false));}
    }
    private static IReadOnlyList<Rect> LineRects(TextBlock source)
    {
        var segments=(IReadOnlyList<(double StartX,double EndX,double Baseline)>)typeof(TextBlock).GetMethod("GetSourceRangeSegments",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(source,[0,source.Text.Length])!;
        var lines=((IEnumerable)typeof(TextBlock).GetField("_layoutLines",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(source)!).Cast<object>().Where(line=>(int)line.GetType().GetProperty("Length")!.GetValue(line)!>0).ToArray();
        if(segments.Count!=lines.Length)throw new InvalidOperationException("Unexpected source fragment coverage");
        var origin=Bounds(source);var result=new List<Rect>();
        for(var i=0;i<lines.Length;i++)
        {
            var baseline=(double)lines[i].GetType().GetProperty("Baseline")!.GetValue(lines[i])!;var height=(double)lines[i].GetType().GetProperty("Height")!.GetValue(lines[i])!;
            result.Add(new(origin.X+segments[i].StartX,origin.Y+segments[i].Baseline-baseline,segments[i].EndX-segments[i].StartX,height));
        }
        return result;
    }
    private static Rect Bounds(FrameworkElement element)
    {
        var x=element.VisualBounds.X;var y=element.VisualBounds.Y;
        for(var parent=VisualTreeHelper.GetParent(element) as FrameworkElement;parent is not null;parent=VisualTreeHelper.GetParent(parent) as FrameworkElement){x+=parent.VisualBounds.X;y+=parent.VisualBounds.Y;}
        return new(x,y,element.ActualWidth,element.ActualHeight);
    }
}
