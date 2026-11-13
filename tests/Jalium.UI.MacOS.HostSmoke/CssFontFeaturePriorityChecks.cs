using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.MacOS;
internal static partial class CssWritingToolsChecks
{
    private sealed record PriorityCase(string Name,string First,string Last,string ReferenceFirst,string ReferenceLast,string Features,string Position="normal",string Caps="normal",bool Synthetic=false);
    private sealed class PriorityReference : FrameworkElement, IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph,double X,double Y,int Start,int Length)> _paint=[];
        internal PriorityReference(PriorityCase row,string family,int width,bool edge,bool fragmented)
        {
            Width=width;Height=280;double size=row.Synthetic?20.8:32;
            var m=TextMeasurement.GetFontMetrics(family,32,400,0);double baseline=m.Ascent+(44-m.Ascent-m.Descent)/2;
            double shift=row.Synthetic?(row.Position=="sub"?-6.4:11.2):0;
            var breaks=new TextBlock{Text=row.ReferenceFirst+row.ReferenceLast,FontFamily=family,FontSize=size};
            Css.SetStyle(breaks,$"line-height:44px;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-feature-settings:{row.Features}");
            int available=Math.Max(1,width-(edge?1:0));
            for(int i=0;i<3;i++){breaks.Measure(new(available,280));breaks.Arrange(new(0,0,available,280));breaks.UpdateLayout();}
            string key=breaks.FontFamily.GetRenderingSource(breaks);int lineIndex=0;
            foreach(var line in BoundaryLines(breaks)) {
                string content=breaks.Text.Substring(line.Start,line.Length);int split=Math.Clamp(row.ReferenceFirst.Length-line.Start,0,content.Length);
                NativeTextParagraph Make(string text){
                    var units=new NativeMethods.ParagraphAdvanceUnit[text.Length];
                    for(int i=0;i<units.Length;i++)units[i]=new(){TextPosition=(uint)i,Length=1,Flags=char.IsWhiteSpace(text[i])?4u:0};
                    return NativeTextParagraph.TryCreateAdvanced([new(text,key,size,400,0,Colors.Black)],family,size,10000,44,FlowDirection.LeftToRight,units,contextPaint:true)??throw new Exception("Raw reference unavailable");
                }
                double y=lineIndex++*44+baseline-shift;
                if(!edge){if(content.Length>0){var paragraph=Make(content);
                    // Match authored paint ownership with independent raw-font
                    // source slices. The fixture deliberately has overhangs.
                    if(fragmented&&split>0&&split<content.Length){_paint.Add((paragraph,0,y,0,split));_paint.Add((paragraph,0,y,split,content.Length-split));}
                    else _paint.Add((paragraph,0,y,0,content.Length));}continue;}
                double x=0;
                if(split>0){var first=Make(content[..split]);_paint.Add((first,0,y,0,split));x=first.Lines[0].Metrics.Line.Width;}
                if(split<content.Length)_paint.Add((Make(content[split..]),x+1,y,0,content.Length-split));
            }
        }
        protected override Size MeasureOverride(Size availableSize)=>new(Width,Height);
        protected override void OnRender(DrawingContext context){base.OnRender(context);foreach(var (paragraph,x,y,start,length) in _paint){var native=paragraph.Lines[0];var line=native.ForSource(start,length,Color.FromRgb(0x14,0x63,0x42),1,[],0);context.DrawText(new FormattedText(paragraph.Text.Substring(start,length),"Arial",32){PlatformTextLine=line,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),MaxTextWidth=10000,MaxTextHeight=280},new(x,y-line.Metrics.Baseline));}}
        public void Dispose(){foreach(var paragraph in _paint.Select(item=>item.Paragraph).Distinct())paragraph.Dispose();}
    }
    internal static int RunFontFeaturePriority()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        string root=Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT")??Directory.GetCurrentDirectory();
        using var font=CssNativeFontResource.Create(File.ReadAllBytes(Path.Combine(root,"tests/Jalium.UI.MacOS.Tests/Assets/FontFeaturePriority/selectable-variants.otf")))??throw new Exception("Priority font unavailable");
        var rows=new List<PriorityCase>();
        foreach(string position in new[]{"sub","super"})foreach(bool synthetic in new[]{false,true})foreach(int index in new[]{0,1,2,3})
            rows.Add(new($"{position}-{(synthetic?"synth":"native")}-{index}","x",synthetic?"z":"x","x",synthetic?"z":"x",$"'{(position=="sub"?"subs":"sups")}' {index}",position,Synthetic:synthetic));
        foreach(int index in new[]{0,1,2,3}) {
            rows.Add(new($"unicase-{index}","A","A","a","a",$"'smcp' {index}",Caps:"unicase"));
            string literal=index==0?"x":new[]{"b","c","d"}[index-1];rows.Add(new($"salt-{index}","x","x",literal,literal,"'salt' "+index));
        }
        rows.Add(new("combined","A x","z","a x","z","'smcp' 2,'sups' 3","super","unicase",true));
        var records=new List<object>();int failed=0;
        try {
            foreach(var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })foreach(int dpi in new[]{1,2})foreach(int width in new[]{24,80,240})foreach(var row in rows)foreach(string path in new[]{"text","run","span","shared","edge"}) {
                context.DefaultRenderingEngine=engine;string label=$"feature-priority-{engine}-{dpi}-{width}-{row.Name}-{path}";var errors=new List<string>();int differentBytes=-1;
                try {
                    byte[] Capture(FrameworkElement view,int size){view.SetRootDpi(new(dpi,dpi));for(int i=0;i<3;i++){view.Measure(new(size,280));view.Arrange(new(0,0,size,280));view.UpdateLayout();}
                        var bitmap=MacOSWritingToolsBitmapCapture.Capture(size*dpi,280*dpi,dpi,dc=>view.Render(dc),()=>true)??throw new Exception("Priority readback failed");
                        if(context.Backend!=RenderBackend.Metal||context.DefaultRenderingEngine!=engine||!ReferenceEquals(RenderContext.Current,context))throw new Exception("Priority backend changed");return Pixels(bitmap);}
                    var sources=new List<(TextBlock Text,string Source)>();var runs=new List<(Run Run,string Source,DependencyObject? Parent)>();
                    FrameworkElement Build(bool reference) {
                        string first=reference?row.ReferenceFirst:row.First,last=reference?row.ReferenceLast:row.Last;FrameworkElement view;
                        if(path is "shared" or "edge") {
                            var flow=new StackPanel();var a=new TextBlock{Text=first,IsTextSelectionEnabled=true};var b=new TextBlock{Text=last,IsTextSelectionEnabled=true};flow.Children.Add(a);flow.Children.Add(b);Css.SetStyleSheet(flow,"TextBlock{display:inline}");view=flow;
                            if(path=="edge")Css.SetStyle(b,"padding-left:1px;");if(!reference){sources.Add((a,first));sources.Add((b,last));}
                        } else {
                            var text=new TextBlock{IsTextSelectionEnabled=true};view=text;
                            if(path=="text")text.Text=first+last;
                            else {var a=new Run(first);var b=new Run(last);text.Inlines.Add(a);if(path=="span")text.Inlines.Add(new Span(b));else text.Inlines.Add(b);if(!reference){runs.Add((a,first,a.Parent));runs.Add((b,last,b.Parent));}}
                            if(!reference)sources.Add((text,first+last));
                        }
                        double size=reference&&row.Synthetic?20.8:32;
                        string features=reference&&row.Name.StartsWith("salt-")?"normal":row.Features;
                        Css.SetStyle(view,$"display:block;font-family:'{font.Family}';font-size:{size.ToString(System.Globalization.CultureInfo.InvariantCulture)}px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-feature-settings:{features};font-variant-position:{(reference?"normal":row.Position)};font-variant-caps:{(reference?"normal":row.Caps)};");
                        if(reference&&row.Synthetic){double Baseline(double em){var m=TextMeasurement.GetFontMetrics(font.Family,em,400,0);return m.Ascent+(44-m.Ascent-m.Descent)/2;}
                            double delta=Baseline(32)-Baseline(size)-(row.Position=="sub"?-6.4:11.2);view.RenderTransform=new TranslateTransform(0,delta);}
                        return view;
                    }
                    var actual=Build(false);using var positionedReference=row.Position!="normal"?new PriorityReference(row,font.Family,width,path=="edge",path is "run" or "span" or "shared"):null;var reference=(FrameworkElement?)positionedReference??Build(true);var changed=Capture(actual,width);var expected=Capture(reference,width);differentBytes=changed.Zip(expected).Count(pair=>pair.First!=pair.Second);
                    Save(changed,width*dpi,280*dpi,label+"-changed.png");Save(expected,width*dpi,280*dpi,label+"-expected.png");if(differentBytes!=0)throw new Exception($"Priority differs from independent raw-feature reference ({differentBytes} bytes)");
                    foreach(var (text,source) in sources){text.SelectAll();if(text.SelectedText!=source)throw new Exception("Priority changed source selection");text.Select(0,0);}
                    foreach(var (run,source,parent) in runs)if(run.Text!=source||!ReferenceEquals(run.Parent,parent))throw new Exception("Priority replaced a run");
                    Capture(actual,400);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Priority resize retained stale context");
                    string css=Css.GetStyle(actual);Css.SetStyle(actual,css+"font-feature-settings:normal;");Capture(actual,width);Css.SetStyle(actual,css);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Priority selector replacement retained stale text");
                } catch(Exception error){errors.Add(error.ToString());failed++;}
                records.Add(new{label,backend=context.Backend.ToString(),engine=engine.ToString(),differentBytes,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            File.WriteAllText(Path.Combine(Output,"font-feature-priority.json"),JsonSerializer.Serialize(new{count=records.Count,failed,records,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));return failed==0?0:1;
        } finally {PopPool(pool);}
    }
}
