using Jalium.UI.Automation;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record InputSpacingCase(string Name, string Kind, string Text = "A A", double Letter = 4, double Word = 6,
        bool Wrap = false, bool Hosted = false, bool Rtl = false, TextAlignment Alignment = TextAlignment.Left, bool Effects = false, bool Gradient = false, bool Stretch = false);
    private static InputSpacingCase[] InputSpacingCases() =>
    [
        new("auto-gradient", "auto", Gradient:true), new("number-gradient", "number", "121", Gradient:true),
        new("text-gradient", "text", Gradient:true), new("password-gradient", "password", Gradient:true), new("revealed-gradient", "revealed", Gradient:true),
        new("text-gradient-wrap", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true, Gradient:true),
        new("text-gradient-emphasis", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true, Effects:true, Gradient:true),
        new("text-stretch-emphasis", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true, Effects:true, Stretch:true),
        new("auto-effects", "auto", Effects:true), new("number-effects", "number", "121", Effects:true),
        new("text-effects", "text", Effects:true), new("password-effects", "password", Effects:true), new("revealed-effects", "revealed", Effects:true),
        new("text-wrap-effects", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true, Effects:true),
        new("auto", "auto"), new("number", "number", "121"), new("text", "text"),
        new("password", "password"), new("revealed", "revealed"),
        new("rich", "rich"), new("rich-shared", "rich-shared"),
        new("auto-hosted", "auto", Hosted:true), new("text-hosted", "text", Hosted:true), new("number-hosted", "number", "121", Hosted:true),
        new("auto-negative", "auto", Letter:-3, Word:-2), new("text-negative", "text", Letter:-3, Word:-2),
        new("auto-clusters", "auto", "A\u0308 👩‍👩‍👧‍👦 汉字"), new("text-clusters", "text", "A\u0308 👩‍👩‍👧‍👦 汉字"),
        new("auto-rtl", "auto", "ab אבג العربية cd", Rtl:true), new("text-rtl", "text", "ab אבג العربية cd", Rtl:true),
        new("auto-ligature", "auto", "Office affinity"), new("text-ligature", "text", "Office affinity"),
        new("auto-center", "auto", Alignment:TextAlignment.Center), new("text-center", "text", Alignment:TextAlignment.Center),
        new("auto-right", "auto", Rtl:true, Alignment:TextAlignment.Right), new("text-right", "text", Rtl:true, Alignment:TextAlignment.Right),
        new("text-wrap", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true),
        new("text-wrap-hosted", "text", "A A A A A A A A A A A A A A A A A A A A A A A A", Wrap:true, Hosted:true),
    ];

    private static (StackPanel Root, Control Input, Func<string> Source) MakeInputSpacingScene(InputSpacingCase row, int width, bool actual)
    {
        Control input; Func<string> source;
        if (row.Kind == "auto") { var value = new AutoCompleteBox { Text = row.Text }; input = value; source = () => value.Text; }
        else if (row.Kind == "number") { var value = new NumberBox { Value = 121, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden }; input = value; source = () => value.Text; }
        else if (row.Kind is "password" or "revealed") { var value = new PasswordBox { Password = row.Text, IsPasswordRevealed = row.Kind == "revealed" }; input = value; source = () => value.Password; }
        else if (row.Kind is "rich" or "rich-shared")
        {
            var paragraph = new Paragraph(new Run(row.Text));
            var style = row.Kind == "rich-shared" ? "display:flex;" : "";
            if (!actual) style += $"letter-spacing:{row.Letter.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;word-spacing:{row.Word.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;";
            Css.SetStyle(paragraph, style);
            var value = new RichTextBox { Document = new FlowDocument(paragraph) { FontFamily = new FontFamily("Georgia"), FontSize = 24 } };
            input = value; source = () => value.Document.GetText().TrimEnd('\n');
        }
        else { var value = new TextBox { Text = row.Text, TextWrapping = row.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap }; input = value; source = () => value.Text; }
        input.Width = width; input.Height = 200; input.FontFamily = new FontFamily("Georgia"); input.FontSize = 24;
        input.Foreground = row.Gradient ? new LinearGradientBrush(Color.FromRgb(35,60,50), Color.FromRgb(63,89,160), 0) : new SolidColorBrush(Color.FromRgb(35,60,50)); input.Background = null;
        if(row.Stretch) input.FontStretch=FontStretches.Condensed;
        input.FlowDirection = row.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (row.Hosted)
        {
            var template = new ControlTemplate(input.GetType()); template.SetVisualTree(() => new Border { Name = "PART_ContentHost" }); input.Template = template;
        }
        input.ClearValue(Control.PaddingProperty); input.ClearValue(Control.BorderThicknessProperty);
        if (input is TextBox text) text.TextAlignment = row.Alignment;
        Css.SetStyle(input, "padding:0;border:0;text-align:" + row.Alignment.ToString().ToLowerInvariant() + ";" +
            (row.Effects ? "text-emphasis:dot" + (row.Gradient?" #233c32":"") + ";" + (actual ? "text-decoration:underline;text-shadow:1px 2px 0 currentColor;" : "") : "") +
            (actual ? $"letter-spacing:{row.Letter.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;word-spacing:{row.Word.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;" : ""));
        var root = new StackPanel { Width = width, Height = 220 }; root.Children.Add(input); FirstLetterLayout(root,width,220);
        return (root,input,source);
    }

    internal static int RunInputSpacing()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;var skipped=new List<string>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            {
                context.DefaultRenderingEngine=engine;
                foreach(var row in SelectionFilter(InputSpacingCases(),"JALIUM_CSS_INPUT_SPACING_CASES",row=>row.Name))
                foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})
                {
                    var label=$"input-spacing-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}";var errors=new List<string>();var largest=0;var differences=0;
                    var scenePool=PushPool();
                    try
                    {
                        var actual=MakeInputSpacingScene(row,width,true);var ordinary=MakeInputSpacingScene(row,width,false);
                        byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,220*dpi,dpi,dc=>
                        {
                            dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,220));
                            if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();draw(recorder);var drawing=recorder.Commit();if(row.Gradient&&!drawing.IsFullyRecordable)errors.Add("Supported gradient text requires retained replay");if(drawing.IsFullyRecordable)DrawingReplayer.Replay(drawing,dc);else draw(dc);}else draw(dc);
                        },()=>true,context)??throw new Exception("Native input-spacing capture unavailable"));
                        if(Environment.GetEnvironmentVariable("JALIUM_CSS_SPACING_TRACE")=="1" && width==312 && dpi==1 && !recorded)
                        {
                            var first=new InputSpacingTrace();actual.Root.Render(first);
                            var second=new InputSpacingTrace();ordinary.Root.Render(new InputSpacingReference(second,row,width));
                            Console.WriteLine("TRACE "+JsonSerializer.Serialize(new{label,actual=first.Rows,expected=second.Rows}));
                        }
                        var pixels=Capture(dc=>actual.Root.Render(dc));var expected=Capture(dc=> { if(row.Kind is "rich" or "rich-shared") ordinary.Root.Render(dc); else ordinary.Root.Render(new InputSpacingReference(dc,row,width)); });
                        if(pixels.Distinct().Take(17).Count()<17)errors.Add("Native text produced no glyph ink");
                        for(var i=0;i<pixels.Length;i++){var delta=Math.Abs(pixels[i]-expected[i]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                        if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                        if(largest>3||width==312&&dpi==1&&!recorded){Save(pixels,width*dpi,220*dpi,label+".png");Save(expected,width*dpi,220*dpi,label+"-expected.png");}
                        if(actual.Source()!=row.Text||ordinary.Source()!=row.Text)errors.Add("Original editable source changed");
                    }
                    catch(Exception error){errors.Add(error.ToString());}
                    finally{PopPool(scenePool);}
                    if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join("; ",errors));
                    if(records.Count%8==0){GC.Collect();GC.WaitForPendingFinalizers();}
                }
            }
            File.WriteAllText(Path.Combine(Output,"input-spacing.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,skipped,recordingScope="Supported gradient text must replay retained snapshots; other unsupported content uses direct fallback.",tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Ordinary untracked input body is replaced by the existing TextBlock layout with explicit spacing dependency properties, preserving the input's independently captured origin and clip. Source strings are verified unchanged."}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    internal static int RunInputSpacingSelection()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;var skipped=new List<string>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            foreach(var kind in new[]{"auto","number","text","password","revealed","text-wrap"})
            foreach(var gradient in new[]{false,true})foreach(var effects in new[]{false,true})
            foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})
            {
                context.DefaultRenderingEngine=engine;var errors=new List<string>();var largest=0;
                var label=$"spacing-selection-{backend}-{engine}-{kind}-{gradient}-{effects}-{width}-{dpi}-{recorded}";
                var scenePool=PushPool();
                try
                {
                    var wrapped=kind=="text-wrap";
                    var row=new InputSpacingCase(label,wrapped?"text":kind,kind=="number"?"121":wrapped?"A A A A A A A A A A A A A A A A A A A A A A A A":"A A",Wrap:wrapped,Effects:effects,Gradient:gradient);
                    var actual=MakeInputSpacingScene(row,width,true);var expected=MakeInputSpacingScene(row,width,true);
                    void Select(Control input,bool css)
                    {
                        if(input is PasswordBox password){password.IsInactiveSelectionHighlightEnabled=true;password.SelectAll();
                            if(!css){password.SelectionBrush=new SolidColorBrush(Color.FromRgb(20,99,66));password.SelectionTextBrush=new SolidColorBrush(Color.FromRgb(246,255,251));password.SelectionOpacity=1;}}
                        else if(input is Jalium.UI.Controls.Primitives.TextBoxBase text){text.IsInactiveSelectionHighlightEnabled=true;text.SelectAll();
                            if(!css){text.SelectionBrush=new SolidColorBrush(Color.FromRgb(20,99,66));text.SelectionTextBrush=new SolidColorBrush(Color.FromRgb(246,255,251));text.SelectionOpacity=1;}}
                    }
                    Select(actual.Input,true);Select(expected.Input,false);Css.SetClass(actual.Input,"spacing-subject");
                    Css.GetStyleSheets(actual.Root).Add(CssStyleSheet.Parse(".spacing-subject::selection{color:#f6fffb;background:#146342;}"));
                    FirstLetterLayout(actual.Root,width,220);FirstLetterLayout(expected.Root,width,220);
                    byte[] Capture(FrameworkElement root)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,220*dpi,dpi,dc=>
                    {
                        dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,220));
                        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);var drawing=recorder.Commit();if(row.Gradient&&!drawing.IsFullyRecordable)errors.Add("Supported gradient text requires retained replay");if(drawing.IsFullyRecordable)DrawingReplayer.Replay(drawing,dc);else root.Render(dc);}else root.Render(dc);
                    },()=>true,context)??throw new Exception("Native selection capture unavailable"));
                    var pixels=Capture(actual.Root);var reference=Capture(expected.Root);
                    for(var i=0;i<pixels.Length;i++)largest=Math.Max(largest,Math.Abs(pixels[i]-reference[i]));
                    if(largest>3){errors.Add($"Maximum channel difference {largest}");Save(pixels,width*dpi,220*dpi,label+".png");Save(reference,width*dpi,220*dpi,label+"-expected.png");}
                    if(actual.Source()!=row.Text||expected.Source()!=row.Text)errors.Add("Selection changed source text");
                }
                catch(Exception error){errors.Add(error.ToString());}
                    finally{PopPool(scenePool);}
                if(errors.Count>0)failures++;records.Add(new{label,largest,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join("; ",errors));
                    if(records.Count%8==0){GC.Collect();GC.WaitForPendingFinalizers();}
            }
            File.WriteAllText(Path.Combine(Output,"input-spacing-selection.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,skipped,recordingScope="Supported gradient text must replay retained snapshots; other unsupported content uses direct fallback.",tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="CSS selection colors compared to public native selection brush properties on independently created spaced editors."}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    internal static int RunInputSpacingLifecycle()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;var skipped=new List<string>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            foreach(var kind in new[]{"auto","number","text","password","revealed","rich","rich-shared","text-wrap"})
            foreach(var gradient in new[]{false,true})foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;var errors=new List<string>();var largest=0;
                var retainedReplayChecked=false;var directFallbackChecked=false;
                var label=$"spacing-lifecycle-{backend}-{engine}-{kind}-{gradient}-{width}-{dpi}";
                var scenePool=PushPool();
                try
                {
                    var wrapped=kind=="text-wrap";
                    var row=new InputSpacingCase(label,wrapped?"text":kind,kind=="number"?"121":wrapped?"A A A A A A A A A A A A A A A A A A A A A A A A":"A A",Wrap:wrapped,Gradient:gradient);
                    var scene=MakeInputSpacingScene(row,width,true);
                    byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,220*dpi,dpi,dc=>
                    {dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,220));draw(dc);},()=>true,context)??throw new Exception("Native lifecycle capture unavailable"));
                    var recorder=new DrawingRecorder();recorder.BindWholeFrame();scene.Root.Render(recorder);var recording=recorder.Commit();
                    var before=Capture(dc=>scene.Root.Render(dc));retainedReplayChecked=recording.IsFullyRecordable;
                    if(gradient&&!recording.IsFullyRecordable)errors.Add("Supported gradient lifecycle requires retained replay");
                    var replay=Capture(dc=>{if(recording.IsFullyRecordable)DrawingReplayer.Replay(recording,dc);else scene.Root.Render(dc);});
                    for(var i=0;i<before.Length;i++)largest=Math.Max(largest,Math.Abs(before[i]-replay[i]));
                    if(largest>3)errors.Add("Initial recording differs from direct paint");
                    if(scene.Source()!=row.Text)errors.Add("Initial recording changed source");
                    switch(scene.Input)
                    {
                        case NumberBox input:input.Value=212;break;
                        case TextBox input:input.Text+="Q";break;
                        case PasswordBox input:input.Password+="Q";break;
                        case RichTextBox input:((Run)((Paragraph)input.Document.Blocks[0]).Inlines[0]).Text+="Q";break;
                    }
                    if(scene.Input.Foreground is SolidColorBrush solid)solid.Color=Color.FromRgb(32,83,122);
                    else if(scene.Input.Foreground is GradientBrush brush)brush.GradientStops[0].Color=Color.FromRgb(32,83,122);
                    Css.SetStyle(scene.Input,"padding:0;border:0;letter-spacing:8px;word-spacing:2px;");
                    // Retire bounded layout caches, then force safe-handle finalizers.
                    // The old recording must remain independently drawable.
                    for(var font=20;font<39;font++)
                    {
                        scene.Input.FontSize=font;FirstLetterLayout(scene.Root,width,220);
                        if(scene.Input is Jalium.UI.IImeSupport ime)ime.GetImeCaretRectangle();
                    }
                    var source=scene.Source();var changed=Capture(dc=>scene.Root.Render(dc));
                    if(before.SequenceEqual(changed))errors.Add("Changed text/font/spacing/brush reused stale paint");
                    GC.Collect();GC.WaitForPendingFinalizers();
                    var immutableReference=recording.IsFullyRecordable?before:changed;
                    directFallbackChecked=!recording.IsFullyRecordable;
                    replay=Capture(dc=>{if(recording.IsFullyRecordable)DrawingReplayer.Replay(recording,dc);else scene.Root.Render(dc);});
                    for(var i=0;i<immutableReference.Length;i++)largest=Math.Max(largest,Math.Abs(immutableReference[i]-replay[i]));
                    if(largest>3){errors.Add($"Retained recording or current direct fallback changed: maximum channel difference {largest}");Save(before,width*dpi,220*dpi,label+"-before.png");Save(replay,width*dpi,220*dpi,label+"-replay.png");}
                    if(scene.Source()!=source)errors.Add("Replaying retired layout changed current source");
                }
                catch(Exception error){errors.Add(error.ToString());}
                    finally{PopPool(scenePool);}
                if(errors.Count>0)failures++;records.Add(new{label,largest,retainedReplayChecked,directFallbackChecked,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join("; ",errors));
                    if(records.Count%8==0){GC.Collect();GC.WaitForPendingFinalizers();}
            }
            File.WriteAllText(Path.Combine(Output,"input-spacing-lifecycle.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,skipped,recordingScope="Supported gradient text must replay retained snapshots; other unsupported content uses direct fallback.",tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Recordable native glyphs survive source, font, spacing and brush changes, cache retirement and finalization. Drawings marked unrecordable use current direct paint after the same changes."}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    internal static int RunInputSpacingGallery()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        var gradientGallery=Environment.GetEnvironmentVariable("JALIUM_CSS_SOFTWARE_GRADIENT_GALLERY")=="1";
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            foreach(var dark in new[]{false,true})foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;const int height=940;
                var ink=dark?Color.FromRgb(155,213,176):Color.FromRgb(20,99,66);
                var muted=dark?Color.FromRgb(175,208,188):Color.FromRgb(68,100,84);
                var paper=dark?Color.FromRgb(24,37,31):Color.FromRgb(243,247,241);
                var card=dark?Color.FromRgb(32,54,42):Colors.White;
                var border=dark?Color.FromRgb(70,103,83):Color.FromRgb(214,226,217);
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(paper)};
                root.Children.Add(new TextBlock{Text=gradientGallery?"渐变文字，后端对照":"输入中的字距与词距",FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(18,26,18,8)});
                root.Children.Add(new TextBlock{Text=gradientGallery?"字形、色彩与排版的原生对照":"文字、选区与光标共享文本布局",FontSize=13,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,0,18,18)});
                var kinds=new[]{"text","auto","number","password","revealed","rich"};
                var labels=new[]{"多行文本 · 自然换行","自动补全 · 强调标记","数值输入 · 自定义字距","密码输入 · 遮蔽源内容","显示密码 · 保留词间距","富文本 · 统一排版"};
                for(var index=0;index<kinds.Length;index++)
                {
                    var kind=kinds[index];var words=kind=="number"?"121":kind is "password" or "revealed"?"Ocean View":kind=="auto"?"字间 留白，自然 阅读。":"海风 吹过 窗边，文字 自然 换行。";
                    var scene=MakeInputSpacingScene(new("gallery",kind,words,Letter:2,Word:5,Wrap:kind=="text",Effects:kind=="auto",Gradient:gradientGallery),width-64,true);
                    scene.Root.Height=68;scene.Input.Height=68;scene.Input.FontSize=18;scene.Input.Foreground=gradientGallery
                        ? new LinearGradientBrush(dark?Color.FromRgb(239,174,124):Color.FromRgb(153,62,37),
                            dark?Color.FromRgb(104,191,216):Color.FromRgb(32,83,122),new Point(0,0),new Point(1,0))
                        : new SolidColorBrush(ink);
                    if(kind=="auto")Css.SetStyle(scene.Input,"padding:0;border:0;letter-spacing:2px;word-spacing:5px;text-emphasis:filled dot;");
                    if(scene.Input is RichTextBox rich){rich.Document.FontSize=18;rich.Document.Foreground=scene.Input.Foreground;}
                    AutomationProperties.SetName(scene.Input,labels[index]);
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=labels[index],FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(0,0,0,8)});content.Children.Add(scene.Root);
                    root.Children.Add(new Border{Child=content,Height=116,Background=new SolidColorBrush(card),BorderBrush=new SolidColorBrush(border),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                root.Children.Add(new TextBlock{Text="排版改变显示，输入内容保持原样。",FontSize=12,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,14,18,0)});
                FirstLetterLayout(root,width,height);var label=$"input-spacing-gallery-{backend}-{engine}-{(dark?"dark":"light")}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Input spacing gallery capture unavailable")),width*dpi,height*dpi,label);
                records.Add(new{label,width,height,dpi,dark,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"input-spacing-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{PopPool(pool);}
    }

    private sealed class InputSpacingTrace:DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset{get;set;}
        private Point _translation;
        private readonly Stack<Point> _stack=[];
        internal readonly List<object> Rows=[];
        public override void DrawText(FormattedText text,Point point)
        {
            var native=text.PlatformTextLine as NativeTextParagraph.Line;
            Rows.Add(new{text=text.Text,size=text.FontSize,family=text.FontFamily,x=point.X+Offset.X+_translation.X,y=point.Y+Offset.Y+_translation.Y,
                text.Ascent,text.Descent,text.Width,text.Height,text.MaxTextWidth,text.MaxTextHeight,text.NativeNoWrap,ink=text.Foreground?.GetType().Name,nativeBaseline=native?.Metrics.Baseline,nativeHeight=native?.Metrics.Line.Height});
        }
        public override void PushTransform(Transform transform){_stack.Push(_translation);if(transform is TranslateTransform t)_translation=new(_translation.X+t.X,_translation.Y+t.Y);}
        public override void PushClip(Geometry g)=>_stack.Push(_translation);public override void PushOpacity(double o)=>_stack.Push(_translation);public override void Pop(){_translation=_stack.Pop();}
        public override void DrawRectangle(Brush? b,Pen? p,Rect r){}public override void DrawGeometry(Brush? b,Pen? p,Geometry g){}public override void DrawLine(Pen p,Point a,Point b){}
        public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y){}public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y){}public override void DrawImage(ImageSource i,Rect r){}public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c){}public override void Close(){}
    }

    private sealed class InputSpacingReference(DrawingContext inner,InputSpacingCase row,int viewportWidth):DrawingContextAdapter,IOffsetDrawingContext
    {
        private bool _painted;
        public Point Offset{get=>(inner as IOffsetDrawingContext)?.Offset??default;set{if(inner is IOffsetDrawingContext offset)offset.Offset=value;}}
        public override void DrawText(FormattedText text,Point point)
        {
            if (row.Effects && (text.FontSize < 24 || _painted)) return;
            _painted = true;
            var metrics=TextMeasurement.GetFontMetrics("Georgia",text.FontSize,text.FontWeight,text.FontStyle);
            var reference=new TextBlock{Text=text.Text,FontFamily=new FontFamily("Georgia"),FontSize=text.FontSize,FontWeight=FontWeight.FromOpenTypeWeight(text.FontWeight),FontStyle=FontStyle.FromOpenTypeStyle(text.FontStyle),
                FontStretch=row.Stretch?FontStretches.Condensed:FontStretches.Normal,Foreground=text.Foreground,TextWrapping=row.Wrap?TextWrapping.Wrap:TextWrapping.NoWrap,TextAlignment=row.Alignment,FlowDirection=row.Rtl?FlowDirection.RightToLeft:FlowDirection.LeftToRight,
                LineHeight=row.Effects?Math.Round(metrics.LineHeight):metrics.LineHeight,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
            reference.SetValue(CssFlowProperties.LetterSpacingProperty,row.Letter);reference.SetValue(CssFlowProperties.WordSpacingProperty,CssLayoutLength.Px(row.Word));reference.SetValue(CssFlowProperties.WhiteSpaceProperty,CssWhiteSpaceCollapse.Preserve);
            reference.SetValue(CssFlowProperties.TextWrapModeProperty,row.Wrap?CssTextWrapMode.Wrap:CssTextWrapMode.NoWrap);
            FormattedText? opaqueMark = null;
            double markHeight = 0;
            if(row.Effects)
            {
                var baseSource=reference.FontFamily.GetRenderingSource(reference);
                var ruby=new FontFeature(FontFeature.TagOf("ruby"),1);
                var family=FontFeatureRenderingSource.TryUnwrap(baseSource,out var features,out var baseFamily,out var caps,out var fontContext)
                    ?FontFeatureRenderingSource.Wrap(baseFamily,FontFeatureRenderingSource.Merge(features,[ruby]),caps,fontContext)
                    :FontFeatureRenderingSource.Wrap(baseSource,[ruby]);
                opaqueMark=new FormattedText("•",family,12){Foreground=row.Gradient?new SolidColorBrush(Color.FromRgb(35,60,50)):text.Foreground};TextMeasurement.MeasureText(opaqueMark);
                markHeight=Math.Max(opaqueMark.Height,opaqueMark.Ascent+opaqueMark.Descent);
                // Input rows reserve integral annotation bands. TextBlock expands
                // CSS half-leading, so derive its line-height from the same
                // independently measured font extents and required row advance.
                var rowAdvance=Math.Round(metrics.LineHeight)+Math.Ceiling(markHeight);
                var cssLineHeight=row.Stretch||row.Gradient?rowAdvance:2*rowAdvance-metrics.Ascent-metrics.Descent-2*markHeight;
                reference.LineHeight=cssLineHeight;
                Css.SetStyle(reference,"line-height:" + cssLineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "px;" + (row.Stretch||row.Gradient?"":"text-emphasis:dot;") + "text-decoration:underline;");
            }
            reference.Measure(new(viewportWidth,double.PositiveInfinity));reference.Arrange(new(0,0,viewportWidth,Math.Max(1,reference.DesiredSize.Height)));
            var baseline = text.PlatformTextLine is NativeTextParagraph.Line native ? native.Metrics.Baseline : metrics.Ascent;
            var referenceBaseline = reference.GetFirstBaselineOffset(reference.DesiredSize.Height);
            CssTextShadowPainter.Capture? shadow = null;
            if(row.Effects)
            {
                var source=new TextBlock{Foreground=text.Foreground};Css.SetStyle(source,"text-shadow:1px 2px 0 currentColor;");
                source.Measure(new(viewportWidth,220));CssEvaluationScheduler.FlushIfPending(Jalium.UI.Threading.Dispatcher.CurrentDispatcher);
                var spans=reference.GetSourceRangeSegments(0,text.Text.Length);
                var advance=spans.Count==0?0:spans.Max(span=>span.EndX)-spans.Min(span=>span.StartX);
                shadow=CssTextShadowPainter.Begin(inner,source,new Rect(point.X-text.FontSize,
                    point.Y-Math.Ceiling(markHeight)-text.FontSize,advance+2*text.FontSize,
                    Math.Max(reference.DesiredSize.Height,Math.Round(metrics.LineHeight)+Math.Ceiling(markHeight))+2*text.FontSize));
            }
            // Anchor the independent layout in the input's physical coordinates.
            var offsetContext=inner as IOffsetDrawingContext;
            var previousOffset=offsetContext?.Offset??default;
            if(offsetContext is not null)offsetContext.Offset=new(previousOffset.X+point.X,previousOffset.Y+point.Y+baseline-referenceBaseline);
            else inner.PushTransform(new TranslateTransform(point.X,point.Y+baseline-referenceBaseline));
            try
            {
                reference.Render(inner);
                if(row.Effects && (row.Kind == "password" || row.Stretch || row.Gradient))
                {
                    // Opaque masks annotate every visible unit. The stretched
                    // fallback uses unrounded source edges for mark centers;
                    // TextBlock selection rectangles round those edges to pixels.
                    var mark=opaqueMark!;
                    for(var index=0;index<text.Text.Length;index++)
                    {
                        if(row.Kind!="password"&&!CssTextEmphasis.ShouldMark(text.Text.AsSpan(index,1)))continue;
                        foreach(var span in reference.GetSourceRangeSegments(index,1))
                        {
                            var x=(span.StartX+span.EndX-mark.WidthIncludingTrailingWhitespace)/2;
                            var y=span.Baseline-metrics.Ascent-markHeight;
                            inner.DrawText(mark,new(x,y));
                        }
                    }
                }
            }
            finally{if(offsetContext is not null)offsetContext.Offset=previousOffset;else inner.Pop();shadow?.End();}
        }
        public override void DrawRectangle(Brush? b,Pen? p,Rect r)=>inner.DrawRectangle(b,p,r);public override void DrawGeometry(Brush? b,Pen? p,Geometry g)=>inner.DrawGeometry(b,p,g);
        public override void DrawLine(Pen p,Point a,Point b)=>inner.DrawLine(p,a,b);public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y)=>inner.DrawRoundedRectangle(b,p,r,x,y);
        public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y)=>inner.DrawEllipse(b,p,q,x,y);public override void DrawImage(ImageSource i,Rect r)=>inner.DrawImage(i,r);public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c)=>inner.DrawBackdropEffect(r,e,c);
        public override void PushClip(Geometry g)=>inner.PushClip(g);public override void PushTransform(Transform t)=>inner.PushTransform(t);public override void PushOpacity(double d)=>inner.PushOpacity(d);public override void Pop()=>inner.Pop();public override void Close(){}
    }
}
