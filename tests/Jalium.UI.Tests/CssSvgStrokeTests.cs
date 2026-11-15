using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgStrokeTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void RecordedSvgPaintRetainsItsOpacityAcrossConsecutiveFrames(bool wholeFrame, bool stroke)
    {
        foreach(var opacity in new[]{.5,.25,1d,.5})
        {
            var paint=stroke?$"fill:none;stroke:red;stroke-width:8;stroke-dasharray:8 8;stroke-opacity:{opacity}":$"fill:red;fill-opacity:{opacity}";
            using var svg=Parse($"rect{{{paint}}}","<rect x='8' y='12' width='64' height='32'/>");
            var recorder=new DrawingRecorder();if(wholeFrame)recorder.BindWholeFrame();
            svg.Drawing!.RenderTo(recorder);
            var target=new RenderTargetBitmap(80,60,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(target);
            DrawingReplayer.Replay(recorder.Commit(),context);context.Close();
            Assert.InRange(Alpha(target.GetPixelBuffer(),12,stroke?12:20),(int)(opacity*255),(int)Math.Ceiling(opacity*255));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AffineSquareDashesUnionTheirCoverageBeforeAntialiasing(bool reflected)
    {
        using var svg=Parse("line{stroke:red;stroke-width:8;stroke-dasharray:8 16;stroke-linecap:square}",
            "<line x1='8' y1='30' x2='72' y2='30'/>");
        var transform=new MatrixTransform(reflected?new Matrix(-.8,.2,.2,.8,74,2):new Matrix(.8,.2,-.2,.8,12,2));
        var actual=new DrawingGroup{Transform=transform};actual.Children.Add(svg.Drawing!);
        var shapes=new GeometryGroup{FillRule=FillRule.Nonzero};
        foreach(var x in new[]{4d,28d,52d})shapes.Children.Add(new RectangleGeometry(new Rect(x,26,16,8)));
        var expected=new DrawingGroup{Transform=transform};expected.Children.Add(new GeometryDrawing(Brushes.Red,null,shapes));
        var a=SoftwareVectorRasterizer.Rasterize(actual,80,60,new Rect(0,0,80,60))!;
        var b=SoftwareVectorRasterizer.Rasterize(expected,80,60,new Rect(0,0,80,60))!;
        for(var i=0;i<a.Length;i++)Assert.InRange(Math.Abs(a[i]-b[i]),0,1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DefaultSvgButtCapsLeaveShortDashGapsTransparent(bool drawingContext)
    {
        using var svg = Parse("line{stroke:red;stroke-width:8;stroke-dasharray:8 8}");
        var pixels = Render(svg, drawingContext);
        Assert.Equal(255, Alpha(pixels, 6, 20));
        Assert.Equal(0, Alpha(pixels, 16, 20));
        var pen = Pen(svg);
        Assert.Equal(PenLineCap.Flat, pen.StartLineCap);
        Assert.Equal(PenLineCap.Flat, pen.EndLineCap);
        Assert.Equal(PenLineCap.Flat, pen.DashCap);
    }

    [Theory]
    [InlineData("butt", false, 0, 0)] [InlineData("round", false, 255, 0)] [InlineData("square", false, 255, 255)]
    [InlineData("butt", true, 0, 0)] [InlineData("round", true, 255, 0)] [InlineData("square", true, 255, 255)]
    public void SvgLineCapsApplyToEveryDash(string cap, bool drawingContext, int center, int corner)
    {
        using var svg = Parse($"line{{stroke:red;stroke-width:8;stroke-dasharray:8 8;stroke-linecap:{cap}}}",
            "<line x1='8' y1='20' x2='76' y2='20'/>");
        var pixels = Render(svg, drawingContext);
        Assert.InRange(Alpha(pixels, 18, 20), Math.Max(0, center-1), center);
        Assert.InRange(Alpha(pixels, 19, 23), Math.Max(0, corner-1), corner);
        Assert.Equal(Pen(svg).StartLineCap, Pen(svg).DashCap);
    }

    [Theory]
    [InlineData("8 8")] [InlineData("8px,8px")] [InlineData("6pt 6pt")]
    [InlineData(".5em .5em")] [InlineData("calc(2px + .375em) 8px")]
    [InlineData("calc(4px * 2) max(4px,8px)")]
    public void StrokeListsUseTheSharedCssLengthAndMathGrammar(string dashes)
    {
        using var svg = Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:{dashes}}}");
        Assert.Equal(new[] {2d,2d}, Pen(svg).DashStyle.Dashes);
        Assert.Equal(0, Alpha(Render(svg), 16, 20));
    }

    [Theory]
    [InlineData("8 8", 0)] [InlineData("8 8", 3)] [InlineData("8 8", -3)]
    [InlineData("8 4 2", 0)] [InlineData("8 4 2", 1000003)] [InlineData("8 4 2", -1000003)]
    public void PatternsRepeatAndOffsetsNormalizeAlongTheLine(string array, int offset)
    {
        using var svg = Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:{array};stroke-dashoffset:{offset}}}");
        var pixels = Render(svg);
        var pattern = array.Split(' ').Select(double.Parse).ToArray();
        if (pattern.Length%2 != 0) pattern = [..pattern,..pattern];
        var total = pattern.Sum();
        for (var x=4; x<76; x++)
        {
            var distance = ((x+.5-4+offset)%total+total)%total;
            var index=0;
            while (distance >= pattern[index]) distance-=pattern[index++];
            Assert.Equal(index%2 == 0 ? 255 : 0, Alpha(pixels,x,20));
        }
    }

    [Theory]
    [InlineData("none")] [InlineData("0")] [InlineData("0 0 0")]
    public void AllZeroPatternsAndNonePaintASolidStroke(string array)
    {
        using var svg=Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:{array};stroke-opacity:.5}}");
        var pixels=Render(svg);
        for(var x=5;x<75;x++)Assert.InRange(Alpha(pixels,x,20),127,128);
    }

    [Theory]
    [InlineData("-8 4")] [InlineData("8,")] [InlineData("8,,4")]
    [InlineData("8.5.5")] [InlineData("8px8px")]
    public void InvalidListsRetainTheEarlierValidDeclaration(string invalid)
    {
        using var svg=Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:40 40;stroke-dasharray:{invalid}}}");
        Assert.Equal(new[]{10d,10d},Pen(svg).DashStyle.Dashes);
    }

    [Fact]
    public void InheritedRelativeLengthsAreComputedUsingTheDeclaringFont()
    {
        using var svg=Parse("g{font-size:16px;stroke:red;stroke-width:4px;stroke-dasharray:calc(1em + 10%) 10px}line{font-size:8px}",
            "<g><line x1='4' y1='20' x2='76' y2='20'/></g>");
        Assert.InRange(Pen(svg).DashStyle.Dashes[0],(16+Math.Sqrt(5000)*.1)/4-.0001,(16+Math.Sqrt(5000)*.1)/4+.0001);
        Assert.Equal(0,Alpha(Render(svg),30,20));
    }

    [Fact]
    public void PercentagesMeasureTheNormalizedViewBoxDiagonal()
    {
        using var svg=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60' viewBox='0 0 160 120'><style>line{stroke:red;stroke-width:10%;stroke-dasharray:10% 10%;stroke-dashoffset:5%}</style><line x1='0' y1='40' x2='160' y2='40'/></svg>");
        Assert.InRange(Pen(svg).Thickness,Math.Sqrt(20000)*.1-.0001,Math.Sqrt(20000)*.1+.0001);
        Assert.Equal(new[]{1d,1d},Pen(svg).DashStyle.Dashes);
        Assert.InRange(Pen(svg).DashStyle.Offset,.49999,.50001);
    }

    [Fact]
    public void EachSubpathRestartsItsDashPattern()
    {
        using var svg=Parse("path{fill:none;stroke:red;stroke-width:4;stroke-dasharray:8 8}",
            "<path d='M4 12H30 M44 32H76'/>");
        var pixels=Render(svg);Assert.Equal(255,Alpha(pixels,46,32));Assert.Equal(0,Alpha(pixels,56,32));
    }

    [Theory]
    [InlineData("pathLength='40'", "", 5)]
    [InlineData("pathLength='80'", "line{path-length:40px}", 5)]
    [InlineData("pathLength='40'", "line{path-length:none}", 2.5)]
    public void AuthorPathLengthCalibratesAbsoluteDashDistances(string attributes,string extra,double dash)
    {
        using var svg=Parse("line{stroke:red;stroke-width:4;stroke-dasharray:10 10}"+extra,
            $"<line x1='0' y1='20' x2='80' y2='20' {attributes}/>");
        Assert.Equal(new[]{dash,dash},Pen(svg).DashStyle.Dashes);
    }

    [Theory]
    [InlineData("8 8",0,255)] [InlineData("8 8",10,0)] [InlineData("8 8",-2,0)]
    [InlineData("8 8",16,255)] [InlineData("0 8",0,0)]
    public void ZeroAuthoredLengthNeverLeaksInfinityIntoTheStroker(string dashes,int offset,int alpha)
    {
        using var svg=Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:{dashes};stroke-dashoffset:{offset}}}",
            "<line x1='0' y1='20' x2='80' y2='20' pathLength='0'/>");
        var pixels=Render(svg);for(var x=2;x<78;x++)Assert.Equal(alpha,Alpha(pixels,x,20));
    }

    [Theory]
    [InlineData(false,"round")] [InlineData(true,"round")]
    [InlineData(false,"square")] [InlineData(true,"square")]
    public void ZeroLengthOnEntriesPaintDotsWithTheSvgCap(bool context,string cap)
    {
        using var svg=Parse($"line{{stroke:red;stroke-width:8;stroke-dasharray:0 16;stroke-linecap:{cap};stroke-opacity:.5}}");
        var pixels=Render(svg,context);Assert.InRange(Alpha(pixels,4,20),127,128);Assert.Equal(0,Alpha(pixels,12,20));
        Assert.InRange(Alpha(pixels,7,23),cap=="square"?127:0,cap=="square"?128:0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void StrokePatternsScaleWithTheSourcePathBeforeAnAffineTransform(bool context)
    {
        using var svg=Parse("line{stroke:red;stroke-width:4;stroke-dasharray:8 8}",
            "<g transform='scale(2 1)'><line x1='4' y1='20' x2='38' y2='20'/></g>");
        var pixels=Render(svg,context);Assert.Equal(255,Alpha(pixels,20,20));Assert.Equal(0,Alpha(pixels,28,20));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AClosedDashAcrossTheSeamKeepsItsMiterAndCompositesOpacityOnce(bool context)
    {
        using var svg=Parse("rect{fill:none;stroke:red;stroke-width:4;stroke-dasharray:30 20;stroke-opacity:.5}",
            "<rect x='8' y='8' width='32' height='24'/>");
        Assert.InRange(Alpha(Render(svg,context),6,6),127,128);
    }

    [Theory]
    [InlineData("1e100 1e100","0",255)] [InlineData("1e100 1e100","1e90",255)]
    [InlineData("1e100 1e100","1.5e100",0)] [InlineData("0 1e100","0",0)]
    public void HugeFiniteCssDistancesAreBoundedBeforeEnteringFloatStrokers(string array,string offset,int alpha)
    {
        using var svg=Parse($"line{{stroke:red;stroke-width:4;stroke-dasharray:{array};stroke-dashoffset:{offset}}}");
        var pixels=Render(svg);for(var x=5;x<75;x++)Assert.Equal(alpha,Alpha(pixels,x,20));
    }

    [Theory]
    [InlineData("4px")] [InlineData("none")] [InlineData("-1")]
    [InlineData("NaN")] [InlineData("Infinity")]
    public void PathLengthPresentationAttributesRequireANonnegativeNumber(string value)
    {
        using var svg=Parse("line{stroke:red;stroke-width:4;stroke-dasharray:10 10}",
            $"<line x1='0' y1='20' x2='80' y2='20' pathLength='{value}'/>");
        Assert.Equal(new[]{2.5,2.5},Pen(svg).DashStyle.Dashes);
    }

    [Fact]
    public void NegativeMathDashLengthsClampToZeroWithoutRevertingTheDeclaration()
    {
        using var svg=Parse("line{stroke:red;stroke-width:4;stroke-dasharray:40 40;stroke-dasharray:calc(-8px) 8px}");
        var pixels=Render(svg);for(var x=5;x<75;x++)Assert.Equal(0,Alpha(pixels,x,20));
    }

    [Theory]
    [InlineData("pathLength='80'","path-length:40px","pathLength='40'",20)]
    [InlineData("pathLength='40'","path-length:none","",10)]
    public void CssPathLengthAlsoCalibratesTextPathOffsets(string attr,string css,string referenceAttr,int minimum)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        string Xml(string attributes,string rules)=>$"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><style>text{{font-family:Arial;font-size:16px}}#p{{{rules}}}</style><defs><path id='p' d='M0 30H80' {attributes}/></defs><text><textPath href='#p' startOffset='10'>Ab</textPath></text></svg>";
        using var actual=SvgImage.FromSvgStringWithTextOutlines(Xml(attr,css));
        using var reference=SvgImage.FromSvgStringWithTextOutlines(Xml(referenceAttr,""));
        var pixels=Render(actual);Assert.Contains((byte)255,pixels);Assert.Equal(Render(reference),pixels);
        Assert.InRange(actual.Drawing!.Bounds.X,minimum-1,minimum+3);
    }

    private static SvgImage Parse(string css,string body="<line x1='4' y1='20' x2='76' y2='20'/>")
        => SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><style><![CDATA[{css}]]></style>{body}</svg>");
    private static Pen Pen(SvgImage svg)=>Geometries(svg.Drawing!).Single().Pen!;
    private static IEnumerable<GeometryDrawing> Geometries(Drawing drawing)
    {
        if(drawing is GeometryDrawing geometry)yield return geometry;
        if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var item in Geometries(child))yield return item;
    }
    private static byte[] Render(SvgImage image,bool context=false)
    {
        if(!context)return SoftwareVectorRasterizer.Rasterize(image.Drawing!,80,60,new Rect(0,0,80,60))!;
        var target=new RenderTargetBitmap(80,60,96,96,PixelFormats.Bgra32);var drawing=new SoftwareDrawingContext(target);
        image.Drawing!.RenderTo(drawing);drawing.Close();return target.GetPixelBuffer().ToArray();
    }
    private static int Alpha(byte[] pixels,int x,int y)=>pixels[(y*80+x)*4+3];
}
