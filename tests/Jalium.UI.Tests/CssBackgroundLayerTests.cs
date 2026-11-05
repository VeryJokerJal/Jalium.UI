using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssBackgroundLayerTests
{
    private const string HalfRed =
        "linear-gradient(rgb(255 0 0 / .5),rgb(255 0 0 / .5))";
    private const string HalfLime =
        "linear-gradient(rgb(0 255 0 / .5),rgb(0 255 0 / .5))";

    private static byte Channel(byte[] pixels, int width, int x, int y, int channel)
        => pixels[(y * width + x) * 4 + channel];

    [Fact]
    public void BackgroundImageList_PaintsFirstImageAboveLaterImagesAndColor()
    {
        var border = new Border();
        Assert.True(Css.Supports("background-image", HalfRed + "," + HalfLime));
        Css.SetStyle(border, "background-color:blue;background-image:" + HalfRed + "," + HalfLime);
        CssBorderRadiusTests.Layout(border, 40, 20);

        var top = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<LinearGradientBrush>(top.Top);
        var lower = Assert.IsType<CssLayeredBackgroundBrush>(top.Bottom);
        Assert.IsType<LinearGradientBrush>(lower.Top);
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<SolidColorBrush>(lower.Bottom).Color.ToArgb());

        var pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.InRange(Channel(pixels, 40, 20, 10, 2), (byte)125, (byte)130);
        Assert.InRange(Channel(pixels, 40, 20, 10, 1), (byte)62, (byte)66);
        Assert.InRange(Channel(pixels, 40, 20, 10, 0), (byte)62, (byte)66);
    }

    [Fact]
    public void BackgroundImageList_NoneLayersPreserveTheVisibleImageAndColor()
    {
        var border = new Border();
        Css.SetStyle(border, "background-color:blue;background-image:none," + HalfLime + ",none");
        CssBorderRadiusTests.Layout(border, 40, 20);

        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<LinearGradientBrush>(layers.Top);
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<SolidColorBrush>(layers.Bottom).Color.ToArgb());
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.InRange(Channel(pixels, 40, 20, 10, 1), (byte)125, (byte)130);
        Assert.InRange(Channel(pixels, 40, 20, 10, 0), (byte)125, (byte)130);

        var empty = new Border();
        Css.SetStyle(empty, "background-color:blue;background-image:none,none");
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<SolidColorBrush>(empty.Background).Color.ToArgb());
    }

    [Fact]
    public void BackgroundShorthand_PaintsImageListAndResetsOmittedColorOrImages()
    {
        var border = new Border();
        Css.SetStyle(border, "background:" + HalfRed + "," + HalfLime + " blue");
        CssBorderRadiusTests.Layout(border, 40, 20);
        var pixels = CssBorderRadiusTests.Pixels(border, 40, 20);
        Assert.InRange(Channel(pixels, 40, 20, 10, 2), (byte)125, (byte)130);
        Assert.InRange(Channel(pixels, 40, 20, 10, 1), (byte)62, (byte)66);

        var resetImage = new Border();
        Css.SetStyle(resetImage,
            "background-color:blue;background-image:" + HalfRed + ";background:lime");
        Assert.Equal(Colors.Lime.ToArgb(),
            Assert.IsType<SolidColorBrush>(resetImage.Background).Color.ToArgb());

        var resetColor = new Border();
        Css.SetStyle(resetColor, "background-color:blue;background:" + HalfRed);
        Assert.IsType<LinearGradientBrush>(resetColor.Background);

        var resetBoth = new Border();
        Css.SetStyle(resetBoth, "background-color:blue;background-image:" + HalfRed + ";background:none");
        Assert.Null(resetBoth.Background);
    }

    [Theory]
    [InlineData("linear-gradient(red,blue),")]
    [InlineData("linear-gradient(red,blue),bogus")]
    [InlineData("none linear-gradient(red,blue)")]
    public void BackgroundImageList_RejectsInvalidLayerWithoutDroppingEarlierDeclaration(string invalid)
    {
        Assert.False(Css.Supports("background-image", invalid));
        var border = new Border();
        Css.SetStyle(border, "background-image:" + HalfRed + ";background-image:" + invalid);
        Assert.IsType<LinearGradientBrush>(border.Background);
    }

    [Fact]
    public void BackgroundSizeList_CyclesAcrossImagesIncludingNone()
    {
        const string images = "url('first.png'),none,url('third.png'),url('fourth.png')";
        Assert.True(Css.Supports("background-size", "contain,cover"));
        var border = new Border();
        Css.SetStyle(border, "background-image:" + images + ";background-size:contain,cover");

        var first = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        var firstImage = Assert.IsType<ImageBrush>(first.Top);
        var third = Assert.IsType<CssLayeredBackgroundBrush>(first.Bottom);
        var thirdImage = Assert.IsType<ImageBrush>(third.Top);
        var fourthImage = Assert.IsType<ImageBrush>(third.Bottom);
        Assert.Equal(Stretch.Uniform, firstImage.Stretch);
        Assert.Equal(Stretch.Uniform, thirdImage.Stretch);
        Assert.Equal(Stretch.UniformToFill, fourthImage.Stretch);

        var area = new Rect(0, 0, 80, 40);
        var contained = TileBrushHelper.ComputeTilePlacements(firstImage, area, 10, 20);
        Assert.Equal(4, contained.Count);
        Assert.Equal(new Rect(0, 0, 20, 40), contained[0].ImageDestRect);
        Assert.Equal(new Rect(60, 0, 20, 40), contained[^1].ImageDestRect);
        var covered = TileBrushHelper.ComputeTilePlacements(fourthImage, area, 10, 20);
        Assert.Single(covered);
        Assert.Equal(new Rect(0, 0, 80, 160), covered[0].ImageDestRect);
    }

    [Fact]
    public void BackgroundSizeList_TruncatesExcessValuesAndRejectsInvalidMembers()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('first.png');background-size:contain,cover");
        Assert.Equal(Stretch.Uniform, Assert.IsType<ImageBrush>(border.Background).Stretch);

        Assert.False(Css.Supports("background-size", "cover,bogus"));
        Assert.False(Css.Supports("background-size", "cover,"));
        var fallback = new Border();
        Css.SetStyle(fallback,
            "background-image:url('first.png');background-size:contain;background-size:cover,bogus");
        Assert.Equal(Stretch.Uniform, Assert.IsType<ImageBrush>(fallback.Background).Stretch);
    }

    [Theory]
    [InlineData("10px", 10, 5)]
    [InlineData("auto 20px", 40, 20)]
    [InlineData("30px auto", 30, 15)]
    [InlineData("50%", 40, 20)]
    [InlineData("50% 25%", 40, 10)]
    [InlineData("calc(50% - 10px) 10px", 30, 10)]
    [InlineData("100% 100%", 80, 40)]
    public void BackgroundSize_ResolvesLengthsPercentagesAndAutoRatio(
        string value, double width, double height)
    {
        Assert.True(Css.Supports("background-size", value));
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-repeat:no-repeat;" +
            "background-size:" + value);
        var image = Assert.IsType<ImageBrush>(border.Background);
        var expected = new Rect(0, 0, width, height);
        Assert.Equal(expected, image.CssBackgroundLayout!.ImageRect(new Rect(0, 0, 80, 40), 20, 10));
        Assert.Equal(expected, Assert.Single(TileBrushHelper.ComputeTilePlacements(
            image, new Rect(0, 0, 80, 40), 20, 10)).ImageDestRect);
    }

    [Theory]
    [InlineData("-1px")]
    [InlineData("10px -1%")]
    [InlineData("10")]
    [InlineData("cover 10px")]
    [InlineData("10px 20px 30px")]
    [InlineData("10px,")]
    [InlineData("10px,invalid")]
    public void BackgroundSize_RejectsInvalidLiteralAndList(string value)
        => Assert.False(Css.Supports("background-size", value));

    [Fact]
    public void BackgroundSize_CalculatedNegativeClampsToZeroAndSuppressesImage()
    {
        Assert.True(Css.Supports("background-size", "calc(-10px) 10px"));
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:calc(-10px) 10px");
        var image = Assert.IsType<ImageBrush>(border.Background);
        Assert.True(image.CssBackgroundLayout!.ImageRect(new Rect(0, 0, 80, 40), 20, 10).IsEmpty);
        Assert.Empty(TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 80, 40), 20, 10));
    }

    [Fact]
    public void BackgroundSize_CyclesAcrossNoneAndShorthandResets()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('first.png'),none,url('third.png'),url('fourth.png');" +
            "background-size:10px auto,25% 50%;background-repeat:no-repeat");
        var first = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        var third = Assert.IsType<CssLayeredBackgroundBrush>(first.Bottom);
        var area = new Rect(0, 0, 80, 40);
        Assert.Equal(new Rect(0, 0, 10, 5),
            Assert.IsType<ImageBrush>(first.Top).CssBackgroundLayout!.ImageRect(area, 20, 10));
        Assert.Equal(new Rect(0, 0, 10, 5),
            Assert.IsType<ImageBrush>(third.Top).CssBackgroundLayout!.ImageRect(area, 20, 10));
        Assert.Equal(new Rect(0, 0, 20, 20),
            Assert.IsType<ImageBrush>(third.Bottom).CssBackgroundLayout!.ImageRect(area, 20, 10));

        var shorthand = new Border();
        Css.SetStyle(shorthand, "background:url('tile.png') center / 30px 20px no-repeat");
        Assert.Equal(new Rect(25, 10, 30, 20),
            Assert.IsType<ImageBrush>(shorthand.Background).CssBackgroundLayout!.ImageRect(area, 20, 10));

        var reset = new Border();
        Css.SetStyle(reset,
            "background-size:30px 20px;background:url('tile.png') no-repeat");
        Assert.Equal(new Rect(0, 0, 20, 10),
            Assert.IsType<ImageBrush>(reset.Background).CssBackgroundLayout!.ImageRect(area, 20, 10));
    }

    [Fact]
    public void BackgroundSize_UsesElementFontSize()
    {
        var border = new Border();
        const string background = "background-image:url('tile.png');background-size:2em auto";
        Css.SetStyle(border, "font-size:20px;" + background);
        Assert.Equal(new Rect(0, 0, 40, 20),
            Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!
                .ImageRect(new Rect(0, 0, 80, 40), 20, 10));
        Css.SetStyle(border, "font-size:10px;" + background);
        Assert.Equal(new Rect(0, 0, 20, 10),
            Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!
                .ImageRect(new Rect(0, 0, 80, 40), 20, 10));
    }

    [Fact]
    public void CssExplicitBackgroundSize_PaintsOnlyTheScaledBitmapBounds()
    {
        using var source = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border();
        Css.SetStyle(border,
            "background:blue url('tile.png') right 1px bottom 1px / 4px 2px no-repeat");
        CssBorderRadiusTests.Layout(border, 8, 4);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = source;
        var pixels = CssBorderRadiusTests.Pixels(border, 8, 4);
        Assert.Equal((byte)255, Channel(pixels, 8, 4, 1, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 6, 2, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 2, 1, 0));
        Assert.Equal((byte)255, Channel(pixels, 8, 4, 0, 0));
    }

    [Fact]
    public void BackgroundShorthand_AppliesSizesPerLayerAndResetsOmittedSize()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background:url('first.png') left top / cover," +
            "url('second.png') right bottom / contain");
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.Equal(Stretch.UniformToFill, Assert.IsType<ImageBrush>(layers.Top).Stretch);
        Assert.Equal(Stretch.Uniform, Assert.IsType<ImageBrush>(layers.Bottom).Stretch);

        var reset = new Border();
        Css.SetStyle(reset,
            "background-image:url('first.png');background-size:cover;background:url('second.png')");
        Assert.Equal(Stretch.None, Assert.IsType<ImageBrush>(reset.Background).Stretch);

        var auto = new Border();
        Css.SetStyle(auto,
            "background-image:url('first.png');background-size:cover;background-size:auto");
        Assert.Equal(Stretch.None, Assert.IsType<ImageBrush>(auto.Background).Stretch);

        var longhandWins = new Border();
        Css.SetStyle(longhandWins,
            "background:url('first.png') center / cover;background-size:contain");
        Assert.Equal(Stretch.Uniform,
            Assert.IsType<ImageBrush>(longhandWins.Background).Stretch);
    }

    [Fact]
    public void BackgroundImage_DefaultAutoUsesIntrinsicSizeAndRepeatsFromTopLeft()
    {
        var border = new Border();
        Css.SetStyle(border, "background-image:url('tile.png')");
        var image = Assert.IsType<ImageBrush>(border.Background);
        Assert.Equal(Stretch.None, image.Stretch);
        var layout = Assert.IsType<CssBackgroundImageLayout>(image.CssBackgroundLayout);
        Assert.Equal(CssBackgroundSizeMode.Auto, layout.Size);
        Assert.True(layout.RepeatX && layout.RepeatY);

        var placements = TileBrushHelper.ComputeTilePlacements(image,
            new Rect(0, 0, 8, 4), 2, 2);
        Assert.Equal(8, placements.Count);
        Assert.Equal(new Rect(0, 0, 2, 2), placements[0].ImageDestRect);
        Assert.Equal(new Rect(6, 2, 2, 2), placements[^1].ImageDestRect);
    }

    [Fact]
    public void BackgroundRepeatList_CyclesThroughNoneAndShorthandResets()
    {
        Assert.True(Css.Supports("background-repeat", "repeat no-repeat,repeat-y"));
        Assert.False(Css.Supports("background-repeat", "repeat,invalid"));
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('first.png'),none,url('third.png'),url('fourth.png');" +
            "background-repeat:no-repeat,repeat-x");
        var first = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        var third = Assert.IsType<CssLayeredBackgroundBrush>(first.Bottom);
        var firstLayout = Assert.IsType<CssBackgroundImageLayout>(
            Assert.IsType<ImageBrush>(first.Top).CssBackgroundLayout);
        var thirdLayout = Assert.IsType<CssBackgroundImageLayout>(
            Assert.IsType<ImageBrush>(third.Top).CssBackgroundLayout);
        var fourthLayout = Assert.IsType<CssBackgroundImageLayout>(
            Assert.IsType<ImageBrush>(third.Bottom).CssBackgroundLayout);
        Assert.False(firstLayout.RepeatX || firstLayout.RepeatY);
        Assert.False(thirdLayout.RepeatX || thirdLayout.RepeatY);
        Assert.True(fourthLayout.RepeatX);
        Assert.False(fourthLayout.RepeatY);

        var shorthand = new Border();
        Css.SetStyle(shorthand,
            "background:url('first.png') no-repeat,url('second.png') repeat-x");
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(shorthand.Background);
        Assert.False(Assert.IsType<ImageBrush>(layers.Top).CssBackgroundLayout!.RepeatX);
        Assert.True(Assert.IsType<ImageBrush>(layers.Bottom).CssBackgroundLayout!.RepeatX);

        var reset = new Border();
        Css.SetStyle(reset,
            "background-image:url('first.png');background-repeat:no-repeat;background:url('second.png')");
        var resetLayout = Assert.IsType<ImageBrush>(reset.Background).CssBackgroundLayout!;
        Assert.True(resetLayout.RepeatX && resetLayout.RepeatY);
    }

    [Fact]
    public void BackgroundRepeat_AcceptsSpaceRoundAndAxisPairs()
    {
        foreach (var value in new[] { "space", "round", "space round", "round no-repeat",
                     "repeat space", "no-repeat round" })
            Assert.True(Css.Supports("background-repeat", value));
        foreach (var value in new[] { "space round repeat", "repeat-x round", "space,invalid" })
            Assert.False(Css.Supports("background-repeat", value));

        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('first.png'),none,url('third.png');" +
            "background-repeat:space round,round no-repeat");
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        var third = Assert.IsType<ImageBrush>(layers.Bottom);
        Assert.Equal(new CssBackgroundRepeat(CssBackgroundRepeatMode.Space, CssBackgroundRepeatMode.Round),
            Assert.IsType<ImageBrush>(layers.Top).CssBackgroundLayout!.Repeat);
        Assert.Equal(new CssBackgroundRepeat(CssBackgroundRepeatMode.Space, CssBackgroundRepeatMode.Round),
            third.CssBackgroundLayout!.Repeat);

        var shorthand = new Border();
        Css.SetStyle(shorthand, "background:url('tile.png') center / 20px 10px round space");
        Assert.Equal(new CssBackgroundRepeat(CssBackgroundRepeatMode.Round, CssBackgroundRepeatMode.Space),
            Assert.IsType<ImageBrush>(shorthand.Background).CssBackgroundLayout!.Repeat);

        var reset = new Border();
        Css.SetStyle(reset,
            "background-repeat:space;background:url('tile.png') no-repeat");
        Assert.Equal(new CssBackgroundRepeat(false, false),
            Assert.IsType<ImageBrush>(reset.Background).CssBackgroundLayout!.Repeat);
    }

    [Fact]
    public void BackgroundRepeat_SpaceDistributesTilesAndIgnoresPositionWhenTwoFit()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-repeat:space no-repeat;" +
            "background-position:right center");
        var image = Assert.IsType<ImageBrush>(border.Background);
        var area = new Rect(0, 0, 100, 40);
        var tiles = TileBrushHelper.ComputeTilePlacements(image, area, 30, 10);
        Assert.Equal(3, tiles.Count);
        Assert.Equal(new Rect(0, 15, 30, 10), tiles[0].ImageDestRect);
        Assert.Equal(new Rect(35, 15, 30, 10), tiles[1].ImageDestRect);
        Assert.Equal(new Rect(70, 15, 30, 10), tiles[2].ImageDestRect);

        var single = TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 50, 40), 30, 10);
        Assert.Equal(new Rect(20, 15, 30, 10), Assert.Single(single).ImageDestRect);

        var oversized = TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 20, 40), 30, 10);
        Assert.Equal(new Rect(-10, 15, 30, 10), Assert.Single(oversized).ImageDestRect);

        var extended = TileBrushHelper.ComputeTilePlacements(image, area, 30, 10,
            new Rect(-100, 0, 300, 40));
        Assert.Equal(9, extended.Count);
        Assert.Equal(new Rect(-100, 15, 30, 10), extended[0].ImageDestRect);
        Assert.Equal(new Rect(100, 15, 30, 10), extended[6].ImageDestRect);
    }

    [Fact]
    public void BackgroundRepeat_RoundResizesTilesAndRestoresAutoAspectRatio()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:30px auto;" +
            "background-repeat:round no-repeat");
        var image = Assert.IsType<ImageBrush>(border.Background);
        var area = new Rect(0, 0, 100, 40);
        var expectedWidth = 100d / 3;
        var expectedHeight = expectedWidth / 3;
        var tiles = TileBrushHelper.ComputeTilePlacements(image, area, 30, 10);
        Assert.Equal(3, tiles.Count);
        Assert.Equal(new Rect(0, 0, expectedWidth, expectedHeight), tiles[0].ImageDestRect);
        Assert.Equal(new Rect(expectedWidth, 0, expectedWidth, expectedHeight), tiles[1].ImageDestRect);
        Assert.Equal(new Rect(2 * expectedWidth, 0, expectedWidth, expectedHeight), tiles[2].ImageDestRect);

        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:auto 13px;" +
            "background-repeat:no-repeat round");
        image = Assert.IsType<ImageBrush>(border.Background);
        var vertical = TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 80, 40), 20, 10);
        Assert.Equal(3, vertical.Count);
        Assert.Equal(new Rect(0, 0, 80d / 3, 40d / 3), vertical[0].ImageDestRect);
        Assert.Equal(new Rect(0, 40d / 3, 80d / 3, 40d / 3), vertical[1].ImageDestRect);

        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:30px 13px;" +
            "background-repeat:round round");
        image = Assert.IsType<ImageBrush>(border.Background);
        var bothRounded = TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 100, 40), 30, 10);
        Assert.Equal(9, bothRounded.Count);
        Assert.Equal(new Rect(200d / 3, 80d / 3, 100d / 3, 40d / 3), bothRounded[8].ImageDestRect);

        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:30px 17px;" +
            "background-repeat:round no-repeat");
        image = Assert.IsType<ImageBrush>(border.Background);
        Assert.Equal(17, image.CssBackgroundLayout!.ImageRect(area, 30, 10).Height);
    }

    [Fact]
    public void CssSpaceRepeat_PaintsTilesAndLeavesGapsAtBackgroundColor()
    {
        using var source = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border();
        Css.SetStyle(border,
            "background:blue url('tile.png') right bottom / 3px 2px space no-repeat");
        CssBorderRadiusTests.Layout(border, 11, 4);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = source;
        var pixels = CssBorderRadiusTests.Pixels(border, 11, 4);
        Assert.Equal((byte)255, Channel(pixels, 11, 1, 2, 2));
        Assert.Equal((byte)255, Channel(pixels, 11, 3, 2, 0));
        Assert.Equal((byte)255, Channel(pixels, 11, 5, 2, 2));
        Assert.Equal((byte)255, Channel(pixels, 11, 7, 2, 0));
        Assert.Equal((byte)255, Channel(pixels, 11, 9, 2, 2));
        Assert.Equal((byte)255, Channel(pixels, 11, 5, 0, 0));
    }

    [Fact]
    public void BackgroundBoxes_ParseListsAndShorthandResets()
    {
        Assert.True(Css.Supports("background-origin", "border-box, content-box"));
        Assert.True(Css.Supports("background-clip", "padding-box, border-box"));
        Assert.True(Css.Supports("background", "url('tile.png') padding-box content-box"));
        Assert.False(Css.Supports("background-origin", "border-box padding-box"));
        Assert.False(Css.Supports("background-clip", "text"));
        Assert.False(Css.Supports("background", "url('tile.png') border-box padding-box content-box"));

        var border = new Border();
        Css.SetStyle(border,
            "background-color:blue;background-image:url('a.png'),none,url('c.png');" +
            "background-origin:content-box,border-box;background-clip:padding-box,content-box");
        var state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(CssBackgroundBox.Content, state.Layers[0].Origin);
        Assert.Equal(CssBackgroundBox.Border, state.Layers[1].Origin);
        Assert.Equal(CssBackgroundBox.Content, state.Layers[2].Origin);
        Assert.Equal(CssBackgroundBox.Padding, state.Layers[2].Clip);
        Assert.Equal(CssBackgroundBox.Padding, state.ColorClip);

        Css.SetStyle(border, "background:blue url('tile.png') padding-box content-box");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(CssBackgroundBox.Padding, state.Layers[0].Origin);
        Assert.Equal(CssBackgroundBox.Content, state.Layers[0].Clip);
        Assert.Equal(CssBackgroundBox.Content, state.ColorClip);

        Css.SetStyle(border,
            "background-origin:content-box;background-clip:content-box;" +
            "background:blue url('tile.png') no-repeat");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(CssBackgroundBox.Padding, state.Layers[0].Origin);
        Assert.Equal(CssBackgroundBox.Border, state.Layers[0].Clip);

        Css.SetStyle(border,
            "background-origin:content-box;background-clip:content-box;" +
            "background:initial");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(CssBackgroundBox.Padding, state.Origins[0]);
        Assert.Equal(CssBackgroundBox.Border, state.Clips[0]);
        Assert.Equal(CssBackgroundRepeat.Both, state.Repeats[0]);
        Assert.Equal(CssBackgroundSizeMode.Auto, state.Sizes[0].Mode);

        Css.SetStyle(border,
            "background-origin:content-box;background-clip:content-box;" +
            "background-origin:unset;background-clip:initial");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(CssBackgroundBox.Padding, state.Origins[0]);
        Assert.Equal(CssBackgroundBox.Border, state.Clips[0]);

        var child = new Border();
        var parent = new Border { Child = child };
        Css.SetStyle(parent,
            "background-origin:content-box,border-box;background-clip:padding-box,content-box");
        Css.SetStyle(child, "background-origin:inherit;background-clip:inherit");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(child));
        Assert.Equal(new[] { CssBackgroundBox.Content, CssBackgroundBox.Border }, state.Origins);
        Assert.Equal(new[] { CssBackgroundBox.Padding, CssBackgroundBox.Content }, state.Clips);

        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        Css.SetStyle(parent,
            "background:blue url('tile.png') left top / 2px 2px no-repeat content-box padding-box");
        var parentLayers = Assert.IsType<CssLayeredBackgroundBrush>(parent.Background);
        Assert.IsType<ImageBrush>(parentLayers.Top).ImageSource = red;
        Css.SetStyle(child, "background:inherit");
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(child));
        Assert.NotNull(state.Color);
        Assert.Equal(CssBackgroundBox.Content, state.Origins[0]);
        Assert.Equal(CssBackgroundBox.Padding, state.Clips[0]);
        Assert.Equal(new CssBackgroundRepeat(false, false), state.Repeats[0]);
        Assert.Equal(2, Assert.IsType<ImageBrush>(state.Layers[0].Image)
            .CssBackgroundLayout!.ImageRect(new Rect(0, 0, 20, 20), 1, 1).Width);

        Css.SetStyle(parent,
            "background:blue url('tile.png') left top / 3px 3px no-repeat border-box content-box");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        state = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(child));
        Assert.Equal(CssBackgroundBox.Border, state.Origins[0]);
        Assert.Equal(CssBackgroundBox.Content, state.Clips[0]);
        Assert.Equal(3, Assert.IsType<ImageBrush>(state.Layers[0].Image)
            .CssBackgroundLayout!.ImageRect(new Rect(0, 0, 20, 20), 1, 1).Width);
    }

    [Fact]
    public void BorderBackgroundOrigin_UsesCurrentBorderAndPaddingForBitmapPosition()
    {
        using var source = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(3) };
        Css.SetStyle(border,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:2px 2px;background-repeat:no-repeat;background-origin:content-box");

        byte[] Draw()
        {
            CssBorderRadiusTests.Layout(border, 20, 20);
            var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
            Assert.IsType<ImageBrush>(layers.Top).ImageSource = source;
            return CssBorderRadiusTests.Pixels(border, 20, 20);
        }

        var pixels = Draw();
        Assert.Equal((byte)255, Channel(pixels, 20, 0, 0, 0));
        Assert.Equal((byte)255, Channel(pixels, 20, 5, 5, 2));
        Assert.Equal((byte)255, Channel(pixels, 20, 2, 2, 0));

        border.Padding = new Thickness(5);
        pixels = Draw();
        Assert.Equal((byte)255, Channel(pixels, 20, 5, 5, 0));
        Assert.Equal((byte)255, Channel(pixels, 20, 7, 7, 2));

        Css.SetStyle(border,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:2px 2px;background-repeat:no-repeat;background-origin:border-box");
        pixels = Draw();
        Assert.Equal((byte)255, Channel(pixels, 20, 0, 0, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectControlBackground_UsesCurrentContentBoxForEachLayer(bool textBox)
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        Control control = textBox ? new TextBox() : new Label();
        control.BorderThickness = new Thickness(2);
        control.Padding = new Thickness(3);
        Css.SetStyle(control,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:2px 2px;background-repeat:no-repeat;" +
            "background-origin:content-box;background-clip:content-box");

        byte[] Draw()
        {
            CssBorderRadiusTests.Layout(control, 20, 20);
            var layers = Assert.IsType<CssLayeredBackgroundBrush>(control.Background);
            Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
            return CssBorderRadiusTests.Pixels(control, 20, 20);
        }

        var pixels = Draw();
        Assert.Equal((byte)0, Channel(pixels, 20, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 5, 5, 2));
        Assert.Equal((byte)255, Channel(pixels, 20, 7, 7, 0));

        control.Padding = new Thickness(5);
        pixels = Draw();
        Assert.Equal((byte)0, Channel(pixels, 20, 5, 5, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 7, 7, 2));

        control.Background = Brushes.Green;
        pixels = CssBorderRadiusTests.Pixels(control, 20, 20);
        Assert.Equal((byte)128, Channel(pixels, 20, 10, 10, 1));
    }

    [Fact]
    public void GradientBackground_UsesPositioningBoxSizePositionAndClip()
    {
        var border = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(3) };
        Css.SetStyle(border,
            "background-color:blue;" +
            "background-image:linear-gradient(to right, red, lime);" +
            "background-size:10px 6px;background-position:right 2px bottom 2px;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:padding-box");
        CssBorderRadiusTests.Layout(border, 30, 20);
        var pixels = CssBorderRadiusTests.Pixels(border, 30, 20);

        Assert.Equal((byte)0, Channel(pixels, 30, 1, 10, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 3, 10, 0));
        Assert.Equal((byte)255, Channel(pixels, 30, 11, 9, 0));
        Assert.True(Channel(pixels, 30, 14, 9, 2) > 180);
        Assert.True(Channel(pixels, 30, 21, 9, 1) > 180);
        Assert.Equal((byte)255, Channel(pixels, 30, 24, 9, 0));

        border.Padding = new Thickness(5);
        pixels = CssBorderRadiusTests.Pixels(border, 30, 20);
        Assert.True(Channel(pixels, 30, 12, 9, 2) > 180);
    }

    [Fact]
    public void GradientBackground_RepeatCanPaintOutsidePositioningBox()
    {
        var border = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(3) };
        Css.SetStyle(border,
            "background-image:linear-gradient(red,red);" +
            "background-size:7px 5px;background-origin:content-box");
        CssBorderRadiusTests.Layout(border, 30, 20);
        var pixels = CssBorderRadiusTests.Pixels(border, 30, 20);
        Assert.Equal((byte)255, Channel(pixels, 30, 0, 0, 2));

        Css.SetStyle(border,
            "background-image:linear-gradient(red,red);" +
            "background-size:7px 5px;background-origin:content-box;" +
            "background-repeat:no-repeat");
        pixels = CssBorderRadiusTests.Pixels(border, 30, 20);
        Assert.Equal((byte)0, Channel(pixels, 30, 0, 0, 3));
        Assert.Equal((byte)255, Channel(pixels, 30, 5, 5, 2));
    }

    [Fact]
    public void GradientBackground_AutoAxisHasNoIntrinsicAspectRatio()
    {
        var layout = new CssBackgroundImageLayout(
            new CssBackgroundSize(new CssLength(10, CssUnit.Px), null),
            new CssBackgroundRepeat(false, false));
        Assert.Equal(new Rect(5, 6, 10, 20),
            layout.GradientTilePattern(new Rect(5, 6, 40, 20)).ImageRect);
    }

    [Fact]
    public void GridBackground_UsesCssLayoutPaddingForOriginAndClip()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var grid = new Grid();

        byte[] Draw(int padding)
        {
            Css.SetStyle(grid,
                $"display:grid;padding:{padding}px;background-color:blue;" +
                "background-image:url('tile.png');background-size:2px 2px;" +
                "background-repeat:no-repeat;background-origin:content-box;" +
                "background-clip:content-box");
            CssBorderRadiusTests.Layout(grid, 20, 20);
            var layers = Assert.IsType<CssLayeredBackgroundBrush>(grid.Background);
            Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
            return CssBorderRadiusTests.Pixels(grid, 20, 20);
        }

        var pixels = Draw(4);
        Assert.Equal((byte)0, Channel(pixels, 20, 3, 3, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 4, 4, 2));
        Assert.Equal((byte)255, Channel(pixels, 20, 7, 7, 0));

        pixels = Draw(6);
        Assert.Equal((byte)0, Channel(pixels, 20, 4, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 6, 6, 2));

        grid.Background = Brushes.Green;
        pixels = CssBorderRadiusTests.Pixels(grid, 20, 20);
        Assert.Equal((byte)128, Channel(pixels, 20, 4, 4, 1));
    }

    [Fact]
    public void TextBlockBackground_UsesItsEffectivePaddingForOriginAndClip()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var text = new TextBlock();

        byte[] Draw(int padding)
        {
            Css.SetStyle(text,
                $"padding:{padding}px;background-color:blue;" +
                "background-image:url('tile.png');background-size:2px 2px;" +
                "background-repeat:no-repeat;background-origin:content-box;" +
                "background-clip:content-box");
            CssBorderRadiusTests.Layout(text, 20, 20);
            var layers = Assert.IsType<CssLayeredBackgroundBrush>(text.Background);
            Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
            return CssBorderRadiusTests.Pixels(text, 20, 20);
        }

        var pixels = Draw(4);
        Assert.Equal((byte)0, Channel(pixels, 20, 3, 3, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 4, 4, 2));
        Assert.Equal((byte)255, Channel(pixels, 20, 7, 7, 0));

        pixels = Draw(6);
        Assert.Equal((byte)0, Channel(pixels, 20, 4, 4, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 6, 6, 2));
    }

    [Theory]
    [InlineData("ScrollViewer")]
    [InlineData("RichTextBox")]
    [InlineData("TabControl")]
    public void DirectRectangleControlBackground_ClipsToCurrentContentBox(string type)
    {
        Control control = type switch
        {
            "ScrollViewer" => new ScrollViewer(),
            "RichTextBox" => new RichTextBox(),
            _ => new TabControl
            {
                TabStripBackground = Brushes.Transparent,
                TabStripBorderBrush = Brushes.Transparent,
            },
        };
        control.BorderThickness = new Thickness(2);
        control.Padding = new Thickness(3);
        Css.SetStyle(control, "background-color:blue;background-clip:content-box");
        CssBorderRadiusTests.Layout(control, 40, 40);

        var pixels = CssBorderRadiusTests.Pixels(control, 40, 40);
        Assert.Equal((byte)0, Channel(pixels, 40, 2, 2, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 10, 10, 0));

        control.Padding = new Thickness(10);
        pixels = CssBorderRadiusTests.Pixels(control, 40, 40);
        Assert.Equal((byte)0, Channel(pixels, 40, 10, 10, 3));
        Assert.Equal((byte)255, Channel(pixels, 40, 15, 15, 0));

        control.Background = Brushes.Green;
        pixels = CssBorderRadiusTests.Pixels(control, 40, 40);
        Assert.Equal((byte)128, Channel(pixels, 40, 15, 15, 1));
    }

    [Fact]
    public void BorderBackgroundClip_ClipsEachImageAndColorToItsOwnBox()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        using var green = BitmapImage.FromPixels([0, 255, 0, 255], 1, 1);
        var border = new Border { BorderThickness = new Thickness(2), Padding = new Thickness(3) };
        Css.SetStyle(border,
            "background-color:blue;background-image:url('red.png'),url('green.png');" +
            "background-clip:content-box,padding-box");
        CssBorderRadiusTests.Layout(border, 20, 20);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
        var lower = Assert.IsType<CssLayeredBackgroundBrush>(layers.Bottom);
        Assert.IsType<ImageBrush>(lower.Top).ImageSource = green;
        var pixels = CssBorderRadiusTests.Pixels(border, 20, 20);
        Assert.Equal((byte)0, Channel(pixels, 20, 0, 0, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 3, 3, 1));
        Assert.Equal((byte)255, Channel(pixels, 20, 6, 6, 2));

        Css.SetStyle(border, "background-color:blue;background-image:none,none;" +
                             "background-clip:border-box,content-box");
        pixels = CssBorderRadiusTests.Pixels(border, 20, 20);
        Assert.Equal((byte)0, Channel(pixels, 20, 0, 0, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 6, 6, 0));
    }

    [Fact]
    public void BorderBackgroundClip_UsesInsetEllipticalCorner()
    {
        var border = new Border { BorderThickness = new Thickness(4), Padding = new Thickness(6) };
        Css.SetStyle(border,
            "border-radius:20px / 18px;background-color:blue;background-clip:content-box");
        CssBorderRadiusTests.Layout(border, 80, 40);
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 40);
        Assert.Equal((byte)0, Channel(pixels, 80, 11, 11, 3));
        Assert.Equal((byte)255, Channel(pixels, 80, 20, 11, 0));
    }

    [Fact]
    public void BorderBackgroundClip_FollowsTemplateBackgroundWithoutOverridingLocalBrush()
    {
        var owner = new Border();
        Css.SetStyle(owner, "background-color:blue;background-clip:content-box");
        var part = new Border
        {
            BorderThickness = new Thickness(2),
            Padding = new Thickness(3),
        };
        part.SetTemplatedParent(owner);
        part.SetLayerValue(Border.BackgroundProperty, owner.Background,
            DependencyObject.LayerValueSource.ParentTemplate);
        CssBorderRadiusTests.Layout(part, 20, 20);
        var pixels = CssBorderRadiusTests.Pixels(part, 20, 20);
        Assert.Equal((byte)0, Channel(pixels, 20, 0, 0, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 6, 6, 0));

        part.Background = Brushes.Green;
        pixels = CssBorderRadiusTests.Pixels(part, 20, 20);
        Assert.Equal((byte)128, Channel(pixels, 20, 6, 6, 1));

        part.Background = owner.Background;
        pixels = CssBorderRadiusTests.Pixels(part, 20, 20);
        Assert.Equal((byte)255, Channel(pixels, 20, 2, 2, 0));
    }

    [Fact]
    public void BorderBackgroundOrigin_SpacePatternExtendsIntoBorderBox()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border { BorderThickness = new Thickness(4) };
        Css.SetStyle(border,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:20px 10px;background-repeat:space no-repeat");
        CssBorderRadiusTests.Layout(border, 80, 40);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
        var pixels = CssBorderRadiusTests.Pixels(border, 80, 40);
        Assert.Equal((byte)255, Channel(pixels, 80, 0, 5, 2));
        Assert.Equal((byte)255, Channel(pixels, 80, 26, 5, 0));
        Assert.Equal((byte)255, Channel(pixels, 80, 77, 5, 2));
    }

    [Fact]
    public void BorderBackgroundOrigin_ZeroSizedPositioningBoxStillPlacesExplicitImage()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border { BorderThickness = new Thickness(4), Padding = new Thickness(6) };
        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:2px 2px;" +
            "background-repeat:no-repeat;background-origin:content-box");
        CssBorderRadiusTests.Layout(border, 20, 20);
        Assert.IsType<ImageBrush>(border.Background).ImageSource = red;
        var pixels = CssBorderRadiusTests.Pixels(border, 20, 20);
        Assert.Equal((byte)0, Channel(pixels, 20, 5, 5, 3));
        Assert.Equal((byte)255, Channel(pixels, 20, 10, 10, 2));
    }

    [Fact]
    public void BorderBackgroundBoxes_PreserveMutableLayeredBrushOpacity()
    {
        using var red = BitmapImage.FromPixels([0, 0, 255, 255], 1, 1);
        var border = new Border();
        Css.SetStyle(border,
            "background:blue url('tile.png') left top / 20px 20px no-repeat");
        CssBorderRadiusTests.Layout(border, 20, 20);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = red;
        layers.Opacity = 0.5;
        var pixels = CssBorderRadiusTests.Pixels(border, 20, 20);
        Assert.InRange(Channel(pixels, 20, 10, 10, 3), (byte)126, (byte)130);
    }

    [Theory]
    [InlineData("right bottom", 60, 30)]
    [InlineData("bottom right", 60, 30)]
    [InlineData("center", 30, 15)]
    [InlineData("left", 0, 15)]
    [InlineData("top", 30, 0)]
    [InlineData("25% 50%", 15, 15)]
    [InlineData("right 10px bottom 5px", 50, 25)]
    [InlineData("bottom 5px right 10px", 50, 25)]
    [InlineData("center bottom 5px", 30, 25)]
    [InlineData("left 10px top", 10, 0)]
    [InlineData("10px bottom", 10, 30)]
    [InlineData("right 10px", 60, 10)]
    [InlineData("calc(50% - 5px) 25%", 25, 7.5)]
    [InlineData("right calc(10% + 2px) bottom 2px", 52, 28)]
    public void BackgroundPosition_ResolvesAgainstRemainingSpace(string value, double x, double y)
    {
        Assert.True(Css.Supports("background-position", value));
        var border = new Border();
        Css.SetStyle(border, "background-image:url('tile.png');background-repeat:no-repeat;" +
            "background-position:" + value);
        var image = Assert.IsType<ImageBrush>(border.Background);
        var layout = Assert.IsType<CssBackgroundImageLayout>(image.CssBackgroundLayout);
        Assert.Equal(new Rect(x, y, 20, 10), layout.ImageRect(new Rect(0, 0, 80, 40), 20, 10));
        var placements = TileBrushHelper.ComputeTilePlacements(image, new Rect(0, 0, 80, 40), 20, 10);
        Assert.Single(placements);
        Assert.Equal(new Rect(x, y, 20, 10), placements[0].ImageDestRect);
    }

    [Theory]
    [InlineData("right left")]
    [InlineData("top bottom")]
    [InlineData("top 10px")]
    [InlineData("10px left")]
    [InlineData("left 10px 20px")]
    [InlineData("left 10px top 5px bottom")]
    [InlineData("20")]
    [InlineData("center center center")]
    [InlineData("left top,")]
    public void BackgroundPosition_RejectsInvalidSyntax(string value)
        => Assert.False(Css.Supports("background-position", value));

    [Fact]
    public void BackgroundPosition_CyclesAcrossNoneAndShorthandResets()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('first.png'),none,url('third.png'),url('fourth.png');" +
            "background-position:right bottom,10px 5px; background-repeat:no-repeat");
        var first = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        var third = Assert.IsType<CssLayeredBackgroundBrush>(first.Bottom);
        var area = new Rect(0, 0, 80, 40);
        Assert.Equal(new Rect(60, 30, 20, 10),
            Assert.IsType<ImageBrush>(first.Top).CssBackgroundLayout!.ImageRect(area, 20, 10));
        Assert.Equal(new Rect(60, 30, 20, 10),
            Assert.IsType<ImageBrush>(third.Top).CssBackgroundLayout!.ImageRect(area, 20, 10));
        Assert.Equal(new Rect(10, 5, 20, 10),
            Assert.IsType<ImageBrush>(third.Bottom).CssBackgroundLayout!.ImageRect(area, 20, 10));

        var shorthand = new Border();
        Css.SetStyle(shorthand,
            "background:url('tile.png') right 10px bottom 5px / auto no-repeat");
        Assert.Equal(new Rect(50, 25, 20, 10),
            Assert.IsType<ImageBrush>(shorthand.Background).CssBackgroundLayout!.ImageRect(area, 20, 10));

        var reset = new Border();
        Css.SetStyle(reset,
            "background-position:right bottom;background:url('tile.png') no-repeat");
        Assert.Equal(new Rect(0, 0, 20, 10),
            Assert.IsType<ImageBrush>(reset.Background).CssBackgroundLayout!.ImageRect(area, 20, 10));
    }

    [Fact]
    public void BackgroundPosition_CentersOversizedImageUsingNegativeFreeSpace()
    {
        var border = new Border();
        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:cover;" +
            "background-repeat:no-repeat;background-position:center");
        var layout = Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!;
        Assert.Equal(new Rect(0, -60, 80, 160),
            layout.ImageRect(new Rect(0, 0, 80, 40), 10, 20));

        Css.SetStyle(border,
            "background-image:url('tile.png');background-size:cover;" +
            "background-repeat:no-repeat;background-position:center calc(50% + 5px)");
        layout = Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!;
        Assert.Equal(new Rect(0, -55, 80, 160),
            layout.ImageRect(new Rect(0, 0, 80, 40), 10, 20));
    }

    [Fact]
    public void BackgroundPosition_UsesElementFontSize()
    {
        var border = new Border();
        const string background = "background-image:url('tile.png');" +
            "background-position:1em 0;background-repeat:no-repeat";
        Css.SetStyle(border, "font-size:20px;" + background);
        Assert.Equal(new Rect(20, 0, 20, 10),
            Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!
                .ImageRect(new Rect(0, 0, 80, 40), 20, 10));
        Css.SetStyle(border, "font-size:10px;" + background);
        Assert.Equal(new Rect(10, 0, 20, 10),
            Assert.IsType<ImageBrush>(border.Background).CssBackgroundLayout!
                .ImageRect(new Rect(0, 0, 80, 40), 20, 10));
    }

    [Fact]
    public void CssShorthandPosition_PaintsBitmapOverColorAtEdgeOffset()
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 0, 255, 0, 255], 2, 1);
        var border = new Border();
        Css.SetStyle(border,
            "background:blue url('tile.png') right 1px bottom 1px / auto no-repeat");
        CssBorderRadiusTests.Layout(border, 8, 4);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = source;

        var pixels = CssBorderRadiusTests.Pixels(border, 8, 4);
        Assert.Equal((byte)255, Channel(pixels, 8, 4, 2, 0));
        Assert.Equal((byte)255, Channel(pixels, 8, 5, 2, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 6, 2, 1));
        Assert.Equal((byte)255, Channel(pixels, 8, 5, 1, 0));
    }

    [Fact]
    public void ManagedBitmap_CssAutoRepeatsAndNoRepeatShowsLowerColor()
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 0, 255, 0, 255], 2, 1);
        var repeat = new Border { Background = new ImageBrush(source)
        {
            CssBackgroundLayout = new CssBackgroundImageLayout(
                CssBackgroundSizeMode.Auto, CssBackgroundRepeat.Both),
        } };
        CssBorderRadiusTests.Layout(repeat, 8, 4);
        var repeated = CssBorderRadiusTests.Pixels(repeat, 8, 4);
        Assert.Equal((byte)255, Channel(repeated, 8, 0, 3, 2));
        Assert.Equal((byte)255, Channel(repeated, 8, 1, 3, 1));
        Assert.Equal((byte)255, Channel(repeated, 8, 2, 3, 2));

        var noRepeat = new Border { Background = new CssLayeredBackgroundBrush(
            Brushes.Blue, new ImageBrush(source)
            {
                CssBackgroundLayout = new CssBackgroundImageLayout(
                    CssBackgroundSizeMode.Auto, new CssBackgroundRepeat(false, false)),
            }) };
        CssBorderRadiusTests.Layout(noRepeat, 8, 4);
        var pixels = CssBorderRadiusTests.Pixels(noRepeat, 8, 4);
        Assert.Equal((byte)255, Channel(pixels, 8, 0, 0, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 1, 0, 1));
        Assert.Equal((byte)255, Channel(pixels, 8, 2, 0, 0));
    }

    [Fact]
    public void CssRepeatX_UsesIntrinsicImageSizeAndLeavesOtherRowsAtBackgroundColor()
    {
        using var source = BitmapImage.FromPixels(
            [0, 0, 255, 255, 0, 255, 0, 255], 2, 1);
        var border = new Border();
        Css.SetStyle(border,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:auto;background-repeat:repeat-x");
        CssBorderRadiusTests.Layout(border, 8, 4);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.IsType<ImageBrush>(layers.Top).ImageSource = source;

        var pixels = CssBorderRadiusTests.Pixels(border, 8, 4);
        Assert.Equal((byte)255, Channel(pixels, 8, 0, 0, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 1, 0, 1));
        Assert.Equal((byte)255, Channel(pixels, 8, 2, 0, 2));
        Assert.Equal((byte)255, Channel(pixels, 8, 0, 3, 0));
        Assert.Equal((byte)0, Channel(pixels, 8, 0, 3, 2));

        var single = new Border();
        Css.SetStyle(single,
            "background-color:blue;background-image:url('tile.png');" +
            "background-size:auto;background-repeat:no-repeat");
        CssBorderRadiusTests.Layout(single, 8, 4);
        var singleLayers = Assert.IsType<CssLayeredBackgroundBrush>(single.Background);
        Assert.IsType<ImageBrush>(singleLayers.Top).ImageSource = source;
        var singlePixels = CssBorderRadiusTests.Pixels(single, 8, 4);
        Assert.Equal((byte)255, Channel(singlePixels, 8, 0, 0, 2));
        Assert.Equal((byte)255, Channel(singlePixels, 8, 2, 0, 0));
        Assert.Equal((byte)0, Channel(singlePixels, 8, 2, 0, 2));
    }

    [Theory]
    [InlineData("url('first.png')/cover")]
    [InlineData("url('first.png') center / bogus")]
    [InlineData("cover")]
    public void BackgroundShorthand_RejectsInvalidSizeSyntax(string shorthand)
        => Assert.False(Css.Supports("background", shorthand));

    [Fact]
    public void ManagedBitmap_CompositesImageOverColorInsideEllipticalBounds()
    {
        var border = new Border();
        Css.SetStyle(border,
            "border-radius:20px / 10px;background-color:blue;" +
            "background-image:linear-gradient(rgb(255 0 0 / .5),rgb(255 0 0 / .5))");
        CssBorderRadiusTests.Layout(border, 80, 40);
        var layers = Assert.IsType<CssLayeredBackgroundBrush>(border.Background);
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<SolidColorBrush>(layers.Bottom).Color.ToArgb());
        Assert.IsType<LinearGradientBrush>(layers.Top);
        var clone = Assert.IsType<CssLayeredBackgroundBrush>(layers.Clone());
        Assert.NotSame(layers.Top, clone.Top);

        var pixels = CssBorderRadiusTests.Pixels(border, 80, 40);
        Assert.InRange(Channel(pixels, 80, 40, 20, 2), (byte)125, (byte)130);
        Assert.InRange(Channel(pixels, 80, 40, 20, 0), (byte)125, (byte)130);
        Assert.Equal((byte)0, Channel(pixels, 80, 0, 0, 3));
    }

    [Fact]
    public void CurrentColorUnderImage_RecompositesAfterForegroundChanges()
    {
        const string background =
            "background-color:currentColor;" +
            "background-image:linear-gradient(rgb(255 0 0 / .5),rgb(255 0 0 / .5))";
        var text = new TextBlock();
        Css.SetStyle(text, "color:blue;" + background);
        CssBorderRadiusTests.Layout(text, 40, 20);
        var blue = CssBorderRadiusTests.Pixels(text, 40, 20);
        Assert.InRange(Channel(blue, 40, 20, 10, 0), (byte)125, (byte)130);

        Css.SetStyle(text, "color:green;" + background);
        CssBorderRadiusTests.Layout(text, 40, 20);
        var green = CssBorderRadiusTests.Pixels(text, 40, 20);
        Assert.InRange(Channel(green, 40, 20, 10, 1), (byte)62, (byte)66);
        Assert.True(Channel(green, 40, 20, 10, 0) < 3);
    }

    [Fact]
    public void MutableNativeForeground_UpdatesTheColorBelowTheImage()
    {
        var foreground = new SolidColorBrush(Colors.Blue);
        var text = new TextBlock { Foreground = foreground };
        Css.SetStyle(text,
            "background-color:currentColor;" +
            "background-image:linear-gradient(rgb(255 0 0 / .5),rgb(255 0 0 / .5))");
        CssBorderRadiusTests.Layout(text, 40, 20);
        var blue = CssBorderRadiusTests.Pixels(text, 40, 20);
        Assert.InRange(Channel(blue, 40, 20, 10, 0), (byte)125, (byte)130);

        foreground.Color = Colors.Green;
        CssBorderRadiusTests.Layout(text, 40, 20);
        var green = CssBorderRadiusTests.Pixels(text, 40, 20);
        Assert.InRange(Channel(green, 40, 20, 10, 1), (byte)62, (byte)66);
        Assert.True(Channel(green, 40, 20, 10, 0) < 3);
    }

    [Fact]
    public async Task WholeFrameRecorder_SnapshotsBothBackgroundLayers()
    {
        var gradient = new LinearGradientBrush(
            Color.FromArgb(128, 255, 0, 0), Color.FromArgb(128, 255, 0, 0), 0);
        var layers = new CssLayeredBackgroundBrush(Brushes.Blue, gradient);
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawRectangle(layers, null, new Rect(0, 0, 10, 10));
        var recorded = recorder.Commit();
        Assert.True(recorded.FullyRecordable);

        gradient.GradientStops[0].Color = Color.FromArgb(128, 0, 255, 0);
        gradient.GradientStops[1].Color = Color.FromArgb(128, 0, 255, 0);

        var pixels = await Task.Run(() =>
        {
            var bitmap = new RenderTargetBitmap(10, 10, 96, 96, PixelFormat.Bgra32);
            var drawing = new SoftwareDrawingContext(bitmap);
            DrawingReplayer.Replay(recorded, drawing);
            drawing.Close();
            return bitmap.GetPixelBuffer();
        });
        Assert.InRange(Channel(pixels, 10, 5, 5, 2), (byte)125, (byte)130);
        Assert.InRange(Channel(pixels, 10, 5, 5, 0), (byte)125, (byte)130);
        Assert.True(Channel(pixels, 10, 5, 5, 1) < 3);
    }

    [Fact]
    public void WholeFrameRecorder_DetectsMutableImageInsideLayeredBackground()
    {
        var source = new WriteableBitmap(1, 1, 96, 96, PixelFormat.Bgra32, null);
        var brush = new CssLayeredBackgroundBrush(Brushes.Blue, new ImageBrush(source));
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawRectangle(brush, null, new Rect(0, 0, 10, 10));
        Assert.False(recorder.Commit().FullyRecordable);
    }
}
