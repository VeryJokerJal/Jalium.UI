using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Drawing = Jalium.UI.Media.Drawing;

namespace Jalium.UI.Tests;

public sealed class CssRadialConeTests
{
    private static readonly Rect Viewport = new(0, 0, 200, 128);
    private static readonly Point[] Samples = [new(32.5, 16.5), new(64.5, 32.5), new(80.5, 64.5), new(96.5, 64.5), new(120.5, 48.5), new(144.5, 64.5), new(168.5, 96.5)];
    private static readonly (double X, double Y, double Fr, double R)[] Circles =
    [
        (96,64,0,40), (96,64,16,40), (96,64,60,40), (96,64,40,40), (96,64,0,0),
        (96,64,24,0), (80,64,8,40), (88,64,64,40), (56,64,40,40),
        (168,64,0,40), (168,64,12,40), (136,64,0,40), (124,64,12,40),
        (116,64,60,40), (135.999,64,0,40), (96,136,8,40),
    ];

    public static IEnumerable<object[]> Cases()
    {
        for (var circle = 0; circle < Circles.Length; circle++)
        for (var spread = 0; spread < 3; spread++)
        for (var route = 0; route < 5; route++) yield return [circle, spread, route];
    }

    [Theory, MemberData(nameof(Cases))]
    public void SvgRadialCirclesUseTheLastPositiveRadiusIntersection(int circle, int spread, int route)
    {
        var c = Circles[circle]; using var image = SvgImage.FromSvgString(Svg(c, spread));
        var pixels = Paint(image.Drawing!, route);
        foreach (var point in Samples)
        {
            var value = Reference(point, c, spread);
            var at = ((int)point.Y * 200 + (int)point.X) * 4;
            if (value is null) { Assert.Equal(0, pixels[at + 3]); continue; }
            var expected = (int)Math.Round(value.Value * 255);
            for (var channel = 0; channel < 3; channel++)
                Assert.True(Math.Abs(pixels[at + channel] - expected) <= 1, $"circle{circle} spread{spread} ({point.X},{point.Y}) channel{channel}: actual{pixels[at + channel]} expected{expected}");
            Assert.Equal(255, pixels[at + 3]);
        }
    }

