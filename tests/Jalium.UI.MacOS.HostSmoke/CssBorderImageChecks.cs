using System.Reflection;
using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssBorderImageChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        if(!context.IsValid||context.Backend!=RenderBackend.Metal)throw new InvalidOperationException("Metal unavailable");
        var passed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{375,720})
        {
            const int height=1100;
            var resolver=new Images();var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");
            var title=new TextBlock{Text="图片边框"};Css.SetClass(title,"title");root.Children.Add(title);
            var description=new TextBlock{Text="九宫格保留四角，边缘按各自规则缩放与平铺；中心填充位于正文后面。"};Css.SetClass(description,"description");root.Children.Add(description);
            var boxes=new List<Border>();
            foreach(var mode in new[]{"stretch","repeat","round","space"})
            {
                var text=new TextBlock{Text=mode switch{"stretch"=>"拉伸 · 四角保留，四边延展", "repeat"=>"重复 · 中央对齐，末端允许裁切", "round"=>"整铺 · 调整图块宽度，铺满整数块", _=>"留空 · 图块前后与间隔保持相同空隙"},IsTextSelectionEnabled=true};
                var box=new Border{Child=text};Css.SetClass(box,"example "+mode);boxes.Add(box);root.Children.Add(box);
            }
            var adaptive=new Border{Child=new TextBlock{Text="密度切换 · 自然尺寸与百分比切片",IsTextSelectionEnabled=true}};Css.SetClass(adaptive,"example adaptive");root.Children.Add(adaptive);
            var failed=new Border{Child=new TextBlock{Text="读取失败 · 保留普通绿色边框",IsTextSelectionEnabled=true}};Css.SetClass(failed,"example failed");root.Children.Add(failed);
            var footer=new TextBlock{Text="边框外扩只增加绘制范围，不改变布局与点命中。恢复声明后，正文与选择保留。"};Css.SetClass(footer,"description");root.Children.Add(footer);
            var sheet=CssStyleSheet.Parse("""
                .root{display:block;padding:24px;color:#233c32;font-size:16px;line-height:1.6;white-space:normal;}
                .title{display:block;font-size:26px;color:#146342;margin-bottom:10px;}
                .description{display:block;margin-bottom:20px;white-space:normal;}
                .example{display:block;height:102px;box-sizing:border-box;margin:0 6px 20px;padding:12px;background:white;border:10px solid #146342;border-image:url('nine.png') 33.333333333333336% fill / 10px / 4px;}
                .stretch{border-image-repeat:stretch;}
                .repeat{border-image-repeat:repeat;}
                .round{border-image-repeat:round;}
                .space{border-image-repeat:space;}
                .adaptive{border-image-source:image-set('nine.png' 1x,'high.png' 2x);border-image-repeat:round;}
                .failed{border-image-source:url('bad.png');}
                ""","native-border-image",new Uri("file:///css-border-image/"),resolver);
            if(sheet.Diagnostics.Count!=0)throw new InvalidOperationException(string.Join(';',sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            foreach(var phase in new[]{"pending-1x","loaded-1x","loaded-2x","restored-1x"})
            {
                var scale=phase=="loaded-2x"?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));
                if(phase!="pending-1x")resolver.Open();
                if(phase=="restored-1x")Css.SetStyle(boxes[0],"border:6px solid #146342;border-image:none;");
                title.Text=phase switch{"pending-1x"=>"图片边框 · 加载中…","loaded-1x"=>"图片边框 · 1×","loaded-2x"=>"图片边框 · 2×",_=>"图片边框 · 声明恢复"};
                Layout(root,width,height);if(phase!="pending-1x")Ready(root,width,height);
                var firstText=(TextBlock)boxes[0].Child!;firstText.Select(0,2);
                if(firstText.SelectedText!="拉伸")throw new InvalidOperationException("Border-image changed text selection");
                using var view=new NSView(new CGRect(0,0,width,height));
                using var target=context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle),width*scale,height*scale);
                target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);
                if(target.RenderingEngine!=engine)throw new InvalidOperationException("Requested renderer unavailable");
                using var drawing=new RenderTargetDrawingContext(target,context);
                if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);
                try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*height*scale*scale*4];
                if(target.FetchReadback(pixels,(uint)(width*scale*4),out var w,out var h)!=JaliumResult.Ok||w!=width*scale||h!=height*scale)throw new InvalidOperationException("Readback failed");
                Color Pixel(double x,double y){var at=((int)(y*scale)*(int)w+(int)(x*scale))*4;return Color.FromArgb(pixels[at+3],pixels[at+2],pixels[at+1],pixels[at]);}
                foreach(var box in boxes.Append(adaptive).Append(failed))
                {
                    var point=box.TransformToAncestor(root);var bounds=new Rect(point.X,point.Y,box.ActualWidth,box.ActualHeight);
                    if(bounds.Bottom>height||bounds.Left<4||bounds.Right>width-4)throw new InvalidOperationException("Border box outside viewport");
                    var expected=phase=="pending-1x"||ReferenceEquals(box,failed)||phase=="restored-1x"&&ReferenceEquals(box,boxes[0])?Color.FromRgb(20,99,66):
                        scale==2&&ReferenceEquals(box,adaptive)?Color.FromRgb(156,102,23):Color.FromRgb(24,75,126);
                    if(!Close(Pixel(point.X+2,point.Y+2),expected))throw new InvalidOperationException("Incorrect corner pixel: "+Pixel(point.X+2,point.Y+2)+" expected "+expected);
                    if(phase!="pending-1x"&&!ReferenceEquals(box,failed)&&!ReferenceEquals(box,boxes[3]))
                    {
                        var center=phase=="restored-1x"&&ReferenceEquals(box,boxes[0])?Colors.White:Color.FromRgb(220,238,228);
                        var actual=Pixel(point.X+bounds.Width*.85,point.Y+bounds.Height-18);
                        if(!Close(actual,center))throw new InvalidOperationException("Neighboring source cell leaked into center fill: "+actual+" expected "+center);
                        if(ReferenceEquals(box,boxes[0])&&phase!="restored-1x")
                        {
                            var edge=Pixel(point.X+8,point.Y+bounds.Height-18);
                            if(!Close(edge,center))throw new InvalidOperationException("Filtering outside a sliced image bled into its center edge: "+edge+" expected "+center);
                        }
                    }
                    if(!ReferenceEquals(root.InputHitTest(new(point.X+2,point.Y+2)),box))throw new InvalidOperationException("Border input ownership changed");
                }
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_BORDER_IMAGE_EVIDENCE_DIR") is {Length:>0} path)
                {
                    Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w,(int)h,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,(int)w*4)));
                    using var output=File.Create(Path.Combine(path,$"border-image-{engine}-{width}-{phase}.png"));encoder.Save(output);
                }
                passed++;
            }
            if(resolver.Requests.Count!=4)throw new InvalidOperationException("Repeated or unexpected URL reads: "+string.Join(',',resolver.Requests));
        }
        Console.WriteLine($"macOS CSS border-image GPU checks: {passed}/16 passed");return passed==16?0:1;
    }

    private static void Ready(FrameworkElement root,int width,int height)
    {
        var assembly=typeof(FrameworkElement).Assembly;
        var property=(DependencyProperty)assembly.GetType("Jalium.UI.Styling.CssBorderImageProperties")!.GetField("ResourcesProperty",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        var node=assembly.GetType("Jalium.UI.Styling.CssNode")!.GetMethod("Get",BindingFlags.Static|BindingFlags.Public)!;
        IEnumerable<FrameworkElement> Walk(FrameworkElement owner){yield return owner;for(var i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++)if(VisualTreeHelper.GetChild(owner,i) is FrameworkElement child)foreach(var nested in Walk(child))yield return nested;}
        for(var round=0;round<3;round++)
        {
            Layout(root,width,height);
            foreach(var owner in Walk(root))if(owner.GetValue(property) is Array resources)foreach(var resource in resources)
            {
                Wait((Task)resource!.GetType().GetProperty("Ready",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(resource)!,true);
                if(resource.GetType().GetMethod("Get",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(resource,[node.Invoke(null,[owner]),false]) is BitmapImage bitmap)Wait(bitmap.PreloadAsync(400,200),false);
            }
        }
        Layout(root,width,height);
    }
    private static void Layout(FrameworkElement root,int width,int height){for(var i=0;i<5;i++){Pump();root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
    private static void Pump()=>typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);
    private static void Wait(Task task,bool requireSuccess){var end=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<end){Pump();Thread.Sleep(1);}if(!task.IsCompleted||requireSuccess&&!task.IsCompletedSuccessfully)throw new InvalidOperationException("Image operation failed",task.Exception);Pump();}
    private static bool Close(Color a,Color b)=>Math.Abs(a.R-b.R)<3&&Math.Abs(a.G-b.G)<3&&Math.Abs(a.B-b.B)<3;
    private static byte[] Nine(int size,bool high)
    {
        var blue=Color.FromRgb(24,75,126);var green=Color.FromRgb(20,99,66);var light=Color.FromRgb(220,238,228);var gold=Color.FromRgb(156,102,23);
        Color[] colors=[high?gold:blue,green,blue,green,light,green,blue,green,blue];var pixels=new byte[size*size*4];
        for(var y=0;y<size;y++)for(var x=0;x<size;x++)
        {var column=Math.Min(2,x*3/size);var row=Math.Min(2,y*3/size);var color=colors[row*3+column];if((row==1) != (column==1)&&((x+y)/3)%2==0)color=light;var at=(y*size+x)*4;pixels[at]=color.B;pixels[at+1]=color.G;pixels[at+2]=color.R;pixels[at+3]=255;}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,pixels,size*4)));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    private sealed class Images:ICssResourceResolver
    {
        internal readonly List<string> Requests=[];private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open()=>_gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);await _gate.Task;return new(uri,new MemoryStream(name=="bad.png"?[1,2,3]:Nine(name=="high.png"?60:30,name=="high.png")));}
    }
}
