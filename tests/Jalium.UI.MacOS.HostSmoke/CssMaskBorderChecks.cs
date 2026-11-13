using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static MaskCase[] MaskBorderCases() => [
        new("alpha", "mask-border:image(rgb(0 0 0 / .5)) 0 fill", (x,y,w) => 128/255d),
        new("luminance", "mask-border:image(rgb(255 0 0 / .5)) 0 fill luminance", (x,y,w) => 27/255d),
        new("opaque-center", "mask-border:image(transparent) 10 / 8px", (x,y,w) => x>=8&&x<w-8&&y>=8&&y<52?1:0),
        new("zero-slice", "mask-border:image(transparent)", (x,y,w) => 1),
        new("filled-transparent", "mask-border:image(transparent) 0 fill", (x,y,w) => 0),
        new("combined", "mask-border:image(rgb(0 0 0 / .5)) 0 fill;mask-image:image(rgb(0 0 0 / .5))", (x,y,w) => 64/255d),
        new("nested", "mask-border:image(rgb(0 0 0 / .5)) 0 fill", (x,y,w) => 64/255d, true),
        new("reset", "mask-border:image(transparent) 0 fill;mask:image(white)", (x,y,w) => 1),
        new("independent-mode", "mask-border:image(rgb(0 0 0 / .5)) 0 fill;mask-mode:luminance", (x,y,w) => 128/255d),
        new("gradient", "mask-border:linear-gradient(to right,white,transparent) 0 fill", (x,y,w) => Math.Round(255*(1-x/w))/255),
        new("round", "mask-border:image(white) 10 fill / 8px round", (x,y,w) => 1),
        new("repeat", "mask-border:image(white) 10 fill / 8px repeat", (x,y,w) => 1),
        new("space", "mask-border:image(white) 10 fill / 8px space", MaskBorderSpaceReference),
        new("nine-slice", $"mask-border:url('{MaskBorderNineData()}') 10 fill / 8px", MaskBorderNineReference),
        new("filter", "mask-border:image(rgb(0 0 0 / .5)) 0 fill", (x,y,w) => 128/255d, SharedCss: "filter:brightness(.5)"),
        new("clip", "mask-border:image(rgb(0 0 0 / .5)) 0 fill", (x,y,w) => 128/255d, SharedCss: "clip-path:polygon(0 0,100% 0,0 100%)"),
        new("missing", "mask-border-source:url('data:application/octet-stream;base64,AQID')", (x,y,w) => 0),
    ];

    internal static int RunMaskBorders() => RunMasksCore(MaskBorderCases(), "mask-border");

    // Literal geometry and source values. No production nine-slice plan, tile
    // iterator, pixel sampler or mask builder participates in this reference.
    private static double MaskBorderNineReference(double x, double y, int width)
    {
        byte[] values = [32,64,96,128,160,192,224,255,0];
        int column = x<8?0:x>=width-8?2:1, row = y<8?0:y>=52?2:1;
        return values[row*3+column]/255d;
    }
    private static double MaskBorderSpaceReference(double x, double y, int width)
    {
        if (x>=8&&x<width-8)
        {
            double tile=(width-20)*.8, left=8+(width-16-tile)/2;
            if(x<left||x>=left+tile)return 0;
        }
        if(y>=8&&y<52&&(y<14||y>=46))return 0;
        return 1;
    }
    private static string MaskBorderNineData()
    {
        byte[] alpha=[32,64,96,128,160,192,224,255,0];var pixels=new byte[30*30*4];
        for(int y=0;y<30;y++)for(int x=0;x<30;x++)
        {int at=(y*30+x)*4;pixels[at]=pixels[at+1]=pixels[at+2]=255;pixels[at+3]=alpha[y/10*3+x/10];}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(30,30,96,96,PixelFormats.Bgra32,null,pixels,120)));
        using var stream=new MemoryStream();encoder.Save(stream);return "data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());
    }
    private static void PrepareMaskBorderResources(FrameworkElement view)
    {
        var resources=new List<CssBackgroundImageResource>();
        void Visit(Visual visual)
        {
            if(visual.GetValue(CssMaskBorderProperties.SourceProperty) is CssMaskBorderSource source)resources.AddRange(source.Resources);
            for(int i=0;i<visual.InternalVisualChildrenCount;i++)if(visual.InternalGetVisualChild(i) is {} child)Visit(child);
        }
        Visit(view);
        void Pump(Func<bool> ready)
        {
            var end=DateTime.UtcNow.AddSeconds(10);
            while(!ready()&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.ProcessQueue();BitmapDecodeNotifier.FlushPendingForTests();Thread.Sleep(1);}
            if(!ready())throw new TimeoutException("Mask border resource did not become ready");
            Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(view.Dispatcher);
        }
        foreach(var resource in resources.Distinct())
        {
            Pump(()=>resource.Ready.IsCompleted);
            if(resource.Get(CssNode.Get(view)) is BitmapImage bitmap)
            {var task=bitmap.PreloadAsync(80,60);Pump(()=>task.IsCompleted);}
        }
    }
}
