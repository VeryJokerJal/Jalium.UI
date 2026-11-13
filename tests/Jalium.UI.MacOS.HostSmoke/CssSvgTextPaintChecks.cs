using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SvgTextPaintCase(string Name, string Caption, string Css, string Body, Func<Drawing> Reference);

    private static PathGeometry TextPaintOutline(string text = "H", double x = 16, double y = 62,
        double scaleX = 1, double angle = 0)
    {
        if (!NativeTextOutline.TryGetPath(text, "Helvetica", 48, 400, 0, out var source, out _, out var baseline))
            throw new Exception("Independent font outline unavailable");
        var geometry = PathMarkupParser.ParseSvgPathData(source).GetFlattenedPathGeometry(.01, ToleranceType.Absolute);
        geometry.FillRule = FillRule.Nonzero;
        var radians = angle * Math.PI / 180;
        Point Map(Point point)
        {
            var px = point.X * scaleX;
            var py = point.Y - baseline;
            return new(x + px * Math.Cos(radians) - py * Math.Sin(radians),
                y + px * Math.Sin(radians) + py * Math.Cos(radians));
        }
        foreach (var figure in geometry.Figures)
        {
            figure.IsClosed = true;
            figure.StartPoint = Map(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) line.Point = Map(line.Point);
                else if (segment is PolyLineSegment lines)
                    for (var i = 0; i < lines.Points.Count; i++) lines.Points[i] = Map(lines.Points[i]);
            }
        }
        return geometry;
    }

    private static Drawing TextPaintReference(Geometry? geometry = null, bool strokeFirst = false,
        bool fill = true, double alpha = 1, double opacity = 1, bool dashes = false, Brush? strokeBrush = null)
    {
        geometry ??= TextPaintOutline();
        var fillBrush = fill ? new SolidColorBrush(Colors.Red) { Opacity = alpha } : null;
        var pen = new Pen(strokeBrush ?? new SolidColorBrush(Colors.Blue) { Opacity = alpha }, 4)
        {
            StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat, DashCap = PenLineCap.Flat,
        };
        if (dashes) pen.DashStyle = new DashStyle([1d, 1d], .5);
        var group = new DrawingGroup { Opacity = opacity };
        var filled = new GeometryDrawing(fillBrush, null, geometry);
        var stroked = new GeometryDrawing(null, pen, geometry);
        group.Children.Add(strokeFirst ? stroked : filled);
        group.Children.Add(strokeFirst ? filled : stroked);
        return group;
    }

    private static SvgTextPaintCase[] SvgTextPaintCases()
    {
        const string text = "<text x='16' y='62'>H</text>";
        static double Advance()
        {
            if (!NativeTextOutline.TryGetPath("H", "Helvetica", 48, 400, 0, out _, out var width, out _)) throw new Exception("Font advance unavailable");
            return width;
        }
        return [
            new("outline", "只有文字轮廓", "text{fill:none}", text, () => TextPaintReference(fill:false)),
            new("normal", "先填色，再描边", "", text, () => TextPaintReference()),
            new("stroke-first", "先描边，保留字形", "text{paint-order:stroke}", text, () => TextPaintReference(strokeFirst:true)),
            new("markers-order", "省略操作按默认补齐", "text{paint-order:markers stroke}", text, () => TextPaintReference(strokeFirst:true)),
            new("dashes", "虚线与偏移", "text{fill:none;stroke-dasharray:4 4;stroke-dashoffset:2}", text, () => TextPaintReference(fill:false,dashes:true)),
            new("opacity", "填色描边统一透明度", "text{opacity:.5}", text, () => TextPaintReference(opacity:.5)),
            new("stroke-opacity", "轮廓独立透明度", "text{fill:none;stroke-opacity:.5}", text, () => TextPaintReference(fill:false,alpha:.5)),
            new("color", "当前色与自定义属性", "text{--ink:blue;color:var(--ink);fill:none;stroke:currentColor}", text, () => TextPaintReference(fill:false)),
            new("curve", "曲线字形与内部留白", "text{fill:none;stroke-opacity:.5}", "<text x='16' y='62'>O</text>",
                () => TextPaintReference(TextPaintOutline("O"),fill:false,alpha:.5)),
            new("gradient", "渐变描边", "text{fill:none;stroke:url(#s)}",
                "<defs><linearGradient id='s'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>"+text,
                () => TextPaintReference(fill:false,strokeBrush:new LinearGradientBrush(Colors.Red,Colors.Blue,new Point(0,0),new Point(1,0)))),
            new("textpath", "沿路径排列的轮廓", "text{fill:none}",
                "<defs><path id='p' d='M16 62H150'/></defs><text><textPath href='#p'>H</textPath></text>",
                () => TextPaintReference(fill:false)),
            new("kerning", "未启用描边时保持字距", "text{stroke:none}",
                "<text x='16' y='62'>A<tspan style='stroke-width:100;paint-order:stroke'>V</tspan></text>",
                () => new GeometryDrawing(Brushes.Red,null,TextPaintOutline("AV"))),
            new("span", "相邻片段保留各自描边", ".a{stroke:red}.b{stroke:blue}text{fill:none}",
                "<text x='16' y='62'><tspan class='a'>H</tspan><tspan class='b'>H</tspan></text>", () =>
                {
                    var group = new DrawingGroup();
                    group.Children.Add(TextPaintReference(fill:false,strokeBrush:Brushes.Red));
                    group.Children.Add(TextPaintReference(TextPaintOutline(x:16+Advance()),fill:false)); return group;
                }),
            new("rotate", "逐字旋转后的轮廓", "text{fill:none}", "<text x='16' y='62' rotate='12'>H</text>",
                () => TextPaintReference(TextPaintOutline(angle:12),fill:false)),
            new("length", "字形与轮廓一同拉伸", "text{fill:none}",
                "<text x='16' y='62' textLength='"+(Advance()*1.5).ToString("R",CultureInfo.InvariantCulture)+"' lengthAdjust='spacingAndGlyphs'>H</text>",
                () =>
                {
                    var group=new DrawingGroup{Transform=new ScaleTransform(1.5,1,16,62)};
                    group.Children.Add(TextPaintReference(fill:false));return group;
                }),
            new("overlap", "重叠字形共享绘制路径", "text{fill-opacity:.5;stroke-opacity:.5;paint-order:stroke}",
                "<text x='16' y='62' dx='0 -24'>HH</text>", () =>
                {
                    var geometry = new GeometryGroup { FillRule=FillRule.Nonzero };
                    geometry.Children.Add(TextPaintOutline());geometry.Children.Add(TextPaintOutline(x:16+Advance()-24));
                    return TextPaintReference(geometry,strokeFirst:true,alpha:.5);
                }),
            new("overlap-stroke", "重叠轮廓不重复加深", "text{fill:none;stroke-opacity:.5}",
                "<text x='16' y='62' dx='0 -24'>HH</text>", () =>
                {
                    var geometry = new GeometryGroup { FillRule=FillRule.Nonzero };
                    geometry.Children.Add(TextPaintOutline());geometry.Children.Add(TextPaintOutline(x:16+Advance()-24));
                    return TextPaintReference(geometry,fill:false,alpha:.5);
                }),
            new("chunks", "绝对坐标开启新文字块", "text{fill:none;stroke-opacity:.5}",
                "<text x='16 16' y='62'>HH</text>", () =>
                {
                    var group=new DrawingGroup();group.Children.Add(TextPaintReference(fill:false,alpha:.5));
                    group.Children.Add(TextPaintReference(fill:false,alpha:.5));return group;
                }),
            new("clip", "裁剪作用于完整轮廓", "text{clip-path:url(#c)}",
                "<defs><clipPath id='c'><rect x='30' width='100' height='90'/></clipPath></defs>"+text, () =>
                {
                    var group=new DrawingGroup{ClipGeometry=PathMarkupParser.ParseSvgPathData("M30 0H130V90H30Z")};
                    group.Children.Add(TextPaintReference());return group;
                }),
            new("shape", "图形也支持描边在前", "rect{fill:red;stroke:blue;stroke-width:8;paint-order:stroke}",
                "<rect x='20' y='24' width='90' height='40'/>", () =>
                {
                    var group=new DrawingGroup();var geometry=new RectangleGeometry(new Rect(20,24,90,40));
                    group.Children.Add(new GeometryDrawing(null,new Pen(Brushes.Blue,8),geometry));
                    group.Children.Add(new GeometryDrawing(Brushes.Red,null,geometry));return group;
                }),
        ];
    }

    internal static int RunSvgTextPaint()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        string Xml(SvgTextPaintCase row) => "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='90'><style><![CDATA[text{font:48px Helvetica;fill:red;stroke:blue;stroke-width:4}"+row.Css+"]]></style>"+row.Body+"</svg>";
        SvgImage LoadImage(SvgTextPaintCase row) => SvgImage.FromSvgString(Xml(row));
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var dpi in new[]{1,2})foreach(var recorded in new[]{false,true})foreach(var affine in new[]{false,true})
            foreach(var route in new[]{"drawing","image","brush"})foreach(var row in SvgTextPaintCases())
            {
                context.DefaultRenderingEngine=engine;
                var label=$"text-paint-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";
                var errors=new List<string>();var largest=0;var differing=0;
                try
                {
                    using var svg=LoadImage(row);
                    var reference=row.Reference();
                    var referenceFrame=new DrawingGroup();
                    referenceFrame.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,160,90))));
                    referenceFrame.Children.Add(reference);
                    var referenceImage=new DrawingImage(referenceFrame);
                    var matrix=affine?new Matrix(.86,.12,-.08,.92,24,16):new Matrix(1,0,0,1,16,12);
                    const int width=208,height=128;
                    byte[] Paint(bool actual)
                    {
                        return Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>
                        {
                            void Draw(DrawingContext target)
                            {
                                if(route=="drawing") (actual?svg.Drawing!:reference).RenderTo(target);
                                else if(route=="image")target.DrawImage(actual?svg:referenceImage,new Rect(0,0,160,90));
                                else target.DrawRectangle(new ImageBrush(actual?svg:referenceImage){Stretch=Stretch.Fill},null,new Rect(0,0,160,90));
                            }
                            if(recorded)
                            {
                                var recorder=new DrawingRecorder();recorder.BindWholeFrame();recorder.PushTransform(new MatrixTransform(matrix));
                                Draw(recorder);recorder.Pop();DrawingReplayer.Replay(recorder.Commit(),dc);
                            }
                            else{dc.PushTransform(new MatrixTransform(matrix));Draw(dc);dc.Pop();}
                        },()=>true,context)??throw new Exception("Text paint capture unavailable"));
                    }
                    var actual=Paint(true);var expected=Paint(false);
                    if(row.Name is "overlap-stroke" or "stroke-opacity" or "opacity" or "curve")
                        for(var i=3;i<actual.Length;i+=4)
                            if(actual[i]>128)throw new Exception("Glyph coverage was composited more than once before opacity");
                    for(var i=0;i<expected.Length;i++)
                    {
                        var delta=Math.Abs(actual[i]-expected[i]);largest=Math.Max(largest,delta);
                        if(delta>3)differing++;
                    }
                    Save(actual,width*dpi,height*dpi,label+".png");Save(expected,width*dpi,height*dpi,label+"-expected.png");
                    if(differing>0)throw new Exception($"Independent outline reference: {differing} channels exceed tolerance 3; maximum {largest}");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,largest,differing,errors});
                Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            foreach(var viewport in new[]{360,720})
            {
                var cases=SvgTextPaintCases();var columns=viewport<600?1:2;var height=24+(int)Math.Ceiling(cases.Length/(double)columns)*112;
                var canvas=new Canvas{Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1))};
                for(var i=0;i<cases.Length;i++)
                {
                    var row=cases[i];var x=24+i%columns*360;var y=16+i/columns*112;
                    var label=new TextBlock{Text=row.Caption,FontSize=15,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=312,Height=22};
                    Jalium.UI.Controls.Canvas.SetLeft(label,x);Jalium.UI.Controls.Canvas.SetTop(label,y);canvas.Children.Add(label);
                    var image=new Image{Source=LoadImage(row),Width=160,Height=90};
                    Jalium.UI.Controls.Canvas.SetLeft(image,x);Jalium.UI.Controls.Canvas.SetTop(image,y+22);canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"text-paint-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output,"text-paint.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));
            return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
}
