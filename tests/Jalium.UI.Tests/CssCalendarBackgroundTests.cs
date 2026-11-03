using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssCalendarBackgroundTests
{
    [Fact]
    public void CssLayers_UseContentBoxForClipAndPosition()
    {
        var calendar = new Calendar();
        Css.SetStyle(calendar,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(calendar, 400, 350);

        var pixels = CssBorderRadiusTests.Pixels(calendar, 400, 350);
        Assert.Equal((byte)0, Channel(pixels, 400, 10, 200, 3));
        Assert.Equal((byte)255, Channel(pixels, 400, 350, 200, 0));
        Assert.True(Channel(pixels, 400, 372, 332, 2) > 100);
        Assert.True(Channel(pixels, 400, 385, 332, 1) > 100);
    }

    [Fact]
    public void CssRadius_ClipsOuterBackground()
    {
        var calendar = new Calendar();
        Css.SetStyle(calendar, "background-color:blue;border-radius:50%");
        CssBorderRadiusTests.Layout(calendar, 400, 350);

        var pixels = CssBorderRadiusTests.Pixels(calendar, 400, 350);
        Assert.Equal((byte)0, Channel(pixels, 400, 399, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 400, 350, 175, 0));
    }

    [Fact]
    public void NativeBackground_KeepsPriority()
    {
        var calendar = new Calendar { Background = Brushes.Lime };
        Css.SetStyle(calendar,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(calendar, 400, 350);

        var pixels = CssBorderRadiusTests.Pixels(calendar, 400, 350);
        Assert.Equal((byte)255, Channel(pixels, 400, 10, 200, 1));
        Assert.Equal((byte)0, Channel(pixels, 400, 10, 200, 0));
    }

    [Fact]
    public void CalendarItemStyleBackground_KeepsItsSurfacePriority()
    {
        var itemStyle = new Style(typeof(CalendarItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Red));
        var calendar = new Calendar { CalendarItemStyle = itemStyle };
        Css.SetStyle(calendar, "background-color:blue;background-clip:content-box");
        CssBorderRadiusTests.Layout(calendar, 400, 350);

        var pixels = CssBorderRadiusTests.Pixels(calendar, 400, 350);
        Assert.Equal((byte)255, Channel(pixels, 400, 350, 200, 2));
        Assert.Equal((byte)0, Channel(pixels, 400, 350, 200, 0));
    }

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];
}
