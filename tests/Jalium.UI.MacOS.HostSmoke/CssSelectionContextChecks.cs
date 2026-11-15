using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record FallbackCase(string Kind,bool Decoration,bool Author,double Offset=2,bool Shadow=false);
    private static string FallbackName(FallbackCase row)=>row.Kind+"-"+(row.Decoration?"decoration"+row.Offset+(row.Shadow?"-shadow":""):"shadow")+"-"+row.Author;
    private static SelectionEditor MakeFallbackEditor(FallbackCase row,int width,string mode)
    {
        var result=MakeSelectionEditor(row.Kind,width,false,mode is "colored" or "mask");
        var color=mode=="mask"?Colors.White:mode=="colored"?(row.Author?Colors.Green:Color.FromRgb(255,248,222)):Color.FromRgb(35,60,50);
        ((Control)result.Control).Foreground=new SolidColorBrush(color);
        if(result.Control is RichTextBox rich)
        {
            rich.Document.Foreground=new SolidColorBrush(color);
            foreach(var paragraph in rich.Document.Blocks.OfType<Paragraph>())foreach(var run in paragraph.Inlines.OfType<Run>())run.Foreground=new SolidColorBrush(color);
        }
        var nativeForeground=new SolidColorBrush(Color.FromRgb(255,248,222));
        if(result.Control is PasswordBox password)
        {password.SelectionTextBrush=nativeForeground;password.SelectionOpacity=1;password.SelectionBrush=mode=="actual"?new SolidColorBrush(SelectionBlue):Brushes.Black;}
        else if(result.Control is TextBoxBase input)
        {input.SelectionTextBrush=nativeForeground;input.SelectionOpacity=1;input.SelectionBrush=mode=="actual"?new SolidColorBrush(SelectionBlue):Brushes.Black;}
        var root=(FrameworkElement)result.Control.Parent!;
        if(mode=="actual")
        {
            var paint=(row.Author?"color:green;background:#20537a;":"")+(row.Decoration
                ?$"text-decoration:underline currentColor;text-decoration-thickness:1px;text-underline-offset:{row.Offset}px;text-decoration-skip-inset:none;"
                :"")+(!row.Decoration||row.Shadow?"text-shadow:2px 3px 0 currentColor;":"");
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject::selection{"+paint+"}"));
        }
        FirstLetterLayout(root,width,180);if(mode=="actual")result.Select(true);return result;
    }
    private static void FallbackShadow(DrawingContext dc,SelectionEditor mask,Geometry area,int width,Color color,Action<DrawingContext>? decoration=null)
    {
        var effect=new DropShadowEffect{Color=color,Opacity=1,BlurRadius=0,CssUsesAlphaMask=true,CssGaussianSigma=0,ShadowDepth=Math.Sqrt(13),Direction=(Math.Atan2(-3,2)*180/Math.PI+360)%360};
        var capture=(IEffectDrawingContext)dc;var offset=(dc as IOffsetDrawingContext)?.Offset??default;var x=(float)Math.Floor(offset.X-12);var y=(float)Math.Floor(offset.Y-12);
        capture.BeginEffectCapture(x,y,width+24,204);dc.PushClip(area);mask.Control.Render(dc);dc.Pop();decoration?.Invoke(dc);capture.EndEffectCapture();capture.ApplyCssTextShadowsOnly(effect,x,y,width+24,204,x,y);
    }
    internal static int RunSelectionFallback()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            var cases=(from kind in new[]{"text","password","rich-native","rich-shared"}from paint in new[]{0,1,2,3,4}select new FallbackCase(kind,paint is 1 or 3 or 4,paint==2,paint is 3 or 4?8:2,paint==4)).ToArray();
            cases=SelectionFilter(cases,"JALIUM_CSS_FALLBACK_CASES",FallbackName);
            foreach(var engine in(backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto}).Where(e=>string.IsNullOrEmpty(requested)||e.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine=engine;
                foreach(var row in cases)foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})
                {
                    GC.Collect();GC.WaitForPendingFinalizers();
                    var actual=MakeFallbackEditor(row,width,"actual");var normal=MakeFallbackEditor(row,width,"normal");var colored=MakeFallbackEditor(row,width,"colored");var mask=MakeFallbackEditor(row,width,"mask");
                    var baselines=new List<double>();var fontData=new List<object>();
                    normal.Control.Render(new SelectionReferenceGeometry((text,point,offset)=>
                    {
                        var metrics=TextMeasurement.GetFontMetrics(text.FontFamily,text.FontSize,text.FontWeight,text.FontStyle);
                        fontData.Add(new{text.FontFamily,text.FontSize,text.IsMeasured,text.Baseline,metrics.Ascent,metrics.Descent,platform=text.PlatformTextLine?.GetType().Name});
                        var baseline=text.PlatformTextLine is NativeTextParagraph.Line native?native.Metrics.Baseline:text.IsMeasured?text.Baseline:metrics.Ascent;
                        baselines.Add(point.Y+offset.Y+baseline);
                    }));
                    normal.Select(true);var geometry=new SelectionReferenceGeometry();normal.Control.Render(geometry);normal.Select(false);
                    var area=SelectionReferenceArea(geometry.Rectangles);var clip=new RectangleGeometry(new(0,0,width,180));var color=row.Author?Colors.Green:Color.FromRgb(255,248,222);
                    var decorationLines=new List<object>();actual.Control.Render(new SelectionReferenceGeometry(line:(pen,a,b,offset)=>decorationLines.Add(new{a,b,offset,pen.Thickness,color=(pen.Brush as SolidColorBrush)?.Color.ToArgb()})));
                    foreach(var recorded in new[]{false,true})
                    {
                        var label=$"selection-fallback-{backend}-{engine}-{FallbackName(row)}-{width}-{dpi}-{recorded}";var errors=new List<string>();var largest=0;var differences=0;
                        try
                        {
                            byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc=>
                            {dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,180));if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();draw(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}else draw(dc);},()=>true,context)??throw new Exception("Fallback capture unavailable"));
                            var pixels=Capture(dc=>actual.Control.Render(dc));var expected=Capture(dc=>
                            {
                                dc.DrawGeometry(new SolidColorBrush(SelectionBlue),null,area);
                                void Decoration(DrawingContext target,Color ink)
                                {
                                    foreach(var rect in geometry.Rectangles)
                                    {
                                        var baseline=baselines.OrderBy(value=>Math.Abs(value-(rect.Bottom-4))).First();
                                        target.PushClip(SelectionReferenceArea([new(rect.Left,0,rect.Width,180)]));
                                        target.DrawLine(new Pen(new SolidColorBrush(ink),1),new(rect.Left,baseline+row.Offset+.5),new(rect.Right,baseline+row.Offset+.5));target.Pop();
                                    }
                                }
                                if(!row.Decoration||row.Shadow)FallbackShadow(dc,mask,area,width,color,row.Decoration?target=>Decoration(target,Colors.White):null);
                                dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,clip,area));normal.Control.Render(dc);dc.Pop();
                                dc.PushClip(area);colored.Control.Render(dc);dc.Pop();
                                if(row.Decoration)Decoration(dc,color);
                            });
                            for(var i=0;i<pixels.Length;i++){var delta=Math.Abs(pixels[i]-expected[i]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                            if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                            if(largest>3||width==312&&dpi==1&&!recorded){Save(pixels,width*dpi,180*dpi,label+".png");Save(expected,width*dpi,180*dpi,label+"-expected.png");}
                            if(actual.Source()!=normal.Source())errors.Add("Editor source changed");
                        }
                        catch(Exception error){errors.Add(error.ToString());}
                        if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors,baselines,fontData,rectangles=geometry.Rectangles,decorationLines});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                    }
                }
            }
            if(records.Count==0)throw new InvalidOperationException("No fallback comparisons requested");
            File.WriteAllText(Path.Combine(Output,"selection-fallback.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Literal native selected-foreground controls, ordinary white editor masks, native selection geometry and ordinary text baselines"}));return failures==0?0:1;
        }
        finally{context.Dispose();PopPool(pool);}
    }
    private const string ContextText = "Alpha affinity continues through another line of ordinary text.";
    private sealed record ContextCase(string Name, string Pseudo, string Highlight, string BodyInk, string PseudoInk,
        string BodyBackground, string PseudoBackground, string ParentHighlight = "", double FontSize = 16,
        double ShadowX = 2, double ShadowY = 3, bool Underline = false, string? BodyDecoration = null,
        string? PseudoDecoration = null, bool Changed = false, bool Rtl = false, bool Partial = false, bool Hidden = false);
    private static ContextCase[] ContextCases() =>
    [
        new("current-first-line", "::first-line", "color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;", "#233c32", "green", "#233c32", "green"),
        new("current-first-letter", "::first-letter", "color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;", "#233c32", "green", "#233c32", "green"),
        new("current-initial", "initial", "color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;", "#233c32", "green", "#233c32", "green"),
        new("mix-first-line", "::first-line", "color:color-mix(in srgb,currentColor 50%,red 50%);background:color-mix(in srgb,currentColor 50%,blue 50%);text-shadow:2px 3px 0 currentColor;text-decoration:underline;text-decoration-color:color-mix(in srgb,currentColor 50%,white 50%);text-underline-offset:1.28px;text-decoration-thickness:1px;text-decoration-skip-inset:none;", "rgb(145,30,25)", "rgb(128,64,0)", "rgb(73,15,140)", "rgb(64,32,128)", Underline: true, BodyDecoration: "rgb(200,143,140)", PseudoDecoration: "rgb(192,160,128)"),
        new("mix-first-letter", "::first-letter", "color:color-mix(in srgb,currentColor 50%,red 50%);background:color-mix(in srgb,currentColor 50%,blue 50%);text-shadow:2px 3px 0 currentColor;", "rgb(145,30,25)", "rgb(128,64,0)", "rgb(73,15,140)", "rgb(64,32,128)"),
        new("explicit-first-line", "::first-line", "color:#fff8de;background:currentColor;text-shadow:2px 3px 0 currentColor;", "#fff8de", "#fff8de", "#fff8de", "#fff8de"),
        new("inherited-shadow", "::first-line", "color:#fff8de;", "#fff8de", "#fff8de", "transparent", "transparent", ParentHighlight: "text-shadow:.5em .25em 0 currentColor;", FontSize: 32, ShadowX: 8, ShadowY: 4),
        new("inherited-decoration", "::first-letter", "color:#fff8de;text-shadow:2px 3px 0 currentColor;", "#fff8de", "#fff8de", "transparent", "transparent", ParentHighlight: "text-decoration:underline;text-decoration-color:color-mix(in srgb,currentColor 50%,red 50%);text-underline-offset:1.28px;text-decoration-thickness:1px;text-decoration-skip-inset:none;", Underline: true, BodyDecoration: "rgb(255,124,111)", PseudoDecoration: "rgb(255,124,111)"),
        new("inherited-color-expression", "::first-line", "background:currentColor;text-shadow:2px 3px 0 currentColor;", "rgb(145,30,25)", "rgb(128,64,0)", "rgb(145,30,25)", "rgb(128,64,0)", ParentHighlight: "color:color-mix(in srgb,currentColor 50%,red 50%);"),
        new("changed-first-line", "::first-line", "color:currentColor;background:currentColor;text-shadow:2px 3px 0 currentColor;", "#233c32", "blue", "#233c32", "blue", Changed: true),
        new("bidi-current", "", "color:currentColor;background:color-mix(in srgb,currentColor 50%,blue 50%);text-shadow:2px 3px 0 currentColor;", "#233c32", "#233c32", "rgb(18,30,153)", "rgb(18,30,153)", Rtl: true),
        new("tight-partial", "", "color:#fff8de;background:#20537a;text-shadow:2px 3px 0 currentColor;", "#fff8de", "#fff8de", "#20537a", "#20537a", FontSize:32,Partial:true),
        new("tight-all", "", "color:#fff8de;background:#20537a;text-shadow:2px 3px 0 currentColor;", "#fff8de", "#fff8de", "#20537a", "#20537a", FontSize:32),
        new("hidden-first-line", "::first-line", "color:#fff8de;background:#20537a;text-shadow:2px 3px 0 currentColor;", "#fff8de", "#fff8de", "#20537a", "#20537a", Hidden:true)
    ];
    private static SelectionScene MakeContextScene(ContextCase row, int width, string mode)
    {
        var root = new StackPanel { Width = width, Height = 180 }; Css.SetClass(root, "parent");
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        var text = new TextBlock { IsTextSelectionEnabled = true, Text = row.Rtl ? "אבגד office affinity 123 العربية זחט" : ContextText,
            FlowDirection = row.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        Css.SetClass(text,"subject"); Css.SetStyle(text,$"display:block;white-space:pre-wrap;font-size:{row.FontSize}px;");root.Children.Add(text);
        if(row.Partial)
        {
            text.Text="";
            text.Inlines.Add(new Jalium.UI.Documents.Run(ContextText[..15]){Foreground=new SolidColorBrush(ContextColor(mode=="colored"?row.BodyInk:mode=="body-mask"?"white":mode is "pseudo-mask" or "unselected"?"transparent":"#233c32"))});
            text.Inlines.Add(new Jalium.UI.Documents.Run(ContextText[15..]){Foreground=new SolidColorBrush(ContextColor(mode is "body-mask" or "pseudo-mask" or "colored"?"transparent":"#233c32"))});
        }
        var pseudo = row.Pseudo == "initial" ? "::first-letter" : row.Pseudo;
        var rules = pseudo.Length == 0 ? "" : ".subject" + pseudo + "{color:green;" + (row.Pseudo == "initial" ? "initial-letter:3;" : row.Pseudo == "::first-line" ? "font-size:20px;" : "font-size:30px;") + "}";
        if (mode == "actual") rules += ".parent::selection{" + row.ParentHighlight + "}.subject::selection{" + row.Highlight + "}";
        else if (mode != "normal")
        {
            var body = mode == "colored" ? row.BodyInk : mode == "body-mask" ? "white" : "transparent";
            var first = mode == "colored" ? row.PseudoInk : mode == "pseudo-mask" ? "white" : "transparent";
            rules += ".subject{color:" + body + ";}";
            if (pseudo.Length > 0) rules += ".subject" + pseudo + "{color:" + first + ";}";
        }
        if (row.Changed) rules += ".subject.changed" + pseudo + "{color:" + (mode is "normal" or "actual" ? "blue" : mode == "colored" ? row.PseudoInk : mode == "pseudo-mask" ? "white" : "transparent") + ";}";
        if (row.Hidden) rules += ".subject{visibility:hidden;}";
        var sheet = CssStyleSheet.Parse(rules); if(sheet.Diagnostics.Count>0)throw new Exception(string.Join(";",sheet.Diagnostics));
        Css.GetStyleSheets(root).Add(sheet);FirstLetterLayout(root,width,180);
        if(row.Changed){Css.SetClass(text,"subject changed");FirstLetterLayout(root,width,180);}
        if(mode=="actual"){if(row.Partial)text.Select(0,15);else text.SelectAll();}return new(root,text);
    }
    private static Color ContextColor(string value)
    {
        // The oracle uses literal byte channels, independent of CSS color math.
        if(value.StartsWith("rgb(",StringComparison.Ordinal))
        {
            var channels=value[4..^1].Split(',').Select(byte.Parse).ToArray();return Color.FromRgb(channels[0],channels[1],channels[2]);
        }
        return (Color)ColorConverter.ConvertFromString(value)!;
    }
    private static int ContextPseudoCount(ContextCase row,TextBlock text)
    {
        if(row.Pseudo.Length==0)return 0;
        var end=1;
        if(row.Pseudo=="::first-line")
        {
            var first=text.GetSourceRangeSegments(0,1);if(first.Count==0)return 0;
            var baseline=first[0].Baseline;
            while(end<text.Text.Length&&text.GetSourceRangeSegments(end,1).Any(segment=>Math.Abs(segment.Baseline-baseline)<.01))end++;
        }
        return end;
    }
    private static Rect[] ContextSubtract(IEnumerable<Rect> rectangles,Rect[] holes)
    {
        var pieces=rectangles.ToList();
        foreach(var hole in holes)
        {
            var next=new List<Rect>();
            foreach(var rect in pieces)
            {
                var intersection=Rect.Intersect(rect,hole);
                if(intersection.Width<=0||intersection.Height<=0){next.Add(rect);continue;}
                if(intersection.Top>rect.Top)next.Add(new(rect.Left,rect.Top,rect.Width,intersection.Top-rect.Top));
                if(intersection.Bottom<rect.Bottom)next.Add(new(rect.Left,intersection.Bottom,rect.Width,rect.Bottom-intersection.Bottom));
                if(intersection.Left>rect.Left)next.Add(new(rect.Left,intersection.Top,intersection.Left-rect.Left,intersection.Height));
                if(intersection.Right<rect.Right)next.Add(new(intersection.Right,intersection.Top,rect.Right-intersection.Right,intersection.Height));
            }
            pieces=next;
        }
        return pieces.ToArray();
    }
    private static void ContextBackground(DrawingContext dc,ContextCase row,int pseudoCount,int sourceLength,Rect[] pseudoRects,Rect[] bodyRects)
    {
        var entries=pseudoRects.Select(rect=>(Color:ContextColor(row.PseudoBackground),Rect:rect))
            .Concat(bodyRects.Select(rect=>(Color:ContextColor(row.BodyBackground),Rect:rect)));
        foreach(var group in entries.GroupBy(entry=>entry.Color.ToArgb()))
        {
            var rects=group.Select(entry=>entry.Rect).OrderBy(rect=>rect.Top).ThenBy(rect=>rect.Left).ToList();
            var brush=new SolidColorBrush(group.First().Color);
            // Neutral inline paragraphs use the established single-rectangle
            // primitive. Metal antialiases its perimeter differently from a
            // path, so the reference chooses the same geometric primitive.
            var combined=row.Pseudo is not ("" or "initial")&&pseudoCount>0&&pseudoCount<sourceLength;
            if(combined&&rects.Count==1)dc.DrawRectangle(brush,null,rects[0]);
            else dc.DrawGeometry(brush,null,SelectionReferenceArea(rects));
        }
    }
    private static void ContextUnderline(DrawingContext dc,TextBlock text,IEnumerable<Rect> rectangles,Color color,int start=0,int length=-1)
    {
        var lines=text.GetSourceRangeSegments(start,length<0?text.Text.Length-start:length);
        foreach(var rect in rectangles)
        {
            var baseline=lines.OrderBy(segment=>Math.Abs(segment.Baseline-(rect.Bottom-4))).First().Baseline;
            dc.DrawLine(new Pen(new SolidColorBrush(color),1),new(rect.Left,baseline+1.78),new(rect.Right,baseline+1.78));
        }
    }
    private static void ContextShadow(DrawingContext dc,TextBlock white,Geometry area,int width,Color color,ContextCase row,Rect[] decoration,int start=0,int length=-1)
    {
        var effect = new DropShadowEffect { Color=color,Opacity=1,BlurRadius=0,CssUsesAlphaMask=true,CssGaussianSigma=0,
            ShadowDepth=Math.Sqrt(row.ShadowX*row.ShadowX+row.ShadowY*row.ShadowY),Direction=(Math.Atan2(-row.ShadowY,row.ShadowX)*180/Math.PI+360)%360 };
        var capture=(IEffectDrawingContext)dc;var offset=(dc as IOffsetDrawingContext)?.Offset??default;
        var left=(float)Math.Floor(offset.X-12);var top=(float)Math.Floor(offset.Y-12);
        capture.BeginEffectCapture(left,top,width+24,204);white.Render(dc);
        if(row.Underline)ContextUnderline(dc,white,decoration,Colors.White,start,length);
        capture.EndEffectCapture();capture.ApplyCssTextShadowsOnly(effect,left,top,width+24,204,left,top);
    }
    internal static int RunSelectionContext()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach(var engine in (backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto}).Where(e=>string.IsNullOrEmpty(requested)||e.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine=engine;
                foreach(var row in ContextCases())foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})
                {
                    GC.Collect();GC.WaitForPendingFinalizers();
                    var actual=MakeContextScene(row,width,"actual");var normal=MakeContextScene(row,width,"normal");var colored=MakeContextScene(row,width,"colored");
                    var bodyMask=MakeContextScene(row,width,"body-mask");var pseudoMask=row.Pseudo.Length>0?MakeContextScene(row,width,"pseudo-mask"):null;var unselected=row.Partial?MakeContextScene(row,width,"unselected"):null;
                    var count=row.Partial?15:normal.Text.Text.Length;var rectangles=OrdinarySelectionRectangles(normal.Text,0,count).Rectangles;var pseudoCount=ContextPseudoCount(row,normal.Text);
                    var pseudoRects=pseudoCount>0?OrdinarySelectionRectangles(normal.Text,0,pseudoCount).Rectangles:[];
                    var area=SelectionReferenceArea(rectangles);var pseudoArea=SelectionReferenceArea(pseudoRects);var bodyRects=ContextSubtract(rectangles,pseudoRects);var bodyArea=SelectionReferenceArea(bodyRects);
                    foreach(var recorded in new[]{false,true})
                    {
                        var label=$"selection-context-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}";var errors=new List<string>();var largest=0;var differences=0;
                        try
                        {
                            byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc=>
                            {dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,180));if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();draw(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}else draw(dc);},()=>true,context)??throw new Exception("Context capture unavailable"));
                            var pixels=Capture(dc=>actual.Text.Render(dc));var expected=Capture(dc=>
                            {
                                ContextBackground(dc,row,pseudoCount,normal.Text.Text.Length,pseudoRects,bodyRects);
                                if(pseudoMask is not null)ContextShadow(dc,pseudoMask.Text,pseudoArea,width,ContextColor(row.PseudoInk),row,pseudoRects,0,pseudoCount);
                                ContextShadow(dc,bodyMask.Text,bodyArea,width,ContextColor(row.BodyInk),row,bodyRects,pseudoCount);
                                unselected?.Text.Render(dc);colored.Text.Render(dc);
                                if(row.Underline)
                                {
                                    ContextUnderline(dc,normal.Text,bodyRects,ContextColor(row.BodyDecoration!),pseudoCount);
                                    ContextUnderline(dc,normal.Text,pseudoRects,ContextColor(row.PseudoDecoration!),0,pseudoCount);
                                }
                            });
                            for(var i=0;i<pixels.Length;i++){var delta=Math.Abs(pixels[i]-expected[i]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                            if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                            if(largest>3||width==312&&dpi==1&&!recorded){Save(pixels,width*dpi,180*dpi,label+".png");Save(expected,width*dpi,180*dpi,label+"-expected.png");}
                            if(actual.Text.Text!=normal.Text.Text||actual.Text.SelectedText!=normal.Text.Text[..count]||actual.Text.RenderSize!=normal.Text.RenderSize)errors.Add("Source, selection or layout changed");
                        }
                        catch(Exception error){errors.Add(error.ToString());}
                        if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output,"selection-context.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Ordinary literal-color controls, ordinary per-typography white glyph masks, selection geometry, independently composed shadows and decoration baselines"}));return failures==0?0:1;
        }
        finally{context.Dispose();PopPool(pool);}
    }
    internal static int RunSelectionContextGallery()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach(var engine in (backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto}).Where(e=>string.IsNullOrEmpty(requested)||e.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                GC.Collect();GC.WaitForPendingFinalizers();context.DefaultRenderingEngine=engine;var height=width<600?740:692;
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(SelectionPaper)};
                root.Children.Add(new TextBlock{Text="选区颜色，跟随文字",FontSize=25,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(35,60,50)),Margin=new Thickness(18,22,18,4)});
                root.Children.Add(new TextBlock{Text="首行、首字与下沉首字 · 保留原始排版",FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,0,18,12)});
                var rows=new[]{ContextCases()[0],ContextCases()[1],ContextCases()[2],ContextCases()[9]};
                var captions=new[]{"首行 · 沿用底层绿色","首字 · 保留字号与基线","下沉首字 · 跨行选区","样式更新 · 绿色切换为蓝色"};
                for(var index=0;index<rows.Length;index++)
                {
                    var row=rows[index] with{Highlight="color:currentColor;background:color-mix(in srgb,currentColor 8%,white 92%);text-shadow:1px 2px 0 color-mix(in srgb,currentColor 18%,transparent 82%);text-decoration:underline currentColor;text-underline-offset:2px;text-decoration-thickness:1px;text-decoration-skip-inset:none;"};
                    var cardHeight=index==2?148:width<600?128:112;
                    var scene=MakeContextScene(row,width-64,"actual");scene.Root.Height=cardHeight-54;
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=captions[index],FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(20,99,66)),Margin=new Thickness(0,0,0,12)});content.Children.Add(scene.Root);
                    root.Children.Add(new Border{Child=content,Height=cardHeight,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(214,226,217)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                root.Children.Add(new TextBlock{Text="颜色随排版更新，原文与选择范围保持不变。",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,12,18,0)});
                FirstLetterLayout(root,width,height);
                var bitmap=MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Context gallery capture unavailable");
                var label=$"selection-context-gallery-{backend}-{engine}-{width}-{dpi}.png";Save(Pixels(bitmap),width*dpi,height*dpi,label);records.Add(new{label,width,height,dpi,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"selection-context-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{context.Dispose();PopPool(pool);}
    }
}
