using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssColor4Tests
{
    private static Color Parse(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.True(CssColorParser.TryParse(ref reader, out var color, out var current));
        Assert.False(current); Assert.True(reader.AtEnd);
        return color;
    }

    // These source values and the #008000 reference are retained from the named
    // WPT reference pages, with an 8-bit channel tolerance for their rounded inputs.
    [Theory]
    [InlineData("hwb(120 0% 49.8039%)")]
    [InlineData("lab(46.2775% -47.5621 48.5837)")]
    [InlineData("lch(46.2775% 67.9892 134.3912)")]
    [InlineData("oklab(51.975% -0.1403 0.10768)")]
    [InlineData("oklch(51.975% 0.17686 142.495)")]
    public void WptGreenReferences_ReachTheCssBackgroundPath(string value)
    {
        var parsed = Parse(value);
        Assert.InRange(parsed.R, (byte)0, (byte)1);
        Assert.InRange(parsed.G, (byte)127, (byte)129);
        Assert.InRange(parsed.B, (byte)0, (byte)1);
        var box = new Border(); Css.SetStyle(box, "background-color:" + value);
        var painted = Assert.IsType<SolidColorBrush>(box.Background).Color;
        Assert.Equal(parsed.R, painted.R); Assert.Equal(parsed.G, painted.G); Assert.Equal(parsed.B, painted.B);
    }

    [Fact]
    public void Hwb_NormalizesAchromaticAndSupportsAlphaAndMissingComponents()
    {
        var gray = Parse("hwb(45 40% 80% / 50%)");
        Assert.Equal((byte)128, gray.A);
        Assert.Equal((byte)85, gray.R); Assert.Equal(gray.R, gray.G); Assert.Equal(gray.R, gray.B);
        Assert.Equal((byte)0, Parse("hwb(none none 100% / none)").A);
        var mixed = Parse("hwb(150 20% 10%)");
        Assert.Equal((byte)51, mixed.R);
        Assert.InRange(mixed.G, (byte)229, (byte)230);
        Assert.InRange(mixed.B, (byte)140, (byte)141);
    }

    [Fact]
    public void LabPercentReferencesAndPolarCoordinates_Agree()
    {
        var lab = Parse("lab(50 -75 0 / 25%)");
        var polar = Parse("lch(50% 50% 180deg / .25)");
        Assert.Equal(lab.ToArgb(), polar.ToArgb());
        var ok = Parse("oklab(.5 .16 -.1)");
        var percent = Parse("oklab(50% 40% -25%)");
        Assert.Equal(ok.ToArgb(), percent.ToArgb());
        Assert.Equal(Parse("oklab(.5 0 0)").ToArgb(), Parse("oklch(50% 0 270deg)").ToArgb());
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
    [InlineData("xyz-d50")]
    [InlineData("xyz-d65")]
    public void PredefinedColorSpaces_AcceptBlackAndWhite(string space)
    {
        Assert.Equal(0xFF000000u, Parse($"color({space} 0 0 0)").ToArgb());
        if (space == "xyz-d50")
        {
            var white = Parse("color(xyz-d50 0.9642956764 1 0.8251046025)");
            Assert.InRange(white.R, (byte)254, (byte)255);
            Assert.InRange(white.G, (byte)254, (byte)255);
            Assert.InRange(white.B, (byte)254, (byte)255);
        }
        else if (space is "xyz" or "xyz-d65")
        {
            var white = Parse($"color({space} 0.9504559271 1 1.0890577508)");
            Assert.InRange(white.R, (byte)254, (byte)255);
            Assert.InRange(white.G, (byte)254, (byte)255);
            Assert.InRange(white.B, (byte)254, (byte)255);
        }
        else
        {
            var white = Parse($"color({space} 1 1 1)");
            Assert.InRange(white.R, (byte)254, (byte)255);
            Assert.InRange(white.G, (byte)254, (byte)255);
            Assert.InRange(white.B, (byte)254, (byte)255);
        }
    }

    [Fact]
    public void LinearSrgbAndP3_AreConvertedBeforeRendering()
    {
        var red = Parse("color(srgb-linear 0.21404114 0 0 / 50%)");
        Assert.Equal((byte)128, red.A);
        Assert.Equal((byte)128, red.R); Assert.Equal((byte)0, red.G); Assert.Equal((byte)0, red.B);
        var p3Gray = Parse("color(display-p3 50% 50% 50%)");
        Assert.InRange(p3Gray.R, (byte)127, (byte)129);
        Assert.Equal(p3Gray.R, p3Gray.G); Assert.Equal(p3Gray.R, p3Gray.B);
        Assert.True(CssValueParsing.TryParseColor("color(srgb 0 1 0)", out var mapped));
        Assert.Equal(0xFF00FF00u, mapped.ToArgb());
    }

    [Fact]
    public void WptPredefined001_ExplicitSrgbMatchesTheGreenReference()
        => Assert.Equal(0xFF009900u, Parse("color(srgb 0 0.6 0)").ToArgb());

    [Fact]
    public void PredefinedSpaces_ConvertTheSameInGamutReferenceColor()
    {
        // CSS Color 4's measured leaf example, with its published rounded inputs.
        var expected = Parse("color(srgb 0.41587 0.503670 0.36664)");
        foreach (var css in new[]
        {
            "color(display-p3 0.43313 0.50108 0.37950)",
            "color(a98-rgb 0.44091 0.49971 0.37408)",
            "color(prophoto-rgb 0.36589 0.41717 0.31333)",
        })
        {
            var actual = Parse(css);
            Assert.InRange(Math.Abs(actual.R - expected.R), 0, 2);
            Assert.InRange(Math.Abs(actual.G - expected.G), 0, 2);
            Assert.InRange(Math.Abs(actual.B - expected.B), 0, 2);
        }
        var encoded = Parse("color(srgb 0.691 0.139 0.259)");
        var linear = Parse("color(srgb-linear 0.435 0.017 0.055)");
        Assert.InRange(Math.Abs(encoded.R - linear.R), 0, 1);
        Assert.InRange(Math.Abs(encoded.G - linear.G), 0, 1);
        Assert.InRange(Math.Abs(encoded.B - linear.B), 0, 1);
    }

    [Fact]
    public void OutOfGamutP3Yellow_MatchesTheSpecOklchDescriptionAfterMapping()
    {
        // CSS Color 4 describes this P3 primary as oklch(.96476 .24503 110.23).
        var p3 = Parse("color(display-p3 1 1 0)");
        var polar = Parse("oklch(0.96476 0.24503 110.23)");
        Assert.InRange(Math.Abs(p3.R - polar.R), 0, 2);
        Assert.InRange(Math.Abs(p3.G - polar.G), 0, 2);
        Assert.InRange(Math.Abs(p3.B - polar.B), 0, 2);
        Assert.NotEqual(0xFFFFFF00u, p3.ToArgb());
    }

    [Fact]
    public void ModernColors_ReachSupportsVariablesAndBorderBrushes()
    {
        Assert.True(Css.Supports("background-color", "oklab(51.975% -0.1403 0.10768)"));
        Assert.True(Css.Supports("border-color", "color(display-p3 0 0.5 0)"));
        var box = new Border();
        Css.SetStyle(box, "--accent:hwb(120 0% 50%); background-color:var(--accent); " +
            "border:2px solid color(srgb 0 0.5 0)");
        Assert.Equal(0xFF008000u, Assert.IsType<SolidColorBrush>(box.Background).Color.ToArgb());
        Assert.Equal(0xFF008000u, Assert.IsType<SolidColorBrush>(box.BorderBrush).Color.ToArgb());
    }

    [Fact]
    public void ModernColors_AreAcceptedAsGradientStops()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:linear-gradient(oklch(51.975% 0.17686 142.495), color(srgb 0 0 1))");
        var brush = Assert.IsType<LinearGradientBrush>(box.Background);
        Assert.InRange(brush.GradientStops[0].Color.G, (byte)127, (byte)129);
        Assert.Equal(0xFF0000FFu, brush.GradientStops[^1].Color.ToArgb());
    }

    [Theory]
    [InlineData("hwb(120, 0%, 50%)")]
    [InlineData("hwb(120 10px 50%)")]
    [InlineData("hwb(120 0%50%)")]
    [InlineData("lab(50%, 0, 0)")]
    [InlineData("lch(50% 20 10px)")]
    [InlineData("oklab(50% .1)")]
    [InlineData("oklch(50% 20% 40 / 20% 30%)")]
    [InlineData("color(unknown 1 0 0)")]
    [InlineData("color(display-p3 1 0)")]
    [InlineData("color(srgb 1, 0, 0)")]
    [InlineData("color(srgb 1-0 0)")]
    [InlineData("color(srgb 1 0 0 / 50% / 20%)")]
    public void InvalidModernColorValues_AreRejected(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.False(CssColorParser.TryParse(ref reader, out _, out _));
        Assert.False(CssValueParsing.TryParseColor(value, out _));
    }
}
