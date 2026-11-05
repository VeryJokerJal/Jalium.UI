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
    internal sealed record GradientTransformCase(string Name, string Caption, bool Radial, bool Absolute,
        bool Svg, bool Template, bool Defaults, Matrix Transform, Matrix Relative, int CssAngle=0)
    {
        internal GradientBrush Brush()
        {
            if(CssAngle!=0)
            {
                var reader=new Jalium.UI.Styling.CssTokenReader(CssAngle+"deg in srgb,black,white");
                if(!Jalium.UI.Styling.CssGradientParser.TryParseGradientFunction("linear-gradient",ref reader,out var parsed)||parsed is not GradientBrush css)throw new Exception("CSS angle brush unavailable");
                css.Transform=new MatrixTransform(Transform);css.RelativeTransform=new MatrixTransform(Relative);css.Opacity=.8;return css;
            }
            GradientBrush brush = Radial
                ? new RadialGradientBrush { Center = Absolute ? new(92,44) : new(.45,.5), GradientOrigin = Absolute ? new(76,44) : new(.35,.5), RadiusX = Absolute ? 72 : .45, RadiusY = Absolute ? 38.4 : .6 }
                : new LinearGradientBrush { StartPoint = Absolute ? new(36,24.8) : new(.1,.2), EndPoint = Absolute ? new(164,60) : new(.9,.75) };
            brush.MappingMode=Absolute?BrushMappingMode.Absolute:BrushMappingMode.RelativeToBoundingBox;
            brush.ColorInterpolationMode=ColorInterpolationMode.SRgbLinearInterpolation;brush.Opacity=.8;
            brush.Transform=new MatrixTransform(Transform);brush.RelativeTransform=new MatrixTransform(Relative);
            brush.GradientStops.Add(new(Color.FromArgb(64,0,0,0),0));brush.GradientStops.Add(new(Color.FromArgb(217,255,255,255),1));return brush;
        }
        internal string Source(bool stroke=false,bool mask=false,bool line=false)
        {
            var tag=Radial?"radialGradient":"linearGradient";
            var geometry=Defaults?"":Radial?Absolute?"cx='92' cy='44' r='72' fx='76' fy='44'":"cx='.45' cy='.5' r='.45' fx='.35' fy='.5'"
                :Absolute?"x1='36' y1='24.8' x2='164' y2='60'":"x1='.1' y1='.2' x2='.9' y2='.75'";
            var matrix=string.Create(CultureInfo.InvariantCulture,$"matrix({Transform.M11} {Transform.M12} {Transform.M21} {Transform.M22} {Transform.OffsetX} {Transform.OffsetY})");
            var stops="<stop stop-color='black' stop-opacity='0.25098039215686274'/><stop offset='1' stop-color='white' stop-opacity='0.8509803921568627'/>";
            var units=$"gradientUnits='{(Absolute?"userSpaceOnUse":"objectBoundingBox")}'";
            var defs=Template?$"<style>#g{{transform:matrix(1.05,.22,-.31,.85,.08,-.05)}}</style><defs><{tag} id='base' {units} {geometry} gradientTransform='translate(.4 .4)'>{stops}</{tag}><{tag} id='middle' href='#base'/><{tag} id='g' href='#middle'/></defs>"
                :$"<defs><{tag} id='g' {units} {geometry} gradientTransform='{matrix}'>{stops}</{tag}></defs>";
            var paint=line?"<line x1='20' y1='12' x2='180' y2='76' stroke='url(#g)' stroke-width='12' stroke-opacity='.8'/>":stroke?"<path d='M20,12H180V76H20Z' fill='none' stroke='url(#g)' stroke-width='80' stroke-opacity='.8'/>"
                :"<rect x='20' y='12' width='160' height='64' fill='url(#g)' fill-opacity='.8'/>";
            if(mask)paint="<defs><mask id='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='200' height='96' mask-type='alpha'>"+paint+"</mask></defs><rect width='200' height='96' fill='#146342' mask='url(#alpha)'/>";
            return "<svg xmlns='http://www.w3.org/2000/svg' width='200' height='96'>"+defs+paint+"</svg>";
        }
        internal double Parameter(Point point)
        {
            if(CssAngle!=0)
            {
                point=InverseGradientReference(point,Transform);point=InverseGradientReference(new((point.X-20)/160,(point.Y-12)/64),Relative);
                var radians=CssAngle*Math.PI/180;var dx=Math.Sin(radians);var dy=-Math.Cos(radians);var length=Math.Abs(160*dx)+Math.Abs(64*dy);
                return .5+((point.X*160-80)*dx+(point.Y*64-32)*dy)/length;
            }
            if(Defaults)return Radial?Math.Sqrt(Math.Pow(point.X-100,2)+Math.Pow(point.Y-48,2))/(Math.Sqrt((200*200+96*96)/2.0)/2):point.X/200;
            if(Svg&&!Absolute)point=new((point.X-20)/160,(point.Y-12)/64);
            point=InverseGradientReference(point,Transform);
            if(!Svg){point=new((point.X-20)/160,(point.Y-12)/64);point=InverseGradientReference(point,Relative);point=new(20+point.X*160,12+point.Y*64);}
            var normalized=Svg&&!Absolute;
            if(!Radial){var dx=normalized ? .8:128;var dy=normalized ? .55:35.2;return((point.X-(normalized ? .1:36))*dx+(point.Y-(normalized ? .2:24.8))*dy)/(dx*dx+dy*dy);}
            var rx=normalized ? .45:72;var ry=Svg?rx:38.4;
            var fx=-2.0/9;var x=(point.X-(normalized ? .45:92))/rx-fx;var y=(point.Y-(normalized ? .5:44))/ry;
            var a=x*x+y*y;var b=fx*x;return a>1e-20?a/(-b+Math.Sqrt(b*b+a*(1-fx*fx))):0;
        }
    }

    internal static GradientTransformCase[] GradientTransformCases()
    {
        var rows=new List<GradientTransformCase>();
        foreach(var radial in new[]{false,true})foreach(var absolute in new[]{false,true})foreach(var affine in new[]{false,true})
            rows.Add(new($"svg-{radial}-{absolute}-{affine}",$"SVG {(radial?"径向":"线性")} · {(absolute?"用户坐标":"对象边界")} · {(affine?"仿射变换":"原始坐标")}",radial,absolute,true,false,false,
                !affine?Matrix.Identity:absolute?new(1.05,.22,-.31,.85,8,-5):new(1.05,.22,-.31,.85,.08,-.05),Matrix.Identity));
        foreach(var radial in new[]{false,true})rows.Add(new($"defaults-{radial}",$"SVG {(radial?"径向":"线性")} · 视口默认值",radial,true,true,false,true,Matrix.Identity,Matrix.Identity));
        foreach(var radial in new[]{false,true})rows.Add(new($"template-{radial}",$"SVG {(radial?"径向":"线性")} · 模板与 CSS 变换",radial,false,true,true,false,new(1.05,.22,-.31,.85,.08,-.05),Matrix.Identity));
        foreach(var radial in new[]{false,true})foreach(var absolute in new[]{false,true})rows.Add(new($"brush-{radial}-{absolute}",$"画刷 {(radial?"径向":"线性")} · {(absolute?"绝对":"相对")} · 双重变换",radial,absolute,false,false,false,new(1,0,.3,.85,-12,7),new(1.1,.2,-.3,.9,.05,-.08)));
        foreach(var angle in new[]{35,125})rows.Add(new($"css-angle-{angle}",$"CSS {angle}° · 实际尺寸与双重变换",false,false,false,false,false,new(1,0,.3,.85,-12,7),new(1.1,.2,-.3,.9,.05,-.08),angle));
        return rows.ToArray();
    }

    internal static int RunGradientTransforms()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        File.WriteAllText(Path.Combine(Output,"gradient-transform-cases.json"),JsonSerializer.Serialize(GradientTransformCases().Where(x=>x.Svg).Select(x=>new{x.Name,x.Caption,Source=x.Source()})));
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})foreach(var affine in new[]{false,true})foreach(var row in GradientTransformCases())
            foreach(var route in row.Svg?new[]{"drawing","image","brush","stroke","mask","line"}:new[]{"drawing","image","brush","stroke"})
            {
                context.DefaultRenderingEngine=engine;var label=$"gradient-transform-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";var errors=new List<string>();var largest=0;var samples=0;
                try
                {
                    using var svg=row.Svg?SvgImage.FromSvgString(row.Source()):null;using var stroke=row.Svg?SvgImage.FromSvgString(row.Source(stroke:true)):null;using var mask=row.Svg?SvgImage.FromSvgString(row.Source(mask:true)):null;using var line=row.Svg?SvgImage.FromSvgString(row.Source(line:true)):null;
                    var drawing=new DrawingGroup();drawing.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new(0,0,200,96))));drawing.Children.Add(svg?.Drawing??new GeometryDrawing(row.Brush(),null,new RectangleGeometry(new(20,12,160,64))));
                    ImageSource source=svg is null?new DrawingImage(drawing):svg;
                    var world=affine?new Matrix(.86,.12,-.08,.92,32,20):new Matrix(1,0,0,1,16,16);
                    var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(256*dpi,160*dpi,dpi,dc=>
                    {
                        void Paint(DrawingContext target)
                        {
                            if(route=="drawing")drawing.RenderTo(target);
                            else if(route=="image")target.DrawImage(source,new Rect(0,0,200,96));
                            else if(route=="brush")target.DrawRectangle(new ImageBrush(source){Stretch=Stretch.Fill},null,new(0,0,200,96));
                            else if(route=="mask")target.DrawImage(mask!,new Rect(0,0,200,96));
                            else if(route=="line")line!.Drawing!.RenderTo(target);
                            else if(stroke is not null)stroke.Drawing!.RenderTo(target);
                            else target.DrawRectangle(null,new Pen(row.Brush(),80),new(20,12,160,64));
                        }
                        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();recorder.PushTransform(new MatrixTransform(world));Paint(recorder);recorder.Pop();DrawingReplayer.Replay(recorder.Commit(),dc);}
                        else{dc.PushTransform(new MatrixTransform(world));Paint(dc);dc.Pop();}
                    },()=>true,context)??throw new Exception("Gradient transform capture unavailable"));
                    foreach(var x in new[]{32.5,68.5,100.5,140.5})
                    {
                        var point=world.Transform(new Point(x,route=="line"?12+(x-20)*.4:40.5));var px=(int)Math.Floor(point.X*dpi);var py=(int)Math.Floor(point.Y*dpi);
                        var local=InverseGradientReference(new((px+.5)/dpi,(py+.5)/dpi),world);var t=Math.Clamp(row.Parameter(local),0,1);var alpha=row.CssAngle!=0?.8:(64+153*t)/255*.8;
                        var expected=route=="mask"?new[]{66/255d,99/255d,20/255d,alpha}:new[]{t,t,t,alpha};var at=(py*256*dpi+px)*4;
                        for(var c=0;c<4;c++){var actual=c==3?pixels[at+3]:pixels[at+c]*pixels[at+3]/255d;var wanted=expected[c]*255*(c==3?1:alpha);var delta=(int)Math.Ceiling(Math.Abs(actual-wanted));largest=Math.Max(largest,delta);samples++;if(delta>3)throw new Exception($"Inverse-coordinate reference ({px},{py}) channel{c}: actual{actual:F3}, expected{wanted:F3}, delta{delta}");}
                    }
                    if(dpi==1&&!recorded&&!affine)Save(pixels,256,160,label+".png");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var row in GradientTransformCases().Where(x=>!x.Svg&&x.CssAngle==0))foreach(var offsetChange in new[]{false,true})
            {
                context.DefaultRenderingEngine=engine;var label=$"gradient-cache-{backend}-{engine}-{row.Name}-{offsetChange}";var errors=new List<string>();var largest=0;var samples=0;
                try
                {
                    var brush=row.Brush();var absolute=new MatrixTransform();var relative=new MatrixTransform();brush.Transform=absolute;brush.RelativeTransform=relative;
                    var expected=new (double Color,double Alpha)[4];var xs=new[]{32.5,68.5,100.5,140.5};
                    var pixels=Pixels(MacOSWritingToolsBitmapCapture.Capture(200,96,1,dc=>
                    {
                        for(var state=0;state<3;state++)
                        {
                            absolute.Matrix=!offsetChange&&state==1?row.Transform:Matrix.Identity;relative.Matrix=!offsetChange&&state==1?row.Relative:Matrix.Identity;
                            var offset=offsetChange&&state==1?new Point(20,12):default;var box=offset==default?new Rect(20,12,160,64):new Rect(0,0,160,64);
                            for(var i=0;i<xs.Length;i++){var t=Math.Clamp(GradientCacheParameter(brush,new(xs[i],40.5),box,offset),0,1);var alpha=(64+153*t)/255*.8;expected[i]=(t*alpha+expected[i].Color*(1-alpha),alpha+expected[i].Alpha*(1-alpha));}
                            dc.PushTransform(new TranslateTransform(offset.X,offset.Y));dc.DrawRectangle(brush,null,box);dc.Pop();
                        }
                    },()=>true,context)??throw new Exception("Cache capture unavailable"));
                    for(var i=0;i<xs.Length;i++)
                    {
                        var at=(40*200+(int)xs[i])*4;
                        for(var c=0;c<4;c++){var actual=c==3?pixels[at+3]:pixels[at+c]*pixels[at+3]/255d;var wanted=(c==3?expected[i].Alpha:expected[i].Color)*255;var delta=(int)Math.Ceiling(Math.Abs(actual-wanted));largest=Math.Max(largest,delta);samples++;if(delta>3)throw new Exception($"Cache mutation/offset reference sample{i} channel{c}: actual{actual:F3}, expected{wanted:F3}, delta{delta}");}
                    }
                    Save(pixels,200,96,label+".png");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,samples,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
            foreach(var viewport in new[]{360,720})
            {
                var rows=GradientTransformCases();var columns=viewport<600?1:2;var height=24+(int)Math.Ceiling(rows.Length/(double)columns)*128;
                var canvas=new Canvas{Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1))};var sources=new List<SvgImage>();
                for(var i=0;i<rows.Length;i++)
                {
                    var row=rows[i];var x=24+i%columns*360;var y=16+i/columns*128;
                    var caption=new TextBlock{Text=row.Caption,FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=312,Height=22};Jalium.UI.Controls.Canvas.SetLeft(caption,x);Jalium.UI.Controls.Canvas.SetTop(caption,y);canvas.Children.Add(caption);
                    ImageSource source;if(row.Svg){var svg=SvgImage.FromSvgString(row.Source());sources.Add(svg);source=svg;}else{var scene=new DrawingGroup();scene.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new(0,0,200,96))));scene.Children.Add(new GeometryDrawing(row.Brush(),null,new RectangleGeometry(new(20,12,160,64))));source=new DrawingImage(scene);}
                    var preview=new Image{Source=source,Width=200,Height=96};Jalium.UI.Controls.Canvas.SetLeft(preview,x);Jalium.UI.Controls.Canvas.SetTop(preview,y+26);canvas.Children.Add(preview);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"gradient-transform-gallery-{backend}-{viewport}.png");foreach(var svg in sources)svg.Dispose();
            }
            File.WriteAllText(Path.Combine(Output,"gradient-transforms.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,reference="Independent inverse affine, gradient projection, focal ray and premultiplied channels",tolerance=3,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false,liveWindow=false}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    private static double GradientCacheParameter(GradientBrush brush,Point point,Rect box,Point offset)
    {
        point=InverseGradientReference(new(point.X-offset.X,point.Y-offset.Y),brush.Transform!.Value);
        point=InverseGradientReference(new((point.X-box.X)/box.Width,(point.Y-box.Y)/box.Height),brush.RelativeTransform!.Value);
        point=new(box.X+point.X*box.Width,box.Y+point.Y*box.Height);var relative=brush.MappingMode==BrushMappingMode.RelativeToBoundingBox;
        Point Map(Point p)=>relative?new(box.X+p.X*box.Width,box.Y+p.Y*box.Height):p;
        if(brush is LinearGradientBrush linear){var a=Map(linear.StartPoint);var b=Map(linear.EndPoint);var dx=b.X-a.X;var dy=b.Y-a.Y;return((point.X-a.X)*dx+(point.Y-a.Y)*dy)/(dx*dx+dy*dy);}
        var radial=(RadialGradientBrush)brush;var center=Map(radial.Center);var origin=Map(radial.GradientOrigin);var rx=radial.RadiusX*(relative?box.Width:1);var ry=radial.RadiusY*(relative?box.Height:1);
        var fx=(origin.X-center.X)/rx;var fy=(origin.Y-center.Y)/ry;var x=(point.X-center.X)/rx-fx;var y=(point.Y-center.Y)/ry-fy;var a2=x*x+y*y;var b2=fx*x+fy*y;return a2>1e-20?a2/(-b2+Math.Sqrt(b2*b2+a2*(1-fx*fx-fy*fy))):0;
    }

    private static Point InverseGradientReference(Point point,Matrix matrix)
    {
        var x=point.X-matrix.OffsetX;var y=point.Y-matrix.OffsetY;var determinant=matrix.M11*matrix.M22-matrix.M12*matrix.M21;
        return new((x*matrix.M22-y*matrix.M21)/determinant,(y*matrix.M11-x*matrix.M12)/determinant);
    }
}
