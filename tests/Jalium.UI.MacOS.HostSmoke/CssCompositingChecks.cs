using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record BlendCase(string Mode,string Caption,int R,int G,int B,int A);
    private static BlendCase[] BlendCases() => [
        new("normal","正常",153,90,156,235),new("multiply","正片叠底",63,77,145,235),new("screen","滤色",158,158,214,235),
        new("overlay","叠加",79,120,204,235),new("darken","变暗",67,90,156,235),new("lighten","变亮",154,145,204,235),
        new("color-dodge","颜色减淡",141,175,224,235),new("color-burn","颜色加深",46,55,186,235),new("hard-light","强光",132,100,204,235),
        new("soft-light","柔光",86,129,204,235),new("difference","差值",132,111,131,235),new("exclusion","排除",141,137,151,235),
        new("hue","色相",182,93,173,235),new("saturation","饱和度",80,141,192,235),new("color","颜色",166,102,168,235),
        new("luminosity","明度",55,133,191,235),new("plus-lighter","加亮",162,166,255,255),
    ];

    private sealed class RawBlendBox : Control
    {
        internal double LayerOpacity = 1;
        protected override void OnRender(DrawingContext context)
        {
            var brush = Background?.CloneCurrentValue();
            if (brush is not null) brush.Opacity = LayerOpacity;
            context.DrawRectangle(brush, null, new Rect(RenderSize));
        }
    }

    private static FrameworkElement BlendScene(BlendCase row,string route)
    {
        if(route is "background" or "brush" or "brush-opacity")
        {
            FrameworkElement box=route=="background" ? new Border { Width=60,Height=40 }
                : new RawBlendBox { Width=60,Height=40,LayerOpacity=route=="brush-opacity"?.5:1 };
            Css.SetStyle(box,"background-color:rgba(39,164,219,.7568627451);background-image:linear-gradient(rgba(197,62,133,.6705882353),rgba(197,62,133,.6705882353));background-blend-mode:"+row.Mode);
            return box;
        }
        if(route=="svg-root")
        {
            var backdrop=new Canvas { Width=60,Height=40,Background=new SolidColorBrush(Color.FromArgb(193,39,164,219)) };
            backdrop.Children.Add(new Image { Width=60,Height=40,Stretch=Stretch.Fill,Source=SvgImage.FromSvgString(
                $"<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40' style='mix-blend-mode:{row.Mode}'><rect width='60' height='40' fill='rgba(197,62,133,.6705882353)'/></svg>") });
            return backdrop;
        }
        if(route is "svg" or "svg-clip")
            return new Image { Width=60,Height=40,Stretch=Stretch.Fill,Source=SvgImage.FromSvgString(
                "<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40'><rect width='60' height='40' fill='rgba(39,164,219,.7568627451)'/>"+
                (route=="svg-clip"?"<defs><clipPath id='c'><rect width='60' height='40'/></clipPath></defs><g clip-path='url(#c)'>":"")+
                $"<rect width='60' height='40' fill='rgba(197,62,133,.6705882353)' style='mix-blend-mode:{row.Mode}'/>"+
                (route=="svg-clip"?"</g>":"")+"</svg>") };
        var root=new Canvas { Width=60,Height=40,Background=new SolidColorBrush(Color.FromArgb(193,39,164,219)) };
        var source=new Border { Width=60,Height=40,Background=new SolidColorBrush(Color.FromArgb(171,197,62,133)) };
        Css.SetStyle(source,"mix-blend-mode:"+row.Mode);root.Children.Add(source);return root;
    }

    internal static int RunCompositing()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})foreach(var affine in new[]{false,true})
            foreach(var route in new[]{"element","background","svg","svg-clip","brush","brush-opacity","svg-root","element-suppressed"})foreach(var row in BlendCases())
            {
                context.DefaultRenderingEngine=engine;
                var label=$"blend-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Mode}";
                var errors=new List<string>();var largest=0;
                try
                {
                    var scene=BlendScene(row,route);scene.Measure(new(60,40));scene.Arrange(new(0,0,60,40));scene.UpdateLayout();
                    var matrix=affine?new Matrix(.8,.2,-.2,.8,12,4):Matrix.Identity;
                    var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(80*dpi,64*dpi,dpi,dc=>
                    {
                        var native=dc as RenderTargetDrawingContext;
                        var suppress=route=="element-suppressed";
                        var original=native?.SimplifyElementEffects??false;
                        if(suppress){native!.SimplifyElementEffects=true;native.BeginEffectCapture(0,0,80,64);}
                        try
                        {
                            if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();recorder.PushTransform(new MatrixTransform(matrix));scene.Render(recorder);recorder.Pop();DrawingReplayer.Replay(recorder.Commit(),dc);}
                            else{dc.PushTransform(new MatrixTransform(matrix));try{scene.Render(dc);}finally{dc.Pop();}}
                        }
                        finally{if(suppress){native!.EndEffectCapture();native.SimplifyElementEffects=original;}}
                    },()=>true,context)??throw new Exception("Native blend capture unavailable"));
                    foreach(var point in new[]{new Point(10,10),new Point(30,20),new Point(45,25)})
                    {
                        var device=matrix.Transform(point);var at=((int)(device.Y*dpi)*80*dpi+(int)(device.X*dpi))*4;
                        var reference = route is "svg-clip" or "svg-root" ? BlendCases()[0] : row;
                        var expected=new[]{reference.B,reference.G,reference.R,route=="brush-opacity"?(int)Math.Round(reference.A*.5):reference.A};
                        for(var channel=0;channel<4;channel++)largest=Math.Max(largest,Math.Abs(pixels[at+channel]-expected[channel]));
                    }
                    if(largest>3)throw new Exception($"Independent Chromium RGBA reference differs by {largest} channels");
                    Save(pixels,80*dpi,64*dpi,label+".png");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            foreach(var viewport in new[]{260,520})
            {
                var rows=BlendCases();var columns=viewport<400?1:2;var height=32+(int)Math.Ceiling(rows.Length/(double)columns)*94;
                var canvas=new Canvas { Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1)) };
                for(var i=0;i<rows.Length;i++)
                {
                    var row=rows[i];var x=20+i%columns*250;var y=16+i/columns*94;
                    var label=new TextBlock { Text=row.Caption+" · "+row.Mode,FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=220,Height=20 };
                    Jalium.UI.Controls.Canvas.SetLeft(label,x);Jalium.UI.Controls.Canvas.SetTop(label,y);canvas.Children.Add(label);
                    var sample=new Canvas { Width=220,Height=60,Background=Brushes.White };
                    var below=new Border { Width=132,Height=42,Background=new SolidColorBrush(Color.FromRgb(0,184,212)) };Jalium.UI.Controls.Canvas.SetLeft(below,12);Jalium.UI.Controls.Canvas.SetTop(below,7);
                    var above=new Border { Width=126,Height=42,Background=new SolidColorBrush(Color.FromRgb(239,71,111)) };Css.SetStyle(above,"mix-blend-mode:"+row.Mode);Jalium.UI.Controls.Canvas.SetLeft(above,78);Jalium.UI.Controls.Canvas.SetTop(above,13);
                    sample.Children.Add(below);sample.Children.Add(above);Jalium.UI.Controls.Canvas.SetLeft(sample,x);Jalium.UI.Controls.Canvas.SetTop(sample,y+22);canvas.Children.Add(sample);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"blend-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output,"compositing.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false,liveWindow=false}));
            return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
}
