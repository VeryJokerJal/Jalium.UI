using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCompositingBackgroundSvgTests
{
    public static TheoryData<string,int,int,int,int> BrowserColors => CssCompositingPaintTests.BrowserColors;

    [Theory] [MemberData(nameof(BrowserColors))]
    public void BackgroundLayersUseTheirOwnIsolatedBackdrop(string mode,int r,int g,int b,int a)
    {
        var owner = new Border { Width=60, Height=40 };
        Css.SetStyle(owner,"background-color:rgba(39,164,219,.7568627451);"+
            "background-image:linear-gradient(rgba(197,62,133,.6705882353),rgba(197,62,133,.6705882353));"+
            "background-blend-mode:"+mode);
        foreach (var recorded in new[]{false,true})
            CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(owner,recorded),10,10,r,g,b,a);
    }

    [Fact]
    public void BlendListsRepeatAndNoneRetainsItsImageIndex()
    {
        var owner = new Border { Width=60, Height=40 };
        Css.SetStyle(owner,"background:red;background-image:linear-gradient(blue,blue),none,linear-gradient(lime,lime);"+
            "background-blend-mode:multiply,screen,normal");
        foreach (var recorded in new[]{false,true})
            CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(owner,recorded),10,10,0,0,0,255);
        Css.SetStyle(owner,"background:red;background-image:linear-gradient(blue,blue);background-blend-mode:screen");
        CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(owner),10,10,255,0,255,255);
        Css.SetStyle(owner,"background:red;background-image:linear-gradient(blue,blue)");
        CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(owner),10,10,0,0,255,255);
    }

    [Theory] [MemberData(nameof(BrowserColors))]
    public void SvgBlendsUseTheSameIndependentBrowserReference(string mode,int r,int g,int b,int a)
    {
        var image = Parse("<rect width='60' height='40' fill='rgba(39,164,219,.7568627451)'/>"+
            $"<rect width='60' height='40' fill='rgba(197,62,133,.6705882353)' style='mix-blend-mode:{mode}'/>");
        var pixels = SoftwareVectorRasterizer.Rasterize(image.Drawing!,60,40)!;
        CssCompositingPaintTests.Pixel(pixels,10,10,r,g,b,a);
        var control = new Image { Width=60,Height=40,Source=image,Stretch=Stretch.Fill };
        foreach (var recorded in new[]{false,true})
            CssCompositingPaintTests.Pixel(CssCompositingPaintTests.Render(control,recorded),10,10,r,g,b,a);
    }

    [Theory]
    [InlineData("",0,0,0)]
    [InlineData("style='isolation:isolate'",0,0,255)]
    [InlineData("transform='translate(0)'",0,0,0)]
    [InlineData("clip-path='url(#c)'",0,0,255)]
    [InlineData("opacity='.5'",128,0,128)]
    public void SvgIsolationIsNotCreatedByATwoDimensionalTransform(string attributes,int r,int g,int b)
    {
        var image = Parse("<defs><clipPath id='c'><rect width='60' height='40'/></clipPath></defs><style>rect.blend{mix-blend-mode:multiply}</style>"+
            $"<rect width='60' height='40' fill='red'/><g {attributes}><rect class='blend' width='60' height='40' fill='blue'/></g>");
        CssCompositingPaintTests.Pixel(SoftwareVectorRasterizer.Rasterize(image.Drawing!,60,40)!,10,10,r,g,b,255);
    }

    [Theory]
    [InlineData("mix-blend-mode","multiply",true)]
    [InlineData("mix-blend-mode","PLUS-LIGHTER",true)]
    [InlineData("mix-blend-mode","multiply,screen",false)]
    [InlineData("mix-blend-mode","plus-darker",false)]
    [InlineData("background-blend-mode","multiply, SCREEN",true)]
    [InlineData("background-blend-mode","normal,",false)]
    [InlineData("background-blend-mode","plus-lighter",true)]
    [InlineData("isolation","isolate",true)]
    [InlineData("isolation","none",false)]
    [InlineData("z-index","auto",true)]
    [InlineData("z-index","0",true)]
    public void GrammarRejectsUnsupportedAndMalformedModes(string name,string value,bool valid)
        => Assert.Equal(valid,Css.Supports(name,value));

    private static SvgImage Parse(string body)
    {
        return SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='60' height='40'>"+body+"</svg>");
    }
}
