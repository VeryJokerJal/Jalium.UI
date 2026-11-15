using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssRelativeColorTests
{
    private static Color Parse(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.True(CssColorParser.TryParse(ref reader, out var color, out var current));
        Assert.False(current);
        Assert.True(reader.AtEnd);
        return color;
    }

    [Theory]
    [InlineData("rgb(from indianred 255 g b)", 255, 92, 92, 255)]
    [InlineData("rgb(from red \\72  g b)", 255, 0, 0, 255)]
    [InlineData("rgb(from rgb(51 102 153 / .8) r g b)", 51, 102, 153, 204)]
    [InlineData("rgb(from red calc(r / 2) g calc(30%))", 128, 0, 77, 255)]
    [InlineData("rgb(from rgb(0 0 0 / 60%) alpha 153 153 / .9)", 1, 153, 153, 230)]
    [InlineData("hsl(from red calc(h + 120) s l)", 0, 255, 0, 255)]
    [InlineData("hwb(from red h w b)", 255, 0, 0, 255)]
    [InlineData("color(from red srgb r g b / .5)", 255, 0, 0, 128)]
    [InlineData("color(from color(srgb 0 0 0 / 60%) srgb alpha .6 .6 / .9)", 153, 153, 153, 230)]
    [InlineData("alpha(from red / .5)", 255, 0, 0, 128)]
    [InlineData("rgb(from rgb(from blue r g b) r g b)", 0, 0, 255, 255)]
    public void Color5RelativeExamples_ResolveTheirNativeChannels(string css,
        int red, int green, int blue, int alpha)
    {
        var result = Parse(css);
        Assert.InRange(Math.Abs(result.R - red), 0, 1);
        Assert.InRange(Math.Abs(result.G - green), 0, 1);
        Assert.InRange(Math.Abs(result.B - blue), 0, 1);
        Assert.InRange(Math.Abs(result.A - alpha), 0, 1);
    }

    [Theory]
    [InlineData("lab", "lab(50 20 30)", "l a b")]
    [InlineData("lch", "lch(50 40 120)", "l c h")]
    [InlineData("oklab", "oklab(.6 .1 .05)", "l a b")]
    [InlineData("oklch", "oklch(.6 .1 120)", "l c h")]
    [InlineData("color", "color(display-p3 .7 .3 .2)", "display-p3 r g b")]
    public void RelativeColor_PreservesOriginAcrossSupportedSpaces(
        string function, string origin, string channels)
    {
        var direct = Parse(origin);
        var relative = Parse($"{function}(from {origin} {channels})");
        Assert.InRange(Math.Abs(direct.R - relative.R), 0, 1);
        Assert.InRange(Math.Abs(direct.G - relative.G), 0, 1);
        Assert.InRange(Math.Abs(direct.B - relative.B), 0, 1);
    }

    [Theory]
    [InlineData("srgb", "r g b")]
    [InlineData("srgb-linear", "r g b")]
    [InlineData("display-p3", "r g b")]
    [InlineData("display-p3-linear", "r g b")]
    [InlineData("a98-rgb", "r g b")]
    [InlineData("prophoto-rgb", "r g b")]
    [InlineData("rec2020", "r g b")]
    [InlineData("xyz", "x y z")]
    [InlineData("xyz-d65", "x y z")]
    [InlineData("xyz-d50", "x y z")]
    public void PredefinedRelativeSpaces_ConvertTheOriginBeforeReadingChannels(
        string space, string channels)
    {
        var color = Parse($"color(from indianred {space} {channels})");
        var direct = Parse("indianred");
        Assert.InRange(Math.Abs(color.R - direct.R), 0, 1);
        Assert.InRange(Math.Abs(color.G - direct.G), 0, 1);
        Assert.InRange(Math.Abs(color.B - direct.B), 0, 1);
    }

    [Fact]
    public void RelativeColor_RetainsMissingAndOutOfGamutSourceChannels()
    {
        var reader = new CssTokenReader("rgb(from rgb(none 100 0) r g b)");
        Assert.True(CssColorParser.TryParseWithData(ref reader, out _, out _, out var missing));
        Assert.Equal(1, missing.Missing & 1);
        Assert.False(missing.Legacy);

        reader = new CssTokenReader("color(from color(display-p3 1 1 0) srgb r g b)");
        Assert.True(CssColorParser.TryParseWithData(ref reader, out _, out _, out var wide));
        Assert.True(wide.Z < 0);

        reader = new CssTokenReader("hsl(from hsl(none 10% 50%) h s l)");
        Assert.True(CssColorParser.TryParseWithData(ref reader, out _, out _, out var hue));
        Assert.Equal(1, hue.Missing & 1);

        reader = new CssTokenReader("hsl(from hsl(none 10% 50%) calc(h + 30) s l)");
        Assert.True(CssColorParser.TryParseWithData(ref reader, out _, out _, out var calculated));
        Assert.Equal(0, calculated.Missing & 1);
        Assert.InRange(calculated.X, 29.9, 30.1);

        reader = new CssTokenReader("rgb(from rgb(10 20 30 / none) r g b)");
        Assert.True(CssColorParser.TryParseWithData(ref reader, out _, out _, out var alpha));
        Assert.Equal(8, alpha.Missing & 8);
        Assert.Equal(0, alpha.Alpha);
    }

    [Fact]
    public void RelativeColors_ParticipateInColorMixAndGradientStops()
    {
        Assert.Equal(0xFF800080u,
            Parse("color-mix(in srgb, rgb(from red r g b), blue)").ToArgb());
        var box = new Border();
        Css.SetStyle(box, "background-image:linear-gradient(in srgb," +
            "rgb(from red r g b),rgb(from blue r g b))");
        var gradient = Assert.IsType<LinearGradientBrush>(box.Background);
        Assert.Equal(0xFFFF0000u, gradient.GradientStops[0].Color.ToArgb());
        Assert.Equal(0xFF0000FFu, gradient.GradientStops[^1].Color.ToArgb());

        Css.SetStyle(box, "color:blue;background-image:linear-gradient(" +
            "rgb(from currentColor r g b),white)");
        CssBorderRadiusTests.Layout(box, 80, 40);
        gradient = Assert.IsType<LinearGradientBrush>(
            Assert.IsType<LinearGradientBrush>(box.Background).CssGradientLayout!.Resolve(80, 40));
        Assert.Equal(0xFF0000FFu, gradient.GradientStops[0].Color.ToArgb());
        Css.SetStyle(box, "color:red;background-image:linear-gradient(" +
            "rgb(from currentColor r g b),white)");
        CssBorderRadiusTests.Layout(box, 80, 40);
        gradient = Assert.IsType<LinearGradientBrush>(
            Assert.IsType<LinearGradientBrush>(box.Background).CssGradientLayout!.Resolve(80, 40));
        Assert.Equal(0xFFFF0000u, gradient.GradientStops[0].Color.ToArgb());
    }

    [Fact]
    public void CurrentColorRelativeColor_ReevaluatesWithTheElement()
    {
        const string relative = "rgb(from currentColor r g calc(b / 2))";
        Assert.True(Css.Supports("background-color", relative));
        var box = new Border();
        Css.SetStyle(box, "color:blue;background-color:" + relative);
        CssBorderRadiusTests.Layout(box, 80, 40);
        Assert.Equal(0xFF000080u,
            Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());

        Css.SetStyle(box, "color:red;background-color:" + relative);
        CssBorderRadiusTests.Layout(box, 80, 40);
        Assert.Equal(0xFFFF0000u,
            Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());

        Css.SetStyle(box,
            "--accent:blue;background-color:rgb(from var(--accent) r g b)");
        CssBorderRadiusTests.Layout(box, 80, 40);
        Assert.Equal(0xFF0000FFu,
            Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());
    }

    [Fact]
    public void InheritedRelativeCurrentColor_UsesTheChildsWinningColor()
    {
        var child = new Border();
        var parent = new Border { Child = child };
        Css.SetStyle(parent,
            "color:red;background-color:rgb(from currentColor r g b)");
        Css.SetStyle(child, "color:blue;background-color:inherit");
        CssBorderRadiusTests.Layout(parent, 80, 40);
        Assert.Equal(0xFFFF0000u,
            Assert.IsType<SolidColorBrush>(parent.Background).Color.ToArgb());
        Assert.Equal(0xFF0000FFu,
            Assert.IsType<SolidColorBrush>(child.Background).Color.ToArgb());
    }

    [Fact]
    public void BorderAndOutlineRelativeCurrentColor_TrackForegroundChanges()
    {
        const string decorations =
            "border:2px solid rgb(from currentColor r g b);" +
            "outline:2px solid alpha(from currentColor / .5)";
        var button = new Button();
        Css.SetStyle(button, "color:blue;" + decorations);
        CssBorderRadiusTests.Layout(button, 80, 40);
        Assert.Equal(0xFF0000FFu,
            Assert.IsType<SolidColorBrush>(button.BorderBrush).Color.ToArgb());
        Assert.Equal(0x800000FFu,
            Assert.IsType<SolidColorBrush>(button.OutlineBrush).Color.ToArgb());

        Css.SetStyle(button, "color:red;" + decorations);
        CssBorderRadiusTests.Layout(button, 80, 40);
        Assert.Equal(0xFFFF0000u,
            Assert.IsType<SolidColorBrush>(button.BorderBrush).Color.ToArgb());
        Assert.Equal(0x80FF0000u,
            Assert.IsType<SolidColorBrush>(button.OutlineBrush).Color.ToArgb());
    }

    [Theory]
    [InlineData("rgb(from red 1, 2, 3)")]
    [InlineData("rgb(from red r q b)")]
    [InlineData("rgb(from red r g)")]
    [InlineData("color(from red unknown r g b)")]
    [InlineData("hsl(from red h s l / 2px)")]
    public void InvalidRelativeForms_AreRejected(string css)
        => Assert.False(Css.Supports("background-color", css));
}
