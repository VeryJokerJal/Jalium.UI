using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssThumbBoxTests
{
    private const int Width = 100;
    private const int Height = 60;

    private const string LayeredBackground =
        "border:8px solid transparent;padding:4px;background-color:blue;" +
        "background-image:linear-gradient(to right,red,lime);" +
        "background-size:20px 10px;background-position:right bottom;" +
        "background-repeat:no-repeat;background-origin:content-box;" +
        "background-clip:content-box";

    [Fact]
    public void DirectThumb_PaintsPositionedCssBackgroundLayers()
    {
        var thumb = DirectThumb();
        Css.SetStyle(thumb, LayeredBackground);
        CssBorderRadiusTests.Layout(thumb, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
        Assert.True(Channel(pixels, 70, 43, 2) > 100);
        Assert.True(Channel(pixels, 85, 43, 1) > 100);
    }

    [Fact]
    public void DirectThumb_BackgroundNoneSuppressesFallbackAndRemovalRestoresIt()
    {
        var thumb = DirectThumb();
        Css.SetStyle(thumb, "background:none");
        CssBorderRadiusTests.Layout(thumb, Width, Height);
        Assert.Equal((byte)0, Channel(CssBorderRadiusTests.Pixels(thumb, Width, Height), 40, 20, 3));

        Css.SetStyle(thumb, "");
        CssBorderRadiusTests.Layout(thumb, Width, Height);
        Assert.Equal((byte)255, Channel(CssBorderRadiusTests.Pixels(thumb, Width, Height), 40, 20, 3));
    }

    [Fact]
    public void DirectThumb_PaintsEllipticalRadiusAndDistinctBorderSides()
    {
        var thumb = DirectThumb();
        Css.SetStyle(thumb,
            "border:8px solid;border-color:red green blue yellow;" +
            "border-radius:50% / 30%;background-color:lime");
        CssBorderRadiusTests.Layout(thumb, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
        Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
        Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
        Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
    }

    [Fact]
    public void DirectThumb_DoubleBorderLeavesMiddleBandClear()
    {
        var thumb = DirectThumb();
        Css.SetStyle(thumb, "background:none;border:9px double red");
        CssBorderRadiusTests.Layout(thumb, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 50, 1, 2));
        Assert.Equal((byte)0, Channel(pixels, 50, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 50, 7, 2));
    }

    [Fact]
    public void DirectThumb_AsymmetricCssEdgesMoveGripIntoContentBox()
    {
        var thumb = DirectThumb();
        thumb.ShowGrip = true;
        Css.SetStyle(thumb,
            "background:none;border:8px solid transparent;padding:4px 4px 4px 20px");
        CssBorderRadiusTests.Layout(thumb, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
        Assert.Equal((byte)0, Channel(pixels, 50, 30, 3));
        Assert.True(Channel(pixels, 58, 30, 3) >= 100);
    }

    [Fact]
    public void DirectThumb_LocalBackgroundKeepsPriorityOverCssLayers()
    {
        var thumb = DirectThumb();
        thumb.Background = Brushes.Red;
        Css.SetStyle(thumb, "background-color:blue;background-image:linear-gradient(lime,lime)");
        CssBorderRadiusTests.Layout(thumb, Width, Height);

        var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
        Assert.Equal((byte)255, Channel(pixels, 40, 20, 2));
        Assert.Equal((byte)0, Channel(pixels, 40, 20, 1));
    }

    [Fact]
    public void ThemeThumb_TransfersCssBoxToTemplateBorder()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var thumb = new Thumb { Width = Width, Height = Height, ShowGrip = false };
            Css.SetStyle(thumb, LayeredBackground);
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(thumb);
            CssBorderRadiusTests.Layout(host, Width, Height);

            var border = Assert.IsType<Border>(thumb.FindName("ThumbBorder"));
            Assert.Equal(new Thickness(4), border.Padding);
            var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
            Assert.Equal((byte)0, Channel(pixels, 10, 30, 3));
            Assert.Equal((byte)255, Channel(pixels, 40, 20, 0));
            Assert.True(Channel(pixels, 70, 43, 2) > 100);
            Assert.True(Channel(pixels, 85, 43, 1) > 100);

            Css.SetStyle(thumb, "");
            CssBorderRadiusTests.Layout(host, Width, Height);
            Assert.Equal(new Thickness(0), border.Padding);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ThemeThumb_TransfersEllipticalRadiusAndSideColors()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var thumb = new Thumb { Width = Width, Height = Height };
            Css.SetStyle(thumb,
                "border:8px solid;border-color:red green blue yellow;" +
                "border-radius:50% / 30%;background-color:lime");
            var host = new Grid { Width = Width, Height = Height };
            host.Children.Add(thumb);
            CssBorderRadiusTests.Layout(host, Width, Height);

            Assert.IsType<Border>(thumb.FindName("ThumbBorder"));
            var pixels = CssBorderRadiusTests.Pixels(thumb, Width, Height);
            Assert.Equal((byte)0, Channel(pixels, 2, 2, 3));
            Assert.Equal((byte)255, Channel(pixels, 50, 4, 2));
            Assert.Equal((byte)128, Channel(pixels, 96, 30, 1));
            Assert.Equal((byte)255, Channel(pixels, 50, 56, 0));
            Assert.Equal((byte)255, Channel(pixels, 4, 30, 1));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Thumb DirectThumb() => new()
    {
        Template = null,
        ShowGrip = false,
        Width = Width,
        Height = Height,
    };

    private static byte Channel(byte[] pixels, int x, int y, int channel)
        => pixels[(y * Width + x) * 4 + channel];

    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }
}
