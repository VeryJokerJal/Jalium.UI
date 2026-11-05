using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.MacOS;
internal static partial class CssWritingToolsChecks
{
    private sealed record OpticalCase(string Name,double Size,string Policy,double Coordinate,string Extra="",bool Descriptor=false);
    private sealed class OpticalReference : FrameworkElement,IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph,double Y,int Split)> _lines=[];
        private readonly bool _fragmented;
        internal OpticalReference(string family,OpticalCase row,int width,bool fragmented)
        {
            Width=width;Height=544;_fragmented=fragmented;string key=FontFeatureRenderingSource.Wrap(family,[],CssCapsVariant.Normal,new(Variations:[new(0x6f70737a,row.Coordinate)]));
            var m=TextMeasurement.GetFontMetrics(key,row.Size,400,0);double height=row.Size*1.5,baseline=m.Ascent+(height-m.Ascent-m.Descent)/2;
            using var format=TextMeasurement.CreateTextFormatFromFamilyList(RenderContext.Current!,key,(float)row.Size,400,0);
            const string source="BB";int start=0;
            while(start<source.Length){int count=1;while(start+count<source.Length&&format.MeasureText(source.Substring(start,count+1),10000,1000).Width<=width+.0001)count++;string text=source.Substring(start,count);
                var units=Enumerable.Range(0,count).Select(i=>new NativeMethods.ParagraphAdvanceUnit{TextPosition=(uint)i,Length=1}).ToArray();
                var paragraph=NativeTextParagraph.TryCreateAdvanced([new(text,key,row.Size,400,0,Colors.Black)],family,row.Size,10000,height,FlowDirection.LeftToRight,units,contextPaint:true)??throw new Exception("Optical reference unavailable");
                _lines.Add((paragraph,_lines.Count*height+baseline,Math.Clamp(1-start,0,count)));start+=count;}
        }
        protected override Size MeasureOverride(Size available)=>new(Width,Height);
        protected override void OnRender(DrawingContext context){foreach(var (paragraph,y,split) in _lines){void Paint(int start,int length){var line=paragraph.Lines[0].ForSource(start,length,Color.FromRgb(0x14,0x63,0x42),1,[],0);context.DrawText(new FormattedText(paragraph.Text.Substring(start,length),"Arial",32){PlatformTextLine=line,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),MaxTextWidth=10000,MaxTextHeight=544},new(0,y-line.Metrics.Baseline));}if(_fragmented&&split>0&&split<paragraph.Text.Length){Paint(0,split);Paint(split,paragraph.Text.Length-split);}else Paint(0,paragraph.Text.Length);}}
        public void Dispose(){foreach(var line in _lines)line.Paragraph.Dispose();}
    }
    internal static int RunFontOpticalSizing()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);string root=Environment.GetEnvironmentVariable("JALIUM_REPO_ROOT")??Directory.GetCurrentDirectory();
        byte[] bytes=File.ReadAllBytes(Path.Combine(root,"tests/Jalium.UI.MacOS.Tests/Assets/FontVariations/variabletest_box.ttf"));using var font=CssNativeFontResource.Create(bytes)??throw new Exception("Optical fixture unavailable");
        var rows=new List<OpticalCase>();foreach(int size in new[]{6,12,24,48,64,128}){rows.Add(new($"auto-{size}",size,"auto",size));rows.Add(new($"none-{size}",size,"none",12));}
        rows.AddRange([new("auto-override",24,"auto",128,"font-variation-settings:'opsz' 128"),new("none-override",48,"none",6,"font-variation-settings:'opsz' 6"),new("auto-descriptor",48,"auto",48,Descriptor:true),new("none-descriptor",48,"none",96,Descriptor:true),new("size-points",24,"auto",24,"font-size:18pt"),new("size-inches",24,"auto",24,"font-size:.25in")]);
        var records=new List<object>();int failed=0;
        try{foreach(var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })foreach(int dpi in new[]{1,2})foreach(int width in new[]{40,160,400})foreach(var row in rows)foreach(string path in new[]{"text","run","span","shared"}){
            context.DefaultRenderingEngine=engine;string label=$"optical-{engine}-{dpi}-{width}-{row.Name}-{path}";var errors=new List<string>();int differentBytes=-1;
            try{FrameworkElement actual;var sources=new List<TextBlock>();var runs=new List<(Run Run,DependencyObject? Parent)>();
                if(path=="shared"){var flow=new StackPanel();foreach(string text in new[]{"B","B"}){var child=new TextBlock{Text=text,IsTextSelectionEnabled=true};sources.Add(child);flow.Children.Add(child);}Css.SetStyleSheet(flow,"TextBlock{display:inline}");actual=flow;}
                else{var block=new TextBlock{IsTextSelectionEnabled=true};sources.Add(block);actual=block;if(path=="text")block.Text="BB";else{var first=new Run("B");var last=new Run("B");block.Inlines.Add(first);if(path=="span")block.Inlines.Add(new Span(last));else block.Inlines.Add(last);runs.Add((first,first.Parent));runs.Add((last,last.Parent));}}
                string family=font.Family;
                if(row.Descriptor){family="OpticalFace";Css.SetStyleSheet(actual,(path=="shared"?"TextBlock{display:inline}":"")+"@font-face{font-family:OpticalFace;src:url(\"data:font/ttf;base64,"+Convert.ToBase64String(bytes)+"\");font-variation-settings:'opsz' 96}");}
                string size=row.Size.ToString(CultureInfo.InvariantCulture);Css.SetStyle(actual,$"display:block;font-family:'{family}';font-size:{size}px;line-height:1.5;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-optical-sizing:{row.Policy};{row.Extra};");
                byte[] Capture(FrameworkElement view,int w){view.SetRootDpi(new(dpi,dpi));for(int i=0;i<3;i++){view.Measure(new(w,544));view.Arrange(new(0,0,w,544));view.UpdateLayout();}var image=MacOSWritingToolsBitmapCapture.Capture(w*dpi,544*dpi,dpi,dc=>view.Render(dc),()=>true)??throw new Exception("Optical readback failed");if(context.Backend!=RenderBackend.Metal||context.DefaultRenderingEngine!=engine||!ReferenceEquals(RenderContext.Current,context))throw new Exception("Optical backend changed");return Pixels(image);}
                if(row.Descriptor){Capture(actual,width);Css.WaitForFontsAsync(actual).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();}
                using var reference=new OpticalReference(font.Family,row,width,path=="shared");var changed=Capture(actual,width);var expected=Capture(reference,width);differentBytes=changed.Zip(expected).Count(pair=>pair.First!=pair.Second);
                Save(changed,width*dpi,544*dpi,label+"-changed.png");Save(expected,width*dpi,544*dpi,label+"-expected.png");if(differentBytes!=0)throw new Exception($"Optical policy differs from explicit raw opsz ({differentBytes} bytes)");
                foreach(var text in sources){text.SelectAll();if(text.SelectedText!=(path=="shared"?"B":"BB"))throw new Exception("Optical source selection changed");text.Select(0,0);}foreach(var (run,parent) in runs)if(run.Text!="B"||!ReferenceEquals(run.Parent,parent))throw new Exception("Optical run ownership changed");
                Capture(actual,500);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Optical resize changed axis choice");string style=Css.GetStyle(actual);Css.SetStyle(actual,style+"font-optical-sizing:"+(row.Policy=="auto"?"none":"auto")+";");Capture(actual,width);Css.SetStyle(actual,style);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Optical reset retained stale glyphs");
            }catch(Exception error){errors.Add(error.ToString());failed++;}records.Add(new{label,backend=context.Backend.ToString(),engine=engine.ToString(),differentBytes,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));}
            File.WriteAllText(Path.Combine(Output,"font-optical.json"),JsonSerializer.Serialize(new{count=records.Count,failed,records,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));return failed==0?0:1;
        }finally{PopPool(pool);}
    }
}
