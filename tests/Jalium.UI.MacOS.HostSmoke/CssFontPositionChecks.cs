using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record PositionCase(string Name,string Family,string Variant,string Prefix,string Suffix,byte Mode,
        bool Deny=false,bool Off=false,string? SuffixFamily=null,bool Caps=false)
    {
        internal string Source=>Prefix+Suffix;
        internal string Paint=>Caps?"ASS":Source;
        internal double Size=>(Mode==2?20.8:32)*(Caps ? .8 : 1);
        internal double Shift=>Mode==2?(Variant=="sub"?-6.4:11.2):0;
        internal string Feature=>Variant=="sub"?"subs":"sups";
    }
    private sealed class PositionReference:FrameworkElement,IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph,double X,double Y)> _paint=[];
        internal PositionReference(PositionCase row,int width,bool edge)
        {
            Width=width;Height=240;
            double Baseline(string family){var m=TextMeasurement.GetFontMetrics(family,32,400,0);return m.Ascent+(44-m.Ascent-m.Descent)/2;}
            double baseline=Math.Max(Baseline(row.Family),Baseline(row.SuffixFamily??row.Family));
            string Font(string family)=>FontFeatureRenderingSource.Wrap(family,[new(FontFeature.TagOf(row.Feature),row.Mode==1&&!row.Off?1u:0u)]);
            NativeTextParagraph Make(string text,int offset)
            {
                var spans=new List<NativeTextParagraph.Span>();int split=row.Caps?text.Length:Math.Clamp(row.Prefix.Length-offset,0,text.Length);
                if(split>0)spans.Add(new(text[..split],Font(row.Family),row.Size,400,0,Color.FromRgb(0x14,0x63,0x42)));
                if(split<text.Length)spans.Add(new(text[split..],Font(row.SuffixFamily??row.Family),row.Size,400,0,Color.FromRgb(0x14,0x63,0x42)));
                return NativeTextParagraph.TryCreate(spans,row.Family,32,10000,44,TextAlignment.Left,FlowDirection.LeftToRight,noWrap:true)
                    ??throw new Exception("Position literal reference unavailable");
            }
            if(edge)
            {
                var first=Make(row.Caps?"A":row.Prefix,0);_paint.Add((first,0,baseline-row.Shift));
                var second=Make(row.Caps?"SS":row.Suffix,row.Prefix.Length);_paint.Add((second,first.Lines[0].Metrics.Line.Width+1,baseline-row.Shift));
            }
            else
            {
                var breaks=new TextBlock{FontFamily=row.Family,FontSize=row.Size};
                if(row.SuffixFamily is null||row.Caps)breaks.Text=row.Paint;
                else {breaks.Inlines.Add(new Run(row.Prefix));var suffix=new Run(row.Suffix){FontFamily=row.SuffixFamily};breaks.Inlines.Add(suffix);}
                Css.SetStyle(breaks,$"line-height:44px;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-feature-settings:'{row.Feature}' {(row.Mode==1&&!row.Off?"on":"off")};");
                for(int i=0;i<3;++i){breaks.Measure(new(width,240));breaks.Arrange(new(0,0,width,240));breaks.UpdateLayout();}
                int line=0;
                foreach(var range in BoundaryLines(breaks)){
                    double lineBaseline=Baseline(row.Family);
                    if(row.SuffixFamily is {} suffixFamily&&range.Start+range.Length>row.Prefix.Length)
                        lineBaseline=Math.Max(lineBaseline,Baseline(suffixFamily));
                    if(range.Length>0)_paint.Add((Make(row.Paint.Substring(range.Start,range.Length),range.Start),0,line*44+lineBaseline-row.Shift));
                    ++line;
                }
            }
        }
        protected override Size MeasureOverride(Size availableSize)=>new(Width,Height);
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach(var (paragraph,x,y) in _paint){var line=paragraph.Lines[0];context.DrawText(new FormattedText(paragraph.Text,"Arial",32){
                PlatformTextLine=line,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),MaxTextWidth=10000,MaxTextHeight=240},new(x,y-line.Metrics.Baseline));}
        }
        public void Dispose(){foreach(var item in _paint)item.Paragraph.Dispose();}
    }

    internal static int RunFontPosition()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var rows=new[]{
            new PositionCase("native-super","Seravek","super","123","456",1),new PositionCase("native-sub","Seravek","sub","123","456",1),
            new PositionCase("synthetic-super","Arial","super","123","456",2),new PositionCase("synthetic-sub","Arial","sub","123","456",2),
            new PositionCase("partial-super","Seravek","super","123","a\u0301",2),new PositionCase("partial-sub","Seravek","sub","123","a\u0301",2),
            new PositionCase("denied","Arial","super","123","xyz",3,Deny:true),new PositionCase("native-denied","Seravek","super","123","456",1,Deny:true),
            new PositionCase("explicit-off","Seravek","super","123","456",1,Off:true),
            new PositionCase("mixed-face","Seravek","super","123","x",2,SuffixFamily:"Arial"),
            new PositionCase("caps-position","Arial","super","a","ß",2,Caps:true),
        };
        var records=new List<object>();int failed=0;
        try{
            foreach(var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })foreach(int dpi in new[]{1,2})foreach(int width in new[]{24,40,220})
            foreach(var row in rows)foreach(string path in new[]{"text","run","span","shared","edge"})
            {
                if((path is "shared" or "edge"||row.Caps)&&width!=220)continue;
                if(row.SuffixFamily is not null&&path=="text")continue;
                context.DefaultRenderingEngine=engine;string label=$"font-position-{engine}-{dpi}-{width}-{row.Name}-{path}";
                var errors=new List<string>();int differentBytes=-1;
                try{
                    byte[] Capture(FrameworkElement view,int size){view.SetRootDpi(new(dpi,dpi));for(int i=0;i<3;++i){view.Measure(new(size,240));view.Arrange(new(0,0,size,240));view.UpdateLayout();}
                        var bitmap=MacOSWritingToolsBitmapCapture.Capture(size*dpi,240*dpi,dpi,dc=>view.Render(dc),()=>true)??throw new Exception("Position readback failed");
                        if(context.Backend!=RenderBackend.Metal||context.DefaultRenderingEngine!=engine||!ReferenceEquals(RenderContext.Current,context))throw new Exception("Position backend/engine changed");return Pixels(bitmap);}
                    var sources=new List<(TextBlock Text,string Source)>();var runs=new List<(Run Run,string Source,DependencyObject? Parent)>();FrameworkElement root;
                    if(path is "shared" or "edge"){
                        var flow=new StackPanel();var first=new TextBlock{Text=row.Prefix,IsTextSelectionEnabled=true};var second=new TextBlock{Text=row.Suffix,IsTextSelectionEnabled=true};
                        flow.Children.Add(first);flow.Children.Add(second);Css.SetStyleSheet(flow,"TextBlock{display:inline}");root=flow;sources.Add((first,row.Prefix));sources.Add((second,row.Suffix));
                        Css.SetStyle(second,(path=="edge"?"padding-left:1px;":"")+(row.SuffixFamily is {} family?$"font-family:'{family}';":""));
                    }else{
                        var text=new TextBlock{IsTextSelectionEnabled=true};root=text;sources.Add((text,row.Source));
                        if(path=="text")text.Text=row.Source;
                        else{var prefix=new Run(row.Prefix);var suffix=new Run(row.Suffix);text.Inlines.Add(prefix);if(path=="span")text.Inlines.Add(new Span(suffix));else text.Inlines.Add(suffix);
                            if(row.SuffixFamily is {} family)Css.SetStyle(suffix,$"font-family:'{family}';");runs.Add((prefix,row.Prefix,prefix.Parent));runs.Add((suffix,row.Suffix,suffix.Parent));}
                    }
                    void Style(bool changed)=>Css.SetStyle(root,$"display:block;font-family:'{row.Family}';font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-variant-position:{(changed?row.Variant:"normal")};font-synthesis-position:{(row.Deny?"none":"auto")};font-variant-caps:{(changed&&row.Caps?"small-caps":"normal")};"+
                        (row.Off?$"font-feature-settings:'{row.Feature}' off;":row.Caps?"font-feature-settings:'smcp' off;":""));
                    Style(false);var normal=Capture(root,width);Style(true);var changed=Capture(root,width);
                    using var reference=new PositionReference(row,width,path=="edge");var expected=Capture(reference,width);
                    differentBytes=changed.Zip(expected).Count(pair=>pair.First!=pair.Second);
                    Save(changed,width*dpi,240*dpi,label+"-changed.png");Save(expected,width*dpi,240*dpi,label+"-expected.png");
                    if(differentBytes!=0)throw new Exception($"Position paint differs from literal reference ({differentBytes} bytes)");
                    foreach(var (text,source) in sources){text.SelectAll();if(text.SelectedText!=source)throw new Exception("Position changed source selection");text.Select(0,0);}
                    foreach(var (run,source,parent) in runs)if(run.Text!=source||!ReferenceEquals(run.Parent,parent))throw new Exception("Position replaced a source run");
                    Capture(root,400);if(!Capture(root,width).SequenceEqual(changed))throw new Exception("Position resize retained stale resolution");
                    Style(false);var restored=Capture(root,width);if(!restored.SequenceEqual(normal))throw new Exception("Position normal retained synthesized forms");
                    Save(restored,width*dpi,240*dpi,label+"-restored.png");
                }catch(Exception error){errors.Add(error.ToString());failed++;}
                records.Add(new{label,backend=context.Backend.ToString(),engine=engine.ToString(),differentBytes,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            File.WriteAllText(Path.Combine(Output,"font-position.json"),JsonSerializer.Serialize(new{count=records.Count,failed,records,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));return failed==0?0:1;
        }finally{PopPool(pool);}
    }
}
