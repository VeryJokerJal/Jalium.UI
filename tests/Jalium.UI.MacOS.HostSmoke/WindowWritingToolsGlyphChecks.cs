using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using DocRun=Jalium.UI.Documents.Run;

namespace Jalium.UI.MacOS;

// Native render-target readback of real controls. No physical input, screen
// capture, or Apple Intelligence service invocation is involved.
internal static class WindowWritingToolsGlyphChecks
{
    static readonly List<object> rows=[];
    static int failures,passed;
    static string Output=>Environment.GetEnvironmentVariable("JALIUM_WRITING_TOOLS_GLYPH_OUTPUT") ?? throw new InvalidOperationException("Set JALIUM_WRITING_TOOLS_GLYPH_OUTPUT to an owned output directory");
    static byte[] Pixels(BitmapSource b){var p=new byte[b.PixelWidth*b.PixelHeight*4];b.CopyPixels(p,b.PixelWidth*4,0);return p;}
    static int Red(byte[] p){int n=0;for(int i=0;i<p.Length;i+=4)if(p[i+3]>40&&p[i+2]>140&&p[i+1]<100&&p[i]<100)++n;return n;}
    static void Check(bool value,string name){if(value){++passed;Console.WriteLine("PASS "+name);}else{++failures;Console.WriteLine("FAIL "+name);}}
    static WriteableBitmap Paint(FrameworkElement element)=>MacOSWritingToolsBitmapCapture.Capture(380,180,1,element.Render,()=>true)??throw new Exception("Native capture failed");
    static BitmapImage Decode(byte[] p){var b=new BitmapImage();b.BeginInit();b.StreamSource=new MemoryStream(p);b.EndInit();return b;}
    static void Save(BitmapSource b,string name){var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(b));using var f=File.Create(Path.Combine(Output,name+".png"));e.Save(f);}
    static void Pump(int ms){var t=System.Diagnostics.Stopwatch.StartNew();while(t.ElapsedMilliseconds<ms){Jalium.UI.Interop.NativeMethods.PlatformPollEvents();Jalium.UI.Threading.Dispatcher.CurrentDispatcher.ProcessQueue();Thread.Sleep(2);}}
    static void Case(int kind,string font,string text)
    {
        FrameworkElement? e=null;Window? w=null;string label=$"kind={kind},font={font},text={text}";
        try
        {
            e=kind switch
            {
                0=>new TextBox{Template=null,Text=text,Padding=new Thickness(10),BorderThickness=new Thickness(0)},
                1=>new TextBox{Text=text,Padding=new Thickness(10),BorderThickness=new Thickness(0)},
                2=>new RichTextBox(new FlowDocument(new Paragraph(new DocRun(text){Foreground=Brushes.Red,FontFamily=new FontFamily(font),FontStyle=FontStyles.Italic,FontSize=40}))),
                _=>new EditControl{Text=text,ShowLineNumbers=false,CaretBrush=Brushes.Transparent,HighlightCurrentLine=false,ShowMinimap=false}
            };
            var control=(Control)e;control.Foreground=Brushes.Red;control.Background=Brushes.Transparent;control.BorderThickness=new Thickness(0);control.BorderBrush=Brushes.Transparent;control.FontFamily=new FontFamily(font);control.FontSize=40;control.FontStyle=FontStyles.Italic;
            w=new Window{Title="Jalium glyph extent check",Width=400,Height=280,ShowActivated=false,Content=e};Application.Current!.MainWindow=w;w.Show();Pump(60);
            e.Measure(new Size(350,140));e.Arrange(new Rect(0,0,350,140));e.UpdateLayout();Pump(10);
            var source=((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(e)!.GetPattern(PatternInterface.Text)!).Source;
            string before=source.Text;var normal=Paint(e);var normalBytes=Pixels(normal);int red=Red(normalBytes);
            var preview=MacOSWritingToolsPresentation.CreatePreview(e,source,0,text.Length,Matrix.Identity,1,null,()=>true);
            int previewRed=preview is null?-1:Red(Pixels(Decode(preview.Png)));
            MacOSWritingToolsPresentation.Prepare(e,source,"whole",0,text.Length,0,()=>true);var hidden=Paint(e);int hiddenRed=Red(Pixels(hidden));
            MacOSWritingToolsPresentation.Finish(e,"whole");bool restored=Pixels(Paint(e)).SequenceEqual(normalBytes);
            rows.Add(new{kind,font,text,red,previewRed,hiddenRed,restored,frame=preview?.Frame,rects=source.GetBoundingRectangles(0,text.Length)});
            Console.WriteLine("OBS "+JsonSerializer.Serialize(rows[^1]));
            Check(red>10,label+" normal paints glyphs");Check(previewRed==red,label+" preview preserves every red glyph pixel");Check(hiddenRed==0,label+" masking leaves no red glyph pixels");Check(restored,label+" finish restores exact pixels");Check(source.Text==before,label+" text stays unchanged");
            if(kind==2 && font=="Helvetica Neue" && text=="fff")
            {Save(normal,"italic-normal");Save(hidden,"italic-hidden");if(preview is not null)File.WriteAllBytes(Path.Combine(Output,"italic-preview.png"),preview.Png);}
            if((previewRed!=red||hiddenRed!=0)&&!File.Exists(Path.Combine(Output,"first-normal.png")))
            {Save(normal,"first-normal");Save(hidden,"first-hidden");if(preview is not null)File.WriteAllBytes(Path.Combine(Output,"first-preview.png"),preview.Png);}
        }
        catch(Exception ex){++failures;Console.WriteLine("ERROR "+label+": "+ex);}
        finally{if(e is not null)MacOSWritingToolsPresentation.Clear(e);w?.Close();Pump(15);}
    }
    static byte[] Canvas(PreviewInfo preview,int width,int height,double dpi)
    {
        var bitmap=Decode(preview.Png); var source=Pixels(bitmap); var result=new byte[width*height*4];
        int x=(int)Math.Round(preview.Frame.X*dpi), y=(int)Math.Round(preview.Frame.Y*dpi);
        for(int row=0;row<bitmap.PixelHeight;++row) for(int col=0;col<bitmap.PixelWidth;++col)
            if(x+col>=0 && x+col<width && y+row>=0 && y+row<height)
                Buffer.BlockCopy(source,(row*bitmap.PixelWidth+col)*4,result,((y+row)*width+x+col)*4,4);
        return result;
    }
    readonly record struct PreviewInfo(byte[] Png,Rect Frame);
    static void Partition(int kind,double dpi,Matrix transform,string tail)
    {
        FrameworkElement? e=null;Window? w=null; string label=$"partial kind={kind},dpi={dpi},matrix={transform},tail={tail}";
        try
        {
            string text="fff      "+tail;
            e=kind switch {0=>new TextBox{Template=null,Text=text,Padding=new Thickness(10)},1=>new TextBox{Text=text,Padding=new Thickness(10)},
                2=>new RichTextBox(new FlowDocument(new Paragraph(new DocRun(text){Foreground=Brushes.Red,FontFamily=new FontFamily("Helvetica Neue"),FontStyle=FontStyles.Italic,FontSize=40}))),
                _=>new EditControl{Text=text,ShowLineNumbers=false,CaretBrush=Brushes.Transparent,HighlightCurrentLine=false,ShowMinimap=false}};
            var control=(Control)e;control.Foreground=Brushes.Red;control.Background=Brushes.Transparent;control.BorderThickness=new Thickness(0);control.BorderBrush=Brushes.Transparent;control.FontFamily=new FontFamily("Helvetica Neue");control.FontSize=40;control.FontStyle=FontStyles.Italic;
            w=new Window{Title="Jalium Writing Tools glyph partition",Width=440,Height=300,ShowActivated=false,Content=e};Application.Current!.MainWindow=w;w.Show();Pump(60);
            e.Measure(new Size(350,140));e.Arrange(new Rect(0,0,350,140));e.UpdateLayout();Pump(10);
            var source=((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(e)!.GetPattern(PatternInterface.Text)!).Source;
            int width=(int)(440*dpi),height=(int)(220*dpi);
            WriteableBitmap PaintTransformed()=>MacOSWritingToolsBitmapCapture.Capture(width,height,dpi,dc=>{dc.PushTransform(new MatrixTransform(transform));e.Render(dc);dc.Pop();},()=>true)??throw new Exception("Transformed native readback failed");
            var normal=Pixels(PaintTransformed());
            var whole=MacOSWritingToolsPresentation.CreatePreview(e,source,0,text.Length,transform,dpi,null,()=>true)??throw new Exception("Whole preview failed");
            var all=Canvas(new(whole.Png,whole.Frame),width,height,dpi);
            if(!all.SequenceEqual(normal) && !File.Exists(Path.Combine(Output,"first-difference.json")))
            {
                var samples=new List<object>();int changed=0,max=0;
                for(int index=0;index<normal.Length;index+=4)
                    if(!normal.AsSpan(index,4).SequenceEqual(all.AsSpan(index,4)))
                    {++changed;for(int c=0;c<4;++c)max=Math.Max(max,Math.Abs(normal[index+c]-all[index+c]));if(samples.Count<12)samples.Add(new{x=index/4%width,y=index/4/width,actual=normal[index..(index+4)],preview=all[index..(index+4)]});}
                File.WriteAllText(Path.Combine(Output,"first-difference.json"),JsonSerializer.Serialize(new{label,changed,max,samples}));
            }
            Check(all.SequenceEqual(normal),label+" whole preview preserves actual BGRA ink including overhang");
            var left=MacOSWritingToolsPresentation.CreatePreview(e,source,0,3,transform,dpi,null,()=>true)??throw new Exception("Partial preview failed");
            var selected=Canvas(new(left.Png,left.Frame),width,height,dpi);
            MacOSWritingToolsPresentation.Prepare(e,source,"left",0,3,0,()=>true);var remaining=Pixels(PaintTransformed());
            bool partition=true,selectedInk=false,remainingInk=false;
            for(int i=0;i<normal.Length;i+=4)
            {
                selectedInk|=selected[i+3]!=0;remainingInk|=remaining[i+3]!=0;
                if(selected[i+3]!=0 && remaining[i+3]!=0)partition=false;
                for(int c=0;c<4;++c) if(normal[i+c]!=Math.Max(selected[i+c],remaining[i+c]))partition=false;
            }
            Check(partition&&selectedInk&&remainingInk,label+" partial selection preserves neighboring glyphs and shaping");
            MacOSWritingToolsPresentation.Prepare(e,source,"right",9,4,0,()=>true);Check(Pixels(PaintTransformed()).All(value=>value==0),label+" interleaved ranges hide all BGRA ink including color emoji");
            MacOSWritingToolsPresentation.Finish(e,"left");Check(Pixels(PaintTransformed()).SequenceEqual(selected),label+" independent finish restores only its range");
            MacOSWritingToolsPresentation.Finish(e,"right");Check(Pixels(PaintTransformed()).SequenceEqual(normal),label+" final finish restores exact BGRA");
            Check(source.Text==text || source.Text.TrimEnd('\n','\r')==text,label+" presentation does not edit source");
            if(!partition || !all.SequenceEqual(normal))
            {File.WriteAllBytes(Path.Combine(Output,$"partition-{kind}-{dpi}-{transform.M12}-{tail}-preview.png"),whole.Png);Save(PaintTransformed(),$"partition-{kind}-{dpi}-{transform.M12}-{tail}-normal");}
        }
        catch(Exception ex){++failures;Console.WriteLine("ERROR "+label+": "+ex);}
        finally{if(e is not null)MacOSWritingToolsPresentation.Clear(e);w?.Close();Pump(15);}
    }
    internal static int Run(bool software = false)
    {
        Jalium.UI.Interop.RenderContext.GetOrCreateCurrent(software ? Jalium.UI.Interop.RenderBackend.Software : Jalium.UI.Interop.RenderBackend.Metal,forceReplace:true);
        Directory.CreateDirectory(Output);nint pool=PushPool();nint app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));
        var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        foreach(string font in new[]{"Helvetica Neue","Times New Roman","Baskerville","Georgia"})foreach(string text in new[]{"Miii","fff","jfjf","中文🙂"})for(int kind=0;kind<4;++kind)Case(kind,font,text);
        foreach(double dpi in new[]{1.0,2.0}) foreach(var transform in new[]{Matrix.Identity,new Matrix(1.15,0.1,0.15,1.1,12,8)})
            for(int kind=0;kind<4;++kind)foreach(string tail in new[]{"Miii","سلام","中文🙂"})Partition(kind,dpi,transform,tail);
        File.WriteAllText(Path.Combine(Output,"checks.json"),JsonSerializer.Serialize(new{passed,failures,rows,physicalInput=false,realService=false,backend=Jalium.UI.Interop.RenderContext.Current?.Backend.ToString(),nativeLibraries=NativeLibraries()}));PopPool(pool);return failures==0?0:1;
    }
    static Dictionary<string,string> NativeLibraries()
    {
        var result=new Dictionary<string,string>();
        for(uint index=0;index<ImageCount();++index)
        {
            string? path=Marshal.PtrToStringUTF8(ImageName(index));
            if(path is not null && Path.GetFileName(path).StartsWith("libjalium.native.",StringComparison.Ordinal))result[Path.GetFileName(path)]=Path.GetFullPath(path);
        }
        return result;
    }
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_image_count")]static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_get_image_name")]static extern nint ImageName(uint index);
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="objc_getClass")]static extern nint Class(string n);
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="sel_registerName")]static extern nint Selector(string n);
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="objc_msgSend")]static extern nint Send(nint o,nint s);
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="objc_msgSend")]static extern void Integer(nint o,nint s,nint v);
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="objc_autoreleasePoolPush")]static extern nint PushPool();
    [DllImport("/usr/lib/libobjc.A.dylib",EntryPoint="objc_autoreleasePoolPop")]static extern void PopPool(nint p);
}
