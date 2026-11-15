using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static readonly (double X,double Y,double Fr,double R)[] ConeCircles =
    [
        (96,64,0,40),(96,64,16,40),(96,64,60,40),(96,64,40,40),
        (96,64,0,0),(96,64,24,0),(80,64,8,40),(88,64,64,40),
        (56,64,40,40),(168,64,0,40),(168,64,12,40),(136,64,0,40),
        (124,64,12,40),(116,64,60,40),(135.999,64,0,40),(96,136,8,40)
    ];
    private static readonly Point[] ConePoints = [new(32.5,16.5),new(64.5,32.5),new(80.5,64.5),new(96.5,64.5),new(120.5,48.5),new(144.5,64.5),new(168.5,96.5)];
    private static string ConeSource(int circle,int spread,bool box,bool transformed,string route="drawing",bool linearLight=false)
    {
        var c=ConeCircles[circle];
        var cx=box?(96-20)/160d:96;var cy=box?(64-12)/96d:64;
        var fx=box?(c.X-20)/160:c.X;var fy=box?(c.Y-12)/96:c.Y;
        var r=box?c.R/160:c.R;var fr=box?c.Fr/160:c.Fr;
        var m=ConePaintMatrix(box,transformed);
        var attributes=string.Create(CultureInfo.InvariantCulture,$"gradientUnits='{(box?"objectBoundingBox":"userSpaceOnUse")}' cx='{cx:R}' cy='{cy:R}' fx='{fx:R}' fy='{fy:R}' r='{r:R}' fr='{fr:R}' spreadMethod='{(spread==1?"repeat":spread==2?"reflect":"pad")}' gradientTransform='matrix({m.M11} {m.M12} {m.M21} {m.M22} {m.OffsetX} {m.OffsetY})'");
        var stops="<stop stop-color='black' stop-opacity='.25'/><stop offset='1' stop-color='white' stop-opacity='.85'/>";
        var paint=route=="ellipse"?"<ellipse cx='100' cy='60' rx='80' ry='48' fill='url(#g)' fill-opacity='.8'/>":route=="stroke"?"<path d='M20,12H180V108H20Z' fill='none' stroke='url(#g)' stroke-width='100' stroke-opacity='.8'/>"
            :route=="line"?"<line x1='20' y1='12' x2='180' y2='108' stroke='url(#g)' stroke-width='120' stroke-opacity='.8'/>"
            :"<rect x='20' y='12' width='160' height='96' fill='url(#g)' fill-opacity='.8'/>";
        if(route=="mask")paint="<defs><mask id='mask' maskUnits='userSpaceOnUse' x='0' y='0' width='200' height='128' mask-type='alpha'>"+paint+"</mask></defs><rect width='200' height='128' fill='#146342' mask='url(#mask)'/>";
        return "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><defs><radialGradient id='base' "+attributes+">"+stops+"</radialGradient><radialGradient id='middle' href='#base'/><radialGradient id='g' href='#middle' color-interpolation='"+(linearLight?"linearRGB":"sRGB")+"'/></defs>"+paint+"</svg>";
    }
    private static Matrix ConePaintMatrix(bool box,bool transformed)=>!transformed?Matrix.Identity:box?new(1,.12,-.18,.9,.04,-.02):new(1,.12,-.18,.9,10,-4);

    internal static int RunRadialCones()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            for(var circle=0;circle<ConeCircles.Length;circle++)for(var spread=0;spread<3;spread++)
            foreach(var box in new[]{false,true})for(var variant=0;variant<4;variant++)
            foreach(var route in new[]{"drawing","image","brush","stroke","line","mask","ellipse"})
            {
                var dpi=(variant&1)+1;var recorded=(variant&2)!=0;var affine=(variant==1||variant==2);var transformed=(circle%2)==0;
                var linearLight=(circle+spread+variant)%2==0;
                context.DefaultRenderingEngine=engine;
                var label=$"radial-cone-{backend}-{engine}-{circle}-{spread}-{box}-{variant}-{route}";var errors=new List<string>();var largest=0;var samples=0;
                try
                {
                    using var svg=SvgImage.FromSvgString(ConeSource(circle,spread,box,transformed,route,linearLight));
                    var world=affine?new Matrix(.86,.12,-.08,.92,32,20):new Matrix(1,0,0,1,16,16);
                    var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(256*dpi,192*dpi,dpi,dc=>
                    {
                        void Paint(DrawingContext target)
                        {
                            if(route=="image"||route=="mask")target.DrawImage(svg,new Rect(0,0,200,128),BitmapScalingMode.NearestNeighbor);
                            else if(route=="brush")target.DrawRectangle(new ImageBrush(svg){Stretch=Stretch.Fill,ScalingMode=BitmapScalingMode.NearestNeighbor},null,new(0,0,200,128));
                            else svg.Drawing!.RenderTo(target);
                        }
                        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();recorder.PushTransform(new MatrixTransform(world));Paint(recorder);recorder.Pop();DrawingReplayer.Replay(recorder.Commit(),dc);}
                        else{dc.PushTransform(new MatrixTransform(world));Paint(dc);dc.Pop();}
                    },()=>true,context)??throw new Exception("Radial cone capture unavailable"));
                    foreach(var sample in ConePoints)
                    {
                        if(route=="ellipse" && (Math.Pow((sample.X-100)/80,2)+Math.Pow((sample.Y-60)/48,2)>.8))continue;
                        var device=world.Transform(sample);var px=(int)Math.Floor(device.X*dpi);var py=(int)Math.Floor(device.Y*dpi);
                        var local=InverseGradientReference(new((px+.5)/dpi,(py+.5)/dpi),world);
                        if(route is "image" or "brush" or "mask")
                        {
                            // Explicit nearest sampling makes this an independent
                            // source-pixel oracle, including transform/DPI sizing.
                            var rasterWidth=(int)Math.Ceiling(200*Math.Sqrt(world.M11*world.M11+world.M12*world.M12));
                            var rasterHeight=(int)Math.Ceiling(128*Math.Sqrt(world.M21*world.M21+world.M22*world.M22));
                            if(route=="brush"){rasterWidth*=dpi;rasterHeight*=dpi;}
                            local=new((Math.Floor(local.X*rasterWidth/200)+.5)*200/rasterWidth,
                                (Math.Floor(local.Y*rasterHeight/128)+.5)*128/rasterHeight);
                        }
                        if(box)local=new((local.X-20)/160,(local.Y-12)/96);
                        local=InverseGradientReference(local,ConePaintMatrix(box,transformed));
                        var c=ConeCircles[circle];
                        if(box){local=new(20+160*local.X,64+(12+96*local.Y-64)*160/96);c=(c.X,64+(c.Y-64)*160/96,c.Fr,c.R);}
                        // An almost-tangent repeating cone has subpixel cycles beyond
                        // the focus; exact phase is not stable after float transport.
                        // Keep those points in double-precision CPU tests; native
                        // comparisons here use the stable half-plane of this case.
                        if(circle==14&&local.X>c.X)continue;
                        var value=ConeReference(local,c,spread);var alpha=value is null?0:(.25+.6*value.Value)*.8;
                        var color=value is null?0:linearLight?ConeEncode(value.Value):value.Value;
                        var expected=route=="mask"?new[]{66/255d,99/255d,20/255d,alpha}:new[]{color,color,color,alpha};var at=(py*256*dpi+px)*4;
                        for(var channel=0;channel<4;channel++)
                        {
                            var actual=channel==3?pixels[at+3]:pixels[at+channel]*pixels[at+3]/255d;
                            var wanted=expected[channel]*255*(channel==3?1:alpha);var delta=(int)Math.Ceiling(Math.Abs(actual-wanted));largest=Math.Max(largest,delta);samples++;
                            if(delta>3)throw new Exception($"Circle reference ({px},{py}) channel{channel}: actual{actual:F3} expected{wanted:F3} delta{delta}");
                        }
                    }
                    if(dpi==1&&!recorded&&!box&&spread==0&&route=="drawing")Save(pixels,256,192,label+".png");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            {
                context.DefaultRenderingEngine=engine;
                var label=$"radial-cache-{backend}-{engine}";var errors=new List<string>();var samples=0;var largest=0;
                try
                {
                    var brush=new RadialGradientBrush{Center=new(96,64),GradientOrigin=new(96,64),RadiusX=40,RadiusY=40,
                        MappingMode=BrushMappingMode.Absolute,UsesSvgRadialInterpolation=true,SvgFocalRadius=16,
                        ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation,Opacity=.8};
                    brush.GradientStops.Add(new(Color.FromScRgb(.25f,0,0,0),0));brush.GradientStops.Add(new(Color.FromScRgb(.85f,1,1,1),1));
                    var xs=new[]{64,80,120,144};var wanted=new (double Color,double Alpha)[4];
                    var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(200,128,1,dc=>
                    {
                        foreach(var fr in new[]{16d,60d,16d})
                        {
                            brush.SvgFocalRadius=fr;
                            for(var i=0;i<xs.Length;i++)
                            {
                                var t=ConeReference(new(xs[i]+.5,64.5),(96,64,fr,40),0)!.Value;var a=(.25+.6*t)*.8;
                                wanted[i]=(t*a+wanted[i].Color*(1-a),a+wanted[i].Alpha*(1-a));
                            }
                            dc.DrawRectangle(brush,null,new(20,12,160,96));
                        }
                    },()=>true,context)!);
                    for(var i=0;i<xs.Length;i++)
                    {
                        var at=(64*200+xs[i])*4;var color=pixels[at]*pixels[at+3]/255d;
                        var delta=(int)Math.Ceiling(Math.Max(Math.Abs(color-wanted[i].Color*255),Math.Abs(pixels[at+3]-wanted[i].Alpha*255)));
                        samples+=2;largest=Math.Max(largest,delta);if(delta>3)throw new Exception("Same-brush focal radius cache is stale: "+delta);
                    }
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                foreach(var linearLight in new[]{false,true})foreach(var ellipse in new[]{false,true})
                {
                    label=$"radial-average-{backend}-{engine}-{linearLight}-{ellipse}";errors=[];largest=0;samples=0;
                    try
                    {
                        var source="<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96' cy='64' r='40' fx='136' fy='64' spreadMethod='repeat' color-interpolation='"+(linearLight?"linearRGB":"sRGB")+"'><stop stop-color='black' stop-opacity='.2'/><stop offset='.25' stop-color='white' stop-opacity='.8'/><stop offset='1' stop-color='black' stop-opacity='.6'/></radialGradient>"+(ellipse?"<ellipse cx='100' cy='64' rx='96' ry='60' fill='url(#g)'/>":"<rect width='200' height='128' fill='url(#g)'/>")+"</svg>";
                        using var svg=SvgImage.FromSvgString(source);var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(200,128,1,dc=>svg.Drawing!.RenderTo(dc),()=>true,context)!);
                        var at=(64*200+168)*4;var average=linearLight?ConeEncode(.5):.5;var alpha=.65;
                        for(var c=0;c<4;c++){var actual=c==3?pixels[at+3]:pixels[at+c]*pixels[at+3]/255d;var expected=c==3?alpha*255:average*alpha*255;var delta=(int)Math.Ceiling(Math.Abs(actual-expected));largest=Math.Max(largest,delta);samples++;if(delta>3)throw new Exception("Weighted boundary color: "+delta);}
                    }
                    catch(Exception error){failures++;errors.Add(error.ToString());}
                    records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                }
            }
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var shifted in new[]{false,true})foreach(var fr in new[]{0,16,60})for(var spread=0;spread<3;spread++)
            foreach(var route in new[]{"drawing","image","brush","ellipse","mask","recorded"})
            {
                context.DefaultRenderingEngine=engine;var label=$"radial-apex-{backend}-{engine}-{shifted}-{fr}-{spread}-{route}";var errors=new List<string>();var largest=0;var samples=0;
                try
                {
                    var fx=shifted?84.5:96.5;var paint=route=="ellipse"?"<ellipse cx='100' cy='64' rx='96' ry='60' fill='url(#g)'/>":"<rect x='20' y='12' width='160' height='96' fill='url(#g)'/>";
                    if(route=="mask")paint="<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='200' height='128' mask-type='alpha'>"+paint+"</mask></defs><rect width='200' height='128' fill='#146342' mask='url(#m)'/>";
                    var text=string.Create(CultureInfo.InvariantCulture,$"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96.5' cy='64.5' r='40' fx='{fx}' fy='64.5' fr='{fr}' spreadMethod='{(spread==1?"repeat":spread==2?"reflect":"pad")}'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient>{paint}</svg>");
                    using var svg=SvgImage.FromSvgString(text);var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(200,128,1,dc=>
                    {
                        if(route=="image")dc.DrawImage(svg,new Rect(0,0,200,128),BitmapScalingMode.NearestNeighbor);
                        else if(route=="brush")dc.DrawRectangle(new ImageBrush(svg){Stretch=Stretch.Fill,ScalingMode=BitmapScalingMode.NearestNeighbor},null,new(0,0,200,128));
                        else if(route=="recorded"){var recorder=new DrawingRecorder();recorder.BindWholeFrame();svg.Drawing!.RenderTo(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}
                        else svg.Drawing!.RenderTo(dc);
                    },()=>true,context)!);
                    var x=shifted?(fr==16?76:fr==60?120:84):96;var at=(64*200+x)*4;
                    var color=fr==60?255:fr==16?(spread==1?85:spread==2?170:0):0;
                    var wanted=route=="mask"?new[]{66,99,20,255}:new[]{color,color,color,255};
                    for(var c=0;c<4;c++){var delta=Math.Abs(pixels[at+c]-wanted[c]);samples++;largest=Math.Max(largest,delta);if(delta>2)throw new Exception($"Nested endpoint channel{c}: actual{pixels[at+c]} expected{wanted[c]}");}
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
            foreach(var viewport in new[]{360,720})
            {
                var captions=new[]{"同心扩张 · 焦点圆半径 16","同心收缩 · 焦点圆半径 60","两圆重合 · 透明区域","收缩至点 · 半径为零","外部焦点 · 圆锥外透明","边界焦点 · 重复加权平均"};
                var circles=new[]{1,2,3,5,10,11};var columns=viewport<600?1:2;var height=24+(int)Math.Ceiling(circles.Length/(double)columns)*162;
                var canvas=new Canvas{Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1))};var sources=new List<SvgImage>();
                for(var i=0;i<circles.Length;i++)
                {
                    var x=24+i%columns*360;var y=16+i/columns*162;
                    var caption=new TextBlock{Text=captions[i],FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=312,Height=22};Jalium.UI.Controls.Canvas.SetLeft(caption,x);Jalium.UI.Controls.Canvas.SetTop(caption,y);canvas.Children.Add(caption);
                    var source=SvgImage.FromSvgString(ConeSource(circles[i],i==5?1:0,false,false));sources.Add(source);
                    var image=new Image{Source=source,Width=200,Height=128};Jalium.UI.Controls.Canvas.SetLeft(image,x);Jalium.UI.Controls.Canvas.SetTop(image,y+26);canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"radial-cone-gallery-{backend}-{viewport}.png");foreach(var svg in sources)svg.Dispose();
            }
            File.WriteAllText(Path.Combine(Output,"radial-cones.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,reference="Independent decimal circle family, inverse affine, premultiplied channels",tolerance=3,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false,liveWindow=false}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
    private static double ConeEncode(double value)=>value<=.0031308?12.92*value:1.055*Math.Pow(value,1/2.4)-.055;
    private static double? ConeReference(Point p,(double X,double Y,double Fr,double R) circles,int spread)
    {
        decimal fx=(decimal)circles.X,fy=(decimal)circles.Y,fr=(decimal)circles.Fr,r=(decimal)circles.R;
        decimal dx=96-fx,dy=64-fy,dr=r-fr,x=(decimal)p.X-fx,y=(decimal)p.Y-fy;
        if(dx==0&&dy==0&&dr==0)return null;
        var a=dx*dx+dy*dy-dr*dr;var b=-2*(x*dx+y*dy+fr*dr);var c=x*x+y*y-fr*fr;
        var roots=new List<decimal>();
        if(a==0){if(b!=0)roots.Add(-c/b);}
        else{var discriminant=b*b-4*a*c;if(discriminant>=0){var q=ConeSqrt(discriminant);roots.Add((-b-q)/(2*a));roots.Add((-b+q)/(2*a));}}
        var valid=roots.Where(t=>fr+t*dr>0).ToArray();
        if(valid.Length==0)
        {
            if(fr==0&&r>0&&dx*dx+dy*dy==r*r)return spread==0?1:.5;
            return null;
        }
        var value=(double)valid.Max();
        return spread switch{1=>value-Math.Floor(value),2=>ConeReflect(value),_=>Math.Clamp(value,0,1)};
    }
    private static decimal ConeSqrt(decimal x)
    {
        if(x==0)return 0;var value=(decimal)Math.Sqrt((double)x);
        for(var i=0;i<24;i++){var next=(value+x/value)/2;if(next==value)break;value=next;}return value;
    }
    private static double ConeReflect(double value){var t=Math.Abs(value)%2;return t>1?2-t:t;}
}
