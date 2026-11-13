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
internal static class CssDocumentContentsChecks
{
    private const string Font="font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;color:#146342;";
    private const string Contents="display:contents;width:900px;height:800px;min-height:700px;margin:30px;padding:20px;border:10px solid red;background:red;columns:2;column-gap:90px;break-before:page;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);Keyboard.Initialize();
        var baseline=Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CONTENTS_BASELINE")=="1";var cases=new List<object>();var failed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        foreach(var kind in new[]{"paragraph","section","list","item","nested","unicode","whitespace","intrinsic","columns","clamp","clamp-columns","marker-only","cell","root","hidden-middle"})
        {
            var doc=Scene(kind,true);var raw=doc.GetText();var source=doc.Blocks.ToArray();using var body=Body(doc);var canvas=CanvasFor(body);Layout(canvas,280,260);var proxies=body.Paragraphs.ToArray();
            var actual=Capture(context,canvas,engine,280,260,1);using var expectedBody=Body(Scene(kind,false));var expectedCanvas=CanvasFor(expectedBody);Layout(expectedCanvas,280,260);
            var expected=Capture(context,expectedCanvas,engine,280,260,1);var equal=actual.SequenceEqual(expected);
            var differentPixels=0;var maxChannelDelta=0;
            for(var pixel=0;pixel<actual.Length;pixel+=4)
            {
                var different=false;
                for(var channel=0;channel<4;channel++)
                {var delta=Math.Abs(actual[pixel+channel]-expected[pixel+channel]);different|=delta!=0;maxChannelDelta=Math.Max(maxChannelDelta,delta);}
                if(different)differentPixels++;
            }
            bool? geometryMatches=kind=="clamp-columns"?SameRetainedGeometry(body,expectedBody):null;
            // Column projection composes separate source transforms. Permit only
            // a few low-coverage antialias samples, with identical text geometry.
            var passed=equal||kind=="clamp-columns"&&geometryMatches==true&&differentPixels<=4&&maxChannelDelta<=8;
            if(kind=="marker-only"&&!baseline&&body.OffsetFromPoint(new(1,36))!=doc.GetDocumentOffset(((Section)doc.Blocks[1]).Blocks.OfType<Paragraph>().First(),0))
                throw new InvalidOperationException("Marker-only hit lost its original boundary");
            if(!passed)failed++;
            Save(actual,280,260,$"contents-{engine}-{kind}.png");Save(expected,280,260,$"reference-{engine}-{kind}.png");
            if(doc.GetText()!=raw||!source.SequenceEqual(doc.Blocks)||!proxies.SequenceEqual(body.Paragraphs))throw new InvalidOperationException("Original document changed");
            cases.Add(new{engine=engine.ToString(),kind,samePixelsAsAnonymousFlow=equal,differentPixels,maxChannelDelta,geometryMatches,passed,
                body.ExtentHeight,lines=body.Lines.Count,referenceHeight=expectedBody.ExtentHeight});
        }
        Reader(context,baseline);Write("native.json",new{baseline,count=cases.Count,failed,cases});return baseline?0:failed==0?0:1;
    }
    private static bool SameRetainedGeometry(FlowDocumentPresentation actual,FlowDocumentPresentation expected)
    {
        if(actual.Lines.Count!=expected.Lines.Count||Math.Abs(actual.ExtentHeight-expected.ExtentHeight)>.0001)return false;
        var a=(ITextProvider)actual.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var b=(ITextProvider)expected.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        foreach(var word in new[]{"AA","BB","CC","DD","EE","FF","PP"})
        {
            var one=a.DocumentRange.FindText(word,false,false)!.GetBoundingRectangles();var two=b.DocumentRange.FindText(word,false,false)!.GetBoundingRectangles();
            if(one.Length!=two.Length||one.Where((value,index)=>Math.Abs(value-two[index])>.0001).Any())return false;
        }
        return true;
    }
    private static FlowDocument Scene(string kind,bool actual)
    {
        const string words="AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO PP";
        var a=kind=="unicode"?"甲乙e\u0301 ":kind=="whitespace"?"A  ":kind is "columns" or "clamp" or "clamp-columns" or "marker-only"?"AA BB CC DD ":"AB";
        var b=kind=="unicode"?"丙丁👨‍👩‍👧‍👦 戊己":kind=="whitespace"?"  B C ":kind is "columns" or "clamp" or "clamp-columns" or "marker-only"?words[12..]:"CD EF";
        FlowDocument doc;
        if(kind is "section" or "list" or "item")
        {
            var p=new Paragraph(new Run("BEFORE"));var q=new Paragraph(new Run("AFTER"));
            if(!actual){if(kind=="list"){var list=new DocList(new ListItem(p));list.ListItems.Add(new ListItem(q));doc=Document(list);}else doc=Document(p,q);}
            else if(kind=="section"){var group=new Section(p);group.Blocks.Add(q);doc=Document(group);Css.SetStyle(group,Contents);}
            else if(kind=="list"){var list=new DocList(new ListItem(p));list.ListItems.Add(new ListItem(q));doc=Document(list);Css.SetStyle(list,Contents);}
            else{var item=new ListItem(p);item.Blocks.Add(q);doc=Document(new DocList(item));Css.SetStyle(item,Contents);}
        }
        else if(kind=="root")
        {
            doc=Document(new Paragraph(new Run("FIRST")),new Paragraph(new Run("SECOND")));
            Css.SetStyle(doc,Font+$"display:{(actual?"contents":"block")};padding:5px;border:2px solid green;line-clamp:1 no-ellipsis;");return doc;
        }
        else
        {
            var p=new Paragraph(new Run(a));var q=new Paragraph(new Run(b));Block content;
            if(actual){Css.SetStyle(p,Contents);Css.SetStyle(q,Contents);var group=new Section(p);group.Blocks.Add(q);Css.SetStyle(group,kind=="clamp-columns"?"line-clamp:2 '…';":Contents);content=group;}
            else{var combined=new Paragraph(new Run(a));combined.Inlines.Add(new Run(b));if(kind=="clamp-columns"){var group=new Section(combined);Css.SetStyle(group,"line-clamp:2 '…';");content=group;}else content=combined;}
            if(kind=="hidden-middle"&&actual)
            {var group=(Section)content;var hidden=new Paragraph(new Run("MASKED"));group.Blocks.Insert(1,hidden);Css.SetStyle(hidden,"display:none;");}
            if(kind=="cell")
            {var row=new TableRow();row.Cells.Add(new TableCell(content));row.Cells.Add(new TableCell(new Paragraph(new Run("CELL"))));var group=new TableRowGroup();group.Rows.Add(row);var table=new Table();table.RowGroups.Add(group);doc=Document(table);Css.SetStyle(table,"border:1px solid green;border-spacing:0;");}
            else doc=kind=="marker-only"?Document(new Paragraph(new Run("HEADER")),content):Document(content);
        }
        if(kind=="unicode")Css.SetStyle(doc,Font+"width:80px;");
        if(kind=="whitespace")Css.SetStyle(doc,Font+"width:45px;white-space:pre-wrap;");
        if(kind=="intrinsic")Css.SetStyle(doc,Font+"width:max-content;border:1px solid green;");
        if(kind is "columns" or "clamp-columns")Css.SetStyle(doc,Font+"columns:2;column-gap:12px;orphans:1;widows:1;");
        if(kind=="clamp")Css.SetStyle(doc,Font+"width:100px;line-clamp:2 '…';");
        if(kind=="marker-only")Css.SetStyle(doc,Font+"width:90px;line-clamp:2 'THIS MARKER DOES NOT FIT';");
        return doc;
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
            var height=width==320?420:640;const string original="沿途的发现值得慢慢记录。连续阅读时，文字自然衔接，原来的段落和文字仍然保留。";
            var run=new Run(original);var details=new Section(new Paragraph(run));details.Blocks.Add(new Paragraph(new Run("切回分段后，仍可继续阅读和选择这些内容。")));Css.SetClass(details,"details");
            var doc=Document(new Paragraph(new Run("今天记录一段沿途的发现。")),details,new Paragraph(new Run("接下来，继续阅读下一段。")));
            Css.SetStyle(doc,"font-family:'PingFang SC';font-size:18px;line-height:28px;white-space:normal;color:#233c32;");
            Css.SetStyleSheet(doc,"Paragraph,Section{margin:0;padding:0;border:0;}.details{margin:12px 0;padding:12px;background:#eef5f0;}Section Paragraph+Paragraph{margin-top:12px;}");
            using var body=new FlowDocumentPresentation();body.SetDocument(doc);Css.SetClass(body,"document");
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");root.Children.Add(Label("让段落自然连读","title"));root.Children.Add(Label("切换排版，原文与选择位置保持不变。","description"));root.Children.Add(body);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);var toggle=Action("分段阅读",0);var edit=Action("补写说明",1);var restore=Action("恢复原文",2);actions.Children.Add(toggle);actions.Children.Add(edit);actions.Children.Add(restore);
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
            var shown=true;
            void Apply()
            {
                Css.SetStyle(details,shown?"display:contents;":"display:block;");
                foreach(var paragraph in details.Blocks)Css.SetStyle(paragraph,shown?"display:contents;":"");
                ((TextBlock)toggle.Child!).Text=shown?"分段阅读":"连续阅读";AutomationProperties.SetName(toggle,shown?"分段阅读":"连续阅读");
                status.Text=shown?"已连续排版，原文和选择位置保持不变。":"已分段排版，可按段阅读与选择。";
            }
            toggle.Activate=()=>{shown=!shown;Apply();};edit.Activate=()=>{run.Text+="再补写一句，留给下一次阅读。";Apply();};restore.Activate=()=>{run.Text=original;shown=true;Apply();};
            foreach(var action in new[]{toggle,edit,restore}){action.MouseLeftButtonUp+=(_,_)=>action.Activate?.Invoke();action.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){action.Activate?.Invoke();e.Handled=true;}};}
            Apply();var viewport=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};byte[]? initial=null;
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","segmented","edited","joined","joined-edited","2x","restored"})
            {
                if(phase=="segmented")Press(toggle,Key.Enter);if(phase=="joined")Press(toggle,Key.Space);
                if(phase is "edited" or "joined-edited")((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                Layout(viewport,width,height);
                if(!baseline&&phase=="initial")
                {if(!toggle.Focus()||!toggle.MoveFocus(FocusNavigationDirection.Next)||!edit.IsFocused||!edit.MoveFocus(FocusNavigationDirection.Next)||!restore.IsFocused)throw new InvalidOperationException("Managed focus traversal differs");}
                Keyboard.ClearFocus();Layout(viewport,width,height);var scale=phase=="2x"?2:1;var pixels=Capture(context,viewport,engine,width,height,scale);Save(pixels,width*scale,height*scale,$"reader-{engine}-{width}-{phase}.png");
                var provider=(ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;var exposed=provider.DocumentRange.GetText(-1);var matches=exposed.Contains(run.Text)&&exposed.Contains("切回分段后")&&(!shown||!exposed.Contains(run.Text+"\n"));
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
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CONTENTS_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_CONTENTS_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
