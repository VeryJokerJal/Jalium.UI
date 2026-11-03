using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssScrollBarBackgroundTests
{
    [Fact]
    public void ContentBoxClip_LeavesPaddingClearAndPaintsContent()
    {
        var bar = new ScrollBar { IsOverlayStyle = false };
        Css.SetStyle(bar,
            "border:8px solid transparent; padding:4px;" +
            "background-color:blue; background-clip:content-box");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        var pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.Equal((byte)0, Channel(pixels, 60, 10, 182, 3));
        Assert.Equal((byte)255, Channel(pixels, 60, 16, 182, 0));
        Assert.Equal((byte)255, Channel(pixels, 60, 16, 182, 3));
    }

    [Fact]
    public void GradientLayer_UsesContentBoxForPlacementAndClip()
    {
        var bar = new ScrollBar { IsOverlayStyle = false };
        Css.SetStyle(bar,
            "border:8px solid transparent; padding:4px;" +
            "background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px; background-position:right bottom;" +
            "background-repeat:no-repeat; background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        var pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.Equal((byte)0, Channel(pixels, 60, 10, 182, 3));
        Assert.Equal((byte)255, Channel(pixels, 60, 16, 182, 0));
        Assert.Equal((byte)0, Channel(pixels, 60, 16, 182, 1));
        Assert.True(Channel(pixels, 60, 30, 182, 2) > 150);
        Assert.True(Channel(pixels, 60, 45, 182, 1) > 150);
        Assert.Equal((byte)255, Channel(pixels, 60, 30, 170, 0));
    }

    [Fact]
    public void ExplicitNone_SuppressesDefaultTrackBackground()
    {
        var bar = new ScrollBar { IsOverlayStyle = false };
        Css.SetStyle(bar, "background:none");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        var pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.Equal((byte)0, Channel(pixels, 60, 10, 182, 3));

        Css.SetStyle(bar, "");
        CssBorderRadiusTests.Layout(bar, 60, 200);
        pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.True(Channel(pixels, 60, 10, 182, 3) > 0);
    }

    [Fact]
    public void NativeBackground_KeepsPriorityOverCssLayers()
    {
        var bar = new ScrollBar { IsOverlayStyle = false, Background = Brushes.Lime };
        Css.SetStyle(bar,
            "border:8px solid transparent; padding:4px;" +
            "background-color:blue; background-clip:content-box");
        CssBorderRadiusTests.Layout(bar, 60, 200);

        var pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.Equal((byte)255, Channel(pixels, 60, 10, 182, 1));
        Assert.Equal((byte)0, Channel(pixels, 60, 10, 182, 0));

        Css.SetStyle(bar, "background:none");
        CssBorderRadiusTests.Layout(bar, 60, 200);
        pixels = CssBorderRadiusTests.Pixels(bar, 60, 200);
        Assert.Equal((byte)255, Channel(pixels, 60, 10, 182, 1));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
