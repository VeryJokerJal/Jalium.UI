using System.Text.Json;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record GradientBoundsRow(string Label,int Largest,string? ActualHash,string? ExpectedHash,List<string> Errors);

    internal static int RunSoftwareGradientText()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<GradientBoundsRow>();var failures=0;
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var text in new[]{"A","Office A\u0308 色彩","אבג العربية"})foreach(var radial in new[]{false,true})
            foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})foreach(var affine in new[]{false,true})
            {
                context.DefaultRenderingEngine=engine;var errors=new List<string>();var largest=0;string? actualHash=null,expectedHash=null;var label=$"gradient-bounds-{backend}-{engine}-{text}-{radial}-{width}-{dpi}-{recorded}-{affine}";
                try
                {
                    Brush brush=radial?new RadialGradientBrush(Color.FromRgb(153,62,37),Color.FromRgb(32,83,122))
                        :new LinearGradientBrush(Color.FromRgb(20,99,66),Color.FromRgb(153,62,37),0);
                    byte[] Capture(bool measure)
                    {
                        var source=new FormattedText(text,"Georgia",28){Foreground=brush,MaxTextWidth=width-40,MaxTextHeight=120};
                        if(measure)TextMeasurement.MeasureText(source);
                        var wasMeasured=source.IsMeasured;var originalWidth=source.Width;var originalHeight=source.Height;
                        var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc=>
                        {
                            dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,180));
                            void Draw(DrawingContext target)
                            {
                                target.PushClip(new RectangleGeometry(new Rect(3,3,width-6,170)));
                                target.PushTransform(new MatrixTransform(affine?new Matrix(.9,.12,.18,1.05,8,3):Matrix.Identity));
                                target.PushOpacity(.73);target.DrawText(source,new Point(14.375,18.625));
                                target.Pop();target.Pop();target.Pop();
                            }
                            if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();Draw(recorder);var drawing=recorder.Commit();if(drawing.IsFullyRecordable)DrawingReplayer.Replay(drawing,dc);else Draw(dc);}
                            else Draw(dc);
                        },()=>true,context)??throw new Exception("Native gradient bounds capture unavailable"));
                        if(source.IsMeasured!=wasMeasured||source.Width!=originalWidth||source.Height!=originalHeight)errors.Add("Painting mutated caller-owned measurement state");
                        return pixels;
                    }
                    var actual=Capture(false);var expected=Capture(true);
                    actualHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(actual));
                    expectedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected));
                    for(var i=0;i<actual.Length;i++)largest=Math.Max(largest,Math.Abs(actual[i]-expected[i]));
                    if(actual.Distinct().Take(17).Count()<17)errors.Add("No native gradient glyph ink");
                    if(largest>3)errors.Add($"Unmeasured and measured paint differ: {largest}");
                }
                catch(Exception error){errors.Add(error.ToString());}
                if(errors.Count>0)failures++;records.Add(new(label,largest,actualHash,expectedHash,errors));
                Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join("; ",errors));
            }
            using(var stream=File.Create(Path.Combine(Output,"software-gradient-text.json")))
            using(var writer=new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();writer.WriteNumber("count",records.Count);writer.WriteNumber("failures",failures);writer.WriteNumber("tolerance",3);
                writer.WriteBoolean("liveWindow",false);writer.WriteBoolean("physicalInput",false);writer.WriteBoolean("voiceOver",false);
                writer.WriteStartArray("records");
                foreach(var row in records)
                {
                    writer.WriteStartObject();writer.WriteString("label",row.Label);writer.WriteNumber("largest",row.Largest);
                    writer.WriteString("actualHash",row.ActualHash);writer.WriteString("expectedHash",row.ExpectedHash);
                    writer.WriteStartArray("errors");foreach(var error in row.Errors)writer.WriteStringValue(error);writer.WriteEndArray();writer.WriteEndObject();
                }
                writer.WriteEndArray();writer.WriteStartObject("nativeLibraries");
                var paths=new Dictionary<string,string>();
                for(uint i=0;i<ImageCount();i++)
                {
                    var path=System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ImageName(i));
                    if(path is null||!Path.GetFileName(path).StartsWith("libjalium.native.",StringComparison.Ordinal))continue;
                    var name=Path.GetFileName(path);path=Path.GetFullPath(path);
                    if(paths.TryGetValue(name,out var previous)&&previous!=path)throw new InvalidOperationException("Multiple loaded native payloads: "+name);
                    paths[name]=path;
                }
                foreach(var entry in paths)
                {
                    writer.WriteStartObject(entry.Key);writer.WriteString("path",entry.Value);
                    writer.WriteString("sha256",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry.Value))).ToLowerInvariant());writer.WriteEndObject();
                }
                writer.WriteEndObject();writer.WriteEndObject();
            }
            return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
}
