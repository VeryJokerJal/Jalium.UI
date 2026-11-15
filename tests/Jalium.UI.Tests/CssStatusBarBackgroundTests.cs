using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssStatusBarBackgroundTests
{
    [Fact]
    public void CssLayers_UseContentBoxForClipAndPosition()
    {
        var bar = new StatusBar();
        Css.SetStyle(bar,
            "border:4px solid transparent;padding:3px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(bar, 80, 40);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 40);
        Assert.Equal((byte)0, Channel(pixels, 80, 5, 20, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 20, 0));
        Assert.True(Channel(pixels, 80, 60, 28, 2) > 100);
        Assert.True(Channel(pixels, 80, 70, 28, 1) > 100);
    }

    [Fact]
    public void CssRadius_ClipsBackgroundAtBorderBox()
    {
        var bar = new StatusBar();
        Css.SetStyle(bar, "background-color:blue;border-radius:50%");
        CssBorderRadiusTests.Layout(bar, 80, 40);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 40);
        Assert.Equal((byte)0, Channel(pixels, 80, 1, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 20, 0));
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var bar = new StatusBar { Background = Brushes.Lime };
        Css.SetStyle(bar,
            "background-color:blue;background-clip:content-box;" +
            "border:4px solid transparent;padding:3px");
        CssBorderRadiusTests.Layout(bar, 80, 40);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 40);
        Assert.Equal((byte)255, Channel(pixels, 80, 5, 20, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 5, 20, 0));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
