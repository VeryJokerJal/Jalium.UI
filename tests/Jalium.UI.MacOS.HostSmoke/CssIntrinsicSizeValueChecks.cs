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
internal static class CssIntrinsicSizeValueChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_SIZING_VALUES_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 700;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root,"root");
            root.Children.Add(Label("宽度随内容变化","title")); root.Children.Add(Label("切换内容尺寸，原文与操作保持可用。","description"));
            var host = new StackPanel(); Css.SetClass(host,"host"); root.Children.Add(host);
            var group = new StackPanel(); Css.SetClass(group,"group"); host.Children.Add(group);
            var firstText = Body("设计稿"); var secondText = Body("实现中");
            var first = new Border {Child=firstText}; var second = new Border {Child=secondText};
            Css.SetClass(first,"record first");Css.SetClass(second,"record second");group.Children.Add(first);group.Children.Add(second);
            var note=Body("后续记录保留在内容下方。");Css.SetClass(note,"body note");root.Children.Add(note);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);
            var minimum=Action("最小宽度"); var fit=Action("适应宽度"); var restore=Action("最大宽度");
            actions.Children.Add(minimum);actions.Children.Add(fit);actions.Children.Add(restore);
            var status=Label("","status");root.Children.Add(status);
            Css.SetStyleSheet(root,".root{display:block;padding:20px;font-family:'PingFang SC';font-size:16px;color:#233c32;}.title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}.description{display:block;line-height:1.6;margin-bottom:20px;}.host{display:block;}.group{display:flex;flex-wrap:wrap;align-items:flex-start;gap:12px;width:max-content;}.record{display:block;flex:none;height:64px;min-width:0;padding:8px;border:1px solid #146342;background:#eef5f0;box-sizing:border-box;}.first{width:168px;}.second{width:112px;}.body{display:block;font-family:'PingFang SC';font-size:16px;line-height:24px;white-space:pre-wrap;text-align:start;}.note{margin-top:16px;}.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:20px;}.action{min-height:44px;padding:8px 10px;border:1px solid #9ab8a8;background:#dceee4;border-radius:8px;box-sizing:border-box;}.action:hover{background:#c9e4d5;}.action:focus-visible{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:24px;margin-top:16px;}");
            var mode="max-content";var rtl=false;byte[]? initial=null;
            void Apply()
            {
                Css.SetStyle(host,$"width:{(mode=="fit-content"?190:width-40)}px;");
                Css.SetStyle(group,$"width:{mode};direction:{(rtl?"rtl":"ltr")};");
                status.Text=mode=="min-content"?"按最小内容宽度排列。":mode=="fit-content"?"适应可用宽度，内容自动换行。":mode=="stretch"?"填满可用宽度，原文顺序保持不变。":"按最大内容宽度排列。";
            }
            void Wire(SizingAction button,string value)
            {
                button.Activate=()=>{mode=value;Apply();};button.MouseLeftButtonUp+=(_,_)=>button.Activate();
                button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){button.Activate();e.Handled=true;}};
            }
            Wire(minimum,"min-content");Wire(fit,"fit-content");Wire(restore,"max-content");
            var originalRuns=new[]{firstText.Inlines[0],secondText.Inlines[0],note.Inlines[0]};
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","minimum","fit","stretch","rtl-2x","restored"})
            {
                mode="max-content";rtl=phase=="rtl-2x";Apply();
                if(phase=="minimum")Press(minimum,Key.Enter);
                if(phase=="fit")((IInvokeProvider)fit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="stretch"){mode="stretch";Apply();}
                if(phase=="restored")Press(restore,Key.Space);
                var scale=rtl?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));Layout(root,width,height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Sizing renderer unavailable");
                using var drawing=new RenderTargetDrawingContext(target,context);if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Sizing readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*scale*height*scale*4];if(target.FetchReadback(pixels,(uint)(width*scale*4),out var actualWidth,out var actualHeight)!=JaliumResult.Ok||actualWidth!=width*scale||actualHeight!=height*scale)throw new InvalidOperationException("Sizing readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_SIZING_VALUES_EVIDENCE_DIR") is {Length:>0} path)
                {Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth,actualHeight,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,actualWidth*4)));using var file=File.Create(Path.Combine(path,$"sizing-{engine}-{width}-{phase}.png"));encoder.Save(file);}
                if(baseline){Console.WriteLine($"Before sizing: width={group.ActualWidth}, height={group.ActualHeight}");return 0;}
                var expected=mode=="min-content"?168:mode=="fit-content"?190:mode=="stretch"?width-40:292;
                Near(expected,group.ActualWidth);Near(mode is "min-content" or "fit-content"?140:64,group.ActualHeight);
                Near(168,first.ActualWidth);Near(112,second.ActualWidth);
                if(note.TransformToAncestor(root).Y<group.TransformToAncestor(root).Y+group.ActualHeight+15)throw new InvalidOperationException("Following record overlaps");
                if(!ReferenceEquals(originalRuns[0],firstText.Inlines[0])||!ReferenceEquals(originalRuns[1],secondText.Inlines[0])||!ReferenceEquals(originalRuns[2],note.Inlines[0]))throw new InvalidOperationException("Original text was replaced");
                foreach(var element in new FrameworkElement[]{host,group,note,actions,status})
                {var at=element.TransformToAncestor(root);if(at.X<19||at.X+element.ActualWidth>width-19||at.Y+element.ActualHeight>height-8)throw new InvalidOperationException("Sizing content outside viewport");}
                foreach(var button in new[]{minimum,fit,restore})
                {if(button.ActualHeight<44||!button.Focusable||button.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Sizing action semantics differ");}
                if(phase=="initial")initial=pixels;
                if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored sizing pixels differ");
                Console.WriteLine($"Sizing values {engine}/{width}/{phase}: width={group.ActualWidth}, height={group.ActualHeight}, following={note.TransformToAncestor(root).Y}");passed++;
            }
        }
        Console.WriteLine($"macOS CSS intrinsic size value GPU checks: {passed}/24 passed");return passed==24?0:1;
    }
    private static void Near(double expected,double actual){if(Math.Abs(expected-actual)>.06)throw new InvalidOperationException($"Sizing geometry differs: expected={expected}, actual={actual}");}
    private static void Press(SizingAction button,Key key)
    {var args=(KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(RoutedEvent),typeof(Key),typeof(ModifierKeys),typeof(bool),typeof(bool),typeof(int)],null)!.Invoke([UIElement.PreviewKeyDownEvent,key,ModifierKeys.None,true,false,0]);button.RaiseEvent(args);if(!args.Handled)throw new InvalidOperationException("Sizing key unhandled");}
    private sealed class SizingAction:Border{internal System.Action? Activate;protected override AutomationPeer OnCreateAutomationPeer()=>new SizingActionPeer(this);}
    private sealed class SizingActionPeer(SizingAction owner):FrameworkElementAutomationPeer(owner),IInvokeProvider
    {protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;protected override string GetClassNameCore()=>"SizingAction";protected override object? GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPatternCore(pattern);public void Invoke()=>owner.Activate?.Invoke();}
    private static TextBlock Body(string words){var text=new TextBlock{FontFamily="PingFang SC",FontSize=16,LineHeight=24};text.Inlines.Add(new Run(words));Css.SetClass(text,"body");return text;}
    private static TextBlock Label(string words,string css){var text=new TextBlock{Text=words};Css.SetClass(text,css);return text;}
    private static SizingAction Action(string words){var result=new SizingAction{Child=Label(words,""),Focusable=true};Css.SetClass(result,"action");AutomationProperties.SetName(result,words);return result;}
    private static void Layout(FrameworkElement root,int width,int height){for(var pass=0;pass<4;pass++){typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
}
