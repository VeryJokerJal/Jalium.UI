using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssProgressBarBoxTests
{
    [Fact]
    public void DirectBackgroundLayers_UseContentBox()
    {
        var bar = DirectBar();
        Css.SetStyle(bar,
            "border:8px solid transparent;padding:4px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 30, 0));
        Assert.True(Channel(pixels, 50, 43, 2) > 100);
        Assert.True(Channel(pixels, 65, 43, 1) > 100);
    }

    [Fact]
    public void DirectBorder_PaintsDistinctSideColors()
    {
        var bar = DirectBar();
        Css.SetStyle(bar, "border:8px solid; border-color:red green blue yellow");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 76, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 40, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
    }

    [Fact]
    public void CssEdges_InsetDirectProgress()
    {
        var bar = DirectBar();
        bar.Value = 50;
        bar.ProgressBrush = Brushes.Red;
        Css.SetStyle(bar, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 8, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 30, 2));
        Assert.True(Channel(pixels, 55, 30, 2) < 255);
    }

    [Fact]
    public void NativeBackgroundAndBorderBrush_KeepPriority()
    {
        var bar = DirectBar();
        bar.Background = Brushes.Lime;
        bar.BorderBrush = Brushes.Lime;
        Css.SetStyle(bar,
            "background-color:blue;background-clip:content-box;" +
            "border:8px solid; border-color:red blue yellow white;padding:4px");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 60);
        Assert.Equal((byte)255, Channel(pixels, 10, 30, 1));
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 0));
        Assert.Equal((byte)255, Channel(pixels, 40, 4, 1));
        Assert.Equal((byte)0, Channel(pixels, 40, 4, 2));
    }

    [Fact]
    public void RemovingCssEdges_RestoresDirectProgressBounds()
    {
        var bar = DirectBar();
        bar.Value = 50;
        bar.ProgressBrush = Brushes.Red;
        Css.SetStyle(bar, "border:8px solid transparent;padding:4px");
        CssBorderRadiusTests.Layout(bar, 80, 60);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(bar, 80, 60), 2, 30, 3));

        Css.SetStyle(bar, string.Empty);
        CssBorderRadiusTests.Layout(bar, 80, 60);
        Assert.Equal((byte)255, Channel(CssBorderRadiusTests.Pixels(bar, 80, 60), 2, 30, 2));
    }

    [Fact]
    public void BackgroundNone_RemovesDirectFallbackTrack()
    {
        var bar = DirectBar();
        Css.SetStyle(bar, "background:none");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(bar, 80, 60), 40, 30, 3));
    }

    [Fact]
    public void RadiusWithoutCssBackground_ClipsDirectFallbackTrack()
    {
        var bar = DirectBar();
        Css.SetStyle(bar, "border-radius:50%");
        CssBorderRadiusTests.Layout(bar, 80, 60);

        var pixels = CssBorderRadiusTests.Pixels(bar, 80, 60);
        Assert.Equal((byte)0, Channel(pixels, 10, 1, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 30, 3));
    }

    private static ProgressBar DirectBar() => new() { Template = null };

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * 80 + x) * 4 + channel];
}
