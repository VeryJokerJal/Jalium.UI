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
internal static class CssDocumentBoxFormatterChecks
{
    private const string Font="font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;color:#146342;";
    private static readonly string[] Scopes=["root","section","list","item","table","group","row","cell","paragraph"];
    private static readonly string[] Modes=["block","flex","grid","inline","inline-block","flow-root","table","table-row-group","table-row","table-cell","table-caption"];
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);Keyboard.Initialize();
        var baseline=Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_BOX_FORMATTERS_BASELINE")=="1";var records=new List<object>();var failed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        {
            foreach(var scope in Scopes)foreach(var mode in Modes)
            {var scene=new Scene(scope);scene.Style($"display:{mode};gap:10px;grid-template-columns:60px 110px;align-items:start;");Compare(engine,scope+"-"+mode,scene);}
            foreach(var kind in new[]{"clipped","sizing","rtl","inline","clamp","columns","long-columns","marker-only","unicode","root-decoration"})
            {
                var scene=new Scene(kind=="root-decoration"?"root":"section");var inline=string.Concat(scene.Paragraphs.Select(paragraph=>scene.Selector(paragraph)+"{display:inline;}"));
                if(kind=="root-decoration")scene.Style("display:grid;grid-template-columns:60px 110px;padding:10px;border:4px solid green;background:rgb(20 99 66 / .2);");
                if(kind=="clipped")scene.Style("display:grid;grid-template-columns:180px;height:36px;overflow:hidden;");
                if(kind=="sizing")scene.Style("display:grid;width:140px;height:100px;padding:10px;border:4px solid green;box-sizing:content-box;grid-template-columns:50% 50%;");
                if(kind=="rtl")scene.Style("display:grid;direction:rtl;grid-template-columns:60px 110px;gap:10px;");
                if(kind=="inline")scene.Style("display:block;",string.Concat(scene.Paragraphs.Select(paragraph=>scene.Selector(paragraph)+"{display:inline;padding:4px;border:1px solid green;background:#eef5f0;}")));
                if(kind is "clamp" or "columns" or "marker-only")
                {
                    scene.Words(["AA BB CC ","DD EE FF ","GG HH II ","JJ KK LL MM NN OO PP"]);
                    scene.Style(kind=="columns"?"display:inline-block;width:180px;height:72px;columns:2;column-gap:12px;":kind=="clamp"?"display:inline-block;width:90px;line-clamp:2 no-ellipsis;":"display:inline-block;width:90px;line-clamp:1 'THIS MARKER DOES NOT FIT';",inline);
                }
                if(kind=="long-columns")
                {
                    scene.Words(["AA BB CC DD EE FF GG HH II JJ KK LL MM NN OO PP QQ RR SS TT UU VV WW XX ","YY ","ZZ ","END"]);
                    scene.Style("display:inline-block;width:180px;height:72px;columns:2;column-gap:12px;",inline);
                }
                if(kind=="unicode"){scene.Words(["甲乙e\u0301 ","丙丁","👨‍👩‍👧‍👦 ","戊己"]);scene.Style("display:block;width:80px;",inline);}
                Compare(engine,kind,scene);
            }
        }
        Reader(context,baseline);Write("native.json",new{baseline,count=records.Count,failed,cases=records});return baseline?0:failed==0?0:1;
        void Compare(RenderingEngine engine,string kind,Scene scene)
        {
            var raw=scene.Document.GetText();using var body=Body(scene.Document);var canvas=CanvasFor(body);var expectedCanvas=CanvasFor(scene.Reference);Layout(canvas,280,280);Layout(expectedCanvas,280,280);var proxies=body.Paragraphs.ToArray();
            var actual=Capture(context,canvas,engine,280,280,1);var expected=Capture(context,expectedCanvas,engine,280,280,1);var equal=actual.SequenceEqual(expected);var differentPixels=0;var maxChannelDelta=0;
            for(var pixel=0;pixel<actual.Length;pixel+=4){var different=false;for(var channel=0;channel<4;channel++){var delta=Math.Abs(actual[pixel+channel]-expected[pixel+channel]);different|=delta!=0;maxChannelDelta=Math.Max(maxChannelDelta,delta);}if(different)differentPixels++;}
            var passed=equal&&Math.Abs(body.ExtentHeight-scene.Reference.DesiredSize.Height)<.0001;if(!passed)failed++;
            Save(actual,280,280,$"formatted-{engine}-{kind}.png");Save(expected,280,280,$"reference-{engine}-{kind}.png");
            if(raw!=scene.Document.GetText()||!proxies.SequenceEqual(body.Paragraphs)||scene.Paragraphs.Where((paragraph,index)=>!ReferenceEquals(paragraph.Inlines,body.Paragraphs[index].Inlines)).Any())throw new InvalidOperationException("Original document or proxies changed");
            records.Add(new{engine=engine.ToString(),kind,samePixels=equal,differentPixels,maxChannelDelta,passed,body.ExtentHeight,referenceHeight=scene.Reference.DesiredSize.Height,lines=body.Lines.Count,cells=body.TableCells.Count});
        }
    }
    private sealed class Scene
    {
        internal readonly Paragraph[] Paragraphs=Enumerable.Range(0,4).Select(i=>new Paragraph(new Run(new string((char)('A'+i),2)))).ToArray();
        internal readonly FlowDocument Document;
        internal readonly StackPanel Reference=new();
        private readonly Dictionary<DependencyObject,string> _classes=[];
        private readonly List<TextBlock> _texts=[];
        private readonly DependencyObject _target;
        private readonly string _sheet;
        internal Scene(string scope)
        {
            var section=new Section();var table=new Table();var group=new TableRowGroup();var rows=new[]{new TableRow(),new TableRow()};var item=new ListItem();var list=new DocList(item);TableCell? cell=null;
            if(scope is "table" or "group" or "row" or "cell")
            {foreach(var row in rows)group.Rows.Add(row);table.RowGroups.Add(group);for(var i=0;i<4;i++){var next=new TableCell(Paragraphs[i]);rows[i/2].Cells.Add(next);cell??=next;}section.Blocks.Add(table);}
            else if(scope is "list" or "item"){item.Blocks.Add(Paragraphs[0]);item.Blocks.Add(Paragraphs[1]);list.ListItems.Add(new ListItem(Paragraphs[2]));list.ListItems.Add(new ListItem(Paragraphs[3]));section.Blocks.Add(list);}
            else foreach(var paragraph in Paragraphs)section.Blocks.Add(paragraph);
            Document=CssDocumentBoxFormatterChecks.Document(section);_target=scope switch{"root"=>Document,"list"=>list,"item"=>item,"table"=>table,"group"=>group,"row"=>rows[0],"cell"=>cell!,"paragraph"=>Paragraphs[0],_=>section};var rules=new List<string>();
            FrameworkElement Build(DependencyObject source)
            {
                var name="n"+_classes.Count;_classes[source]=name;Css.SetClass(source,name);var role=source switch{Table=>"table",TableRowGroup=>"table-row-group",TableRow=>"table-row",TableCell=>"table-cell",_=>"block"};
                rules.Add($".{name}{{display:{role};margin:0;padding:0;border:0;border-spacing:0;{(source is Table?"width:180px;":"")}}}");FrameworkElement result;
                // Both sides contain independent Run nodes. This isolates box
                // projection from the separate plain/rich native bidi paths.
                if(source is Paragraph paragraph){var text=new TextBlock();text.Inlines.Add(new Run(((Run)paragraph.Inlines[0]).Text));_texts.Add(text);result=text;}
                else{var panel=ReferenceEquals(source,Document)?Reference:new StackPanel();IEnumerable<DependencyObject> children=source switch{FlowDocument d=>d.Blocks,Section s=>s.Blocks,DocList l=>l.ListItems,ListItem i=>i.Blocks,Table t=>t.RowGroups,TableRowGroup g=>g.Rows,TableRow row=>row.Cells,TableCell c=>c.Blocks,_=>[]};foreach(var child in children)panel.Children.Add(Build(child));result=panel;}
                Css.SetClass(result,name);return result;
            }
            Build(Document);_sheet=string.Concat(rules);Css.SetStyle(Reference,Font);
        }
        internal string Selector(DependencyObject source)=>"."+_classes[source];
        internal void Style(string style,string extra="")
        {
            var sheet=_sheet+$"{Selector(_target)}{{{style}}}"+extra;Css.SetStyleSheet(Document,sheet);Css.SetStyleSheet(Reference,sheet);
            // The source is a CSS document root. Canvas is only a capture
            // fixture and must not turn the reference root into a table part.
            if(ReferenceEquals(_target,Document)&&new[]{"table-row-group","table-row","table-cell","table-caption"}.Any(mode=>style.StartsWith($"display:{mode};",StringComparison.Ordinal)))
                Css.SetStyle(Reference,Font+"display:block;");
        }
        internal void Words(string[] words){for(var i=0;i<4;i++){((Run)Paragraphs[i].Inlines[0]).Text=words[i];((Run)_texts[i].Inlines[0]).Text=words[i];}}
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
            var height=width==320?420:640;const string original="沿途的发现值得慢慢记录。连读排版时，文字自然衔接，原来的段落和文字仍然保留。";
            var run=new Run(original);var first=new Paragraph(run);var second=new Paragraph(new Run("记录光影的变化，也记录走过的路。"));var third=new Paragraph(new Run("把小小的发现留在这里，下一次还可以继续补写。"));
            var details=new Section(first);details.Blocks.Add(second);details.Blocks.Add(third);Css.SetClass(details,"details");var doc=Document(details);
            Css.SetStyle(doc,"font-family:'PingFang SC';font-size:18px;line-height:28px;white-space:normal;color:#233c32;");
            Css.SetStyleSheet(doc,"Paragraph,Section{margin:0;padding:0;border:0;}.details{margin:0;}");
            using var body=new FlowDocumentPresentation();body.SetDocument(doc);Css.SetClass(body,"document");
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");root.Children.Add(Label("让段落自由排版","title"));root.Children.Add(Label("切换网格与连读，原文和选择位置保持不变。","description"));root.Children.Add(body);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);var toggle=Action("网格阅读",0);var edit=Action("补写说明",1);var restore=Action("恢复原文",2);actions.Children.Add(toggle);actions.Children.Add(edit);actions.Children.Add(restore);
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
                Css.SetStyle(details,shown?"display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:12px;":"display:block;");
                foreach(var paragraph in details.Blocks)Css.SetStyle(paragraph,shown?"display:block;min-width:0;padding:12px;border:1px solid #aac6b6;background:#eef5f0;":"display:inline;");
                ((TextBlock)toggle.Child!).Text=shown?"连读排版":"网格阅读";AutomationProperties.SetName(toggle,shown?"连读排版":"网格阅读");
                status.Text=shown?"已切换网格，可逐段阅读与选择。":"已连续排版，原文和选择位置保持不变。";
            }
            toggle.Activate=()=>{shown=!shown;Apply();};edit.Activate=()=>{run.Text+="再补写一句，留给下一次阅读。";Apply();};restore.Activate=()=>{run.Text=original;shown=true;Apply();};
            foreach(var action in new[]{toggle,edit,restore}){action.GotKeyboardFocus+=(_,_)=>action.BringIntoView();action.MouseLeftButtonUp+=(_,_)=>action.Activate?.Invoke();action.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Enter or Key.Space){action.Activate?.Invoke();e.Handled=true;}};}
            Apply();var viewport=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};byte[]? initial=null;
            foreach(var phase in baseline?new[]{"initial"}:new[]{"initial","segmented","edited","joined","joined-edited","2x","restored"})
            {
                if(phase=="segmented")Press(toggle,Key.Enter);if(phase=="joined")Press(toggle,Key.Space);
                if(phase is "edited" or "joined-edited")((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if(phase=="restored")((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                Layout(viewport,width,height);
                if(!baseline&&phase=="initial")
                {if(!toggle.Focus()||!toggle.MoveFocus(FocusNavigationDirection.Next)||!edit.IsFocused||!edit.MoveFocus(FocusNavigationDirection.Next)||!restore.IsFocused)throw new InvalidOperationException("Managed focus traversal differs");}
                if(!baseline&&phase=="initial")
                {
                    toggle.Focus();Layout(viewport,width,height);var at=toggle.TransformToAncestor(viewport);
                    if(at.Y<0||at.Y+toggle.ActualHeight>height)throw new InvalidOperationException("Focused action outside viewport");
                    Save(Capture(context,viewport,engine,width,height,1),width,height,$"reader-{engine}-{width}-focused.png");
                }
                Keyboard.ClearFocus();viewport.ScrollToTop();Layout(viewport,width,height);var scale=phase=="2x"?2:1;var pixels=Capture(context,viewport,engine,width,height,scale);Save(pixels,width*scale,height*scale,$"reader-{engine}-{width}-{phase}.png");
                var provider=(ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;var exposed=provider.DocumentRange.GetText(-1);var matches=exposed.Contains(run.Text)&&exposed.Contains("记录光影")&&(shown||!exposed.Contains(run.Text+"\n"));
                if(!baseline)
                {
                    if(!matches||body.Paragraphs.Count!=3||!ReferenceEquals(run,first.Inlines[0])||!ReferenceEquals(first,details.Blocks[0])||!ReferenceEquals(second,details.Blocks[1]))throw new InvalidOperationException("Display state or original source differs");
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
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_BOX_FORMATTERS_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_BOX_FORMATTERS_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
