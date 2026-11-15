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
internal static class CssInlineBlockDescendantChecks
{
    private sealed record Row(StackPanel Host,Border Frame,StackPanel Content,TextBlock Lead,TextBlock Before,TextBlock Body,TextBlock After,TextBlock Tail,Run Original,FrameworkElement Block,TextBlock? Intro,Border? Float,string Mode);
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if(!context.IsValid||context.Backend!=RenderBackend.Metal)throw new InvalidOperationException("Metal unavailable");
        var passed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{375,720})
        {
            const int height=1400;
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");var rows=new List<Row>();
            foreach(var (mode,title) in new[]{("single","行内祖先 · 块级正文"),("consecutive","相邻块级内容 · 合并外边距"),("export","块内浮动 · 后续正文环绕"),("pseudo","TextBlock · 块级伪元素")})
            {
                var row=new StackPanel{Tag=title};Css.SetClass(row,"row");root.Children.Add(row);
                var lead=new TextBlock{Text="外层文字："};Css.SetClass(lead,"inline-source");row.Children.Add(lead);
                var before=new TextBlock{Text="行内前段"};Css.SetClass(before,"inline-source");
                var body=new TextBlock{IsTextSelectionEnabled=true};Css.SetClass(body,mode is "single" or "consecutive"?"body block":"body inline-source"+(mode=="pseudo"?" own":""));
                var original=new Run("块级后代使用外层段落的宽度，保留原始文字和选择，");body.Inlines.Add(original);
                body.Inlines.Add(new Run("原始 Run 与命中归属") {Foreground=new SolidColorBrush(Color.FromRgb(20,76,142)),FontWeight=FontWeights.Bold});
                body.Inlines.Add(new Run(" 始终不变。"));Css.GetAttributes(body)["data-status"]="初始内容";
                var after=new TextBlock{Text="行内后段"};Css.SetClass(after,"inline-source");
                var content=new StackPanel();Css.SetClass(content,"inline-content");content.Children.Add(before);
                FrameworkElement block=body;TextBlock? intro=null;Border? floating=null;
                if(mode=="consecutive")
                {
                    intro=new TextBlock{Text="第一个块级子级"};Css.SetClass(intro,"intro");content.Children.Add(intro);
                    var whitespace=new TextBlock{Text="  "};Css.SetClass(whitespace,"inline-source");content.Children.Add(whitespace);Css.SetStyle(body,"margin-top: 20px;");
                }
                if(mode=="export")
                {
                    var label=new TextBlock{Text="侧栏"};floating=new Border{Child=label};Css.SetClass(floating,"float");
                    var exporter=new Border{Child=floating};Css.SetClass(exporter,"exporter");content.Children.Add(exporter);block=exporter;
                }
                content.Children.Add(body);content.Children.Add(after);
                var frame=new Border{Child=content};Css.SetClass(frame,"frame");row.Children.Add(frame);
                var tail=new TextBlock{Text=" 〔初始状态〕"};Css.SetClass(tail,"inline-source");row.Children.Add(tail);
                rows.Add(new(row,frame,content,lead,before,body,after,tail,original,block,intro,floating,mode));
            }
            var sheet=CssStyleSheet.Parse("""
                .root { display: block; padding: 20px; font-family: 'PingFang SC', sans-serif; font-size: 16px; color: #172f26; }
                .root::before { content: 'CSS 行内祖先与块级后代'; display: block; font-size: 24px; color: #146342; margin-bottom: 18px; }
                .row { display: flow-root; padding: 14px; margin-bottom: 18px; border: 1px solid #abcdbd; background: #f4f8f5; line-height: 1.65; white-space: normal; }
                .row::before { content: attr(Tag); display: block; font-size: 18px; color: #146342; margin-bottom: 8px; }
                .inline-source, .inline-content, .frame { display: inline; }
                .frame { border: 1px solid #468467; border-radius: 4px; padding: 0 5px; background: #def0e5; }
                .block { display: block; padding: 8px; border: 1px solid #7299b5; background: #ffffff; }
                .intro { display: block; height: 28px; margin-bottom: 12px; color: #235c7c; background: #e1eef5; }
                .exporter { display: block; }
                .float { float: var(--side, left); width: 50px; height: 55px; padding: 5px; margin: 0 8px 5px 0; border: 1px solid #468467; background: #c8e4d4; color: #146342; }
                .own::before { content: '块级标签'; display: var(--label-display, block); height: 28px; color: #235c7c; background: #e1eef5; }
                .own::after { content: ' / ' attr(data-status); display: inline; color: #32674f; }
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
                    if(phase!="initial"){row.Tail.Text=" 〔更新状态〕";Css.GetAttributes(row.Body)["data-status"]="更新内容";}
                    if(phase=="updated")
                    {
                        row.Original.Text="更新正文以后，"+row.Original.Text;
                        if(row.Mode=="single"){Css.SetStyle(row.Body,"display: inline; padding: 0 4px;");row.Host.Tag="块级正文已切换为行内";}
                        if(row.Mode=="consecutive")Css.SetStyle(row.Body,"margin-top: 8px;");
                        if(row.Float is not null)Css.SetStyle(row.Float,"--side: right;");
                        if(row.Mode=="pseudo"){Css.SetStyle(row.Body,"--label-display: inline;");row.Host.Tag="伪元素已切换为行内";}
                    }
                    row.Body.Select(2,6);
                }
                root.Measure(new Size(0,height));root.Measure(new Size(double.PositiveInfinity,height));root.Measure(new Size(width+97,height));
                for(var i=0;i<5;i++){root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
                var ink=new List<Rect>();var whiteChecks=new List<Point>();
                foreach(var row in rows)
                {
                    if(!ReferenceEquals(row.Body.Inlines[0],row.Original)||row.Body.Inlines.Count!=3||row.Body.SelectedText!=row.Body.Text.Substring(2,6))throw new InvalidOperationException("Source or selection changed");
                    if(row.Host.Children.Count!=3||!ReferenceEquals(row.Frame.Child,row.Content)||!ReferenceEquals(VisualTreeHelper.GetParent(row.Body),row.Content))throw new InvalidOperationException("Original native tree changed");
                    foreach(var text in new[]{row.Lead,row.Before,row.Body,row.After,row.Tail})
                    foreach(var rect in LineRects(text))
                    {
                        if(rect.X<-.01||rect.Y<-.01||rect.Right>width+.01||rect.Bottom>height+.01)throw new InvalidOperationException($"Text overflow {row.Mode}: {rect}");
                        var point=new Point(rect.X+rect.Width*.3,rect.Y+rect.Height*.65);var hit=root.InputHitTest(point);
                        if(!ReferenceEquals(hit,text))throw new InvalidOperationException($"Source hit {row.Mode}/{text.Text}: {(hit as TextBlock)?.Text}, point={point}");ink.Add(rect);
                        if(row.Float is not null&&!ReferenceEquals(text,row.Lead)&&!ReferenceEquals(text,row.Before))
                        {
                            var obstacle=Bounds(row.Float);
                            if(rect.IntersectsWith(obstacle)&&Math.Min(rect.Right,obstacle.Right)-Math.Max(rect.X,obstacle.X)>.01&&Math.Min(rect.Bottom,obstacle.Bottom)-Math.Max(rect.Y,obstacle.Y)>.01)throw new InvalidOperationException("Exported float overlap");
                        }
                    }
                    if(row.Mode=="consecutive"||row.Mode=="single"&&phase!="updated")
                    {
                        var bounds=Bounds(row.Body);var host=Bounds(row.Host);var expected=host.Width-30;
                        if(Math.Abs(bounds.X-host.X-15)>.01||Math.Abs(bounds.Width-expected)>.01)throw new InvalidOperationException("Block used an inline ancestor's width");
                        if(bounds.Y+.01<LineRects(row.Before)[^1].Bottom)throw new InvalidOperationException("Block failed to split its preceding paragraph");
                        whiteChecks.Add(new Point(bounds.X+3,bounds.Y+bounds.Height*.5));
                    }
                    if(row.Intro is not null)
                    {
                        var gap=Bounds(row.Body).Y-Bounds(row.Intro).Bottom;
                        if(Math.Abs(gap-(phase=="updated"?12:20))>.01)throw new InvalidOperationException($"Block margin collapse: {gap}");
                        ink.AddRange(LineRects(row.Intro));
                    }
                    if(row.Float is not null)
                    {
                        var b=Bounds(row.Float);ink.Add(new Rect(b.X+7,b.Y+7,b.Width-14,b.Height-14));
                        if(Math.Abs(row.Block.ActualHeight)>.01)throw new InvalidOperationException("Exported float incorrectly contributed to its normal block height");
                    }
                }
                if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("GPU readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*height*4];if(target.FetchReadback(pixels,(uint)(width*4),out var w,out var h)!=JaliumResult.Ok||w!=width||h!=height)throw new InvalidOperationException("GPU readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_BLOCK_EVIDENCE_DIR") is {Length:>0} path)
                {
                    Directory.CreateDirectory(path);var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(path,$"inline-block-descendants-{engine}-{width}-{phase}.png"));encoder.Save(output);
                }
                foreach(var point in whiteChecks){var p=((int)point.Y*width+(int)point.X)*4;if(pixels[p]<240||pixels[p+1]<240||pixels[p+2]<240)throw new InvalidOperationException("Inline ancestor decoration painted over the block interior");}
                foreach(var bounds in ink)
                {
                    var count=0;for(var y=Math.Max(0,(int)bounds.Y);y<Math.Min(height,Math.Ceiling(bounds.Bottom));y++)for(var x=Math.Max(0,(int)bounds.X);x<Math.Min(width,Math.Ceiling(bounds.Right));x++){var p=(y*width+x)*4;if(pixels[p]<180&&pixels[p+1]<150&&pixels[p+2]<150)count++;}
                    if(count<3)throw new InvalidOperationException("Native source glyphs missing");
                }
                passed++;
            }
        }
        Console.WriteLine($"macOS CSS inline block descendant GPU checks: {passed}/12 passed");return passed==12?0:1;
    }
    private static IReadOnlyList<Rect> LineRects(TextBlock source)
    {
        var segments = (IReadOnlyList<(double StartX, double EndX, double Baseline)>)typeof(TextBlock)
            .GetMethod("GetSourceRangeSegments", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(source, [0, source.Text.Length])!;
        var lines = ((IEnumerable)typeof(TextBlock).GetField("_layoutLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!).Cast<object>()
            .Where(line => (int)line.GetType().GetProperty("Length")!.GetValue(line)! > 0).ToArray();
        if (segments.Count != lines.Length) throw new InvalidOperationException("Unexpected source fragment coverage");
        var origin = Bounds(source); var result = new List<Rect>();
        for (var i = 0; i < lines.Length; i++)
        {
            var baseline = (double)lines[i].GetType().GetProperty("Baseline")!.GetValue(lines[i])!;
            var height = (double)lines[i].GetType().GetProperty("Height")!.GetValue(lines[i])!;
            result.Add(new Rect(origin.X + segments[i].StartX, origin.Y + segments[i].Baseline - baseline,
                segments[i].EndX - segments[i].StartX, height));
        }
        return result;
    }
    private static Rect Bounds(FrameworkElement element)
    {
        var x = element.VisualBounds.X; var y = element.VisualBounds.Y;
        for (var parent = VisualTreeHelper.GetParent(element) as FrameworkElement; parent is not null; parent = VisualTreeHelper.GetParent(parent) as FrameworkElement)
        { x += parent.VisualBounds.X; y += parent.VisualBounds.Y; }
        return new(x, y, element.ActualWidth, element.ActualHeight);
    }
}
