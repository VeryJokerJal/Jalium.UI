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
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssDocumentDisplayChecks
{
    private const string Font="font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;color:#146342;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);Keyboard.Initialize();
        var baseline=Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_DISPLAY_BASELINE")=="1";var cases=new List<object>();var failed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var scope in new[]{"paragraph","section","list","item","table","group","row","cell","root"})
        {
            var source=new Paragraph(new Run("MASKED"));Block child=source;DependencyObject target=source;
            if(scope=="section"){var section=new Section(source);child=section;target=section;}
            if(scope is "list" or "item"){var item=new ListItem(source);var list=new DocList(item);child=list;target=scope=="list"?list:item;}
            if(scope is "table" or "group" or "row" or "cell")
            {var cell=new TableCell(source);var row=new TableRow();row.Cells.Add(cell);var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);child=table;target=scope=="table"?table:scope=="group"?group:scope=="row"?row:cell;}
            var doc=Document(new Paragraph(new Run("BEFORE")),child,new Paragraph(new Run("AFTER")));if(scope=="root")target=doc;var original=doc.GetText();
            Css.SetStyle(target,(scope=="root"?Font:"")+"display:none;margin:19px;padding:13px;border:3px solid red;height:200px;");
            using var body=Body(doc);var canvas=CanvasFor(body);Layout(canvas,240,180);var actual=Capture(context,canvas,engine,240,180,1);
            var reference=Document(scope=="root"?[]:[new Paragraph(new Run("BEFORE")),new Paragraph(new Run("AFTER"))]);using var expectedBody=Body(reference);var expectedCanvas=CanvasFor(expectedBody);Layout(expectedCanvas,240,180);
            var expected=Capture(context,expectedCanvas,engine,240,180,1);var equal=actual.SequenceEqual(expected);if(!equal)failed++;
            Save(actual,240,180,$"none-{engine}-{scope}.png");Save(expected,240,180,$"reference-{engine}-{scope}.png");
            if(doc.GetText()!=original||!ReferenceEquals(source.Inlines,body.Paragraphs[1].Inlines))throw new InvalidOperationException("Original document changed");
            cases.Add(new{engine=engine.ToString(),scope,samePixelsAsRemovedSource=equal});
        }
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var value in new[]{("AB","CD",180),("甲乙","丙丁戊己",40),("e\u0301","e\u0301 e\u0301",40)})
        {
            var hidden=new Run("秘密👨‍👩‍👧‍👦"){FontSize=120};var span=new Span(hidden);span.Inlines.Add(new LineBreak());var paragraph=new Paragraph(new Run(value.Item1));paragraph.Inlines.Add(span);paragraph.Inlines.Add(new Run(value.Item2));
            var doc=Document(paragraph);Css.SetStyle(doc,Font+$"width:{value.Item3}px;");Css.SetStyle(span,"display:none;background:red;text-decoration:underline;line-height:300px;");using var body=Body(doc);var canvas=CanvasFor(body);Layout(canvas,240,180);
            var reference=Document(new Paragraph(new Run(value.Item1+value.Item2)));Css.SetStyle(reference,Font+$"width:{value.Item3}px;");using var expectedBody=Body(reference);var expectedCanvas=CanvasFor(expectedBody);Layout(expectedCanvas,240,180);
            var actual=Capture(context,canvas,engine,240,180,1);var expected=Capture(context,expectedCanvas,engine,240,180,1);var equal=actual.SequenceEqual(expected);if(!equal)failed++;
            var id=value.Item1=="AB"?"latin":value.Item1.StartsWith("甲")?"cjk":"combining";
            Save(actual,240,180,$"inline-{engine}-{id}.png");Save(expected,240,180,$"inline-reference-{engine}-{id}.png");
            cases.Add(new{engine=engine.ToString(),scope="inline "+value.Item1,samePixelsAsRemovedSource=equal});
        }
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var kind in new[]{"column","rows","collapsed-rows"})
        {
            FlowDocument Make(bool actual)
            {
                var table=new Table();var group=new TableRowGroup();table.RowGroups.Add(group);
                if(kind=="column")
                {
                    var row=new TableRow();row.Cells.Add(new TableCell(new Paragraph(new Run("A"))));row.Cells.Add(new TableCell(new Paragraph(new Run("B"))));group.Rows.Add(row);
                    if(actual){var column=new TableColumn{Width=new GridLength(150),Background=Brushes.Red};table.Columns.Add(column);Css.SetStyle(column,"display:none;");}
                }
                else
                {
                    var first=new TableRow();first.Cells.Add(new TableCell(new Paragraph(new Run("SPAN"))){RowSpan=2});first.Cells.Add(new TableCell(new Paragraph(new Run("A"))));group.Rows.Add(first);
                    if(actual){var hidden=new TableRow();hidden.Cells.Add(new TableCell(new Paragraph(new Run("MASKED"))));group.Rows.Add(hidden);Css.SetStyle(hidden,"display:none;height:300px;border:20px solid red;");}
                    var last=new TableRow();last.Cells.Add(new TableCell(new Paragraph(new Run("B"))));group.Rows.Add(last);
                }
                var doc=Document(table);if(kind=="collapsed-rows")Css.SetStyle(table,"border-collapse:collapse;border:2px solid green;");return doc;
            }
            using var body=Body(Make(true));using var reference=Body(Make(false));var canvas=CanvasFor(body);var expectedCanvas=CanvasFor(reference);Layout(canvas,240,180);Layout(expectedCanvas,240,180);
            var actual=Capture(context,canvas,engine,240,180,1);var expected=Capture(context,expectedCanvas,engine,240,180,1);var equal=actual.SequenceEqual(expected);if(!equal)failed++;
            Save(actual,240,180,$"table-{engine}-{kind}.png");Save(expected,240,180,$"table-reference-{engine}-{kind}.png");cases.Add(new{engine=engine.ToString(),scope=kind,samePixelsAsRemovedSource=equal});
        }
        Reader(context,baseline);Write("native.json",new{baseline,count=cases.Count,failed,cases});return baseline?0:failed==0?0:1;
    }
    private static FlowDocument Document(params Block[] blocks)
    {
        var doc=new FlowDocument{PagePadding=new(0),FontFamily="Courier New",FontSize=16,LineHeight=24};foreach(var block in blocks)doc.Blocks.Add(block);
        Css.SetStyle(doc,Font);Css.SetStyleSheet(doc,"Paragraph,Section,List,ListItem,Table,TableCell{margin:0;padding:0;border:0;}Table{border-spacing:0;}");return doc;
    }
    private static FlowDocumentPresentation Body(FlowDocument doc){var body=new FlowDocumentPresentation();body.SetDocument(doc);body.Width=180;return body;}
    private static Canvas CanvasFor(FrameworkElement body){var canvas=new Canvas{Background=Brushes.White};canvas.Children.Add(body);Canvas.SetLeft(body,16);Canvas.SetTop(body,16);return canvas;}
    private static void Reader(RenderContext context,bool baseline)
    {
        var records=new List<object>();
        foreach(var engine in baseline?new[]{RenderingEngine.Impeller}:new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var width in baseline?new[]{375}:new[]{320,375,720})
        {
            var height=width==320?420:640;const string original="这段补充说明可以随时显示或隐藏。隐藏时，正文会自然衔接，原来的段落和文字仍然保留。";
            var run=new Run(original);var details=new Section(new Paragraph(run));details.Blocks.Add(new Paragraph(new Run("再次显示后，仍可继续阅读和选择这些内容。")));Css.SetClass(details,"details");
            var doc=Document(new Paragraph(new Run("今天记录一段沿途的发现。")),details,new Paragraph(new Run("接下来，继续阅读下一段。")));
            Css.SetStyle(doc,"font-family:'PingFang SC';font-size:18px;line-height:28px;white-space:normal;color:#233c32;");
            Css.SetStyleSheet(doc,"Paragraph,Section{margin:0;padding:0;border:0;}.details{margin:12px 0;padding:12px;background:#eef5f0;}Section Paragraph+Paragraph{margin-top:12px;}");
            using var body=new FlowDocumentPresentation();body.SetDocument(doc);Css.SetClass(body,"document");
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");root.Children.Add(Label("让正文自然衔接","title"));root.Children.Add(Label("需要时展开说明，阅读时保持专注。","description"));root.Children.Add(body);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);var toggle=Action("显示说明",0);var edit=Action("补写说明",1);var restore=Action("恢复原文",2);actions.Children.Add(toggle);actions.Children.Add(edit);actions.Children.Add(restore);
            var status=Label("","status");AutomationProperties.SetLiveSetting(status,AutomationLiveSetting.Polite);root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root{display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:white;}
                .title{display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px;}
                .description{display:block;margin:0 0 20px;}
                .document{display:block;max-width:440px;margin:0 0 20px;}
                .actions{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px;}
                .action{display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342;}
                .action:hover{background:#c5e2d1;}.action:active{background:#b4d7c2;}.action:focus-visible{border-color:#146342;}
                .status{display:block;font-size:16px;line-height:24px;}
                """));
            var shown=false;
            void Apply(){Css.SetStyle(details,shown?"display:block;":"display:none;");((TextBlock)toggle.Child!).Text=shown?"隐藏说明":"显示说明";AutomationProperties.SetName(toggle,shown?"隐藏说明":"显示说明");status.Text=shown?"说明已显示，可阅读完整内容。":"说明已隐藏，正文保持连续。";}
            toggle.Activate=()=>{shown=!shown;Apply();};edit.Activate=()=>{run.Text+="再补写一句，留给下一次阅读。";Apply();};restore.Activate=()=>{run.Text=original;shown=false;Apply();};
            foreach(var action in new[]{toggle,edit,restore}){action.MouseLeftButtonUp+=(_,_)=>action.Activate?.Invoke();action.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){action.Activate?.Invoke();e.Handled=true;}};}
            Apply();var viewport=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};byte[]? initial=null;
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","shown","edited","hidden","hidden-edited","2x","restored"})
            {
                if(phase=="shown")Press(toggle,Key.Enter);if(phase=="hidden")Press(toggle,Key.Space);
                if(phase is "edited" or "hidden-edited")((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                Layout(viewport,width,height);
                if(!baseline&&phase=="initial")
                {if(!toggle.Focus()||!toggle.MoveFocus(FocusNavigationDirection.Next)||!edit.IsFocused||!edit.MoveFocus(FocusNavigationDirection.Next)||!restore.IsFocused)throw new InvalidOperationException("Managed focus traversal differs");}
                Keyboard.ClearFocus();Layout(viewport,width,height);var scale=phase=="2x"?2:1;var pixels=Capture(context,viewport,engine,width,height,scale);Save(pixels,width*scale,height*scale,$"reader-{engine}-{width}-{phase}.png");
                var provider=(ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;var exposed=provider.DocumentRange.GetText(-1);var matches=exposed.Contains("这段补充说明")==shown;
                if(!baseline)
                {
                    if(!matches||body.Paragraphs.Count!=4||!ReferenceEquals(run,((Paragraph)details.Blocks[0]).Inlines[0]))throw new InvalidOperationException("Display state or original source differs");
                    foreach(var action in new[]{toggle,edit,restore}){var at=action.TransformToAncestor(root);if(action.ActualHeight<44||at.X<19||at.X+action.ActualWidth>width-19||action.GetAutomationPeer()!.GetAutomationControlType()!=AutomationControlType.Button||string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName()))throw new InvalidOperationException("Reader action layout or semantics differ");}
                    if(phase=="initial")initial=pixels;if(phase=="restored"&&!pixels.SequenceEqual(initial!))throw new InvalidOperationException("Restored pixels differ");
                }
                if(!baseline&&viewport.ExtentHeight>height)
                {
                    viewport.ScrollToBottom();Layout(viewport,width,height);var at=restore.TransformToAncestor(viewport);
                    if(at.Y<0||at.Y+restore.ActualHeight>height)throw new InvalidOperationException("Expanded reader action unreachable");
                    Save(Capture(context,viewport,engine,width,height,1),width,height,$"reader-{engine}-{width}-{phase}-bottom.png");viewport.ScrollToTop();Layout(viewport,width,height);
                }
                records.Add(new{engine=engine.ToString(),width,height,phase,shown,exposureMatches=matches,viewport.ExtentHeight,viewport.ViewportHeight});
            }
            viewport.ScrollToBottom();Layout(viewport,width,height);if(!baseline){var at=restore.TransformToAncestor(viewport);if(at.Y<0||at.Y+restore.ActualHeight>height)throw new InvalidOperationException("Scrolled action unreachable");}
            Save(Capture(context,viewport,engine,width,height,1),width,height,$"reader-{engine}-{width}-scrolled.png");
        }
        Write("reader.json",new{baseline,count=records.Count,cases=records});
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
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_DISPLAY_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_DISPLAY_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
