using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record EmphasisCase(string Name,string Style,string Mark,string Text,bool Under=false,int[]? Starts=null,string Pseudo="");
    private static EmphasisCase[] EmphasisCases()=>
    [
        new("filled-dot","filled dot","•","文字着重排版"),
        new("open-dot","open dot","◦","Office affinity",true),
        new("filled-circle","filled circle","●","ffi fi fj"),
        new("open-circle","open circle","○","汉字 A\u0308 \u200B😀",true,[0,1,3,7]),
        new("filled-double","filled double-circle","◉","العربية"),
        new("open-double","open double-circle","◎","ab אבג cd",true),
        new("filled-triangle","filled triangle","▲","保留原始字形和行间距离"),
        new("open-triangle","open triangle","△","Office affinity flows through another ordinary wrapped line.",true),
        new("filled-sesame","filled sesame","﹅","汉字与标点，保持原文。",false,[0,1,2,3,4,6,7,8,9]),
        new("open-sesame","open sesame","﹆","字体与着重号各自塑形",true),
        new("custom","\"※\"","※","自定义符号"),
        new("custom-cluster","\"A\\301\"","Á","四个字符",true),
        new("punctuation","filled dot","•","A ,、。#%&@§¶⁋⁓〽️",false,[0,5,6,7,8,9,10,11,12,13]),
        new("combined-space","filled dot","•"," \u0308A\u00A0\u202F\u200B\u0378",true,[0,2]),
        new("first-letter","filled dot","•","AV",false,[0],"::first-letter"),
        new("first-line","filled dot","•","AB\nCD",false,null,"::first-line"),
    ];

    private static (StackPanel Root,TextBlock Text) MakeEmphasisScene(EmphasisCase row,int width,bool actual)
    {
        var root=new StackPanel{Width=width,Height=180};
        Css.SetStyle(root,"display:block;font-family:Georgia;font-size:24px;line-height:normal;color:#233c32;");
        var text=new TextBlock{Text=row.Text,IsTextSelectionEnabled=true,TextWrapping=TextWrapping.Wrap,SelectionBrush=Brushes.Black};
        Css.SetClass(text,"subject");root.Children.Add(text);Css.SetStyle(text,"white-space:pre-wrap;");
        if(actual)Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".subject"+row.Pseudo+"{text-emphasis:"+row.Style+" #146342;text-emphasis-position:"+(row.Under?"under left":"over right")+";}"));
        FirstLetterLayout(root,width,180);return(root,text);
    }

    private static double EmphasisBaseline(FormattedText text,Point point,Point offset)
        =>point.Y+offset.Y+(text.PlatformTextLine is NativeTextParagraph.Line line?line.Metrics.Baseline:
            text.IsMeasured?text.Baseline:TextMeasurement.GetFontMetrics(text.FontFamily,text.FontSize,text.FontWeight,text.FontStyle).Ascent);

    internal static int RunTextEmphasis()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach(var engine in(backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto}).Where(e=>string.IsNullOrEmpty(requested)||e.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine=engine;
                foreach(var row in SelectionFilter(EmphasisCases(),"JALIUM_CSS_EMPHASIS_CASES",row=>row.Name))foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})
                {
                    GC.Collect();GC.WaitForPendingFinalizers();
                    var actual=MakeEmphasisScene(row,width,true);
                    var referenceRow=row;
                    var ordinary=MakeEmphasisScene(referenceRow,width,false);
                    var baselinePoints=new List<double>();ordinary.Root.Render(new SelectionReferenceGeometry((text,point,offset)=>baselinePoints.Add(EmphasisBaseline(text,point,offset))));
                    ordinary.Text.SelectAll();var naturalGeometry=new SelectionReferenceGeometry();ordinary.Root.Render(naturalGeometry);ordinary.Text.Select(0,0);
                    var firstRect=naturalGeometry.Rectangles.OrderBy(rect=>rect.Top).First();var naturalHeight=firstRect.Height;var naturalBaseline=baselinePoints.Min()-firstRect.Top;
                    var family=ordinary.Text.FontFamily.GetRenderingSource(ordinary.Text);var primary=TextMeasurement.GetFontMetrics(family,24);
                    var ruby=new FontFeature(FontFeature.TagOf("ruby"),1);
                    var markFamily=FontFeatureRenderingSource.TryUnwrap(family,out var features,out var baseFamily,out var caps,out var fontContext)
                        ?FontFeatureRenderingSource.Wrap(baseFamily,FontFeatureRenderingSource.Merge(features,[ruby]),caps,fontContext):FontFeatureRenderingSource.Wrap(family,[ruby]);
                    // The expected annotations are ordinary, literal half-size text.
                    // No candidate emphasis parser, painter or mark classifier is used.
                    var mark=new FormattedText(row.Mark,markFamily,12){Foreground=new SolidColorBrush(Color.FromRgb(20,99,66))};TextMeasurement.MeasureText(mark);
                    var markHeight=Math.Max(mark.Height,mark.Ascent+mark.Descent);
                    var above=row.Under?naturalBaseline:Math.Max(naturalBaseline,primary.Ascent+markHeight);
                    var below=row.Under?Math.Max(naturalHeight-naturalBaseline,primary.Descent+markHeight):naturalHeight-naturalBaseline;
                    var height=above+below;
                    if(row.Pseudo.Length==0)Css.SetStyle(ordinary.Text,"white-space:pre-wrap;line-height:"+height.ToString("R",CultureInfo.InvariantCulture)+"px;");
                    else Css.GetStyleSheets(ordinary.Root).Add(CssStyleSheet.Parse(".subject"+row.Pseudo+"{font-size:24px;line-height:"+height.ToString("R",CultureInfo.InvariantCulture)+"px;}"));
                    FirstLetterLayout(ordinary.Root,width,180);
                    baselinePoints.Clear();ordinary.Root.Render(new SelectionReferenceGeometry((text,point,offset)=>baselinePoints.Add(EmphasisBaseline(text,point,offset))));
                    var referenceBaseline=baselinePoints.Min();var shift=above-referenceBaseline;
                    var units=StringInfo.ParseCombiningCharacters(referenceRow.Text);var starts=row.Starts??units.Where(i=>referenceRow.Text[i] is not (' ' or '.')).ToArray();var annotations=new List<Point>();
                    if(row.Pseudo=="::first-line")starts=starts.Where(start=>start<row.Text.IndexOf('\n')).ToArray();
                    foreach(var start in starts)
                    {
                        var next=units.FirstOrDefault(value=>value>start,referenceRow.Text.Length);ordinary.Text.Select(start,next-start);var geometry=new SelectionReferenceGeometry();ordinary.Root.Render(geometry);ordinary.Text.Select(0,0);
                        foreach(var rect in geometry.Rectangles)
                            annotations.Add(new(rect.Left+(rect.Width-mark.WidthIncludingTrailingWhitespace)/2,
                                rect.Top+referenceBaseline+(row.Under?primary.Descent:-primary.Ascent-markHeight)));
                    }
                    foreach(var recorded in new[]{false,true})
                    {
                        var label=$"text-emphasis-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}";var errors=new List<string>();var largest=0;var differences=0;
                        try
                        {
                            byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc=>
                            {dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,180));if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();draw(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}else draw(dc);},()=>true,context)??throw new Exception("Emphasis native capture unavailable"));
                            var pixels=Capture(dc=>actual.Root.Render(dc));
                            var expected=Capture(dc=>
                            {
                                ordinary.Root.Render(new EmphasisReferenceContext(dc,height,shift,row.Pseudo=="::first-line"));
                                foreach(var origin in annotations)dc.DrawText(mark,new Point(origin.X,origin.Y+shift));
                            });
                            for(var i=0;i<pixels.Length;i++){var delta=Math.Abs(pixels[i]-expected[i]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                            if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                            if(largest>3||width==312&&dpi==1&&!recorded){Save(pixels,width*dpi,180*dpi,label+".png");Save(expected,width*dpi,180*dpi,label+"-expected.png");}
                            if(actual.Text.Text!=row.Text||actual.Text.FontSize!=24)errors.Add("Source or font size changed");
                        }
                        catch(Exception error){errors.Add(error.ToString());}
                        if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors,naturalHeight,naturalBaseline,markHeight,expectedHeight=height,expectedBaseline=above,referenceBaseline,shift,annotations=annotations.Count});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                    }
                }
            }
            if(records.Count==0)throw new InvalidOperationException("No emphasis comparisons requested");
            File.WriteAllText(Path.Combine(Output,"text-emphasis.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,oracle="Ordinary retained text with independently computed annotation space, native source-range geometry, explicit character positions and literal ruby-feature half-size glyphs"}));return failures==0?0:1;
        }
        finally{context.Dispose();PopPool(pool);}
    }

    internal static int RunTextEmphasisGallery()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            var requested=Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach(var engine in(backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto}).Where(e=>string.IsNullOrEmpty(requested)||e.ToString().Equals(requested,StringComparison.OrdinalIgnoreCase)))
            foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                GC.Collect();GC.WaitForPendingFinalizers();context.DefaultRenderingEngine=engine;var height=width<600?752:736;
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(SelectionPaper)};
                root.Children.Add(new TextBlock{Text="着重标记，保留原文",FontSize=25,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(35,60,50)),Margin=new Thickness(18,22,18,4)});
                root.Children.Add(new TextBlock{Text="逐字标记 · 字素完整 · 原生排版",FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,0,18,12)});
                var rows=new[]{new EmphasisCase("gallery-under","filled dot","•","这是保留原文的着重号",true),new EmphasisCase("gallery-sesame","open sesame","﹆","空心芝麻形着重号"),
                    new EmphasisCase("gallery-first-line","filled dot","•","Only this line is marked\nThe next line keeps its original form.",false,null,"::first-line"),new EmphasisCase("gallery-clusters","\"※\"","※","A\u0308 与 😀 都保持完整",true)};
                var captions=new[]{"汉字着重 · 标记在文字下方","空心芝麻形 · 使用原生字体","首行限定 · 后续文字保持原样","组合字符 · 保留完整字素"};
                for(var index=0;index<rows.Length;index++)
                {
                    var cardHeight=index==2?164:132;var scene=MakeEmphasisScene(rows[index],width-64,true);scene.Root.Height=cardHeight-54;
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=captions[index],FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(20,99,66)),Margin=new Thickness(0,0,0,12)});content.Children.Add(scene.Root);
                    root.Children.Add(new Border{Child=content,Height=cardHeight,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(214,226,217)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                root.Children.Add(new TextBlock{Text="标记参与行高，原文与文字节点保持不变。",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,12,18,0)});
                FirstLetterLayout(root,width,height);var label=$"text-emphasis-gallery-{backend}-{engine}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Gallery capture unavailable")),width*dpi,height*dpi,label);
                records.Add(new{label,width,height,dpi,layoutSettled=true});
            }
            File.WriteAllText(Path.Combine(Output,"text-emphasis-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{context.Dispose();PopPool(pool);}
    }

    private sealed class EmphasisReferenceContext(DrawingContext inner,double firstLineHeight,double shift,bool firstOnly):DrawingContextAdapter,IOffsetDrawingContext
    {
        public Point Offset{get=>(inner as IOffsetDrawingContext)?.Offset??default;set{if(inner is IOffsetDrawingContext offset)offset.Offset=value;}}
        public override void DrawText(FormattedText text,Point point)=>inner.DrawText(text,new Point(point.X,point.Y+(!firstOnly||EmphasisBaseline(text,point,Offset)<firstLineHeight?shift:0)));
        public override void DrawRectangle(Brush? b,Pen? p,Rect r)=>inner.DrawRectangle(b,p,r);public override void DrawGeometry(Brush? b,Pen? p,Geometry g)=>inner.DrawGeometry(b,p,g);public override void DrawLine(Pen p,Point a,Point b)=>inner.DrawLine(p,a,b);
        public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y)=>inner.DrawRoundedRectangle(b,p,r,x,y);public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y)=>inner.DrawEllipse(b,p,q,x,y);
        public override void DrawImage(ImageSource i,Rect r)=>inner.DrawImage(i,r);public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c)=>inner.DrawBackdropEffect(r,e,c);
        public override void PushClip(Geometry g)=>inner.PushClip(g);public override void PushTransform(Transform t)=>inner.PushTransform(t);public override void PushOpacity(double d)=>inner.PushOpacity(d);public override void Pop()=>inner.Pop();public override void Close(){}
    }
}
