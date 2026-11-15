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
internal static class CssCyclicPercentageSizingChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_CYCLIC_PERCENT_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 700;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root,"root");
            root.Children.Add(Label("内容与比例共存","title"));root.Children.Add(Label("先测量内容，再按容器比例分配宽度。","description"));
            var group = new StackPanel();Css.SetClass(group,"group");root.Children.Add(group);
            var body = new TextBlock {FontFamily="PingFang SC",FontSize=16,LineHeight=24};var run = new Run("原文记录");body.Inlines.Add(run);Css.SetClass(body,"body");
            var leaf = new Border {Width=200,Height=44,Child=body};var record = new Border {Child=leaf};Css.SetClass(record,"record");group.Children.Add(record);
            var following=Label("后续记录保留在容器下方。","following");root.Children.Add(following);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);
            var percent=Action("比例宽度");var calc=Action("叠加长度");var definite=Action("确定容器");
            actions.Children.Add(percent);actions.Children.Add(calc);actions.Children.Add(definite);
            var status=Label("","status");root.Children.Add(status);
            Css.SetStyleSheet(root,".root{display:block;padding:20px;font-family:'PingFang SC';font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:20px;}.group{display:block;width:min-content;box-sizing:border-box;padding:12px;border:1px solid #9ab8a8;background:#ffffff;}.record{display:block;width:50%;min-width:0;box-sizing:border-box;padding:8px;border:1px solid #146342;background:#eef5f0;}.body{display:block;font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}.following{display:block;line-height:24px;margin-top:16px;}.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:20px;}.action{min-height:44px;padding:8px 10px;border:1px solid #9ab8a8;background:#dceee4;border-radius:8px;box-sizing:border-box;}.action:hover{background:#c9e4d5;}.action:focus-visible{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:24px;margin-top:16px;}");
            var mode="percent";var rtl=false;byte[]? initial=null;
            void Apply()
            {
                Css.SetStyle(group,$"width:{(mode=="definite"?"280px":"min-content")};direction:{(rtl?"rtl":"ltr")};");
                Css.SetStyle(record,"width:"+(mode=="calc"?"calc(50% + 20px)":"50%")+";");
                status.Text=mode=="calc"?"比例分配后叠加长度，原文保持可读。":mode=="definite"?"容器宽度确定后，子级重新计算比例。":"容器按内容测量，子级使用一半宽度。";
            }
            void Wire(PercentAction button,string value)
            {
                button.Activate=()=>{mode=value;Apply();};button.MouseLeftButtonUp+=(_,_)=>button.Activate();
                button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){button.Activate();e.Handled=true;}};
            }
            Wire(percent,"percent");Wire(calc,"calc");Wire(definite,"definite");
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","calc","definite","edited","rtl-2x","restored"})
            {
                mode="percent";rtl=phase=="rtl-2x";leaf.Width=phase=="edited"?240:200;Apply();
                if(phase=="calc")Press(calc,Key.Enter);
                if(phase=="definite")((IInvokeProvider)definite.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")Press(percent,Key.Space);
                var scale=rtl?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));Layout(root,width,height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Percent renderer unavailable");
                using var drawing=new RenderTargetDrawingContext(target,context);if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Percent readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*scale*height*scale*4];if(target.FetchReadback(pixels,(uint)(width*scale*4),out var actualWidth,out var actualHeight)!=JaliumResult.Ok||actualWidth!=width*scale||actualHeight!=height*scale)throw new InvalidOperationException("Percent readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_CYCLIC_PERCENT_EVIDENCE_DIR") is {Length:>0} path)
                {Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth,actualHeight,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,actualWidth*4)));using var file=File.Create(Path.Combine(path,$"percent-{engine}-{width}-{phase}.png"));encoder.Save(file);}
                if(baseline){Console.WriteLine($"Before cyclic percentage: group={group.ActualWidth}, record={record.ActualWidth}");return 0;}
                var expectedGroup=mode=="definite"?280:leaf.Width+44;
                Near(expectedGroup,group.ActualWidth);Near((expectedGroup-26)/2+(mode=="calc"?20:0),record.ActualWidth);
                Near(88,group.ActualHeight);Near(62,record.ActualHeight);
                if(following.TransformToAncestor(root).Y<group.TransformToAncestor(root).Y+group.ActualHeight+15)throw new InvalidOperationException("Following record overlaps");
                if(!ReferenceEquals(run,body.Inlines[0])||!ReferenceEquals(record.Child,leaf)||!ReferenceEquals(leaf.Child,body))throw new InvalidOperationException("Original content replaced");
                foreach(var element in new FrameworkElement[]{group,following,actions,status})
                {var at=element.TransformToAncestor(root);if(at.X<19||at.X+element.ActualWidth>width-19||at.Y+element.ActualHeight>height-8)throw new InvalidOperationException("Percent content outside viewport");}
                foreach(var button in new[]{percent,calc,definite})
                {if(button.ActualHeight<44||!button.Focusable||button.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Percent action semantics differ");}
                if(phase=="initial")initial=pixels;
                if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored percent pixels differ");
                Console.WriteLine($"Cyclic percentage {engine}/{width}/{phase}: group={group.ActualWidth}, record={record.ActualWidth}, height={group.ActualHeight}");passed++;
            }
        }
        Console.WriteLine($"macOS CSS cyclic percentage GPU checks: {passed}/24 passed");return passed==24?0:1;
    }
    private static void Near(double expected,double actual){if(Math.Abs(expected-actual)>.06)throw new InvalidOperationException($"Percent geometry differs: expected={expected}, actual={actual}");}
    private static void Press(PercentAction button,Key key)
    {var args=(KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(RoutedEvent),typeof(Key),typeof(ModifierKeys),typeof(bool),typeof(bool),typeof(int)],null)!.Invoke([UIElement.PreviewKeyDownEvent,key,ModifierKeys.None,true,false,0]);button.RaiseEvent(args);if(!args.Handled)throw new InvalidOperationException("Percent key unhandled");}
    private sealed class PercentAction:Border{internal System.Action? Activate;protected override AutomationPeer OnCreateAutomationPeer()=>new PercentActionPeer(this);}
    private sealed class PercentActionPeer(PercentAction owner):FrameworkElementAutomationPeer(owner),IInvokeProvider
    {protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;protected override string GetClassNameCore()=>"PercentAction";protected override object? GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPatternCore(pattern);public void Invoke()=>owner.Activate?.Invoke();}
    private static TextBlock Label(string words,string css){var text=new TextBlock{Text=words};Css.SetClass(text,css);return text;}
    private static PercentAction Action(string words){var result=new PercentAction{Child=Label(words,""),Focusable=true};Css.SetClass(result,"action");AutomationProperties.SetName(result,words);return result;}
    private static void Layout(FrameworkElement root,int width,int height){for(var pass=0;pass<4;pass++){typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
}
