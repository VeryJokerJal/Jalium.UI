using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssColorMixTests
{
    private static Color Parse(string css)
    {
        var reader = new CssTokenReader(css);
        Assert.True(CssColorParser.TryParse(ref reader, out var color, out var current));
        Assert.False(current);
        Assert.True(reader.AtEnd);
        return color;
    }

    [Fact]
    public void DefaultOklabAndExplicitSrgb_UseDifferentInterpolationSpaces()
    {
        var srgb = Parse("color-mix(in srgb, white, black)");
        Assert.InRange(srgb.R, (byte)127, (byte)129);
        Assert.Equal(srgb.R, srgb.G);
        Assert.Equal(srgb.R, srgb.B);
        var oklab = Parse("color-mix(white, black)");
        Assert.InRange(oklab.R, (byte)98, (byte)101);
        Assert.Equal(oklab.R, oklab.G);
        Assert.Equal(oklab.R, oklab.B);
        Assert.Equal(oklab.ToArgb(), Parse("color-mix(in oklab, white, black)").ToArgb());
    }

    [Theory]
    [InlineData("color-mix(in srgb, red, blue)", 128, 0, 128, 255)]
    [InlineData("color-mix(in srgb, red 25%, blue)", 64, 0, 191, 255)]
    [InlineData("color-mix(in srgb, 25% red, blue)", 64, 0, 191, 255)]
    [InlineData("color-mix(in srgb, red calc(25%), blue)", 64, 0, 191, 255)]
    [InlineData("color-mix(in srgb, red 70%, blue 70%)", 128, 0, 128, 255)]
    [InlineData("color-mix(in srgb, red 30%, blue 30%)", 128, 0, 128, 153)]
    [InlineData("color-mix(in srgb, red, lime, blue)", 85, 85, 85, 255)]
    [InlineData("color-mix(in srgb, red 40%)", 255, 0, 0, 102)]
    [InlineData("color-mix(in srgb, red /*, ignored */ 25%, blue)", 64, 0, 191, 255)]
    [InlineData("color-mix(in srgb, red 0%, blue 0%)", 0, 0, 0, 0)]
    public void WptColorMixWeights_ReachExpectedNativeChannels(string css,
        int red, int green, int blue, int alpha)
    {
        var result = Parse(css);
        Assert.InRange(Math.Abs(result.R - red), 0, 1);
        Assert.InRange(Math.Abs(result.G - green), 0, 1);
        Assert.InRange(Math.Abs(result.B - blue), 0, 1);
        Assert.InRange(Math.Abs(result.A - alpha), 0, 1);
    }

    [Fact]
    public void HslHueMethod_ControlsThePathAcrossTheColorWheel()
    {
        var shorter = Parse("color-mix(in hsl shorter hue, red, blue)");
        var increasing = Parse("color-mix(in hsl increasing hue, red, blue)");
        Assert.True(shorter.R > 250 && shorter.B > 250 && shorter.G < 5);
        Assert.True(increasing.G > 250 && increasing.R < 5 && increasing.B < 5);
    }

    [Theory]
    [InlineData("srgb")]
    [InlineData("srgb-linear")]
    [InlineData("display-p3")]
    [InlineData("display-p3-linear")]
    [InlineData("a98-rgb")]
    [InlineData("prophoto-rgb")]
    [InlineData("rec2020")]
    [InlineData("xyz")]
    [InlineData("xyz-d65")]
    [InlineData("xyz-d50")]
    [InlineData("lab")]
    [InlineData("lch")]
    [InlineData("oklab")]
    [InlineData("oklch")]
    [InlineData("hsl")]
    [InlineData("hwb")]
    public void EverySupportedInterpolationSpace_ParsesInColorMix(string space)
        => Assert.True(Css.Supports("background-color", $"color-mix(in {space}, red, blue)"));

    [Fact]
    public void NestedMixes_RetainSourceSpaceAndMissingChannels()
    {
        var nested = Parse("color-mix(in srgb, color-mix(in srgb, red, blue), white)");
        Assert.InRange(nested.R, (byte)190, (byte)193);
        Assert.InRange(nested.G, (byte)127, (byte)129);
        Assert.InRange(nested.B, (byte)190, (byte)193);

        var carried = Parse("color-mix(in srgb, rgb(none 0 0), rgb(none 0 0), red)");
        Assert.Equal(0xFFFF0000u, carried.ToArgb());

        var wide = Parse("color-mix(in display-p3, color-mix(in display-p3, color(display-p3 1 1 0)), black)");
        var direct = Parse("color-mix(in display-p3, color(display-p3 1 1 0), black)");
        Assert.InRange(Math.Abs(wide.R - direct.R), 0, 1);
        Assert.InRange(Math.Abs(wide.G - direct.G), 0, 1);
        Assert.InRange(Math.Abs(wide.B - direct.B), 0, 1);
    }

    [Fact]
    public void AlphaPremultiplication_AndPercentageMultiplier_AreSeparateSteps()
    {
        var color = Parse("color-mix(in srgb, rgb(255 0 0 / .7) 20%, rgb(0 255 0 / .2) 60%)");
        Assert.InRange(color.A, (byte)65, (byte)67);
        Assert.InRange(color.R, (byte)136, (byte)138);
        Assert.InRange(color.G, (byte)117, (byte)119);
        Assert.Equal((byte)0, color.B);
    }

    [Fact]
    public void Mixes_ApplyThroughCssVariablesBrushesAndGradientStops()
    {
        const string value = "color-mix(in srgb, red 25%, blue)";
        Assert.True(Css.Supports("background-color", value));
        Assert.True(Css.Supports("border-color", value));
        Assert.True(Css.Supports("(color:color-mix(in srgb, red, blue))"));
        var box = new Border();
        Css.SetStyle(box, "--accent:" + value + ";background-color:var(--accent);border:2px solid " + value);
        Assert.Equal(Parse(value).ToArgb(), Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());
        Assert.Equal(Parse(value).ToArgb(), Assert.IsType<SolidColorBrush>(box.BorderBrush).Color.ToArgb());

        Css.SetStyle(box, "--accent:red;background-color:color-mix(in srgb, var(--accent), blue)");
        Assert.Equal(Parse("color-mix(in srgb, red, blue)").ToArgb(),
            Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());

        Css.SetStyle(box, "background-image:linear-gradient(color-mix(in srgb, red, blue), white)");
        Assert.IsType<LinearGradientBrush>(box.Background);
    }

    [Theory]
    [InlineData("color-mix(in hsl, red -10%, blue)")]
    [InlineData("color-mix(in hsl, red 150%, blue)")]
    [InlineData("color-mix(in hsl hue, red, blue)")]
    [InlineData("color-mix(in hsl shorter, red, blue)")]
    [InlineData("color-mix(in srgb longer hue, red, blue)")]
    [InlineData("color-mix(in unknown, red, blue)")]
    [InlineData("color-mix(in srgb red, blue)")]
    [InlineData("color-mix(in srgb, red blue)")]
    [InlineData("color-mix(in srgb, red, blue blue)")]
    [InlineData("color-mix(in srgb, red,)")]
    public void WptInvalidColorMixForms_AreRejected(string css)
        => Assert.False(Css.Supports("background-color", css));
}
