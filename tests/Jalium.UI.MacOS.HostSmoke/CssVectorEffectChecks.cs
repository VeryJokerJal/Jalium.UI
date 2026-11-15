using System.Text.Json;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static string VectorSvg(string body) => "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'>" + body + "</svg>";
    internal static int RunVectorEffects()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        var cases=new (string Caption,string Actual,string Expected)[]
        {
            ("非等比缩放 · 虚线保持 4px", "<g transform='scale(2,3)'><line x1='10' y1='16' x2='40' y2='16' stroke='#146342' stroke-width='4' stroke-dasharray='4 4' vector-effect='non-scaling-stroke'/></g>","<path d='M20,48H80' fill='none' stroke='#146342' stroke-width='4' stroke-dasharray='4 4'/>"),
            ("倾斜变换 · 圆角与连接", "<path d='M5,8L25,8L25,24L12,16' transform='matrix(2,.4,.7,1.5,8,5)' fill='none' stroke='#146342' stroke-width='4' stroke-linecap='round' stroke-linejoin='round' vector-effect='non-scaling-stroke'/>","<path d='M23.6,19L63.6,27L74.8,51L43.2,33.8' fill='none' stroke='#146342' stroke-width='4' stroke-linecap='round' stroke-linejoin='round'/>"),
            ("嵌套视口 · 共享根坐标", "<svg x='10' y='10' width='80' height='80' viewBox='0 0 40 20' preserveAspectRatio='none'><line x1='5' y1='10' x2='35' y2='10' stroke='#146342' stroke-width='4' vector-effect='non-scaling-stroke'/></svg>","<path d='M20,50H80' fill='none' stroke='#146342' stroke-width='4'/>"),
            ("描边标记 · 大小保持一致", "<defs><marker id='m' markerWidth='3' markerHeight='3' refX='1' refY='1' orient='auto' overflow='visible'><rect width='2' height='2' fill='#1260b0'/></marker></defs><g transform='scale(2,3)'><line x1='10' y1='16' x2='40' y2='16' stroke='#146342' stroke-width='4' vector-effect='non-scaling-stroke' marker-end='url(#m)'/></g>","<path d='M20,48H80' fill='none' stroke='#146342' stroke-width='4'/><rect x='76' y='44' width='8' height='8' fill='#1260b0'/>"),
            ("透明度与剪裁 · 绘制顺序", "<defs><clipPath id='c'><rect x='5' y='5' width='30' height='25'/></clipPath></defs><g transform='scale(2,3)'><rect x='10' y='8' width='20' height='14' fill='#1260b0' stroke='#146342' stroke-width='4' vector-effect='non-scaling-stroke' paint-order='stroke fill' opacity='.5' clip-path='url(#c)'/></g>","<path d='M20,24H60V66H20Z' fill='#1260b0' stroke='#146342' stroke-width='4' paint-order='stroke fill' opacity='.5'/>"),
            ("曲线缩放 · 轮廓保持平滑", "<path d='M5,20Q20,0,35,20' transform='scale(2,3)' fill='none' stroke='#146342' stroke-width='4' vector-effect='non-scaling-stroke'/>","<path d='M10,60Q40,0,70,60' fill='none' stroke='#146342' stroke-width='4'/>"),
            ("渐变描边 · 保留绘制坐标", "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='10' x2='40'><stop stop-color='#146342'/><stop offset='1' stop-color='#1260b0'/></linearGradient></defs><path d='M10,10H40V30H10Z' transform='matrix(2,.2,.3,2,5,5)' fill='none' stroke='url(#g)' stroke-width='4' vector-effect='non-scaling-stroke'/>","<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='10' x2='40' gradientTransform='matrix(2,.2,.3,2,5,5)'><stop stop-color='#146342'/><stop offset='1' stop-color='#1260b0'/></linearGradient></defs><path d='M28,27L88,33L94,73L34,67Z' fill='none' stroke='url(#g)' stroke-width='4'/>")
        };
        // Independent quadratic samples avoid using the ordinary native
        // curve flattener as the accuracy oracle for a transformed curve.
        var curve=string.Join("L",Enumerable.Range(0,513).Select(i=>{var t=i/512d;return string.Create(CultureInfo.InvariantCulture,$"{10+60*t:R},{60-120*t+120*t*t:R}");}));
        cases[5]=(cases[5].Caption,cases[5].Actual,"<path d='M"+curve+"' fill='none' stroke='#146342' stroke-width='4'/>");
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var row in cases)foreach(var route in new[]{"drawing","image","brush"})foreach(var recorded in new[]{false,true})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;var label=$"vector-{backend}-{engine}-{Array.IndexOf(cases,row)}-{route}-{recorded}-{dpi}";var errors=new List<string>();var largest=0;
                try
                {
                    byte[] Capture(string body,bool reference=false)
                    {
                        using var svg=SvgImage.FromSvgString(VectorSvg(body));
                        return Pixels(MacOSWritingToolsBitmapCapture.Capture(128*dpi,128*dpi,dpi,dc=>
                        {
                            void Paint(DrawingContext target)
                            {
                                target.PushTransform(new MatrixTransform(new Matrix(1,0,0,1,12,12)));
                                if(route=="image")target.DrawImage(svg,new Rect(0,0,100,100),BitmapScalingMode.NearestNeighbor);
                                else if(route=="brush")target.DrawRectangle(new ImageBrush(svg){Stretch=Stretch.Fill,ScalingMode=BitmapScalingMode.NearestNeighbor},null,new Rect(0,0,100,100));
                                else if(reference && backend==RenderBackend.Software)
                                {
                                    var pixels=SoftwareVectorRasterizer.Rasterize(svg.Drawing!,100*dpi,100*dpi,new Rect(0,0,100,100))!;
                                    var bitmap=BitmapSource.Create(100*dpi,100*dpi,96*dpi,96*dpi,PixelFormats.Bgra32,null,pixels,400*dpi);
                                    target.DrawImage(bitmap,new Rect(0,0,100,100),BitmapScalingMode.NearestNeighbor);
                                }
                                else svg.Drawing!.RenderTo(target);
                                target.Pop();
                            }
                            if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();Paint(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}else Paint(dc);
                        },()=>true,context)??throw new Exception("Vector-effect capture unavailable"));
                    }
                    var actual=Capture(row.Actual);var expected=Capture(row.Expected,true);
                    for(var i=0;i<actual.Length;i+=4)for(var channel=0;channel<4;channel++)
                    {
                        var a=channel==3?actual[i+3]:(actual[i+channel]*actual[i+3]+127)/255;var b=channel==3?expected[i+3]:(expected[i+channel]*expected[i+3]+127)/255;
                        var error=Math.Abs(a-b);largest=Math.Max(largest,error);if(error>3&&errors.Count<4)errors.Add($"pixel {i/4} channel {channel}: {a} != {b}");
                    }
                    if(errors.Count>0){Save(actual,128*dpi,128*dpi,label+".png");Save(expected,128*dpi,128*dpi,label+"-expected.png");}
                }
                catch(Exception error){errors.Add(error.ToString());}
                if(errors.Count>0)failures++;records.Add(new{label,largest,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
            foreach(var viewport in new[]{360,720})
            {
                var columns=viewport<600?1:2;var height=24+(int)Math.Ceiling(6d/columns)*162;var sources=new List<SvgImage>();
                var canvas=new Canvas{Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1))};
                for(var i=0;i<6;i++)
                {
                    var x=24+i%columns*360;var y=16+i/columns*162;
                    var caption=new TextBlock{Text=cases[i].Caption,FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=312,Height=22};Jalium.UI.Controls.Canvas.SetLeft(caption,x);Jalium.UI.Controls.Canvas.SetTop(caption,y);canvas.Children.Add(caption);
                    var svg=SvgImage.FromSvgString(VectorSvg(cases[i].Actual));sources.Add(svg);var image=new Image{Source=svg,Width=200,Height=128,Stretch=Stretch.Uniform};Jalium.UI.Controls.Canvas.SetLeft(image,x);Jalium.UI.Controls.Canvas.SetTop(image,y+26);canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"vector-effect-gallery-{backend}-{viewport}.png");foreach(var source in sources)source.Dispose();
            }
            File.WriteAllText(Path.Combine(Output,"vector-effects.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,reference="Independent authored SVG viewport paths; premultiplied channels",nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false,liveWindow=false}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
}
