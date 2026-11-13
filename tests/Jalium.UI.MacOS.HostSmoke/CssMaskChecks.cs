using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record MaskCase(string Name, string Css, Func<double, double, int, double> Alpha, bool Nested = false, string SharedCss = "", bool ExpandedReference = false);
    private static MaskCase[] MaskCases() => [
        new("alpha", "mask:image(rgb(0 0 0 / .5))", (x,y,w) => 128/255d),
        new("luminance", "mask:image(rgb(255 0 0 / .5)) luminance", (x,y,w) => 27/255d),
        new("add", "mask-image:image(rgb(0 0 0 / .5)),image(rgb(0 0 0 / .25));mask-composite:add", (x,y,w) => 160/255d),
        new("subtract", "mask-image:image(rgb(0 0 0 / .5)),image(rgb(0 0 0 / .25));mask-composite:subtract", (x,y,w) => 96/255d),
        new("intersect", "mask-image:image(rgb(0 0 0 / .5)),image(rgb(0 0 0 / .25));mask-composite:intersect", (x,y,w) => 32/255d),
        new("exclude", "mask-image:image(rgb(0 0 0 / .5)),image(rgb(0 0 0 / .25));mask-composite:exclude", (x,y,w) => 128/255d),
        new("nested", "mask:image(rgb(0 0 0 / .5))", (x,y,w) => 64/255d, true),
        new("gradient", "mask:linear-gradient(to right,white,transparent)", (x,y,w) => Math.Round(255*(1-x/w))/255),
        new("position-size", "mask:image(white) right bottom / 50% 50% no-repeat", (x,y,w) => x>=w/2d&&y>=30?1:0),
        new("round", "mask:image(white) 0 0 / 10px 10px round", (x,y,w) => 1),
        new("filter", "mask:image(rgb(0 0 0 / .5))", (x,y,w) => 128/255d, SharedCss: "filter:brightness(.5)"),
        new("clip", "mask:image(rgb(0 0 0 / .5))", (x,y,w) => 128/255d, SharedCss: "clip-path:polygon(0 0,100% 0,0 100%)"),
        new("missing", "mask-image:url('data:application/octet-stream;base64,AQID')", (x,y,w) => 0),
    ];

    private static Canvas MaskScene(MaskCase row, int width, bool masked)
    {
        var owner = new Canvas { Width = width, Height = 60 };
        var content = new Canvas { Width = width, Height = 60 };
        content.Children.Add(new Border { Width = width - 16, Height = 40, Background = Brushes.Red });
        var blue = new Border { Width = width - 24, Height = 36, Background = Brushes.Blue };
        Jalium.UI.Controls.Canvas.SetLeft(blue, 20); Jalium.UI.Controls.Canvas.SetTop(blue, 16); content.Children.Add(blue);
        var text = new TextBlock { Text = "CSS 遮罩 / Ab", FontSize = 14, Foreground = Brushes.White, Width = width, Height = 22 };
        Jalium.UI.Controls.Canvas.SetLeft(text, 8); Jalium.UI.Controls.Canvas.SetTop(text, 20); content.Children.Add(text);
        owner.Children.Add(content);
        Css.SetStyle(owner, (masked ? row.Css : "mask:none") + ";" + row.SharedCss);
        if (masked)
        {
            if (row.Nested) Css.SetStyle(content, "mask:image(rgb(0 0 0 / .5))");
        }
        return owner;
    }

    internal static int RunMasks() => RunMasksCore(MaskCases(), "mask");

    private static int RunMasksCore(MaskCase[] cases, string prefix)
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); int failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 80, 160 })
            foreach (bool transformed in new[] { false, true }) foreach (bool recorded in new[] { false, true }) foreach (var row in cases)
            {
                context.DefaultRenderingEngine = engine;
                string label = $"{prefix}-{backend}-{engine}-{dpi}-{width}-{transformed}-{recorded}-{row.Name}";
                var errors = new List<string>(); int differing = 0; int largest = 0;
                try
                {
                    var actual = MaskScene(row, width, true); var source = MaskScene(row, width, false);
                    var matrix = transformed ? new Matrix(.8,.2,-.2,.8,32,12) : new Matrix(1,0,0,1,16,12);
                    const int height = 112; int canvasWidth = width + 64;
                    byte[] Paint(FrameworkElement view, bool white)
                    {
                        view.SetRootDpi(new(dpi,dpi));
                        for (int i = 0; i < 3; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(view.Dispatcher); view.Measure(new(width,60)); view.Arrange(new(0,0,width,60)); view.UpdateLayout(); }
                        PrepareMaskBorderResources(view);
                        PrepareSvgMaskResources(view);
                        return Pixels(MacOSWritingToolsBitmapCapture.Capture(canvasWidth*dpi,height*dpi,dpi,dc =>
                        {
                            if (white) dc.DrawRectangle(Brushes.White,null,new Rect(-16,-16,canvasWidth+32,height+32));
                            if (recorded)
                            {
                                var recorder = new DrawingRecorder(); recorder.BindWholeFrame();
                                recorder.PushTransform(new MatrixTransform(matrix)); view.Render(recorder); recorder.Pop();
                                DrawingReplayer.Replay(recorder.Commit(),dc);
                            }
                            else { dc.PushTransform(new MatrixTransform(matrix)); view.Render(dc); dc.Pop(); }
                        },()=>true,context) ?? throw new Exception("Mask capture unavailable"));
                    }
                    var pixels = Paint(actual,true); var unmasked = Paint(source,false); var expected = new byte[pixels.Length];
                    var inverse = matrix; inverse.Invert();
                    for (int y=0;y<height*dpi;y++) for(int x=0;x<canvasWidth*dpi;x++)
                    {
                        var local = inverse.Transform(new Point((x+.5)/dpi,(y+.5)/dpi));
                        double mask = MaskReference(row,local,width,dpi); int at=(y*canvasWidth*dpi+x)*4;
                        double alpha=unmasked[at+3]/255d*mask;
                        for(int c=0;c<3;c++) expected[at+c]=(byte)Math.Clamp(Math.Round(unmasked[at+c]*alpha+255*(1-alpha)),0,255);
                        expected[at+3]=255;
                        for(int c=0;c<4;c++){int delta=Math.Abs(pixels[at+c]-expected[at+c]);largest=Math.Max(largest,delta);if(delta>2)++differing;}
                    }
                    Save(pixels,canvasWidth*dpi,height*dpi,label+".png"); Save(expected,canvasWidth*dpi,height*dpi,label+"-expected.png");
                    if(differing>0)throw new Exception($"Independent group/mask reference differs in {differing} channels; maximum delta {largest}");
                    Css.SetStyle(actual,"mask:none;"+row.SharedCss); var cleared=Paint(actual,true);
                    if(!cleared.SequenceEqual(Paint(source,true)) && !row.Nested)throw new Exception("Removing mask did not restore exact source pixels");
                }
                catch(Exception error){errors.Add(error.ToString());failures++;}
                records.Add(new{label,differing,largest,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            foreach (int viewport in new[]{260,520})
            {
                int columns=viewport<400?1:2; int index=0;
                var visibleCases=cases.Where(row=>row.Name!="missing").ToArray();
                int galleryHeight=36+(int)Math.Ceiling(visibleCases.Length/(double)columns)*82;
                var gallery = new Canvas { Width=viewport, Height=galleryHeight, Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1)) };
                foreach(var row in visibleCases)
                {
                    int x=20+(index%columns)*250,y=20+(index/columns)*82;
                    var title=new TextBlock{Text=row.Name,FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=220,Height=20};
                    Jalium.UI.Controls.Canvas.SetLeft(title,x);Jalium.UI.Controls.Canvas.SetTop(title,y);gallery.Children.Add(title);
                    var scene=MaskScene(row,160,true);Jalium.UI.Controls.Canvas.SetLeft(scene,x);Jalium.UI.Controls.Canvas.SetTop(scene,y+20);gallery.Children.Add(scene);index++;
                }
                Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(gallery.Dispatcher);
                gallery.SetRootDpi(new(1,1));gallery.Measure(new(viewport,galleryHeight));gallery.Arrange(new(0,0,viewport,galleryHeight));gallery.UpdateLayout();
                PrepareMaskBorderResources(gallery);
                PrepareSvgMaskResources(gallery);
                var image=MacOSWritingToolsBitmapCapture.Capture(viewport,galleryHeight,1,dc=>gallery.Render(dc),()=>true,context)!;
                Save(Pixels(image),viewport,galleryHeight,$"{prefix}-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output,prefix == "mask" ? "masks.json" : prefix + ".json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));
            return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    // The reference uses literal positioning areas and Porter-Duff alpha values.
    // It never calls the CSS mask painter, image layout helpers or native sampler.
    private static double MaskReference(MaskCase row,Point local,int width,int dpi)
    {
        var bounds = row.ExpandedReference ? new Rect(-16,-16,width+64,112) : new Rect(0,0,width,60);
        if(local.X<bounds.X||local.X>=bounds.Right||local.Y<bounds.Y||local.Y>=bounds.Bottom)return 0;
        int rasterWidth=(int)(bounds.Width*dpi),rasterHeight=(int)(bounds.Height*dpi);
        double sx=Math.Clamp((local.X-bounds.X)*dpi-.5,0,rasterWidth-1),sy=Math.Clamp((local.Y-bounds.Y)*dpi-.5,0,rasterHeight-1);
        int ix=(int)sx,iy=(int)sy;double fx=sx-ix,fy=sy-iy;
        double At(int x,int y)=>row.Alpha(bounds.X+(x+.5)/dpi,bounds.Y+(y+.5)/dpi,width);
        double a=At(ix,iy),b=At(Math.Min(rasterWidth-1,ix+1),iy),c=At(ix,Math.Min(rasterHeight-1,iy+1)),d=At(Math.Min(rasterWidth-1,ix+1),Math.Min(rasterHeight-1,iy+1));
        return (a*(1-fx)+b*fx)*(1-fy)+(c*(1-fx)+d*fx)*fy;
    }
}
