using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed partial class CssRubyMergeLayoutTests
{
    public static IEnumerable<object[]> PixelCases()
    {
        foreach(var mode in new[]{"horizontal-tb","vertical-rl","vertical-lr"})
        foreach(var direction in new[]{"ltr","rtl"})
        foreach(var align in new[]{"start","center","space-between","space-around"})
        foreach(var scene in new[]{"pooled","fit","wrapped","hidden"})
        {
            yield return [mode,direction,align,scene,RenderBackend.Software,RenderingEngine.Impeller];
            yield return [mode,direction,align,scene,RenderBackend.Metal,RenderingEngine.Impeller];
            yield return [mode,direction,align,scene,RenderBackend.Metal,RenderingEngine.Vello];
        }
    }

    [Theory,MemberData(nameof(PixelCases))]
    public void MergedRubyPixelsMatchIndependentPhysicalSources(string mode,string direction,string align,string scene,RenderBackend backend,RenderingEngine engine)
    {
        if(backend==RenderBackend.Metal&&Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD")!="1")
        {
            var command=System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")??throw new InvalidOperationException("Metal main-thread runner is required."))!;
            var start=new System.Diagnostics.ProcessStartInfo(command[0]){RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in command.Skip(1).Concat(new[]{"merge-ruby",mode,direction,align,scene,engine.ToString()}))start.ArgumentList.Add(arg);
            start.Environment["JALIUM_CSS_METAL_CHILD"]="1";using var process=System.Diagnostics.Process.Start(start)!;
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            if(!process.WaitForExit(90000)){process.Kill(entireProcessTree:true);Assert.Fail("Metal merge runner exceeded 90 seconds.");}
            string receipt=output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult();Assert.True(process.ExitCode==0,receipt);Assert.Contains("ENGINE "+engine,receipt);Assert.Contains("BACKEND Metal",receipt);return;
        }
        using var context=RenderContext.GetOrCreateCurrent(backend);
        using var surface=backend==RenderBackend.Metal?new NativePlatformWindow("Jalium CSS merged Ruby pixels",1500,500,320,320,1,0):null;
        surface?.Show(activate:false);Assert.Equal(backend,context.Backend);Console.WriteLine("BACKEND "+context.Backend);
        string first=scene=="pooled"?"とうとう":scene=="fit"?"とう":scene=="hidden"?"東":"とうとう";
        double inline=scene=="wrapped"?68:240;
        var a=Create(mode,direction,scene=="hidden"?"auto":"merge",first,"と");
        Css.SetStyle(a.Root,Style(mode,direction,scene=="hidden"?"auto":"merge")+$"ruby-align:{align};text-autospace:no-autospace;color:black;");
        Layout(a.Root,mode,inline);var reference=new Canvas();
        var baseFont=TextMeasurement.GetFontMetrics("Hiragino Sans",32,400,0);var noteFont=TextMeasurement.GetFontMetrics("Hiragino Sans",16,400,0);
        double B=(double)baseFont.Ascent+baseFont.Descent,N=(double)noteFont.Ascent+noteFont.Descent;
        double advance=B+N,baseTop=B/2+N-20,noteTop=B/2-20;
        // The reference uses literal CJK advances (32/16px) and independent
        // native paragraphs. It contains no Ruby roles or Ruby layout calls.
        if(scene=="wrapped")
        {
            double one=direction=="rtl"?4:0,two=direction=="rtl"?36:0;
            Add("東",32,one+(align=="start"?(direction=="rtl"?32:0):16),0,false,32);
            Add(first,16,one,0,true,64);
            Add("京",32,two,1,false,32);
            Add("と",16,two+(align=="start"?(direction=="rtl"?16:0):8),1,true,16);
        }
        else
        {
            double total=scene=="pooled"?80:64,origin=direction=="rtl"?240-total:0;
            double column=scene=="pooled"?40:32;
            double baseOffset=scene=="pooled"?(align=="start"?(direction=="rtl"?8:0):4):0;
            Add("東",32,origin+(direction=="rtl"?column:0)+baseOffset,0,false,32);
            Add("京",32,origin+(direction=="rtl"?0:column)+baseOffset,0,false,32);
            if(scene=="hidden")Add("と",16,origin+(direction=="rtl"?0:32)+(align=="start"?(direction=="rtl"?16:0):8),0,true,16);
            else
            {
                double firstWidth=scene=="pooled"?64:32,free=scene=="pooled"?0:16;
                double gap=align=="space-between"?free/2:align=="space-around"?free/3:0;
                double edge=align=="start"?0:align=="center"?free/2:align=="space-around"?gap/2:0;
                firstWidth+=gap;double firstAt=direction=="rtl"?total-edge-firstWidth:edge;
                double lastAt=direction=="rtl"?total-edge-firstWidth-gap-16:edge+firstWidth+gap;
                Add(first,16,origin+firstAt,0,true,firstWidth,gap>0);
                Add("と",16,origin+lastAt,0,true,16);
            }
        }
        Layout(reference,"horizontal-tb",240);var actual=Pixels(a.Root.Render);var expected=Pixels(reference.Render);Save("actual",actual);Save("reference",expected);
        var evidence=Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if(!string.IsNullOrWhiteSpace(evidence))
            File.WriteAllText(Path.Combine(evidence,$"merge-ruby-{mode}-{direction}-{align}-{scene}-{backend}-{engine}-geometry.json"),
                System.Text.Json.JsonSerializer.Serialize(new { a.Root.DesiredSize, a.Root.RenderSize,
                    sources=a.Bases.Concat(a.Notes).Select(text=>new{text.Text,text.IsVisible,text.RenderSize,matrix=text.GetRenderMatrix(),bounds=text.DocumentTextBounds(0,text.Text.Length)}),
                    reference=reference.Children.OfType<TextBlock>().Select(text=>new{text.Text,text.RenderSize,matrix=text.GetRenderMatrix(),bounds=text.DocumentTextBounds(0,text.Text.Length)}) },
                    new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Assert.Contains(actual.Where((_,i)=>i%4==3),alpha=>alpha>0);Assert.Equal(expected,actual);

        void Add(string value,double size,double x,int row,bool annotation,double width,bool justify=false)
        {
            double height=annotation?N:B,local=annotation?noteTop:baseTop,y=row*advance+local;
            Rect slot=mode=="horizontal-tb"?new(x,y,width,height):new(240-y-height,x,height,width);
            // Over is physically right in vertical-lr; each new column uses
            // B+N advance rather than adding the authored base line twice.
            if(mode=="vertical-lr")slot=new((row+1)*advance-local-height,x,height,width);
            var text=Text(value,size);text.Width=slot.Width;text.Height=slot.Height;
            Css.SetStyle(text,$"font-family:'Hiragino Sans';font-size:{size}px;writing-mode:{mode};direction:{direction};line-height:{height.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}px;white-space:pre;text-autospace:no-autospace;color:black;"+(justify?"text-align:justify;text-align-last:justify;":"text-align:start;"));
            Canvas.SetLeft(text,slot.X);Canvas.SetTop(text,slot.Y);reference.Children.Add(text);
        }
        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target=surface is null?context.CreateOffscreenRenderTarget(320,320):context.CreateRenderTarget(surface.GetSurface(),320,320);
            target.SetRenderingEngine(engine);Assert.Equal(engine,target.RenderingEngine);Console.WriteLine("ENGINE "+target.RenderingEngine);
            target.SetDpi(96,96);target.RequestReadback();target.BeginDraw();target.Clear(0,0,0,0);
            using(var dc=new RenderTargetDrawingContext(target,context)){dc.PushTransform(new TranslateTransform(32,32));draw(dc);dc.Pop();dc.Close();}target.EndDraw();var pixels=new byte[320*320*4];Assert.Equal(JaliumResult.Ok,target.FetchReadback(pixels,320*4,out _,out _));return pixels;
        }
        void Save(string kind,byte[] pixels)
        {
            var folder=Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");if(string.IsNullOrWhiteSpace(folder))return;Directory.CreateDirectory(folder);
            var bitmap=BitmapSource.Create(320,320,96,96,PixelFormats.Bgra32,null,pixels,320*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(folder,$"merge-ruby-{mode}-{direction}-{align}-{scene}-{backend}-{engine}-{kind}.png"));encoder.Save(stream);
        }
    }
}
