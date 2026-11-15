using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class CssBlendMathTests
{
    [Theory] [MemberData(nameof(CssCompositingPaintTests.BrowserColors),MemberType=typeof(CssCompositingPaintTests))]
    public void AllBlendFunctionsMatchIndependentBrowserPixels(string name,int r,int g,int b,int a)
    {
        var mode=Enum.Parse<CssBlendMode>(name.Replace("-",""),true);
        var color=CssBlend.Composite(Color.FromArgb(171,197,62,133),Color.FromArgb(193,39,164,219),mode);
        Assert.InRange(Math.Abs(color.R-r),0,2);Assert.InRange(Math.Abs(color.G-g),0,2);
        Assert.InRange(Math.Abs(color.B-b),0,2);Assert.InRange(Math.Abs(color.A-a),0,2);
    }
    [Theory] [InlineData("ColorDodge",0,255,0)] [InlineData("ColorBurn",255,0,255)]
    public void SingularChannelsUseTheSpecifiedEndpointPrecedence(string mode,int backdrop,int source,int expected)
    {
        var color=CssBlend.Composite(Color.FromRgb((byte)source,(byte)source,(byte)source),
            Color.FromRgb((byte)backdrop,(byte)backdrop,(byte)backdrop),Enum.Parse<CssBlendMode>(mode));
        Assert.Equal(expected,color.R);Assert.Equal(expected,color.G);Assert.Equal(expected,color.B);Assert.Equal(255,color.A);
    }
    [Theory] [InlineData("Hue")] [InlineData("Saturation")] [InlineData("Color")] [InlineData("Luminosity")]
    public void TransparentBackdropsDoNotChangeSourceColors(string mode)
    {
        var source=Color.FromArgb(128,197,62,133);var backdrop=Color.FromArgb(0,255,255,255);
        Assert.Equal(source,CssBlend.Composite(source,backdrop,Enum.Parse<CssBlendMode>(mode)));
    }
}
