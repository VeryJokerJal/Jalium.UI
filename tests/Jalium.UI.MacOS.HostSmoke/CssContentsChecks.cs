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
using System.Text.Json;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssContentsChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);Keyboard.Initialize();
        var baseline=Environment.GetEnvironmentVariable("JALIUM_CSS_CONTENTS_BASELINE")=="1";var records=new List<object>();var failed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var mode in new[]{"block","flex","grid","table","inline","text","generated"})
        foreach(var width in new[]{320,720})
        {
            var actual=Scene(mode,true,width);var reference=Scene(mode,false,width);Layout(actual,width,240);Layout(reference,width,240);
            var pixels=Capture(context,actual,engine,width,240,1);var expected=Capture(context,reference,engine,width,240,1);var equal=pixels.SequenceEqual(expected);
            var sourceRoot=(StackPanel)actual.Children[0];var sourceGroup=(FrameworkElement)sourceRoot.Children[1];var sourceGraph=ReferenceEquals(sourceGroup.VisualParent,sourceRoot)&&Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(sourceRoot)).Any(i=>ReferenceEquals(VisualTreeHelper.GetChild(sourceRoot,i),sourceGroup));
            var cleanAfterPaint=!sourceGroup.IsSubtreeDirty;var edited=sourceGroup is Panel p?(UIElement)p.Children[0]:sourceGroup;edited.InvalidateVisual();var childDirtyReachesRoot=sourceRoot.IsSubtreeDirty;
            var repaint=Capture(context,actual,engine,width,240,1);var repaintStable=repaint.SequenceEqual(pixels);if(!equal||!baseline&&(!sourceGraph||!cleanAfterPaint||!childDirtyReachesRoot||!repaintStable))failed++;
            Save(pixels,width,240,$"contents-{engine}-{mode}-{width}.png");Save(expected,width,240,$"reference-{engine}-{mode}-{width}.png");records.Add(new{engine=engine.ToString(),mode,width,equal,sourceGraph,cleanAfterPaint,childDirtyReachesRoot,repaintStable});
        }
        if(!baseline) Reader(context);Write("native.json",new{baseline,count=records.Count,failed,records});return baseline?0:failed==0?0:1;
    }
    private static Canvas Scene(string mode,bool wrapped,int width)
    {
        var root=new StackPanel();var canvas=new Canvas{Background=Brushes.White};canvas.Children.Add(root);Canvas.SetLeft(root,20);Canvas.SetTop(root,20);
        Css.SetStyle(root,$"display:{(mode is "inline" or "text" or "generated"?"block":mode)};width:{width-40}px;gap:8px;grid-template-columns:100px 100px;border-spacing:0;font-family:'PingFang SC';font-size:18px;line-height:28px;color:#233c32;");
        TextBlock Text(string text){var t=new TextBlock{Text=text};Css.SetStyle(t,mode is "inline" or "text" or "generated"?"display:inline;":"display:block;");return t;}
        FrameworkElement Item(string text)
        {
            if(mode is "inline" or "text" or "generated")return Text(text);
            var b=new Border{Child=Text(text)};Css.SetStyle(b,(mode=="table"?"display:table-cell;":"display:block;")+"box-sizing:border-box;flex:none;width:100px;height:48px;padding:8px;background:#dceee4;");return b;
        }
        root.Children.Add(Item("沿途"));
        if(wrapped)
        {
            FrameworkElement group;
            if(mode=="text")group=Text("风景与发现");
            else {var p=new StackPanel();p.Children.Add(Item("风景"));p.Children.Add(Item("发现"));group=p;}
            Css.SetClass(group,"elided");root.Children.Add(group);Css.SetStyle(group,"display:contents;width:900px;height:800px;margin:30px;padding:20px;border:10px solid red;background:red;overflow:hidden;opacity:0;transform:translate(90px,70px);clip-path:inset(100%);");
            if(mode=="generated")Css.SetStyleSheet(root,".elided::before{content:'记';display:contents;background:red;padding:30px;} .elided::after{content:'录';display:inline;}");
        }
        else
        {
            if(mode=="generated")root.Children.Add(Text("记"));
            if(mode=="text")root.Children.Add(Text("风景与发现"));else {root.Children.Add(Item("风景"));root.Children.Add(Item("发现"));}
            if(mode=="generated")root.Children.Add(Text("录"));
        }
        root.Children.Add(Item("继续"));return canvas;
    }
    private static void Reader(RenderContext context)
    {
        var records=new List<object>();
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in new[]{320,375,720})
        {
            var height=width==320?420:640;var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");
            root.Children.Add(Label("把注意力留给内容","title"));root.Children.Add(Label("切换阅读方式，保留每一条记录。","description"));
            var group=new StackPanel();Css.SetClass(group,"group");root.Children.Add(group);
            var one=Label("清晨走过熟悉的小路，发现路边的叶子已经悄悄变了颜色。","entry");var two=Label("把沿途的细节写下来，留给下次出发时重读。","entry");group.Children.Add(one);group.Children.Add(two);
            root.Children.Add(Label("下一段旅程，仍然值得期待。","entry"));
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);var toggle=Action("显示分组",0);var edit=Action("补写记录",1);var restore=Action("恢复原文",2);actions.Children.Add(toggle);actions.Children.Add(edit);actions.Children.Add(restore);
            var status=Label("","status");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);root.Children.Add(status);
            Css.SetStyleSheet(root,"""
                .root{display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:white;}
                .title{display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px;}
                .description{display:block;margin:0 0 20px;}
                .group{margin:0 0 16px;padding:16px;border:2px solid #c5e2d1;background:#eef5f0;}
                .entry{display:block;font-size:18px;line-height:28px;margin:0 0 16px;}
                .actions{display:flex;flex-wrap:wrap;gap:8px;margin:4px 0 12px;}
                .action{display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342;}
                .action:hover{background:#c5e2d1;}.action:active{background:#b4d7c2;}.action:focus-visible{border-color:#146342;}
                .status{display:block;font-size:16px;line-height:24px;}
                """);
            var original=one.Text;var grouped=false;
            void Apply(){Css.SetStyle(group,grouped?"display:block;":"display:contents;");((TextBlock)toggle.Child!).Text=grouped?"简洁阅读":"显示分组";AutomationProperties.SetName(toggle,grouped?"简洁阅读":"显示分组");status.Text=grouped?"分组已展开，记录保持原样。":"简洁阅读中，所有记录仍可查看。";}
            toggle.Activate=()=>{grouped=!grouped;Apply();};edit.Activate=()=>{one.Text+="再补写一句，留给下一次阅读。";Apply();};restore.Activate=()=>{one.Text=original;grouped=false;Apply();};
            foreach(var action in new[]{toggle,edit,restore}){action.MouseLeftButtonUp+=(_,_)=>action.Activate?.Invoke();action.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){action.Activate?.Invoke();e.Handled=true;}};}
            Apply();var viewport=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};byte[]? initial=null;
            foreach(var phase in new[]{"initial","grouped","edited","simple","2x","restored"})
            {
                if(phase=="grouped")Press(toggle,Key.Enter);if(phase=="simple")Press(toggle,Key.Space);if(phase=="edited")((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();if(phase=="restored")((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                Layout(viewport,width,height);if(phase=="initial"&&(!toggle.Focus()||!toggle.MoveFocus(FocusNavigationDirection.Next)||!edit.IsFocused||!edit.MoveFocus(FocusNavigationDirection.Next)||!restore.IsFocused))throw new InvalidOperationException("Managed focus traversal differs");
                Keyboard.ClearFocus();Layout(viewport,width,height);var scale=phase=="2x"?2:1;var pixels=Capture(context,viewport,engine,width,height,scale);Save(pixels,width*scale,height*scale,$"reader-{engine}-{width}-{phase}.png");
                if(phase=="initial")initial=pixels;if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored pixels differ");
                if(!ReferenceEquals(one,group.Children[0])||!ReferenceEquals(two,group.Children[1]))throw new InvalidOperationException("Original nodes moved");
                foreach(var action in new[]{toggle,edit,restore}){var at=action.TransformToAncestor(root);if(action.ActualHeight<44||at.X<19||at.X+action.ActualWidth>width-19||action.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Action bounds or semantics differ");}
                if(viewport.ExtentHeight>height){viewport.ScrollToBottom();Layout(viewport,width,height);var at=restore.TransformToAncestor(viewport);if(at.Y<0||at.Y+restore.ActualHeight>height)throw new InvalidOperationException("Restore action unreachable");Save(Capture(context,viewport,engine,width,height,1),width,height,$"reader-{engine}-{width}-{phase}-bottom.png");viewport.ScrollToTop();Layout(viewport,width,height);}
                records.Add(new{engine=engine.ToString(),width,phase,grouped,sourceParentsPreserved=true,managedActions=true,extent=viewport.ExtentHeight});
            }
        }
        Write("reader.json",records);
    }
    private static void Press(DisplayAction action, Key key)
    {
        if (!action.Focus()) throw new InvalidOperationException("Reader action could not receive managed focus");
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        action.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Display action key unhandled");
    }
    private sealed class DisplayAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new DisplayActionPeer(this); }
    private sealed class DisplayActionPeer(DisplayAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "DisplayAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static DisplayAction Action(string text, int tabIndex)
    {
        var result = new DisplayAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
    }
    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Requested paragraph renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Paragraph readback failed");
        return pixels;
    }
    private static void Write(string name, object value)
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_CONTENTS_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_CONTENTS_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name)); encoder.Save(file);
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); }
    }
}
