using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record InputInkCase(string Name,string Css,Color Expected,bool Inherited=false,
        bool Changed=false,bool Disabled=false,bool Local=false,bool Effects=false,bool Selected=false,bool Runs=false);

    private static InputInkCase[] InputInkCases() =>
    [
        new("direct","color:#146342;",Color.FromRgb(20,99,66)),
        new("inherited","color:#20537a;",Color.FromRgb(32,83,122),Inherited:true),
        new("alpha","color:rgba(20,99,66,.5);",Color.FromArgb(128,20,99,66)),
        new("transparent","color:transparent;",Colors.Transparent),
        new("variables","color:var(--ink);",Color.FromRgb(32,83,122),Changed:true),
        new("disabled","",Color.FromRgb(102,45,112),Disabled:true),
        new("local","color:#146342 !important;",Color.FromRgb(137,65,18),Local:true),
        new("effects","color:#146342;",Color.FromRgb(20,99,66),Effects:true),
        new("selection","color:#146342;",Color.FromRgb(20,99,66),Effects:true,Selected:true),
        new("runs","color:#146342;",Color.FromRgb(20,99,66),Runs:true),
    ];

    private static bool InputInkCaseApplies(string kind,InputInkCase row) => row.Runs
        ? kind is "rich" or "rich-shared"
        : !(row.Effects && kind is "auto" or "number");

    private static (StackPanel Root,Control Input,Func<string> Source) MakeInputInkScene(
        string kind,InputInkCase row,int width,bool reference)
    {
        var root=new StackPanel{Width=width,Height=220};
        Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;--ink:#146342;");
        Control input;Func<string> source;
        if(kind=="text") { var value=new TextBox{Text="Office affinity",TextWrapping=TextWrapping.Wrap,IsInactiveSelectionHighlightEnabled=true};input=value;source=()=>value.Text; }
        else if(kind=="auto") { var value=new AutoCompleteBox{Text="Office affinity"};input=value;source=()=>value.Text; }
        else if(kind=="number") { var value=new NumberBox{Value=12345};input=value;source=()=>value.Text; }
        else if(kind=="password") { var value=new PasswordBox{Password="A 、😀e\u0301",IsInactiveSelectionHighlightEnabled=true};input=value;source=()=>value.Password; }
        else
        {
            var paragraph=new Paragraph();var first=new Run(row.Runs?"Office ":"Office affinity");
            if(row.Runs)first.Foreground=new SolidColorBrush(Color.FromRgb(32,83,122));
            paragraph.Inlines.Add(first);Run? second=null;
            if(row.Runs){second=new Run("affinity");paragraph.Inlines.Add(second);}
            if(kind=="rich-shared")Css.SetStyle(paragraph,"display:flex;");
            var document=new FlowDocument(paragraph){FontFamily=new FontFamily("Georgia"),FontSize=24};
            var value=new RichTextBox{Document=document,IsInactiveSelectionHighlightEnabled=true};input=value;
            source=()=>{if(!ReferenceEquals(document,value.Document)||!ReferenceEquals(paragraph,first.Parent))throw new Exception("Rich source identity changed");return document.GetText().TrimEnd('\n');};
            // The independent reference supplies ordinary document/run colors directly,
            // bypassing the candidate owner's CSS-to-native foreground bridge.
            if(reference)
            {
                if(!row.Runs)document.Foreground=new SolidColorBrush(row.Expected);
                if(second is not null)second.Foreground=new SolidColorBrush(row.Expected);
            }
        }
        input.Width=width;input.Height=200;input.FontFamily=new FontFamily("Georgia");input.FontSize=24;
        input.Background=null;input.Padding=default;input.BorderThickness=default;
        Css.SetClass(input,"subject");root.Children.Add(input);
        if(reference || row.Local)input.Foreground=new SolidColorBrush(row.Expected);
        if(row.Inherited)Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;"+row.Css);
        else Css.SetStyle(input,row.Css+(row.Effects?"text-emphasis:'※';text-decoration:underline currentColor;text-shadow:1px 2px 0 currentColor;":""));
        if(row.Disabled)
        {
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject:disabled{color:#662d70;}"));
            input.IsEnabled=false;
        }
        if(row.Selected)
        {
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{color:currentColor;background:#dcebe4;text-decoration:underline currentColor;}"));
            if(input is TextBox text)text.Select(1,5);
            else if(input is PasswordBox password)password.Select(1,3);
            else if(input is Jalium.UI.Controls.Primitives.TextBoxBase single) { single.IsInactiveSelectionHighlightEnabled = true; single.SelectAll(); }
            else ((RichTextBox)input).SelectAll();
        }
        FirstLetterLayout(root,width,220);
        if(row.Changed)
        {
            root.Render(new SelectionReferenceGeometry());
            Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;--ink:#20537a;");
            FirstLetterLayout(root,width,220);
        }
        return(root,input,source);
    }

    internal static int RunInputForeground()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach(var engine in (backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
                .Where(value=>string.IsNullOrEmpty(requested)||value.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine=engine;
                foreach(var kind in SelectionFilter(new[]{"text","auto","number","password","rich","rich-shared"},"JALIUM_CSS_INPUT_INK_KINDS",value=>value))
                foreach(var row in SelectionFilter(InputInkCases(),"JALIUM_CSS_INPUT_INK_CASES",value=>value.Name).Where(value=>InputInkCaseApplies(kind,value)))
                foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})
                {
                    GC.Collect();GC.WaitForPendingFinalizers();var label=$"input-foreground-{backend}-{engine}-{kind}-{row.Name}-{width}-{dpi}-{recorded}";
                    var errors=new List<string>();var largest=0;var differences=0;
                    try
                    {
                        var actual=MakeInputInkScene(kind,row,width,false);var expected=MakeInputInkScene(kind,row,width,true);
                        var before=actual.Source();
                        byte[] Capture(StackPanel root)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,220*dpi,dpi,dc=>
                        {
                            dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,220));
                            if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}
                            else root.Render(dc);
                        },()=>true,context)??throw new Exception("Input foreground capture unavailable"));
                        var pixels=Capture(actual.Root);var reference=Capture(expected.Root);
                        for(var index=0;index<pixels.Length;index++){var delta=Math.Abs(pixels[index]-reference[index]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                        if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                        if(largest>3||width==312&&dpi==1&&!recorded){Save(pixels,width*dpi,220*dpi,label+".png");Save(reference,width*dpi,220*dpi,label+"-expected.png");}
                        if(actual.Source()!=before||expected.Source()!=before)errors.Add("Source changed while painting");
                    }
                    catch(Exception error){errors.Add(error.ToString());}
                    if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors});
                    Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                }
            }
            if(records.Count==0)throw new Exception("No input foreground comparisons requested");
            File.WriteAllText(Path.Combine(Output,"input-foreground.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),
                liveWindow=false,physicalInput=false,voiceOver=false,oracle="Ordinary native local foreground and explicit rich document/run colors; candidate paints CSS-computed owner colors. Same original text, geometry, effects and selection."}));
            return failures==0?0:1;
        }
        finally{context.Dispose();PopPool(pool);}
    }

    internal static int RunInputForegroundGallery()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var dark in new[]{false,true})foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;const int height=900;
                var ink=dark?Color.FromRgb(155,213,176):Color.FromRgb(20,99,66);
                var muted=dark?Color.FromRgb(175,208,188):Color.FromRgb(68,100,84);
                var paper=dark?Color.FromRgb(24,37,31):Color.FromRgb(243,247,241);
                var card=dark?Color.FromRgb(32,54,42):Colors.White;
                var border=dark?Color.FromRgb(70,103,83):Color.FromRgb(214,226,217);
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(paper)};
                Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;");
                root.Children.Add(new TextBlock{Text="输入颜色随样式变化",FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(18,26,18,8)});
                root.Children.Add(new TextBlock{Text="正文、着重号和选区使用同一颜色规则",FontSize=13,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,0,18,18)});
                var kinds=new[]{"text","auto","number","password","rich","rich-shared"};
                var labels=new[]{"文本输入","自动补全","数值输入","密码输入","富文本段落","带着重号的富文本"};
                for(var index=0;index<kinds.Length;index++)
                {
                    var kind=kinds[index];var color=dark?"#9bd5b0":"#146342";
                    var scene=MakeInputInkScene(kind,new("gallery","color:"+color+";",ink),width-64,false);
                    scene.Root.Height=46;scene.Input.Height=46;scene.Input.FontSize=16;
                    if(scene.Input is TextBox text)text.Text="文字颜色遵从当前样式。";
                    else if(scene.Input is AutoCompleteBox auto)auto.Text="输入内容，继续编辑。";
                    else if(scene.Input is RichTextBox rich)
                    {rich.Document.FontSize=16;((Run)((Paragraph)rich.Document.Blocks[0]).Inlines[0]).Text="文字与标记使用当前颜色。";}
                    if(kind=="rich-shared")Css.SetStyle(scene.Input,"color:"+color+";text-emphasis:filled dot;");
                    AutomationProperties.SetName(scene.Input,labels[index]);
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=labels[index],FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(0,0,0,10)});content.Children.Add(scene.Root);
                    root.Children.Add(new Border{Child=content,Height=108,Background=new SolidColorBrush(card),BorderBrush=new SolidColorBrush(border),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                root.Children.Add(new TextBlock{Text="样式只改变显示，输入内容保持原样。",FontSize=12,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,14,18,0)});
                FirstLetterLayout(root,width,height);var label=$"input-foreground-gallery-{backend}-{engine}-{(dark?"dark":"light")}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Input foreground gallery capture unavailable")),width*dpi,height*dpi,label);
                records.Add(new{label,width,height,dpi,dark,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"input-foreground-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{context.Dispose();PopPool(pool);}
    }
}
