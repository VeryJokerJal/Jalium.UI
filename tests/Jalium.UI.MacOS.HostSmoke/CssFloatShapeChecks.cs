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
internal static class CssFloatShapeChecks
{
    private sealed record Row(StackPanel Host,Border Float,TextBlock Body,Run Original,string Mode);
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if(!context.IsValid||context.Backend!=RenderBackend.Metal)throw new InvalidOperationException("Metal unavailable");
        var passed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{375,720})
        {
            const int height=1180;
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");var rows=new List<Row>();
            foreach(var (mode,title,contour,side) in new[]{
                ("circle","圆形轮廓 · 左侧浮动","circle(50%)","left"),
                ("triangle","三角形 · 整行避让","polygon(0 0,100% 100%,0 100%)","left"),
                ("concave","凹多边形 · 圆形间距","path('M0 0 H96 L38 48 L96 96 H0 Z')","left"),
                ("ellipse","椭圆 · 右侧浮动","ellipse(50% 40%)","right")})
            {
                var row=new StackPanel{Tag=title};Css.SetClass(row,"row");root.Children.Add(row);
                var floating=new Border();Css.SetClass(floating,"float");Css.SetStyle(floating,$"--contour:{contour};--side:{side};margin:{(side=="right"?"0 0 8px 12px":"0 12px 8px 0")};");row.Children.Add(floating);
                var body=new TextBlock{IsTextSelectionEnabled=true};Css.SetClass(body,"body");
                var original=new Run("文字沿着浮动图形的实际轮廓排列，每一行都会保留完整的字形空间。窄窗口中继续换行，宽窗口中恢复舒展的段落。");body.Inlines.Add(original);
                body.Inlines.Add(new Run("原始 Run 与选择范围") {Foreground=new SolidColorBrush(Color.FromRgb(24,75,126)),FontWeight=FontWeights.Bold});
                body.Inlines.Add(new Run("在动态更新后依然保留，点击文字也会命中原来的控件。"));row.Children.Add(body);
                rows.Add(new(row,floating,body,original,mode));
            }
            var sheet=CssStyleSheet.Parse("""
                .root { display:block; padding:22px; color:#233c32; font-size:16px; }
                .root::before { content:'CSS 浮动形状环绕'; display:block; font-size:24px; color:#146342; margin-bottom:18px; }
                .row { display:flow-root; padding:14px; margin-bottom:16px; border:1px solid #abcdbd; background:#f4f8f5; line-height:1.65; white-space:normal; }
                .row::before { content:attr(Tag); display:block; font-size:18px; color:#146342; margin-bottom:10px; }
                .float { float:var(--side); width:96px; height:96px; margin:0 12px 8px 0; background:#72b495; clip-path:var(--contour); shape-outside:var(--contour) border-box; shape-margin:var(--gap,8px); }
                .body { display:inline; }
                """);
            if(sheet.Diagnostics.Count>0)throw new InvalidOperationException("CSS diagnostics");Css.GetStyleSheets(root).Add(sheet);
            using var view=new NSView(new CGRect(0,0,width,height));
            using var target=context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle),width,height);
            target.SetDpi(96,96);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Requested renderer unavailable");
            using var drawing=new RenderTargetDrawingContext(target,context);
            foreach(var phase in new[]{"initial","same-width","updated"})
            {
                foreach(var row in rows)
                {
                    if(phase=="same-width")row.Original.Text="内容更新后，"+row.Original.Text;
                    if(phase=="updated")
                    {
                        var contour=row.Mode switch {
                            "circle"=>"inset(8% round 35%)",
                            "triangle"=>"polygon(100% 0,0 100%,100% 100%)",
                            "concave"=>"shape(from 100% 0,line to 0 0,line to 60% 50%,line to 0 100%,line to 100% 100%,close)",
                            _=>"ellipse(40% 50%)"};
                        var side=row.Mode=="ellipse"?"left":"right";
                        Css.SetStyle(row.Float,$"--contour:{contour};--side:{side};--gap:12px;margin:{(side=="right"?"0 0 8px 12px":"0 12px 8px 0")};");
                        row.Host.Tag=row.Mode switch {"circle"=>"更新后 · 圆角矩形","triangle"=>"更新后 · 右侧三角形","concave"=>"更新后 · 响应式路径",_=>"更新后 · 左侧椭圆"};
                    }
                    row.Body.Select(2,6);
                }
                root.Measure(new Size(0,height));root.Measure(new Size(double.PositiveInfinity,height));root.Measure(new Size(width+97,height));
                for(var i=0;i<5;i++){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
                var ink=new List<Rect>();
                foreach(var row in rows)
                {
                    if(!ReferenceEquals(row.Body.Inlines[0],row.Original)||row.Body.Inlines.Count!=3||row.Body.SelectedText!=row.Body.Text.Substring(2,6))throw new InvalidOperationException("Source or selection changed");
                    if(row.Host.Children.Count!=2||!ReferenceEquals(VisualTreeHelper.GetParent(row.Body),row.Host))throw new InvalidOperationException("Original native tree changed");
                    var boxes=LineRects(row.Body);var floating=Bounds(row.Float);
                    if(phase=="initial"&&row.Mode=="triangle"&&Math.Abs(boxes[0].X-floating.X-floating.Width)<.1)throw new InvalidOperationException("Triangle fell back to a rectangular exclusion");
                    foreach(var rect in boxes)
                    {
                        if(rect.X<-.01||rect.Y<-.01||rect.Right>width+.01||rect.Bottom>height+.01)throw new InvalidOperationException($"Text overflow {row.Mode}: {rect}");
                        var point=new Point(rect.X+rect.Width*.3,rect.Y+rect.Height*.65);var hit=root.InputHitTest(point);
                        if(!ReferenceEquals(hit,row.Body))throw new InvalidOperationException($"Source hit {row.Mode}: {(hit as TextBlock)?.Text}, point={point}");ink.Add(rect);
                    }
                }
                if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*height*4];if(target.FetchReadback(pixels,(uint)(width*4),out var w,out var h)!=JaliumResult.Ok||w!=width||h!=height)throw new InvalidOperationException("GPU readback failed");
                var path=Environment.GetEnvironmentVariable("JALIUM_CSS_FLOAT_SHAPE_EVIDENCE_DIR");
                if(!string.IsNullOrEmpty(path))
                {
                    Directory.CreateDirectory(path);var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output=File.Create(Path.Combine(path,$"float-shapes-{engine}-{width}-{phase}.png"));encoder.Save(output);
                }
                foreach(var bounds in ink)
                {
                    var count=0;var green=0;
                    for(var y=Math.Max(0,(int)bounds.Y);y<Math.Min(height,Math.Ceiling(bounds.Bottom));y++)
                    for(var x=Math.Max(0,(int)bounds.X);x<Math.Min(width,Math.Ceiling(bounds.Right));x++)
                    {
                        var p=(y*width+x)*4;if(pixels[p]<180&&pixels[p+1]<150&&pixels[p+2]<150)count++;
                        if(Math.Abs(pixels[p]-149)<4&&Math.Abs(pixels[p+1]-180)<4&&Math.Abs(pixels[p+2]-114)<4)green++;
                    }
                    if(count<3)throw new InvalidOperationException("Native source glyphs missing");
                    if(green>0)throw new InvalidOperationException("Painted float shape intersects a source line");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS float shape GPU checks: {passed}/12 passed");return passed==12?0:1;
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
