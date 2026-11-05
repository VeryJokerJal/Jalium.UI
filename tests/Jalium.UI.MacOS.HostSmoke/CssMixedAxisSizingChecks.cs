using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssMixedAxisSizingChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_MIXED_AXIS_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root,"root");
            root.Children.Add(Label("给内容留足空间","title"));root.Children.Add(Label("内容自然展开，后续记录依次排布。","description"));
            var group = new StackPanel();Css.SetClass(group,"group");root.Children.Add(group);
            var body = new TextBlock {FontFamily="PingFang SC",FontSize=16,LineHeight=24};var run = new Run("原文记录始终保留");body.Inlines.Add(run);Css.SetClass(body,"body");
            var leaf = new Border {Height=44,Child=body};var record = new Border {Child=leaf};Css.SetClass(record,"record");group.Children.Add(record);
            var following=Label("下一条记录，随内容向下排列。","following");root.Children.Add(following);
            var gap = new StackPanel();Css.SetClass(gap,"gap");root.Children.Add(gap);
            var first=Label("今天 · 设计记录","gap-item");var second=Label("明天 · 继续整理","gap-item");gap.Children.Add(first);gap.Children.Add(second);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);
            var natural=Action("自然高度");var length=Action("更多留白");var definite=Action("固定高度");actions.Children.Add(natural);actions.Children.Add(length);actions.Children.Add(definite);
            var status=Label("内容自然展开，留白保持稳定。","status");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 16px; }
                .group { display:block;box-sizing:border-box;padding:12px;border:1px solid #d4e3d9;background:#eef5f0;margin:0 0 16px; }
                .record { display:block;box-sizing:border-box;padding:12px;border:1px solid #c5d9cc;background:#ffffff; }
                .body { display:block;font-size:16px;line-height:24px; }
                .following { display:block;margin:0 0 16px; }
                .gap { display:flex;flex-direction:column;align-items:stretch;row-gap:calc(12px + 10%);margin:0 0 16px; }
                .gap-item { display:block;box-sizing:border-box;flex:none;height:40px;padding:8px 12px;background:#eef5f0; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            string mode="natural";bool rtl=false;byte[]? initial=null;
            void Apply()
            {
                Css.SetStyle(root,$"direction:{(rtl?"rtl":"ltr")};");
                Css.SetStyle(group,"height:"+(mode=="definite"?"240px":"auto")+";");
                Css.SetStyle(record,"min-height:calc("+(mode=="length"?120:100)+"px + 50%);");
                Css.SetStyle(gap,"height:"+(mode=="definite"?"140px":"auto")+";");
                status.Text=mode=="length"?"增加留白后，下一条记录仍清晰可见。":mode=="definite"?"固定高度下，内容与间距重新分配。":"内容自然展开，留白保持稳定。";
            }
            void Wire(SizeAction button,string value)
            {
                button.Activate=()=>{mode=value;Apply();};button.MouseLeftButtonUp+=(_,_)=>button.Activate();
                button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){button.Activate();e.Handled=true;}};
            }
            Wire(natural,"natural");Wire(length,"length");Wire(definite,"definite");
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","length","definite","edited","rtl-2x","restored"})
            {
                mode="natural";rtl=phase=="rtl-2x";leaf.Height=phase=="edited"?140:44;Apply();
                if(phase=="length")Press(length,Key.Enter);
                if(phase=="definite")((IInvokeProvider)definite.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")Press(natural,Key.Space);
                var scale=rtl?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));Layout(root,width,height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Mixed-axis renderer unavailable");
                using var drawing=new RenderTargetDrawingContext(target,context);if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Mixed-axis readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*scale*height*scale*4];if(target.FetchReadback(pixels,(uint)(width*scale*4),out var actualWidth,out var actualHeight)!=JaliumResult.Ok||actualWidth!=width*scale||actualHeight!=height*scale)throw new InvalidOperationException("Mixed-axis readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_MIXED_AXIS_EVIDENCE_DIR") is {Length:>0} path)
                {Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth,actualHeight,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,actualWidth*4)));using var file=File.Create(Path.Combine(path,$"mixed-{engine}-{width}-{phase}.png"));encoder.Save(file);}
                if(baseline){Console.WriteLine($"Before mixed-axis: group={group.ActualHeight}, record={record.ActualHeight}, gap={gap.ActualHeight}, second={second.TransformToAncestor(gap).Y}");return 0;}
                var expectedRecord=mode=="definite"?207:phase=="edited"?166:mode=="length"?120:100;
                Near(expectedRecord,record.ActualHeight);Near(mode=="definite"?240:expectedRecord+26,group.ActualHeight);
                Near(mode=="definite"?140:92,gap.ActualHeight);Near(mode=="definite"?66:52,second.TransformToAncestor(gap).Y);
                if(following.TransformToAncestor(root).Y<group.TransformToAncestor(root).Y+group.ActualHeight+15)throw new InvalidOperationException("Following record overlaps");
                if(!ReferenceEquals(run,body.Inlines[0])||!ReferenceEquals(record.Child,leaf)||!ReferenceEquals(leaf.Child,body))throw new InvalidOperationException("Original content replaced");
                foreach(var element in new FrameworkElement[]{group,following,gap,actions,status})
                {var at=element.TransformToAncestor(root);if(at.X<19||at.X+element.ActualWidth>width-19||at.Y+element.ActualHeight>height-8)throw new InvalidOperationException("Mixed-axis content outside viewport");}
                foreach(var button in new[]{natural,length,definite})
                {if(button.ActualHeight<44||!button.Focusable||button.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Mixed-axis action semantics differ");}
                if(AutomationProperties.GetLiveSetting(status)!=AutomationLiveSetting.Polite)throw new InvalidOperationException("Status semantics differ");
                if(phase=="initial")initial=pixels;
                if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored mixed-axis pixels differ");
                Console.WriteLine($"Mixed-axis {engine}/{width}/{phase}: group={group.ActualHeight}, record={record.ActualHeight}, gap={gap.ActualHeight}");passed++;
            }
        }
        Console.WriteLine($"macOS CSS mixed-axis GPU checks: {passed}/24 passed");return passed==24?0:1;
    }
    private static void Near(double expected,double actual){if(Math.Abs(expected-actual)>.06)throw new InvalidOperationException($"Mixed-axis geometry differs: expected={expected}, actual={actual}");}
    private static void Press(SizeAction button,Key key)
    {var args=(KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(RoutedEvent),typeof(Key),typeof(ModifierKeys),typeof(bool),typeof(bool),typeof(int)],null)!.Invoke([UIElement.PreviewKeyDownEvent,key,ModifierKeys.None,true,false,0]);button.RaiseEvent(args);if(!args.Handled)throw new InvalidOperationException("Mixed-axis key unhandled");}
    private sealed class SizeAction:Border{internal System.Action? Activate;protected override AutomationPeer OnCreateAutomationPeer()=>new SizeActionPeer(this);}
    private sealed class SizeActionPeer(SizeAction owner):FrameworkElementAutomationPeer(owner),IInvokeProvider
    {protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;protected override string GetClassNameCore()=>"SizeAction";protected override object? GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPatternCore(pattern);public void Invoke()=>owner.Activate?.Invoke();}
    private static TextBlock Label(string words,string css){var text=new TextBlock{Text=words};Css.SetClass(text,css);return text;}
    private static SizeAction Action(string words){var result=new SizeAction{Child=Label(words,""),Focusable=true};Css.SetClass(result,"action");AutomationProperties.SetName(result,words);return result;}
    private static void Layout(FrameworkElement root,int width,int height){for(var pass=0;pass<4;pass++){typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
}
