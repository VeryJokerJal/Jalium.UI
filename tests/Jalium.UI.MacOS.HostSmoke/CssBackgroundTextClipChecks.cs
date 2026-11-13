using System.Security.Cryptography;
using System.Text.Json;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunBackgroundTextClip()
    {
        var pool = PushPool(); Send(Class("NSApplication"), Selector("sharedApplication"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        var rows = new List<TextClipCheckRow>(); int failures = 0; var savedDpi = FrameworkElement.LayoutDpiScale;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            using (var context = new RenderContext(backend, GpuPreference.Auto, engine))
            foreach (int dpi in new[] { 1, 2 })
            foreach (var text in new[] { "CSS Oe\u0301 世界", "مرحبا CSS", "אבג CSS" })
            foreach (var decoration in new[] { "", "text-decoration:underline wavy;", "text-emphasis:filled dot;" })
            {
                FrameworkElement.LayoutDpiScale = dpi;
                string label = $"{backend}-{engine}-{dpi}-{rows.Count}"; var errors = new List<string>();
                try
                {
                    TextBlock Make(bool clipped)
                    {
                        var block = new TextBlock { Text = text };
                        string clip = Environment.GetEnvironmentVariable("JALIUM_CSS_TEXT_CLIP_DISABLE") == "1" ? "border-box" : "text";
                        Css.SetClass(block, "test");
                        var sheet = CssStyleSheet.Parse(".test{box-sizing:border-box;width:300px;height:140px;padding:12px;font-family:Georgia;font-size:32px;" +
                            decoration + (clipped ? "color:transparent;background:red;background-clip:"+clip+";" : "color:white;") + "}");
                        if (sheet.Diagnostics.Count > 0) throw new Exception("CSS parse diagnostics");
                        Css.GetStyleSheets(block).Add(sheet); VisualTreeHelper.SetRootDpi(block,new DpiScale(dpi,dpi));
                        for (int i = 0; i < 4; i++) { Dispatcher.GetForCurrentThread().ProcessQueue(); block.Measure(new(300,140)); block.Arrange(new(0,0,300,140)); block.UpdateLayout(); }
                        return block;
                    }
                    var actual = Make(true); var expected = Make(false);
                    var current = RenderContext.Current;
                    byte[] Capture(Action<DrawingContext> paint, bool worker = false) => CaptureGradientRecording(context, engine, dpi, paint, worker, 300, 140);
                    var pixels = Capture(actual.Render);
                    using var referenceContext = RenderContext.CreateRasterizerContext();
                    var reference = CaptureGradientRecording(referenceContext, RenderingEngine.Auto, dpi, expected.Render, false, 300, 140, paintPaper: false);
                    var paper = Capture(_ => { });
                    for (int i = 0; i < reference.Length; i += 4)
                    {
                        int alpha = reference[i+3];
                        reference[i] = (byte)((paper[i] * (255-alpha) + 127) / 255);
                        reference[i+1] = (byte)((paper[i+1] * (255-alpha) + 127) / 255);
                        reference[i+2] = (byte)((255 * alpha + paper[i+2] * (255-alpha) + 127) / 255);
                        reference[i+3] = (byte)(alpha + (paper[i+3] * (255-alpha) + 127) / 255);
                    }
                    int largest = pixels.Zip(reference, (a,b) => Math.Abs(a-b)).Max();
                    int ink = 0; for (int i = 0; i < pixels.Length; i += 4) if (pixels[i+2] > 200 && pixels[i+1] < 120) ink++;
                    if (ink < 20) errors.Add("No text ink");
                    if (largest > 3) errors.Add("Glyph/decorations reference differs: " + largest);
                    if (!ReferenceEquals(current, RenderContext.Current)) errors.Add("Private rasterizer replaced current context");
                    var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); actual.Render(recorder); var buffer = recorder.Commit();
                    if (!buffer.IsFullyRecordable) errors.Add("Text clip is not fully recordable");
                    var replay = Capture(dc => DrawingReplayer.Replay(buffer, dc), true);
                    int replayDifference = replay.Zip(pixels, (a,b) => Math.Abs(a-b)).Max();
                    if (replayDifference > 0) errors.Add("Worker replay differs: " + replayDifference);
                    if (rows.Count < 12) { Save(pixels,300*dpi,140*dpi,label+"-actual.png"); Save(reference,300*dpi,140*dpi,label+"-reference.png"); }
                    rows.Add(new(label, largest, ink, buffer.IsFullyRecordable, Convert.ToHexString(SHA256.HashData(pixels)), errors));
                }
                catch (Exception error) { errors.Add(error.ToString()); rows.Add(new(label, 0, 0, false, null, errors)); }
                failures += errors.Count;
            }
            WriteTextClipChecks("background-text-clip.json", rows);
            Console.WriteLine($"background-clip:text {rows.Count} scenarios, {failures} failures");
            return failures == 0 ? 0 : 1;
        }
        finally { FrameworkElement.LayoutDpiScale = savedDpi; PopPool(pool); }
    }
    private sealed record TextClipCheckRow(string Label, int Largest, int Ink, bool FullyRecordable, string? Hash, List<string> Errors);
    private sealed record TextClipScene(FrameworkElement Root, Action Change, Func<string> Source);

    internal static int RunBackgroundTextClipScenes()
    {
        var pool = PushPool(); Send(Class("NSApplication"), Selector("sharedApplication")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        var rows = new List<TextClipCheckRow>(); var savedDpi = FrameworkElement.LayoutDpiScale;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            using (var context = new RenderContext(backend, GpuPreference.Auto, engine))
            foreach (int dpi in new[] { 1, 2 })
            foreach (string kind in new[] { "descendants", "float", "out-of-flow", "text", "password", "number", "auto", "rich", "placeholder", "gradient", "tiles", "layers", "first-line", "empty", "tall", "reentrant" })
            foreach (bool transform in new[] { false, true })
            {
                FrameworkElement.LayoutDpiScale = dpi;
                string label = $"scene-{backend}-{engine}-{dpi}-{kind}-{transform}"; var errors = new List<string>();
                int largest = 0, ink = 0; bool recordable = false; string? hash = null;
                try
                {
                    var actual = MakeTextClipScene(kind, false); var expected = MakeTextClipScene(kind, true);
                    var current = RenderContext.Current; var focus = Keyboard.FocusedElement; var source = actual.Source();
                    void Paint(DrawingContext dc, FrameworkElement root)
                    {
                        if (transform) dc.PushTransform(new MatrixTransform(new Matrix(.91,.12,-.08,.96,14,3)));
                        dc.PushClip(new RectangleGeometry(new Rect(2,2,296,136)));
                        dc.PushOpacity(.73); root.Render(dc); dc.Pop(); dc.Pop();
                        if (transform) dc.Pop();
                    }
                    byte[] Capture(Action<DrawingContext> paint, bool worker = false) => CaptureGradientRecording(context, engine, dpi, paint, worker, 300, 140);
                    var pixels = Capture(dc => Paint(dc,actual.Root)); hash = Convert.ToHexString(SHA256.HashData(pixels));
                    var paper = Capture(_ => { });
                    for (int i=0;i<pixels.Length;i+=4) if (Math.Abs(pixels[i]-paper[i])+Math.Abs(pixels[i+1]-paper[i+1])+Math.Abs(pixels[i+2]-paper[i+2])>20) ink++;
                    if (kind!="empty" && ink < 20) errors.Add("No clipped text paint");
                    if(kind=="empty" && !pixels.SequenceEqual(paper))errors.Add("Empty text painted a background rectangle");
                    if (!transform)
                    {
                        using var referenceContext = RenderContext.CreateRasterizerContext();
                        var mask = CaptureGradientRecording(referenceContext, RenderingEngine.Auto, dpi, expected.Root.Render, false, 300, 140, paintPaper:false);
                        string paintStyle = TextClipPaint(kind);
                        var paint = kind=="first-line" ? MakeTextClipScene(kind,false,boxPaint:true).Root : MakeTextClipPaint(paintStyle);
                        var paintPixels = CaptureGradientRecording(context, engine, dpi, paint.Render, false, 300, 140, paintPaper:false);
                        byte[] reference = (byte[])paper.Clone();
                        if (kind == "layers")
                        {
                            var lower = MakeTextClipPaint("background:linear-gradient(#24528c,#24528c);background-clip:content-box;");
                            reference = Capture(dc => { dc.PushClip(new RectangleGeometry(new Rect(2,2,296,136)));dc.PushOpacity(.73);lower.Render(dc);dc.Pop();dc.Pop(); });
                        }
                        for (int i=0;i<reference.Length;i+=4)
                        {
                            double coverage = mask[i+3]/255.0 * .73;
                            double alpha = paintPixels[i+3]/255.0 * coverage;
                            for (int channel=0;channel<3;channel++)
                                reference[i+channel]=(byte)Math.Clamp((int)Math.Round(paintPixels[i+channel]*(backend==RenderBackend.Software?alpha:coverage)+reference[i+channel]*(1-alpha)),0,255);
                            reference[i+3]=(byte)Math.Clamp((int)Math.Round(255*alpha+reference[i+3]*(1-alpha)),0,255);
                        }
                        // The enclosing viewport clip excludes the first/last two DIPs.
                        for (int y=2*dpi;y<138*dpi;y++) for(int x=2*dpi;x<298*dpi;x++) for(int c=0;c<4;c++)
                            largest=Math.Max(largest,Math.Abs(pixels[(y*300*dpi+x)*4+c]-reference[(y*300*dpi+x)*4+c]));
                        if (largest>3)
                        {
                            errors.Add("Independent white-ink/paint reference differs: "+largest);
                            Save(reference,300*dpi,140*dpi,label+"-reference.png");
                            Save(mask,300*dpi,140*dpi,label+"-white-ink.png");
                        }
                    }
                    RecordedDrawing Record() { var recorder=new DrawingRecorder();recorder.BindWholeFrame();Paint(recorder,actual.Root);return recorder.Commit(); }
                    var recording=Record(); recordable=recording.IsFullyRecordable;
                    if(!recordable) errors.Add("Text clip requires direct fallback");
                    void Compare(byte[] a,byte[] b,string why) { int difference=a.Zip(b,(x,y)=>Math.Abs(x-y)).Max();if(difference>0)errors.Add(why+": "+difference); }
                    Compare(pixels,Capture(dc=>DrawingReplayer.Replay(recording,dc),true),"Worker replay differs");
                    if(!ReferenceEquals(current,RenderContext.Current)||!ReferenceEquals(focus,Keyboard.FocusedElement)||source!=actual.Source())errors.Add("Rendering changed context/focus/source");
                    if(kind is "text" or "password" or "rich")
                    {
                        var unselected=CssBackgroundTextClip.Capture(actual.Root,new Rect(0,0,300,140),dpi);
                        if(actual.Root is PasswordBox password){password.IsInactiveSelectionHighlightEnabled=true;password.SelectAll();}
                        else if(actual.Root is Jalium.UI.Controls.Primitives.TextBoxBase editor){editor.IsInactiveSelectionHighlightEnabled=true;editor.SelectAll();}
                        var selected=CssBackgroundTextClip.Capture(actual.Root,new Rect(0,0,300,140),dpi);
                        if(unselected is null||selected is null||!unselected.Alpha.SequenceEqual(selected.Alpha))errors.Add("Selection changed text clip geometry");
                        var selectedPixels=Capture(dc=>Paint(dc,actual.Root));var selectedRecording=Record();
                        Compare(selectedPixels,Capture(dc=>DrawingReplayer.Replay(selectedRecording,dc),true),"Selected text replay differs");
                        if(actual.Root is PasswordBox resetPassword)resetPassword.Select(0,0);
                        else if(actual.Root is TextBox resetEditor)resetEditor.Select(0,0);
                        else if(actual.Root is RichTextBox resetRich)resetRich.Selection.Select(resetRich.Document.ContentStart,resetRich.Document.ContentStart);
                    }

                    actual.Change(); LayoutTextClipScene(actual.Root); TextMeasurement.ClearCache();GC.Collect();GC.WaitForPendingFinalizers();
                    Compare(pixels,Capture(dc=>DrawingReplayer.Replay(recording,dc),true),"Retained mask changed with live text");
                    var changed=Capture(dc=>Paint(dc,actual.Root));
                    if(changed.SequenceEqual(pixels))
                    {
                        errors.Add("Changed text retained stale mask: "+source+" => "+actual.Source());
                        Save(changed,300*dpi,140*dpi,label+"-changed.png");
                    }
                    var next=Record();Compare(changed,Capture(dc=>DrawingReplayer.Replay(next,dc),true),"New recording retained stale mask");
                    if(kind is "gradient" or "tiles" or "layers")
                    foreach(int width in new[]{180,420})
                    {
                        Css.SetStyle(actual.Root,"width:"+width+"px;");LayoutTextClipScene(actual.Root);
                        var resized=Capture(dc=>Paint(dc,actual.Root));var resizedRecording=Record();
                        Compare(resized,Capture(dc=>DrawingReplayer.Replay(resizedRecording,dc),true),"Resized background/text replay differs");
                        Compare(pixels,Capture(dc=>DrawingReplayer.Replay(recording,dc),true),"Resizing changed the retained mask");
                        if(dpi==1&&!transform)Save(resized,300,140,label+"-width"+width+".png");
                    }
                    if(dpi==1&&!transform)Save(pixels,300,140,label+".png");
                }
                catch(Exception error){errors.Add(error.ToString());}
                rows.Add(new(label,largest,ink,recordable,hash,errors));
            }
            WriteTextClipChecks("background-text-clip-scenes.json",rows);
            int failures=rows.Sum(row=>row.Errors.Count);Console.WriteLine($"background text clip scenes: {rows.Count}, failures {failures}");return failures==0?0:1;
        }
        finally { FrameworkElement.LayoutDpiScale = savedDpi; PopPool(pool); }
    }

    private sealed class ReentrantTextClipBlock : TextBlock
    {
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            Render(context); // Must retain Visual's cycle guard during the ink pass.
        }
    }

    private static string TextClipPaint(string kind) => kind switch
    {
        "gradient" or "layers" => "background:linear-gradient(115deg,#28bd82,#ce3d98);",
        "tiles" => "background:linear-gradient(90deg,#28bd82,#ce3d98) 7px 9px / 70px 56px space round;",
        _ => "background:red;",
    };
    private static TextClipScene MakeTextClipScene(string kind,bool reference,bool boxPaint=false)
    {
        FrameworkElement root; Action change; Func<string> source;
        var text=kind=="reentrant" ? new ReentrantTextClipBlock{Text="CSS Oe\u0301 世界"} : new TextBlock{Text="CSS Oe\u0301 世界"};
        if(kind=="text") { var box=new TextBox{Text="CSS Oe\u0301 世界"};root=box;change=()=>box.Text+=" Q";source=()=>box.Text; }
        else if(kind=="placeholder") { var box=new TextBox{PlaceholderText="CSS Oe\u0301 世界"};root=box;change=()=>box.PlaceholderText+=" Q";source=()=>box.Text+"|"+box.PlaceholderText; }
        else if(kind=="password") { var box=new PasswordBox{Password="A e\u0301世界"};root=box;change=()=>box.Password+="Q";source=()=>box.Password; }
        else if(kind=="number") { var box=new NumberBox{Value=12345,SpinButtonPlacementMode=NumberBoxSpinButtonPlacementMode.Hidden};root=box;change=()=>box.Value=2026;source=()=>box.Text; }
        else if(kind=="auto") { var box=new AutoCompleteBox{Text="CSS Oe\u0301 世界"};root=box;change=()=>box.Text+="Q";source=()=>box.Text; }
        else if(kind=="rich") { var run=new Run("CSS Oe\u0301 世界");var p=new Paragraph();p.Inlines.Add(run);var document=new FlowDocument();document.Blocks.Add(p);var box=new RichTextBox{Document=document};root=box;change=()=>run.Text+=" Q";source=()=>run.Text; }
        else if(kind is "descendants" or "float" or "out-of-flow")
        {
            var panel=new StackPanel();panel.Children.Add(text);var child=new TextBlock{Text="אבג مرحبا"};panel.Children.Add(child);Css.SetClass(child,"child");
            root=new Border{Child=panel};change=()=>text.Text+=" Q";source=()=>text.Text+"|"+child.Text;
        }
        else
        {
            if(kind=="empty")text.Text="";
            if(kind=="first-line")text.Text="第一行 CSS\n第二行保持透明";
            root=text;change=()=>text.Text=kind=="first-line" ? "Q "+text.Text : text.Text+" Q";source=()=>text.Text;
        }
        Css.SetClass(root,"test");
        string foreground=reference?"white":"transparent";
        string style=".test{box-sizing:border-box;width:300px;height:140px;padding:12px;border:0;font-family:Georgia;font-size:28px;line-height:40px;"+
            "text-decoration:underline;text-emphasis:filled dot;color:"+foreground+";"+
            (reference?"background:none;":TextClipPaint(kind)+"background-clip:text;")+"}"+
            ".test::placeholder{color:"+foreground+";opacity:1;}";
        if(kind=="tall")style+=".test{height:20000px;}";
        if(kind=="first-line")style+=".test{color:"+foreground+";background:none}.test::first-line{color:"+foreground+";"+
            (reference?"background:none;":boxPaint?"background:red;background-clip:border-box;":"background:red;background-clip:text;")+"}";
        if(kind=="layers"&&!reference)style+=".test{background-image:linear-gradient(115deg,#28bd82,#ce3d98),linear-gradient(#24528c,#24528c);background-clip:text,content-box;}";
        if(kind=="float")style+=".child{float:left;width:150px;height:55px;}";
        if(kind=="out-of-flow")style+=".test{position:relative}.child{position:absolute;left:130px;top:85px;"+(reference?"visibility:hidden;":"")+"}";
        var sheet=CssStyleSheet.Parse(style);if(sheet.Diagnostics.Count>0)throw new Exception("Scene CSS diagnostics: "+string.Join(";",sheet.Diagnostics));
        Css.GetStyleSheets(root).Add(sheet);LayoutTextClipScene(root);return new(root,change,source);
    }
    private static Border MakeTextClipPaint(string background)
    {
        var root=new Border();Css.SetClass(root,"paint");Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".paint{box-sizing:border-box;width:300px;height:140px;padding:12px;border:0;"+background+"}"));LayoutTextClipScene(root);return root;
    }
    private static void LayoutTextClipScene(FrameworkElement root)
    {
        VisualTreeHelper.SetRootDpi(root,new DpiScale(FrameworkElement.LayoutDpiScale,FrameworkElement.LayoutDpiScale));
        for(int i=0;i<5;i++){Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);root.Measure(new(300,140));root.Arrange(new(0,0,300,140));root.UpdateLayout();}
    }
    private static void WriteTextClipChecks(string filename,List<TextClipCheckRow> rows)
    {
        using var stream=File.Create(Path.Combine(Output,filename));using var writer=new Utf8JsonWriter(stream,new(){Indented=true});
        writer.WriteStartObject();writer.WriteNumber("count",rows.Count);writer.WriteNumber("failures",rows.Sum(row=>row.Errors.Count));
        writer.WriteString("reference","Independent opaque white glyph/decorations on native Software, alpha multiplied by independent native background paint");
        writer.WriteBoolean("workerReplay",true);writer.WriteBoolean("liveWindow",false);writer.WriteBoolean("physicalInput",false);writer.WriteBoolean("voiceOver",false);
        writer.WriteStartArray("rows");foreach(var row in rows){writer.WriteStartObject();writer.WriteString("label",row.Label);writer.WriteNumber("largest",row.Largest);writer.WriteNumber("ink",row.Ink);writer.WriteBoolean("fullyRecordable",row.FullyRecordable);writer.WriteString("hash",row.Hash);writer.WriteStartArray("errors");foreach(var error in row.Errors)writer.WriteStringValue(error);writer.WriteEndArray();writer.WriteEndObject();}writer.WriteEndArray();
        writer.WriteStartObject("nativeLibraries");for(uint i=0;i<ImageCount();i++){var path=System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ImageName(i));if(path is null||!Path.GetFileName(path).StartsWith("libjalium.native.",StringComparison.Ordinal))continue;writer.WriteStartObject(Path.GetFileName(path));writer.WriteString("path",Path.GetFullPath(path));writer.WriteString("sha256",Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());writer.WriteEndObject();}writer.WriteEndObject();writer.WriteEndObject();
    }
}
