using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssStatusBarItemBoxTests
{
    [Fact]
    public void CssLayers_UseContentBoxForClipAndPosition()
    {
        var item = new StatusBarItem();
        Css.SetStyle(item,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(item, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(item, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 20, 30, 0));
        Assert.True(Channel(pixels, 80, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 80, 65, 43, 1) > 100);
    }

    [Fact]
    public void CssBorderAndPadding_InsetChildContent()
    {
        var item = new StatusBarItem { Content = new Border { Background = Brushes.Red } };
        Css.SetStyle(item, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(item, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(item, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 14, 30, 2));
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var item = new StatusBarItem { Background = Brushes.Lime };
        Css.SetStyle(item,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(item, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(item, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 10, 30, 0));
    }

    [Fact]
    public void NativeBorderBrush_KeepsPriorityOverCssSideColors()
    {
        var item = new StatusBarItem { BorderBrush = Brushes.Lime };
        Css.SetStyle(item, "border:8px solid; border-color:red blue yellow white");
        CssBorderRadiusTests.Layout(item, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(item, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 80, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 80, 40, 4, 2));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
