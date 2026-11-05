using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SvgStrokeCase(string Name, string Caption, string Css, Func<int,string> Body,
        Func<int,Drawing> Reference);

    private static SvgStrokeCase[] SvgStrokeCases()
    {
        static string Line(int width) => $"<line x1='8' y1='30' x2='{width-8}' y2='30'/>";
        static Drawing Pattern(int width, double phase=0, string cap="butt", double opacity=1)
            => StrokeLineReference(width, [8,8], phase, cap, opacity);
        return [
            new("butt","默认平端","line{stroke-dasharray:8 8}",Line,w=>Pattern(w)),
            new("round","圆形端帽","line{stroke-dasharray:8 8;stroke-linecap:round}",Line,w=>Pattern(w,cap:"round")),
            new("square","方形端帽","line{stroke-dasharray:8 16;stroke-linecap:square}",Line,w=>StrokeLineReference(w,[8,16],0,"square")),
            new("units","字体单位与变量","line{font-size:16px;--dash:6pt;stroke-dasharray:var(--dash) calc(.5em)}",Line,w=>Pattern(w)),
            new("odd","奇数列表","line{stroke-dasharray:8 4 2;stroke-dashoffset:3}",Line,w=>StrokeLineReference(w,[8,4,2,8,4,2],3)),
            new("offset","负向大偏移","line{stroke-dasharray:8 8;stroke-dashoffset:-1000003}",Line,w=>Pattern(w,13)),
            new("solid","全零列表","line{stroke-dasharray:0 0;stroke-opacity:.5}",Line,w=>StrokeLineReference(w,[w,0],0,opacity:.5)),
            new("dots","零长度圆点","line{stroke-dasharray:0 16;stroke-linecap:round;stroke-opacity:.5}",Line,w=>StrokeLineReference(w,[0,16],0,"round",.5)),
            new("registered","注册自定义长度","@property --dash{syntax:'<length>';inherits:false;initial-value:8px}line{stroke-dasharray:var(--dash) 8px}",Line,w=>Pattern(w)),
            new("path-length","CSS 路径校准","line{path-length:40px;stroke-dasharray:10 10}",Line,w=>StrokeLineReference(w,[(w-16)/4d,(w-16)/4d],0)),
            new("path-zero-on","零路径长度 · 实线","line{path-length:0px;stroke-dasharray:8 8}",Line,w=>StrokeLineReference(w,[w,0],0)),
            new("path-zero-gap","零路径长度 · 间隙","line{path-length:0px;stroke-dasharray:8 8;stroke-dashoffset:10}",Line,w=>new DrawingGroup()),
            new("path-zero-percent","百分比保留视口基准","line{path-length:0px;stroke-dasharray:12.5% 12.5%}",Line,w=>
                StrokeLineReference(w,[Math.Sqrt((w*(double)w+3600)/2)*.125,Math.Sqrt((w*(double)w+3600)/2)*.125],0)),
            new("negative-math","负数学长度归零","line{stroke-dasharray:calc(-8px) 8px}",Line,w=>new DrawingGroup()),
            new("closed","闭合接缝与透明度","rect{stroke-dasharray:30 20;stroke-opacity:.5}",w=>$"<rect x='8' y='14' width='{w-16}' height='32'/>",ClosedStrokeReference),
            new("curve","曲线中的路径校准","circle{path-length:80px;stroke-dasharray:10 10}",w=>$"<circle cx='{w/2}' cy='30' r='20'/>",CircleStrokeReference),
        ];
    }

    // References are filled rectangles, disks and annular sectors with literal
    // positions. They never read the SVG pen or call its pattern/length resolver.
    private static Drawing StrokeLineReference(int width, double[] pattern, double phase, string cap="butt", double opacity=1)
    {
        var shapes = new GeometryGroup { FillRule=FillRule.Nonzero };
        static Geometry Disk(double x)
        {
            var figure=new PathFigure{StartPoint=new(x+4,30),IsClosed=true};
            for(var i=1;i<512;i++){var angle=2*Math.PI*i/512;figure.Segments.Add(new LineSegment(new(x+4*Math.Cos(angle),30+4*Math.Sin(angle))));}
            var path=new PathGeometry();path.Figures.Add(figure);return path;
        }
        foreach (var (from,to) in StrokeReferenceRanges(width-16,pattern,phase))
        {
            if (to>from) shapes.Children.Add(new RectangleGeometry(new Rect(8+from,26,to-from,8)));
            if(cap=="round")
            {
                shapes.Children.Add(Disk(8+from));
                shapes.Children.Add(Disk(8+to));
            }
            if(cap=="square")shapes.Children.Add(new RectangleGeometry(new Rect(4+from,26,to-from+8,8)));
        }
        return new GeometryDrawing(new SolidColorBrush(Colors.Red){Opacity=opacity},null,shapes);
    }

    private static List<(double From,double To)> StrokeReferenceRanges(double length,double[] pattern,double phase)
    {
        var ranges=new List<(double,double)>();var period=pattern.Sum();
        for(var cycle=-2;cycle*period-phase<=length;cycle++)
        {
            double position=cycle*period-phase;
            for(var i=0;i<pattern.Length;i++)
            {
                var next=position+pattern[i];
                // A new dash is not started after traversal reaches the endpoint.
                if(i%2==0&&next>=0&&position<length)ranges.Add((Math.Max(0,position),Math.Min(length,next)));
                position=next;
            }
        }
        return ranges;
    }

    private static Drawing ClosedStrokeReference(int width)
    {
        var vertices=new[]{new Point(8,14),new Point(width-8,14),new Point(width-8,46),new Point(8,46),new Point(8,14)};
        var distances=new[]{0d,width-16d,width+16d,2d*width,2d*width+32};
        var ranges=StrokeReferenceRanges(distances[^1],[30,20],0);
        var shapes=new GeometryGroup{FillRule=FillRule.Nonzero};
        foreach(var (from,to) in ranges)
        {
            for(var edge=0;edge<4;edge++)
            {
                var start=Math.Max(from,distances[edge]);var end=Math.Min(to,distances[edge+1]);if(end<=start)continue;
                var a=vertices[edge];var b=vertices[edge+1];var length=distances[edge+1]-distances[edge];
                Point At(double value)=>new(a.X+(b.X-a.X)*(value-distances[edge])/length,a.Y+(b.Y-a.Y)*(value-distances[edge])/length);
                var p=At(start);var q=At(end);
                shapes.Children.Add(new RectangleGeometry(p.Y==q.Y?new Rect(Math.Min(p.X,q.X),p.Y-4,Math.Abs(q.X-p.X),8):new Rect(p.X-4,Math.Min(p.Y,q.Y),8,Math.Abs(q.Y-p.Y))));
            }
            for(var corner=1;corner<4;corner++)
                if(from<distances[corner]&&to>distances[corner])
                    shapes.Children.Add(new RectangleGeometry(new Rect(vertices[corner].X+(corner==3?-4:0),vertices[corner].Y+(corner==1?-4:0),4,4)));
        }
        if(ranges.Any(r=>r.From==0&&r.To>0)&&ranges.Any(r=>r.To==distances[^1]&&r.From<r.To))
            shapes.Children.Add(new RectangleGeometry(new Rect(4,10,4,4)));
        return new GeometryDrawing(new SolidColorBrush(Colors.Red){Opacity=.5},null,shapes);
    }

    private static Drawing CircleStrokeReference(int width)
    {
        var geometry=new PathGeometry{FillRule=FillRule.Nonzero};
        for(var dash=0;dash<4;dash++)
        {
            var start=dash*Math.PI/2;var end=start+Math.PI/4;
            Point At(double radius,double angle)=>new(width/2d+radius*Math.Cos(angle),30+radius*Math.Sin(angle));
            var figure=new PathFigure{StartPoint=At(24,start),IsClosed=true};
            for(var step=1;step<=48;step++)figure.Segments.Add(new LineSegment(At(24,start+(end-start)*step/48)));
            for(var step=48;step>=0;step--)figure.Segments.Add(new LineSegment(At(16,start+(end-start)*step/48)));
            geometry.Figures.Add(figure);
        }
        return new GeometryDrawing(Brushes.Red,null,geometry);
    }

    internal static int RunSvgStrokes()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;var cases=SvgStrokeCases();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var dpi in new[]{1,2})foreach(var width in new[]{80,160})
            foreach(var transformed in new[]{false,true})foreach(var recorded in new[]{false,true})foreach(var imageRoute in new[]{false,true})foreach(var row in cases)
            {
                context.DefaultRenderingEngine=engine;
                var label=$"svg-stroke-{backend}-{engine}-{dpi}-{width}-{transformed}-{recorded}-{imageRoute}-{row.Name}";
                var errors=new List<string>();int interiorLargest=0,edgeLargest=0,edgeChannels=0,strictChannels=0,differing=0;
                try
                {
                    using var svg=SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='60'><style><![CDATA[*{{fill:none;stroke:red;stroke-width:8}}{row.Css}]]></style>{row.Body(width)}</svg>");
                    var reference=new DrawingGroup();reference.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,width,60))));reference.Children.Add(row.Reference(width));
                    var referenceImage=new DrawingImage(reference);
                    if(imageRoute&&transformed&&dpi==1&&width==80&&row.Name=="curve")
                    {
                        Save(SoftwareVectorRasterizer.Rasterize(svg.Drawing!,66,50,new Rect(0,0,width,60))!,66,50,$"svg-stroke-source-{backend}-{engine}-curve.png");
                        Save(SoftwareVectorRasterizer.Rasterize(reference,66,50,new Rect(0,0,width,60))!,66,50,$"svg-stroke-source-{backend}-{engine}-curve-expected.png");
                    }
                    var matrix=transformed?new Matrix(.8,.2,-.2,.8,32,12):new Matrix(1,0,0,1,16,12);
                    var captureWidth=(width+64)*dpi;const int height=112;
                    byte[] Paint(bool actual)
                    {
                        // Metal StrokePath uses the analytic coverage path for
                        // both engine selections. Keep that same coverage policy
                        // for the independently filled reference geometry.
                        context.DefaultRenderingEngine=actual?engine:backend==RenderBackend.Metal?RenderingEngine.Impeller:RenderingEngine.Auto;
                        return Pixels(MacOSWritingToolsBitmapCapture.Capture(captureWidth,height*dpi,dpi,dc=>
                    {
                        dc.DrawRectangle(Brushes.White,null,new Rect(-16,-16,width+96,height+32));
                        void Render(DrawingContext target)
                        {
                            if(imageRoute)target.DrawImage(actual?svg:referenceImage,new Rect(0,0,width,60));
                            else if(actual)svg.Drawing!.RenderTo(target);else reference.RenderTo(target);
                        }
                        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();recorder.PushTransform(new MatrixTransform(matrix));Render(recorder);recorder.Pop();DrawingReplayer.Replay(recorder.Commit(),dc);}
                        else{dc.PushTransform(new MatrixTransform(matrix));Render(dc);dc.Pop();}
                    },()=>true,context)??throw new Exception("Stroke capture unavailable"));
                    }
                    var actual=Paint(true);var expected=Paint(false);
                    for(var y=0;y<height*dpi;y++)for(var x=0;x<captureWidth;x++)
                    {
                        var at=(y*captureWidth+x)*4;var edge=false;
                        for(var yy=Math.Max(0,y-1);yy<=Math.Min(height*dpi-1,y+1)&&!edge;yy++)
                        for(var xx=Math.Max(0,x-1);xx<=Math.Min(captureWidth-1,x+1);xx++)
                            if(Math.Abs(expected[(yy*captureWidth+xx)*4+1]-expected[at+1])>4){edge=true;break;}
                        var tolerance=edge?36:2;
                        for(var channel=0;channel<4;channel++)
                        {
                            var delta=Math.Abs(actual[at+channel]-expected[at+channel]);
                            if(edge){edgeChannels++;edgeLargest=Math.Max(edgeLargest,delta);}else{strictChannels++;interiorLargest=Math.Max(interiorLargest,delta);}
                            if(delta>tolerance)differing++;
                        }
                    }
                    Save(actual,captureWidth,height*dpi,label+".png");Save(expected,captureWidth,height*dpi,label+"-expected.png");
                    if(differing>0)throw new Exception($"Independent filled-shape reference: {differing} channels exceed tolerance; interior {interiorLargest}, AA boundary {edgeLargest}");
                }
                catch(Exception error){failures++;errors.Add(error.ToString());}
                records.Add(new{label,interiorLargest,edgeLargest,strictChannels,edgeChannels,differing,errors});
                Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            foreach(var viewport in new[]{260,520})
            {
                var columns=viewport<400?1:2;var height=36+(int)Math.Ceiling(cases.Length/(double)columns)*86;
                var canvas=new Canvas{Width=viewport,Height=height,Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1))};
                for(var i=0;i<cases.Length;i++)
                {
                    var row=cases[i];var x=20+i%columns*250;var y=20+i/columns*86;
                    var label=new TextBlock{Text=row.Caption,FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(0x14,0x63,0x42)),Width=220,Height=20};
                    Jalium.UI.Controls.Canvas.SetLeft(label,x);Jalium.UI.Controls.Canvas.SetTop(label,y);canvas.Children.Add(label);
                    var svg=SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='160' height='60'><style><![CDATA[*{{fill:none;stroke:#146342;stroke-width:8}}{row.Css}]]></style>{row.Body(160)}</svg>");
                    var image=new Image{Source=svg,Width=160,Height=60};Jalium.UI.Controls.Canvas.SetLeft(image,x);Jalium.UI.Controls.Canvas.SetTop(image,y+20);canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport,height));canvas.Arrange(new(0,0,viewport,height));canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport,height,1,dc=>canvas.Render(dc),()=>true,context)!),viewport,height,$"svg-stroke-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output,"svg-stroke.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,interiorTolerance=2,antialiasBoundaryTolerance=36,referenceCoverageEngine=backend==RenderBackend.Metal?"Impeller analytic coverage":"Software analytic coverage",nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));
            return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }
}
