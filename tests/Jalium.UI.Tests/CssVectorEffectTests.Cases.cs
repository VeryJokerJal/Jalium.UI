using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

public sealed partial class CssVectorEffectTests
{
    public static IEnumerable<object[]> AffineCases()
    {
        foreach(var transform in new[]{"matrix(2,0,0,3,8,5)","matrix(2,.4,.7,1.5,8,5)","matrix(-2,.3,.4,1.5,85,5)","matrix(.4,1.7,-1.1,.3,70,10)","matrix(.3,0,0,.4,8,5)"})
        foreach(var cap in new[]{"butt","round","square"})
        foreach(var join in new[]{"miter","round","bevel"})
        foreach(var route in new[]{0,1,2,3}) yield return [transform,cap,join,route];
    }
    [Theory] [MemberData(nameof(AffineCases))]
    public void AffinePathsUseAnIsotropicPen(string transform,string cap,string join,int route)
    {
        var numbers=transform[7..^1].Split(',').Select(v=>double.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        var matrix=new Matrix(numbers[0],numbers[1],numbers[2],numbers[3],numbers[4],numbers[5]);
        string Map(double x,double y){var p=matrix.Transform(new Point(x,y));return string.Create(CultureInfo.InvariantCulture,$"{p.X:R},{p.Y:R}");}
        var style=$"fill='none' stroke='#146342' stroke-width='4' stroke-linecap='{cap}' stroke-linejoin='{join}' stroke-dasharray='9 5'";
        using var actual=Svg($"<path d='M5,8L25,8L25,24L12,16' {style} transform='{transform}' vector-effect='non-scaling-stroke'/>");
        using var expected=Svg($"<path d='M{Map(5,8)}L{Map(25,8)}L{Map(25,24)}L{Map(12,16)}' {style}/>");
        EqualPixels(Render(expected,0),Render(actual,route),2);
    }
    [Theory]
    [InlineData("vector-effect:non-scaling-stroke",4)]
    [InlineData("vector-effect:NON-SCALING-STROKE",4)]
    [InlineData("vector-effect:non-scaling-stroke;vector-effect:bogus",4)]
    [InlineData("--effect:non-scaling-stroke;vector-effect:var(--effect)",4)]
    [InlineData("vector-effect:non-scaling-stroke;vector-effect:var(--missing)",12)]
    [InlineData("vector-effect:non-scaling-stroke;vector-effect:unset",12)]
    [InlineData("vector-effect:non-scaling-stroke;vector-effect:initial",12)]
    [InlineData("vector-effect:inherit",4)]
    [InlineData("",12)]
    public void CascadeAndNonInheritance(string style,int expectedWidth)
    {
        using var svg=Svg($"<g transform='scale(1,3)' vector-effect='non-scaling-stroke'><line x1='10' y1='16' x2='90' y2='16' stroke='red' stroke-width='4' style='{style}'/></g>");
        var p=Render(svg,0);Assert.Equal(expectedWidth,Enumerable.Range(0,100).Count(y=>p[(y*100+40)*4+3]>127));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SupportsLayersAndPresentationAttributesUseTheSharedCascade(int route)
    {
        using var svg=Svg("<style>@layer a,b;@layer a{line{vector-effect:non-scaling-stroke}}@layer b{line{vector-effect:revert-layer}}@supports not (vector-effect:non-rotation){line{stroke:red}}@supports(vector-effect:non-scaling-stroke){line{stroke-width:4}}</style><g transform='scale(1,3)'><line x1='10' y1='16' x2='90' y2='16'/></g>");
        var p=Render(svg,route);Assert.Equal(255,p[(48*100+40)*4+2]);Assert.Equal(0,p[(51*100+40)*4+3]);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void PathLengthCalibratesDashesBeforeNonScalingStroke(int route)
    {
        using var svg=Svg("<line x1='10' y1='50' x2='90' y2='50' pathLength='40' transform='scale(2,1)' stroke='red' stroke-width='4' stroke-dasharray='4 4' vector-effect='non-scaling-stroke'/>");
        var p=Render(svg,route);for(var x=20;x<100;x++)Assert.Equal((x-20)%16<8?255:0,p[(50*100+x)*4+3]);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RootAndNestedViewportsBelongToTheFurthestHost(int route)
    {
        using var actual=Svg("<svg x='10' y='10' width='80' height='80' viewBox='0 0 40 20' preserveAspectRatio='none'><line x1='5' y1='10' x2='35' y2='10' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/></svg>");
        using var expected=Svg("<line x1='20' y1='50' x2='80' y2='50' stroke='red' stroke-width='4'/>");
        EqualPixels(Render(expected,0),Render(actual,route),1);
        using var root=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 50 25' preserveAspectRatio='none'><line x1='10' y1='12.5' x2='40' y2='12.5' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/></svg>");
        EqualPixels(Render(expected,route),Render(root,route),1);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void UseInstancesAndExplicitInheritanceRemainIndependent(int route)
    {
        using var svg=Svg("<defs><line id='a' x1='10' y1='10' x2='40' y2='10' stroke='red' stroke-width='4' vector-effect='inherit'/></defs><use href='#a' transform='scale(2,3)' vector-effect='non-scaling-stroke'/><use href='#a' y='10' transform='scale(2,3)' vector-effect='none'/>");
        var p=Render(svg,route);Assert.Equal(255,p[(30*100+40)*4+3]);Assert.Equal(0,p[(33*100+40)*4+3]);Assert.Equal(255,p[(64*100+40)*4+3]);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void OpacityClipAndPaintOrderApplyOnceToTheWholeElement(int route)
    {
        using var actual=Svg("<defs><clipPath id='c'><rect x='5' y='5' width='30' height='25'/></clipPath></defs><g transform='scale(2,3)'><rect x='10' y='8' width='20' height='14' fill='blue' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke' paint-order='stroke fill' opacity='.5' clip-path='url(#c)'/></g>");
        using var expected=Svg("<defs><clipPath id='c'><rect x='10' y='15' width='60' height='75'/></clipPath></defs><path d='M20,24H60V66H20Z' fill='blue' stroke='red' stroke-width='4' paint-order='stroke fill' opacity='.5' clip-path='url(#c)'/>");
        EqualPixels(Render(expected,0),Render(actual,route),1);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void GradientStrokePaintStaysInItsOriginalUserSpace(int route)
    {
        const string gradient="<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='10' x2='40' gradientTransform='rotate(15)'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs>";
        using var actual=Svg(gradient+"<g transform='matrix(2,.2,.3,2,5,5)'><rect x='10' y='10' width='30' height='20' fill='none' stroke='url(#g)' stroke-width='4' vector-effect='non-scaling-stroke'/></g>");
        using var expected=Svg(gradient.Replace("rotate(15)","matrix(2,.2,.3,2,5,5) rotate(15)")+"<path d='M28,27L88,33L94,73L34,67Z' fill='none' stroke='url(#g)' stroke-width='4'/>");
        EqualPixels(Render(expected,0),Render(actual,route),3);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void NonScalingStrokeSurvivesFrozenDrawingClones(bool current)
    {
        using var svg=Svg("<g transform='scale(2,3)'><line x1='5' y1='16' x2='45' y2='16' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/></g>");
        var clone=current?svg.Drawing!.CloneCurrentValue():svg.Drawing!.Clone();clone.Freeze();var p=SoftwareVectorRasterizer.Rasterize(clone,100,100,new Rect(0,0,100,100))!;Assert.Equal(0,p[(51*100+40)*4+3]);
    }
    [Theory] [InlineData("scale(0,2)")] [InlineData("matrix(1,1,1,1,0,0)")]
    public void SingularTransformsDoNotProduceAnUnboundedStroke(string transform)
    {
        using var svg=Svg($"<line x1='10' y1='10' x2='40' y2='40' stroke='red' stroke-width='4' transform='{transform}' vector-effect='non-scaling-stroke'/>");
        Assert.All(Render(svg,0),value=>Assert.Equal(0,value));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void StrokeUnitMarkersUseTheSameHostAsTheNonScalingStroke(int route)
    {
        using var actual=Svg("<defs><marker id='m' markerWidth='3' markerHeight='3' refX='1' refY='1' orient='auto' overflow='visible'><rect width='2' height='2' fill='blue'/></marker></defs><g transform='scale(2,3)'><line x1='10' y1='16' x2='40' y2='16' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke' marker-end='url(#m)'/></g>");
        using var expected=Svg("<line x1='20' y1='48' x2='80' y2='48' stroke='red' stroke-width='4'/><rect x='76' y='44' width='8' height='8' fill='blue'/>");
        EqualPixels(Render(expected,0),Render(actual,route),1);
    }
    [Theory] [InlineData(false,1)] [InlineData(true,1)] [InlineData(false,2)] [InlineData(true,2)]
    public void SvgImageBrushUsesItsLogicalTileSize(bool vectorRasterizer,int density)
    {
        using var svg=Svg("<line x1='10' y1='50' x2='90' y2='50' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/>");
        var drawing=new GeometryDrawing(new ImageBrush(svg){Stretch=Stretch.Fill,ScalingMode=BitmapScalingMode.NearestNeighbor},null,new RectangleGeometry(new Rect(0,0,200,200)));
        byte[] p;
        if(vectorRasterizer)p=SoftwareVectorRasterizer.Rasterize(drawing,200*density,200*density,new Rect(0,0,200,200))!;
        else{var target=new RenderTargetBitmap(200*density,200*density,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(target);context.PushTransform(new ScaleTransform(density,density));drawing.RenderTo(context);context.Pop();context.Close();p=target.GetPixelBuffer();}
        Assert.Equal(4*density,Enumerable.Range(0,200*density).Count(y=>p[(y*200*density+100*density)*4+3]>127));
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void TinyGeometryScaleKeepsNonScalingStrokeInsideItsLayerBounds(int route)
    {
        using var actual=Svg("<g transform='translate(30,30) scale(.1)' opacity='.5'><rect x='0' y='0' width='100' height='100' fill='none' stroke='red' stroke-width='8' vector-effect='non-scaling-stroke'/></g>");
        using var expected=Svg("<path d='M30,30H40V40H30Z' fill='none' stroke='red' stroke-width='8' opacity='.5'/>");
        EqualPixels(Render(expected,0),Render(actual,route),1);
    }
    [Fact]
    public void AChangedGroupTransformRecomputesStrokeWithoutReparsing()
    {
        using var svg=Svg("<g transform='scale(2,3)'><line x1='5' y1='16' x2='45' y2='16' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/></g>");
        var root=Assert.IsType<DrawingGroup>(svg.Drawing);var group=Assert.IsType<DrawingGroup>(root.Children.Single());
        foreach(var scale in new[]{3d,2d,1d,3d}){group.Transform=new ScaleTransform(2,scale);var p=Render(svg,1);Assert.Equal(4,Enumerable.Range(0,100).Count(y=>p[(y*100+40)*4+3]>127));}
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void GlyphOutlinesUseTheSameHostStrokeAsShapes(int route)
    {
        const string text="<text x='8' y='20' font-size='16' font-family='Arial' fill='none' stroke='red' stroke-width='2'>HI</text>";
        using var actual=Svg("<g transform='scale(2,3)'>"+text.Replace("stroke-width='2'","stroke-width='2' vector-effect='non-scaling-stroke'")+"</g>");
        using var source=Svg(text);var expected=new DrawingGroup();
        void Visit(Drawing drawing)
        {
            if(drawing is DrawingGroup group){foreach(var child in group.Children)Visit(child);}
            else if(drawing is GeometryDrawing { Geometry: { } geometry, Pen: { } pen })
            {
                var path=geometry.GetFlattenedPathGeometry(.0001,ToleranceType.Absolute);
                foreach(var figure in path.Figures){figure.StartPoint=new(figure.StartPoint.X*2,figure.StartPoint.Y*3);foreach(var segment in figure.Segments){if(segment is LineSegment line)line.Point=new(line.Point.X*2,line.Point.Y*3);else if(segment is PolyLineSegment poly)for(var i=0;i<poly.Points.Count;i++)poly.Points[i]=new(poly.Points[i].X*2,poly.Points[i].Y*3);}}
                expected.Children.Add(new GeometryDrawing(null,pen,path));
            }
        }
        Visit(source.Drawing!);Assert.NotEmpty(expected.Children);
        expected.Children.Insert(0,new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,100,100))));
        using var expectedImage=new SvgImage{Drawing=expected};
        var pixels=Render(expectedImage,0);
        // The same font outlines are the reference; only the host-space stroke
        // operation is independent, so this does not claim text shaping parity.
        EqualPixels(pixels,Render(actual,route),3);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void QuadraticStrokeMatchesIndependentHostPolyline(int route)
    {
        var curve=string.Join("L",Enumerable.Range(0,513).Select(i=>{var t=i/512d;return string.Create(CultureInfo.InvariantCulture,$"{10+60*t:R},{60-120*t+120*t*t:R}");}));
        using var actual=Svg("<path d='M5,20Q20,0,35,20' transform='scale(2,3)' fill='none' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/>");
        using var expected=Svg("<path d='M"+curve+"' fill='none' stroke='red' stroke-width='4'/>");
        EqualPixels(Render(expected,0),Render(actual,route),3);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ObjectBoxRadialStrokeKeepsFocalCirclesInLocalPaintCoordinates(int route)
    {
        const string stops="<stop stop-color='red'/><stop offset='1' stop-color='blue'/>";
        using var actual=Svg("<defs><radialGradient id='g' cx='.5' cy='.5' r='.5' fx='.35' fy='.5' fr='.15'>"+stops+"</radialGradient></defs><path d='M10,10H40V30H10Z' transform='matrix(2,.2,.3,2,5,5)' fill='none' stroke='url(#g)' stroke-width='4' vector-effect='non-scaling-stroke'/>");
        using var expected=Svg("<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='.5' cy='.5' r='.5' fx='.35' fy='.5' fr='.15' gradientTransform='matrix(60,6,6,40,28,27)'>"+stops+"</radialGradient></defs><path d='M28,27L88,33L94,73L34,67Z' fill='none' stroke='url(#g)' stroke-width='4'/>");
        EqualPixels(Render(expected,0),Render(actual,route),3);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ZeroPathLengthLimitCoversTheCompleteHostPath(int route)
    {
        using var svg=Svg("<line x1='0' y1='50' x2='1' y2='50' transform='scale(100,1)' pathLength='0' stroke='red' stroke-width='4' stroke-dasharray='4 4' vector-effect='non-scaling-stroke'/>");
        var pixels=Render(svg,route);for(var x=0;x<100;x++)Assert.Equal(255,pixels[(50*100+x)*4+3]);
    }
    private static SvgImage Svg(string body)=>SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'>"+body+"</svg>");
    private static void EqualPixels(byte[] expected,byte[] actual,int tolerance)
    { Assert.Equal(expected.Length,actual.Length);for(var i=0;i<actual.Length;i++)Assert.True(Math.Abs(expected[i]-actual[i])<=tolerance,$"byte {i}: {expected[i]} != {actual[i]}"); }
}
