using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssPasswordBoxBackgroundTests
{
    [Fact]
    public void CssLayers_UseContentBoxForClipAndPosition()
    {
        var box = new PasswordBox();
        Css.SetStyle(box,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 20, 30, 0));
        Assert.True(Channel(pixels, 80, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 80, 65, 43, 1) > 100);
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var box = new PasswordBox { Background = Brushes.Lime };
        Css.SetStyle(box,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(box, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(box, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 0));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
