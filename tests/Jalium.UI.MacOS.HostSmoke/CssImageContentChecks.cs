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
internal static class CssImageContentChecks
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
            var resolver=new Images();
            resolver.Bytes["low.png"]=Png(64,32,Color.FromRgb(20,99,66));
            resolver.Bytes["high.png"]=Png(128,64,Color.FromRgb(24,75,126));
            resolver.Bytes["inline.png"]=Png(28,18,Color.FromRgb(20,99,66));
            resolver.Bytes["bullet.png"]=Png(18,12,Color.FromRgb(20,99,66));
            resolver.Bytes["bad.png"]=[1,2,3];
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");
            var mixedSection=Section("文字、计数器与行内图片");
            var mixed=new TextBlock{IsTextSelectionEnabled=true,Tag="图示"};Css.SetClass(mixed,"mixed");
            var source=new Run("原始正文仍然可以选择。生成内容与图片共享行盒，窄窗口会自然换行，源文本和 Run 对象保持原样。");mixed.Inlines.Add(source);mixedSection.Children.Add(mixed);root.Children.Add(mixedSection);
            var listSection=Section("列表图片与失败回退");
            var outside=List("outside","图片标记对齐正文的首行",2);listSection.Children.Add(outside);
            var inside=List("inside","行内标记参与文字换行与命中",2);listSection.Children.Add(inside);
            var failed=new StackPanel();Css.SetClass(failed,"failed");failed.Children.Add(Text("无效图片使用十进制标记。"));listSection.Children.Add(failed);root.Children.Add(listSection);
            var replacementSection=Section("普通元素的图片替换");
            var replacement=new TextBlock{Text="原来的正文已恢复，选择范围和原始内容仍然保留。",IsTextSelectionEnabled=true};Css.SetClass(replacement,"replacement");
            replacementSection.Children.Add(replacement);replacementSection.Children.Add(Text("图片使用自然比例；恢复 content:normal 后重新显示原来的正文。"));root.Children.Add(replacementSection);
            var gradientSection=Section("渐变内容与当前颜色");var gradient=Text("生成图片随当前文字颜色更新。");Css.SetClass(gradient,"gradient");gradientSection.Children.Add(gradient);root.Children.Add(gradientSection);
            var sheet=CssStyleSheet.Parse("""
                .root{display:block;padding:20px;color:#233c32;font-size:16px;line-height:1.6;white-space:normal;counter-reset:chapter 7;}
                .root::before{content:attr(Tag);display:block;font-size:24px;color:#146342;margin-bottom:18px;}
                .section{display:block;padding:12px;margin-bottom:14px;border:1px solid #abcdbd;background:#f4f8f5;white-space:normal;}
                .section::before{content:attr(Tag);display:block;font-size:18px;color:#146342;margin-bottom:10px;}
                .mixed{display:block;white-space:normal;}
                .mixed::before{content:'第' counter(chapter) '节 ' url('inline.png') attr(Tag) '：混合内容在同一段落里排版。 ' / '第' counter(chapter) '节图示';color:#146342;}
                .list{display:block;padding-left:28px;margin-bottom:8px;list-style:decimal url('bullet.png');}
                .inside{list-style-position:inside;}
                .row{display:list-item;margin-bottom:3px;}
                .row-text{display:inline;white-space:normal;}
                .failed{display:list-item;margin-left:28px;list-style:decimal url('bad.png');}
                .replacement{content:image-set('low.png' 1x,'high.png' 2x) / '品牌图片';box-sizing:content-box;width:128px;height:64px;padding:8px;border:1px solid #abcdbd;border-radius:12px;object-fit:contain;object-position:center;margin-bottom:10px;}
                .gradient::before{content:linear-gradient(to right,currentcolor,#9fd2b6);display:block;width:80px;height:24px;margin-bottom:8px;}
                ""","native-image-content",new Uri("file:///css-image-content/"),resolver);
            if(sheet.Diagnostics.Count!=0)throw new InvalidOperationException(string.Join(';',sheet.Diagnostics));
            Css.GetStyleSheets(root).Add(sheet);
            foreach(var phase in new[]{"pending-1x","loaded-1x","loaded-2x","restored-1x"})
            {
                var scale=phase=="loaded-2x"?2:1;
                VisualTreeHelper.SetRootDpi(root,new(scale,scale));
                if(phase!="pending-1x")resolver.Open();
                if(phase=="restored-1x")
                {
                    Css.SetStyle(replacement,"content:normal;width:auto;height:auto;padding:0;border:0;");
                    Css.SetStyle(gradient,"color:#184b7e;");
                }
                root.Tag=phase switch{"pending-1x"=>"图片内容 · 加载中","loaded-1x"=>"图片内容 · 1×","loaded-2x"=>"图片内容 · 2×",_=>"图片内容 · 原内容恢复"};
                Layout(root,width,height);
                if(phase!="pending-1x")Ready(root,width,height,phase=="restored-1x"?6:7);
                mixed.Select(2,6);replacement.Select(1,4);
                if(root.Children.Count!=4||mixed.Inlines.Count!=1||!ReferenceEquals(mixed.Inlines[0],source)||mixed.SelectedText!=source.Text.Substring(2,6))throw new InvalidOperationException("Authored content changed");
                if(replacement.Text!="原来的正文已恢复，选择范围和原始内容仍然保留。"||replacement.SelectedText!=replacement.Text.Substring(1,4))throw new InvalidOperationException("Replacement changed original text or selection");
                if(replacement.GetAutomationPeer()!.GetName()!=(phase=="restored-1x"?replacement.Text:"品牌图片"))throw new InvalidOperationException("Replacement accessible name did not update");
                var images=ImagesIn(root).Where(image=>Bool(image,"Valid")).ToArray();
                foreach(var image in images)
                {
                    var bounds=Bounds(image);
                    if(bounds.Right>width+.01||bounds.Bottom>height||bounds.Left<0||bounds.Top<0)throw new InvalidOperationException("Image outside viewport");
                    if(root.InputHitTest(new(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2)) is not FrameworkElement hit||hit.GetType().Name.StartsWith("CssGenerated",StringComparison.Ordinal))throw new InvalidOperationException("Private formatting box escaped input hit mapping");
                }
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
                if(phase is "loaded-1x" or "loaded-2x")
                {
                    var image=ImagesIn(replacement).Single();var bounds=Bounds(image);var actual=Pixel(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
                    var expected=scale==2?Color.FromRgb(24,75,126):Color.FromRgb(20,99,66);
                    if(!Close(actual,expected))throw new InvalidOperationException("Wrong content candidate pixel: "+actual);
                    if(replacement.ActualWidth!=146||replacement.ActualHeight!=82)throw new InvalidOperationException("Replacement content box changed");
                }
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_IMAGE_CONTENT_EVIDENCE_DIR") is {Length:>0} path)
                {
                    Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w,(int)h,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,(int)w*4)));
                    using var output=File.Create(Path.Combine(path,$"image-content-{engine}-{width}-{phase}.png"));encoder.Save(output);
                }
                Color Pixel(double x,double y){var i=((int)(y*scale)*(int)w+(int)(x*scale))*4;return Color.FromArgb(pixels[i+3],pixels[i+2],pixels[i+1],pixels[i]);}
                passed++;
            }
            if(resolver.Requests.Count!=5)throw new InvalidOperationException("Unexpected or repeated resource fetches: "+string.Join(',',resolver.Requests));
        }
        Console.WriteLine($"macOS CSS image content GPU checks: {passed}/16 passed");return passed==16?0:1;
    }

    private static StackPanel Section(string title){var section=new StackPanel{Tag=title};Css.SetClass(section,"section");return section;}
    private static TextBlock Text(string value)=>new(){Text=value};
    private static StackPanel List(string position,string text,int count)
    {
        var list=new StackPanel();Css.SetClass(list,"list "+position);
        for(var i=0;i<count;i++){var row=new StackPanel();Css.SetClass(row,"row");var body=Text(text+"。");Css.SetClass(body,"row-text");row.Children.Add(body);list.Children.Add(row);}return list;
    }
    private static bool Bool(FrameworkElement owner,string name)=>(bool)owner.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    private static IEnumerable<FrameworkElement> ImagesIn(Visual root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            if(VisualTreeHelper.GetChild(root,i) is FrameworkElement child)
            {
                if(child.GetType().Name=="CssGeneratedImage")yield return child;
                foreach(var image in ImagesIn(child))yield return image;
            }
    }
    private static void Ready(FrameworkElement root,int width,int height,int count)
    {
        var stop=DateTime.UtcNow.AddSeconds(12);
        while(DateTime.UtcNow<stop)
        {
            Layout(root,width,height);
            var images=ImagesIn(root).ToArray();
            if(images.Length>=count&&images.All(image=>Bool(image,"Valid")))
            {
                foreach(var image in images)
                    if(image.GetType().GetField("_brush",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(image) is ImageBrush{ImageSource:BitmapImage bitmap})Wait(bitmap.PreloadAsync(300,150));
                Layout(root,width,height);return;
            }
            Thread.Sleep(1);
        }
        throw new InvalidOperationException("Content images did not become ready: "+ImagesIn(root).Count());
    }
    private static void Layout(FrameworkElement root,int width,int height){for(var i=0;i<5;i++){Pump();root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
    private static void Pump()=>typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);
    private static void Wait(Task task){var end=DateTime.UtcNow.AddSeconds(10);while(!task.IsCompleted&&DateTime.UtcNow<end){Pump();Thread.Sleep(1);}if(!task.IsCompletedSuccessfully)throw new InvalidOperationException("Decode failed",task.Exception);Pump();}
    private static bool Close(Color a,Color b)=>Math.Abs(a.R-b.R)<3&&Math.Abs(a.G-b.G)<3&&Math.Abs(a.B-b.B)<3;
    private static Rect Bounds(FrameworkElement element){var point=element.TransformToAncestor(VisualTreeHelper.GetRoot(element) as Visual??element);return new(point.X,point.Y,element.ActualWidth,element.ActualHeight);}
    private static byte[] Png(int width,int height,Color color)
    {
        var pixels=new byte[width*height*4];for(var i=0;i<pixels.Length;i+=4){pixels[i]=color.B;pixels[i+1]=color.G;pixels[i+2]=color.R;pixels[i+3]=255;}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));
        using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    private sealed class Images:ICssResourceResolver
    {
        internal readonly Dictionary<string,byte[]> Bytes=[];internal readonly List<string> Requests=[];
        private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Open()=>_gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);await _gate.Task;return new(uri,new MemoryStream(Bytes[name],writable:false));}
    }
}
