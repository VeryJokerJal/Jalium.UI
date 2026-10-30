using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssPageBoxTests
{
    [Fact]
    public void CssLayers_UseContentBoxForClipAndPosition()
    {
        var page = new Page();
        Css.SetStyle(page,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(page, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(page, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 20, 30, 0));
        Assert.True(Channel(pixels, 80, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 80, 65, 43, 1) > 100);
    }

    [Fact]
    public void CssBorderAndPadding_InsetPageChild()
    {
        var page = new Page { Content = new Border { Background = Brushes.Red } };
        Css.SetStyle(page, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(page, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(page, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 14, 30, 2));
    }

    [Fact]
    public void CssBorder_PaintsDistinctSideColors()
    {
        var page = new Page();
        Css.SetStyle(page, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(page, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(page, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 80, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 80, 4, 30, 1));
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var page = new Page { Background = Brushes.Lime };
        Css.SetStyle(page,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(page, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(page, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 0));
    }

    [Fact]
    public void ContentBoxWidth_IncludesCssBorderAndPadding()
    {
        var page = new Page();
        Css.SetStyle(page,
            "width:20px;box-sizing:content-box;border:5px solid red;padding:3px");
        CssBorderRadiusTests.Layout(page, 80, 60);

        Assert.Equal(36, page.VisualBounds.Width);
    }

    [Fact]
    public void RemovingCssEdges_RestoresChildPlacement()
    {
        var page = new Page { Content = new Border { Background = Brushes.Red } };
        Css.SetStyle(page, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(page, 80, 60);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(page, 80, 60), 80, 2, 30, 3));

        Css.SetStyle(page, string.Empty);
        CssBorderRadiusTests.Layout(page, 80, 60);
        Assert.Equal((byte)255, Channel(CssBorderRadiusTests.Pixels(page, 80, 60), 80, 2, 30, 2));
    }

    [Fact]
    public void PercentagePadding_UsesContainingWidthForBothAxes()
    {
        var page = new Page { Content = new Border { Background = Brushes.Red } };
        Css.SetStyle(page, "padding:10%");
        CssBorderRadiusTests.Layout(page, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(page, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 6, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 30, 2));
        Assert.Equal((byte)0, Channel(pixels, 80, 40, 6, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 10, 2));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
