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
internal static class CssDocumentTableContentsChecks
{
    private const string Font="font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;color:#146342;";
    private const string Contents="display:contents;width:900px;height:800px;min-height:700px;margin:30px;padding:20px;border:10px solid red;background:red;columns:2;column-gap:90px;break-before:page;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy=NSApplicationActivationPolicy.Prohibited;
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);Keyboard.Initialize();
        var baseline=Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_TABLE_CONTENTS_BASELINE")=="1";var records=new List<object>();var failed=0;
        foreach(var engine in new[]{RenderingEngine.Impeller,RenderingEngine.Vello})
        {
            for(var mask=0;mask<32;mask++)
            {
                var (actual,doc,_)=Matrix(mask,true);using var body=(FlowDocumentPresentation)actual;
                var (expected,_,labels)=Matrix(mask,false);Compare(engine,"mask-"+mask,body,doc!,expected,labels);
            }
            foreach(var whitespace in new[]{"normal","pre-wrap"})
            {
                var(actual,doc,_)=Matrix(1,true,whitespace);using var body=(FlowDocumentPresentation)actual;
                var(expected,_,labels)=Matrix(1,false,whitespace);Compare(engine,"consecutive-whitespace-"+whitespace,body,doc!,expected,labels);
            }
            foreach(var scope in new[]{"table","group","row","cell"})foreach(var collapse in new[]{false,true})
            {
                var doc=Scope(scope,true,collapse);using var body=Body(doc);using var expected=Body(Scope(scope,false,collapse));Compare(engine,scope+(collapse?"-collapsed":"-separate"),body,doc,expected,null);
            }
        }
        Reader(context,baseline);Write("native.json",new{baseline,count=records.Count,failed,cases=records});return baseline?0:failed==0?0:1;
        void Compare(RenderingEngine engine,string kind,FlowDocumentPresentation body,FlowDocument doc,FrameworkElement reference,IReadOnlyList<TextBlock>? labels)
        {
            var raw=doc.GetText();var canvas=CanvasFor(body);var expectedCanvas=CanvasFor(reference);Layout(canvas,280,260);Layout(expectedCanvas,280,260);var originals=body.Paragraphs.ToArray();
            var actual=Capture(context,canvas,engine,280,260,1);var expected=Capture(context,expectedCanvas,engine,280,260,1);var equal=actual.SequenceEqual(expected);var differentPixels=0;var maxChannelDelta=0;
            for(var pixel=0;pixel<actual.Length;pixel+=4){var different=false;for(var channel=0;channel<4;channel++){var delta=Math.Abs(actual[pixel+channel]-expected[pixel+channel]);different|=delta!=0;maxChannelDelta=Math.Max(maxChannelDelta,delta);}if(different)differentPixels++;}
            var geometry=SameGeometry(body,reference,labels);var passed=equal&&geometry;
            if(!passed)failed++;Save(actual,280,260,$"contents-{engine}-{kind}.png");Save(expected,280,260,$"reference-{engine}-{kind}.png");
            if(raw!=doc.GetText()||!originals.SequenceEqual(body.Paragraphs))throw new InvalidOperationException("Original table or paragraph changed");
            records.Add(new{engine=engine.ToString(),kind,samePixels=equal,differentPixels,maxChannelDelta,geometryMatches=geometry,passed,body.ExtentHeight,referenceHeight=reference.DesiredSize.Height,cells=body.TableCells.Count});
        }
    }
    private static bool SameGeometry(FlowDocumentPresentation actual,FrameworkElement expected,IReadOnlyList<TextBlock>? labels)
    {
        if(Math.Abs(actual.ExtentHeight-expected.DesiredSize.Height)>.0001)return false;
        var source=actual.Paragraphs;
        for(var i=0;i<source.Count;i++)
        {
            var text=labels is null?((FlowDocumentPresentation)expected).Paragraphs[i]:labels[i];var a=source[i].DocumentTextBounds(0,1);var b=text.DocumentTextBounds(0,1);
            if(a.Count!=b.Count)return false;var at=source[i].TransformToAncestor(actual);var bt=text.TransformToAncestor(expected);
            for(var j=0;j<a.Count;j++)if(Math.Abs(a[j].X+at.X-b[j].X-bt.X)>.0001||Math.Abs(a[j].Y+at.Y-b[j].Y-bt.Y)>.0001||Math.Abs(a[j].Width-b[j].Width)>.0001||Math.Abs(a[j].Height-b[j].Height)>.0001)return false;
        }
        return true;
    }
    private static (FrameworkElement Root,FlowDocument? Document,TextBlock[] Texts) Matrix(int mask,bool native,string? whitespace=null)
    {
        string Style(int bit,string display)=>$"display:{((mask&(1<<bit))!=0?"contents":display)};margin:0;padding:0;border:0;"+(bit==0?"width:180px;border-spacing:0;":"");
        if(native)
        {
            var table=TableFor("group");var doc=Document(table);Css.SetStyle(table,Style(0,"table"));
            if(whitespace is not null)
            {
                foreach(var value in new[]{"\t "," "}){var text=new Paragraph(new Run(value));Css.SetStyle(text,"display:contents;");doc.Blocks.Insert(0,text);}
                foreach(var value in new[]{" "," "}){var text=new Paragraph(new Run(value));Css.SetStyle(text,"display:contents;");doc.Blocks.Add(text);}
                Css.SetStyle(doc,Font+$"white-space:{whitespace};");
            }
            foreach(var group in table.RowGroups){Css.SetStyle(group,Style(1,"table-row-group"));foreach(var row in group.Rows){Css.SetStyle(row,Style(2,"table-row"));foreach(var cell in row.Cells){Css.SetStyle(cell,Style(3,"table-cell"));Css.SetStyle(cell.Blocks[0],Style(4,"block"));}}}
            return(Body(doc),doc,[]);
        }
        var root=new StackPanel();Css.SetStyle(root,Font+"display:block;"+(whitespace is not null?$"white-space:{whitespace};":""));var labels=new List<TextBlock>();
        void Space(string value){var text=new TextBlock{Text=value};Css.SetStyle(text,"display:contents;");root.Children.Add(text);labels.Add(text);}
        if(whitespace is not null){Space(" ");Space("\t ");}
        var tableBox=new StackPanel();var groupBox=new StackPanel();root.Children.Add(tableBox);tableBox.Children.Add(groupBox);Css.SetStyle(tableBox,Style(0,"table"));Css.SetStyle(groupBox,Style(1,"table-row-group"));
        for(var i=0;i<2;i++){var row=new StackPanel();groupBox.Children.Add(row);Css.SetStyle(row,Style(2,"table-row"));for(var j=0;j<2;j++){var cell=new StackPanel();row.Children.Add(cell);Css.SetStyle(cell,Style(3,"table-cell"));var text=new TextBlock{Text=((char)('A'+i*2+j)).ToString()};cell.Children.Add(text);labels.Add(text);Css.SetStyle(text,Style(4,"block"));}}
        if(whitespace is not null){Space(" ");Space(" ");}
        return(root,null,labels.ToArray());
    }
    private static FlowDocument Scope(string scope,bool actual,bool collapsed)
    {
        var table=TableFor(actual?"group":scope);var doc=Document(table);Css.SetStyle(table,!actual&&scope=="table"?"width:auto;":collapsed?"border-collapse:collapse;border:2px solid green;":"border:2px solid green;border-spacing:3px;");
        if(actual)
        {
            IEnumerable<TextElement> targets=scope switch{"table"=>[table],"group"=>table.RowGroups,"row"=>table.RowGroups.SelectMany(group=>group.Rows),_=>table.RowGroups.SelectMany(group=>group.Rows).SelectMany(row=>row.Cells)};
            foreach(var target in targets)Css.SetStyle(target,Contents+(scope=="table"&&collapsed?"border-collapse:collapse;":""));
        }
        if(scope!="cell")foreach(var cell in table.RowGroups.SelectMany(group=>group.Rows).SelectMany(row=>row.Cells))Css.SetStyle(cell,"padding:3px;border:2px solid #146342;background:#eef5f0;");
        return doc;
    }
    private static Table TableFor(string scope)
    {
        var table=new Table();var group=new TableRowGroup();table.RowGroups.Add(group);
        if(scope=="row"){var row=new TableRow();foreach(var value in new[]{"A","B","C","D"})row.Cells.Add(new TableCell(new Paragraph(new Run(value))));group.Rows.Add(row);}
        else foreach(var pair in new[]{new[]{"A","B"},new[]{"C","D"}}){var row=new TableRow();group.Rows.Add(row);if(scope=="cell"){var cell=new TableCell();foreach(var value in pair)cell.Blocks.Add(new Paragraph(new Run(value)));row.Cells.Add(cell);}else foreach(var value in pair)row.Cells.Add(new TableCell(new Paragraph(new Run(value))));}
        return table;
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
            var run=new Run(original);var first=new Paragraph(run);var second=new Paragraph(new Run("切回表格后，仍可继续阅读和选择这些内容。"));
            var details=new Table();var group=new TableRowGroup();var row=new TableRow();var one=new TableCell(first);var two=new TableCell(second);row.Cells.Add(one);row.Cells.Add(two);group.Rows.Add(row);details.RowGroups.Add(group);Css.SetClass(details,"details");
            var doc=Document(new Paragraph(new Run("今天记录一段沿途的发现。")),details,new Paragraph(new Run("接下来，继续阅读下一段。")));
            Css.SetStyle(doc,"font-family:'PingFang SC';font-size:18px;line-height:28px;white-space:normal;color:#233c32;");
            Css.SetStyleSheet(doc,"Paragraph,Table,TableCell{margin:0;padding:0;border:0;}.details{margin:12px 0;border:1px solid #aac6b6;background:#eef5f0;}TableCell{padding:10px;}");
            using var body=new FlowDocumentPresentation();body.SetDocument(doc);Css.SetClass(body,"document");
            var root=new StackPanel{Background=Brushes.White};Css.SetClass(root,"root");root.Children.Add(Label("让表格自然连读","title"));root.Children.Add(Label("切换排版，原文与选择位置保持不变。","description"));root.Children.Add(body);
            var actions=new StackPanel();Css.SetClass(actions,"actions");root.Children.Add(actions);var toggle=Action("表格阅读",0);var edit=Action("补写说明",1);var restore=Action("恢复原文",2);actions.Children.Add(toggle);actions.Children.Add(edit);actions.Children.Add(restore);
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
                foreach(var source in new TextElement[]{details,group,row,one,two,first,second})Css.SetStyle(source,shown?"display:contents;":"");
                ((TextBlock)toggle.Child!).Text=shown?"表格阅读":"连续阅读";AutomationProperties.SetName(toggle,shown?"表格阅读":"连续阅读");
                status.Text=shown?"已连续排版，原文和选择位置保持不变。":"已恢复表格，可按单元格阅读与选择。";
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
                var provider=(ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;var exposed=provider.DocumentRange.GetText(-1);var matches=exposed.Contains(run.Text)&&exposed.Contains("切回表格后")&&(!shown||!exposed.Contains(run.Text+"\n"));
                if(!baseline)
                {
                    if(!matches||body.Paragraphs.Count!=4||!ReferenceEquals(run,first.Inlines[0])||!ReferenceEquals(first,one.Blocks[0])||!ReferenceEquals(second,two.Blocks[0]))throw new InvalidOperationException("Display state or original source differs");
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
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_TABLE_CONTENTS_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_TABLE_CONTENTS_EVIDENCE_DIR") is not { Length: > 0 } path) return;
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
