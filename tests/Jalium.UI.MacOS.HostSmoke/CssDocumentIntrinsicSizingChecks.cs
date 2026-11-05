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
internal static class CssDocumentIntrinsicSizingChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_INTRINSIC_BASELINE") == "1";
        var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root,"root");
            root.Children.Add(Label("原文也有自己的留白","title"));root.Children.Add(Label("文档段落随内容调整，下一条记录依次排布。","description"));
            var run=new Run("原文始终保留\n让段落按自己的节奏展开");var paragraph=new Paragraph(run);var following=new Paragraph(new Run("下一条记录\n继续整理今天的新想法。"));
            var record=new Section(paragraph);Css.SetClass(record,"record");var group=new Section(record);Css.SetClass(group,"group");Css.SetClass(following,"following");
            var document=new FlowDocument{FontFamily="PingFang SC",FontSize=16,LineHeight=24,PagePadding=new(0),PageWidth=width-40,PageHeight=760};document.Blocks.Add(group);document.Blocks.Add(following);
            Css.SetStyleSheet(document,"""
                FlowDocument { color:#233c32;font-size:16px;line-height:24px;white-space:pre-wrap; }
                Paragraph,Section { margin:0;padding:0;box-sizing:border-box; }
                .group { height:calc-size(auto,size + 24px);background:#eef5f0;margin:0 0 16px; }
                .record { padding:12px;border:1px solid #c5d9cc;background:#ffffff; }
                .following { padding:12px;background:#eef5f0; }
                @keyframes extra-space { from { height:calc-size(auto,size + 24px); } to { height:calc-size(auto,size + 72px); } }
                """);
            using var body=new FlowDocumentPresentation();body.SetDocument(document);Css.SetClass(body,"document");root.Children.Add(body);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);
            var natural=Action("自然留白");var roomy=Action("更多留白");var restore=Action("恢复原文");actions.Children.Add(natural);actions.Children.Add(roomy);actions.Children.Add(restore);
            var status=Label("原文已展开，段落留白随内容调整。","status");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 16px; }
                .document { display:block;margin:0 0 16px; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            string mode="natural";bool rtl=false;byte[]? initial=null;DependencyProperty? animated=null;
            void Apply()
            {
                if(animated is not null){typeof(DependencyObject).GetMethod("ClearCssAnimatedValue",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(group,[animated]);animated=null;}
                Css.SetStyle(root,$"direction:{(rtl?"rtl":"ltr")};");Css.SetStyle(document,$"direction:{(rtl?"rtl":"ltr")};");
                Css.SetStyle(group,mode=="roomy"?"height:calc-size(auto,size + 72px);":"height:calc-size(auto,size + 24px);");
                status.Text=mode=="roomy"?"段落留白已增加，原文保持完整。":"原文已展开，段落留白随内容调整。";
            }
            void Wire(SizeAction button,string value)
            {
                button.Activate=()=>{mode=value;Apply();};button.MouseLeftButtonUp+=(_,_)=>button.Activate();
                button.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){button.Activate();e.Handled=true;}};
            }
            Wire(natural,"natural");Wire(roomy,"roomy");Wire(restore,"natural");
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","half","roomy","edited","rtl-2x","restored"})
            {
                mode="natural";rtl=phase=="rtl-2x";
                run.Text=phase=="edited"?"原文始终保留\n让段落按自己的节奏展开\n补充一行新的观察\n也给下一条记录留足空间":"原文始终保留\n让段落按自己的节奏展开";Apply();
                if(phase=="half"){Layout(root,width,height);animated=SampleHeight(group,.5);status.Text="留白正在增加，原文位置保持稳定。";}
                if(phase=="roomy")Press(roomy,Key.Enter);
                if(phase=="edited")((IInvokeProvider)natural.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")Press(restore,Key.Space);
                var scale=rtl?2:1;VisualTreeHelper.SetRootDpi(root,new(scale,scale));Layout(root,width,height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96*scale,96*scale);target.SetRenderingEngine(engine);if(target.RenderingEngine!=engine)throw new InvalidOperationException("Document intrinsic renderer unavailable");
                using var drawing=new RenderTargetDrawingContext(target,context);if(target.RequestReadback()!=JaliumResult.Ok)throw new InvalidOperationException("Document intrinsic readback request failed");
                target.SetFullInvalidation();target.BeginDraw();target.Clear(1,1,1,1);try{root.Render(drawing);}finally{target.EndDraw();}
                var pixels=new byte[width*scale*height*scale*4];if(target.FetchReadback(pixels,(uint)(width*scale*4),out var actualWidth,out var actualHeight)!=JaliumResult.Ok||actualWidth!=width*scale||actualHeight!=height*scale)throw new InvalidOperationException("Document intrinsic readback failed");
                if(Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_INTRINSIC_EVIDENCE_DIR") is {Length:>0} path)
                {Directory.CreateDirectory(path);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(actualWidth,actualHeight,96*scale,96*scale,PixelFormats.Bgra32,null,pixels,actualWidth*4)));using var file=File.Create(Path.Combine(path,$"document-intrinsic-{engine}-{width}-{phase}.png"));encoder.Save(file);}
                var second=body.Paragraphs[1].TransformToAncestor(body).Y;
                if(baseline){Console.WriteLine($"Before document intrinsic: next={second}, extent={body.ExtentHeight}");return 0;}
                var expected=phase=="half"?122:phase=="roomy"||phase=="edited"?146:98;
                Near(expected+28,second);
                if(!ReferenceEquals(run,paragraph.Inlines[0])||!ReferenceEquals(record,paragraph.Parent)||!ReferenceEquals(group,record.Parent))throw new InvalidOperationException("Original document content replaced");
                foreach(var element in new FrameworkElement[]{body,actions,status})
                {var at=element.TransformToAncestor(root);if(at.X<19||at.X+element.ActualWidth>width-19||at.Y+element.ActualHeight>height-8)throw new InvalidOperationException("Document content outside viewport");}
                foreach(var button in new[]{natural,roomy,restore})
                {if(button.ActualHeight<44||!button.Focusable||button.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(button.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Document intrinsic action semantics differ");}
                if(AutomationProperties.GetLiveSetting(status)!=AutomationLiveSetting.Polite)throw new InvalidOperationException("Status semantics differ");
                if(phase=="initial")initial=pixels;
                if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored calc-size pixels differ");
                Console.WriteLine($"Document intrinsic {engine}/{width}/{phase}: next={second}, extent={body.ExtentHeight}");passed++;
            }
        }
        Console.WriteLine($"macOS CSS calc-size GPU checks: {passed}/24 passed");return passed==24?0:1;
    }
    private static DependencyProperty SampleHeight(DependencyObject element,double progress)
    {
        const BindingFlags stat=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        var assembly=typeof(Css).Assembly;var node=assembly.GetType("Jalium.UI.Styling.CssNode")!.GetMethod("Get",stat)!.Invoke(null,[element]);
        var keyframes=assembly.GetType("Jalium.UI.Styling.CssKeyframes")!;var rule=keyframes.GetMethod("Find",stat)!.Invoke(null,[node,"extra-space"]);
        var samplers=(System.Collections.IDictionary)keyframes.GetMethod("BuildSamplers",stat)!.Invoke(null,[node,rule])!;
        var property=samplers.Keys.Cast<DependencyProperty>().Single(p=>p.Name=="CssSizingHeight");var sampler=samplers[property]!;
        var timing=Activator.CreateInstance(assembly.GetType("Jalium.UI.Styling.CssTimingFunction")!,["linear",0d,0d,1d,1d,1,"jump-end"]);
        var sample=sampler.GetType().GetMethod("Sample",BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(double),timing!.GetType()],null)!.Invoke(sampler,[progress,timing]);
        typeof(DependencyObject).GetMethod("SetCssAnimatedValue",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(element,[property,sample]);return property;
    }
    private static void Near(double expected,double actual){if(Math.Abs(expected-actual)>.06)throw new InvalidOperationException($"Document intrinsic geometry differs: expected={expected}, actual={actual}");}
    private static void Press(SizeAction button,Key key)
    {var args=(KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(RoutedEvent),typeof(Key),typeof(ModifierKeys),typeof(bool),typeof(bool),typeof(int)],null)!.Invoke([UIElement.PreviewKeyDownEvent,key,ModifierKeys.None,true,false,0]);button.RaiseEvent(args);if(!args.Handled)throw new InvalidOperationException("Document intrinsic key unhandled");}
    private sealed class SizeAction:Border{internal System.Action? Activate;protected override AutomationPeer OnCreateAutomationPeer()=>new SizeActionPeer(this);}
    private sealed class SizeActionPeer(SizeAction owner):FrameworkElementAutomationPeer(owner),IInvokeProvider
    {protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Button;protected override string GetClassNameCore()=>"SizeAction";protected override object? GetPatternCore(PatternInterface pattern)=>pattern==PatternInterface.Invoke?this:base.GetPatternCore(pattern);public void Invoke()=>owner.Activate?.Invoke();}
    private static TextBlock Label(string words,string css){var text=new TextBlock{Text=words};Css.SetClass(text,css);return text;}
    private static SizeAction Action(string words){var result=new SizeAction{Child=Label(words,""),Focusable=true};Css.SetClass(result,"action");AutomationProperties.SetName(result,words);return result;}
    private static void Layout(FrameworkElement root,int width,int height){for(var pass=0;pass<4;pass++){typeof(Dispatcher).GetMethod("ProcessQueue",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher,null);root.Measure(new(width,height));root.Arrange(new(0,0,width,height));root.UpdateLayout();}}
}
