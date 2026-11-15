using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssVectorEffectTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void NonUniformScaleKeepsStrokeAndDashesInSvgViewport(int route)
    {
        using var svg = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><style>line{fill:none;stroke:red;stroke-width:4;stroke-dasharray:4 4;vector-effect:non-scaling-stroke}</style><g transform='scale(2,3)'><line x1='10' y1='16' x2='40' y2='16'/></g></svg>");
        var pixels = Render(svg, route);
        Assert.Equal(255, pixels[(48*100+21)*4+3]);
        Assert.Equal(0, pixels[(48*100+25)*4+3]);
        Assert.Equal(0, pixels[(51*100+21)*4+3]);
    }

    [Theory]
    [InlineData(100,1)] [InlineData(200,1)] [InlineData(100,2)] [InlineData(200,2)]
    public void SvgImageSizeAndRasterDensityAreIndependent(int size, int density)
    {
        using var svg = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 100 100'><line x1='10' y1='50' x2='90' y2='50' stroke='red' stroke-width='4' vector-effect='non-scaling-stroke'/></svg>");
        var target = new RenderTargetBitmap(size*density,size*density,96,96,PixelFormats.Bgra32);
        var context = new SoftwareDrawingContext(target);
        context.PushTransform(new ScaleTransform(density,density));
        context.DrawImage(svg,new Rect(0,0,size,size)); context.Pop(); context.Close();
        var pixels=target.GetPixelBuffer();var x=size*density/2;
        Assert.Equal(4*density,Enumerable.Range(0,size*density).Count(y=>pixels[(y*size*density+x)*4+3]>127));
    }

    internal static byte[] Render(SvgImage svg, int route, int size=100)
    {
        if(route==0)return SoftwareVectorRasterizer.Rasterize(svg.Drawing!,size,size,new Rect(0,0,size,size))!;
        var target=new RenderTargetBitmap(size,size,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(target);
        if(route==1)svg.Drawing!.RenderTo(context);
        else if(route==2){var recorder=new DrawingRecorder();svg.Drawing!.RenderTo(recorder);DrawingReplayer.Replay(recorder.Commit(),context);}
        else context.DrawImage(svg,new Rect(0,0,size,size),BitmapScalingMode.NearestNeighbor);
        context.Close();return target.GetPixelBuffer();
    }
}