    [Theory]
    [InlineData("r='40' fr='-2'",0)] [InlineData("r='40' fr='-2'",1)] [InlineData("r='40' fr='-2'",2)] [InlineData("r='40' fr='-2'",3)] [InlineData("r='40' fr='-2'",4)]
    [InlineData("r='-2' fr='16'",0)] [InlineData("r='-2' fr='16'",1)] [InlineData("r='-2' fr='16'",2)] [InlineData("r='-2' fr='16'",3)] [InlineData("r='-2' fr='16'",4)]
    public void NegativeSvgRadiiDoNotProducePaint(string radii,int route)
    {
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96' cy='64' "+radii+"><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient><rect width='200' height='128' fill='url(#g)'/></svg>");
        var pixels = Paint(image.Drawing!,route); Assert.True(Enumerable.Range(0,200*128).All(i=>pixels[i*4+3]==0));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void FrPercentagesFollowTheTemplateAndViewportDiagonal(int route)
    {
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><defs><radialGradient id='base' gradientUnits='userSpaceOnUse' cx='96' cy='64' r='40' fr='10%'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient><radialGradient id='mid' href='#base'/><radialGradient id='g' href='#mid'/></defs><rect width='200' height='128' fill='url(#g)'/></svg>");
        var pixels = Paint(image.Drawing!,route);var radius=Math.Sqrt((200*200+128*128)/2.0)*.1;
        foreach(var p in Samples){var expected=(int)Math.Round(Math.Clamp((Math.Sqrt(Math.Pow(p.X-96,2)+Math.Pow(p.Y-64,2))-radius)/(40-radius),0,1)*255);Assert.InRange(Math.Abs(pixels[((int)p.Y*200+(int)p.X)*4]-expected),0,1);}
    }

    [Theory]
    [InlineData("sRGB",false)] [InlineData("linearRGB",true)]
    public void BoundaryRepeatUsesTheOffsetWeightedAverageInTheInterpolationSpace(string space,bool linearLight)
    {
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96' cy='64' r='40' fx='136' fy='64' spreadMethod='repeat' color-interpolation='"+space+"'><stop stop-color='black' stop-opacity='.2'/><stop offset='.25' stop-color='white' stop-opacity='.8'/><stop offset='1' stop-color='black' stop-opacity='.6'/></radialGradient><rect width='200' height='128' fill='url(#g)'/></svg>");
        var pixels=Paint(image.Drawing!,1);var at=(64*200+168)*4;var average=.5;var encoded=linearLight?1.055*Math.Pow(average,1/2.4)-.055:average;
        Assert.InRange(Math.Abs(pixels[at]-(int)Math.Round(encoded*255)),0,2);
        Assert.InRange(Math.Abs(pixels[at+3]-(int)Math.Round((.25*(.2+.8)/2+.75*(.8+.6)/2)*255)),0,1);
    }

    [Fact]
    public void GradientGeometryAttributesDoNotAcceptCssDeclarations()
    {
        var start="<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse'";
        var end="><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient><rect width='200' height='128' fill='url(#g)'/></svg>";
        using var normal=SvgImage.FromSvgString(start+end);using var styled=SvgImage.FromSvgString(start+" style='cx:0;cy:0;r:5px;fx:0;fy:0;fr:3px'"+end);
        Assert.Equal(Paint(normal.Drawing!,1),Paint(styled.Drawing!,1));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void FocalRadiusMutationInvalidatesSnapshotsAndPreservesClones(int route)
    {
        using var image=SvgImage.FromSvgString(Svg((96,64,16,40),0));
        var brush=FindRadial(image.Drawing!)!;
        var original=Paint(image.Drawing!,route);var hash=brush.ComputeContentHash();
        var snapshot=Assert.IsType<RadialGradientBrush>(DrawInputSnapshotter.SnapshotBrush(brush));
        var clone=brush.Clone();var current=brush.CloneCurrentValue();
        Assert.Equal(16,snapshot.SvgFocalRadius);Assert.True(snapshot.UsesSvgRadialInterpolation);
        brush.SvgFocalRadius=60;
        Assert.NotEqual(hash,brush.ComputeContentHash());
        Assert.NotSame(snapshot,DrawInputSnapshotter.SnapshotBrush(brush));
        Assert.False(original.SequenceEqual(Paint(image.Drawing!,route)));
        Assert.Equal(16,clone.SvgFocalRadius);Assert.Equal(16,current.SvgFocalRadius);
        var cloned=new GeometryDrawing(clone,null,new RectangleGeometry(new(20,12,160,96)));
        Assert.Equal(original,Paint(cloned,route));
        brush.SvgFocalRadius=16;Assert.Equal(original,Paint(image.Drawing!,route));
        clone.Freeze();Assert.Throws<InvalidOperationException>(()=>clone.SvgFocalRadius=8);
    }

    [Fact]
    public void SvgCircleModeParticipatesInContentAndSnapshotIdentity()
    {
        var brush=new RadialGradientBrush(Colors.Black,Colors.White);
        var before=brush.ComputeContentHash();var snapshot=DrawInputSnapshotter.SnapshotBrush(brush);
        brush.UsesSvgRadialInterpolation=true;
        Assert.NotEqual(before,brush.ComputeContentHash());Assert.NotSame(snapshot,DrawInputSnapshotter.SnapshotBrush(brush));
        Assert.True(brush.Clone().UsesSvgRadialInterpolation);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void MarkerContextPaintKeepsItsFocalCircleInSourceCoordinates(int route)
    {
        const string start="<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><defs>";
        const string gradient="<radialGradient id='g' cx='.4' cy='.5' r='.6' fx='.2' fy='.5' fr='.2'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient>";
        const string marker="<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10'><rect width='20' height='20' fill='context-fill'/></marker>";
        using var actual=SvgImage.FromSvgString(start+gradient+marker+"</defs><rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/></svg>");
        const string user="<radialGradient id='u' gradientUnits='userSpaceOnUse' cx='56' cy='45' r='24' fx='48' fy='45' fr='8' gradientTransform='matrix(1 0 0 .75 0 11.25)'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient>";
        using var expected=SvgImage.FromSvgString(start+gradient+user+"</defs><rect x='40' y='30' width='40' height='30' fill='url(#g)'/><rect x='30' y='20' width='20' height='20' fill='url(#u)'/></svg>");
        var wanted=Paint(expected.Drawing!,route);var painted=Paint(actual.Drawing!,route);
        Assert.InRange(wanted.Zip(painted,(a,b)=>Math.Abs(a-b)).Max(),0,1);
    }

    private static RadialGradientBrush? FindRadial(Drawing drawing)
    {
        if(drawing is GeometryDrawing { Brush:RadialGradientBrush radial })return radial;
        if(drawing is DrawingGroup group)foreach(var child in group.Children)if(FindRadial(child) is {} found)return found;
        return null;
    }

    public static IEnumerable<object[]> ApexCases()
    {
        foreach(var shifted in new[]{false,true})foreach(var fr in new[]{0,16,60})
        for(var spread=0;spread<3;spread++)for(var route=0;route<5;route++)
            yield return [shifted,fr,spread,route];
    }

    [Theory,MemberData(nameof(ApexCases))]
    public void NestedCircleApexUsesItsContinuousColorLimit(bool shifted,int fr,int spread,int route)
    {
        var fx=shifted?84.5:96.5;
        var text=string.Create(CultureInfo.InvariantCulture,$"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96.5' cy='64.5' r='40' fx='{fx}' fy='64.5' fr='{fr}' spreadMethod='{(spread==1?"repeat":spread==2?"reflect":"pad")}'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient><rect x='20' y='12' width='160' height='96' fill='url(#g)'/></svg>");
        using var image=SvgImage.FromSvgString(text);var pixels=Paint(image.Drawing!,route);
        var x=shifted?(fr==16?76:fr==60?120:84):96;var at=(64*200+x)*4;
        var expected=fr==60?255:fr==16?(spread==1?85:spread==2?170:0):0;
        Assert.Equal(255,pixels[at+3]);for(var c=0;c<3;c++)Assert.InRange(Math.Abs(pixels[at+c]-expected),0,1);
    }

    // Independent decimal circle-family reference. The product uses normalized
    // double geometry and a cancellation-safe quadratic; this oracle calculates
    // both intersections directly at 28-digit precision and orders valid circles.
    private static double? Reference(Point p,(double X,double Y,double Fr,double R) circles,int spread)
    {
        decimal fx=(decimal)circles.X,fy=(decimal)circles.Y,fr=(decimal)circles.Fr,r=(decimal)circles.R;
        decimal dx=96-fx,dy=64-fy,dr=r-fr,x=(decimal)p.X-fx,y=(decimal)p.Y-fy;
        if(dx==0&&dy==0&&dr==0)return null;
        var a=dx*dx+dy*dy-dr*dr;var b=-2*(x*dx+y*dy+fr*dr);var c=x*x+y*y-fr*fr;
        var roots=new List<decimal>();
        if(a==0){if(b!=0)roots.Add(-c/b);}
        else{var discriminant=b*b-4*a*c;if(discriminant>=0){var q=Sqrt(discriminant);roots.Add((-b-q)/(2*a));roots.Add((-b+q)/(2*a));}}
        var valid=roots.Where(t=>fr+t*dr>0).ToArray();
        if(valid.Length==0)
        {
            if(fr==0&&r>0&&dx*dx+dy*dy==r*r)return spread==0?1:.5;
            return null;
        }
        var value=(double)valid.Max();
        return spread switch{1=>value-Math.Floor(value),2=>Reflect(value),_=>Math.Clamp(value,0,1)};
    }
    private static decimal Sqrt(decimal x)
    {
        if(x==0)return 0;var value=(decimal)Math.Sqrt((double)x);
        for(var i=0;i<24;i++){var next=(value+x/value)/2;if(next==value)break;value=next;}return value;
    }
    private static double Reflect(double value){var t=Math.Abs(value)%2;return t>1?2-t:t;}
    private static string Svg((double X,double Y,double Fr,double R) c,int spread) => string.Create(CultureInfo.InvariantCulture,$"<svg xmlns='http://www.w3.org/2000/svg' width='200' height='128'><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='96' cy='64' r='{c.R}' fx='{c.X}' fy='{c.Y}' fr='{c.Fr}' spreadMethod='{(spread==1?"repeat":spread==2?"reflect":"pad")}'><stop stop-color='black'/><stop offset='1' stop-color='white'/></radialGradient><rect x='20' y='12' width='160' height='96' fill='url(#g)'/></svg>");
    private static byte[] Paint(Drawing drawing,int route)
    {
        if(route==1)return SoftwareVectorRasterizer.Rasterize(drawing,200,128,Viewport)!;
        var bitmap=new RenderTargetBitmap(200,128,96,96,PixelFormats.Bgra32);var dc=new SoftwareDrawingContext(bitmap);
        if(route==4){var recorder=new DrawingRecorder();recorder.BindWholeFrame();drawing.RenderTo(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}
        else if(route is 2 or 3){var scene=new DrawingGroup();scene.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(Viewport)));scene.Children.Add(drawing);if(route==2)dc.DrawImage(new DrawingImage(scene),Viewport);else dc.DrawRectangle(new ImageBrush(new DrawingImage(scene)){Stretch=Stretch.Fill},null,Viewport);}
        else drawing.RenderTo(dc);
        dc.Close();var pixels=new byte[200*128*4];bitmap.CopyPixels(pixels,200*4,0);return pixels;
    }
}
