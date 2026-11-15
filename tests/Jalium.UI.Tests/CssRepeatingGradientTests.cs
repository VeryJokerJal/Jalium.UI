using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRepeatingGradientTests
{
    private static GradientBrush Resolve(string css, double width, double height)
    {
        var box = new Border(); Css.SetStyle(box, "background-image:" + css);
        var source = Assert.IsAssignableFrom<GradientBrush>(box.Background);
        Assert.NotNull(source.CssGradientLayout);
        return Assert.IsAssignableFrom<GradientBrush>(source.CssGradientLayout.Resolve(width, height));
    }

    private static Color At(GradientBrush brush, double position)
    {
        var stops = brush.GradientStops;
        for (var i = 0; i < stops.Count - 1; i++)
        {
            var left = stops[i]; var right = stops[i + 1];
            if (position > right.Offset) continue;
            if (Math.Abs(position - right.Offset) < 1e-9) return right.Color;
            if (right.Offset <= left.Offset) continue;
            var t = (float)((position - left.Offset) / (right.Offset - left.Offset));
            return Color.FromScRgb(left.Color.ScA + (right.Color.ScA - left.Color.ScA) * t,
                left.Color.ScR + (right.Color.ScR - left.Color.ScR) * t,
                left.Color.ScG + (right.Color.ScG - left.Color.ScG) * t,
                left.Color.ScB + (right.Color.ScB - left.Color.ScB) * t);
        }
        return stops[^1].Color;
    }

    [Fact]
    public void LinearRepeat_UsesTheAuthoredPeriodAcrossThePaintedWidth()
    {
        const string css = "repeating-linear-gradient(to right, red 0px, blue 20px)";
        Assert.True(Css.Supports("background-image", css));
        var brush = Resolve(css, 100, 20);
        Assert.Equal(GradientSpreadMethod.Pad, brush.SpreadMethod); // finite used-size expansion
        Assert.True(brush.GradientStops.Count > 10);
        var first = At(brush, .05); var repeated = At(brush, .25);
        Assert.InRange(Math.Abs(first.R - repeated.R), 0, 2);
        Assert.InRange(Math.Abs(first.B - repeated.B), 0, 2);
        var beforeSeam = At(brush, .19); var afterSeam = At(brush, .21);
        Assert.True(beforeSeam.B > 220 && afterSeam.R > 220);
        var wider = Resolve(css, 200, 20);
        Assert.InRange(Math.Abs(At(wider, .025).R - At(wider, .125).R), 0, 2);
    }

    [Fact]
    public void RepeatPhase_IsAnchoredAtTheFirstAndLastSourceStops()
    {
        var brush = Resolve("repeating-linear-gradient(to right, red 10px, blue 50px)", 200, 20);
        var firstMiddle = At(brush, .15); // x=30, between the 10px and 50px stops
        var nextMiddle = At(brush, .35); // x=70, same phase in the next 40px period
        Assert.InRange(Math.Abs(firstMiddle.R - nextMiddle.R), 0, 2);
        Assert.InRange(Math.Abs(firstMiddle.B - nextMiddle.B), 0, 2);
    }

    [Fact]
    public void RadialRepeat_CoversCornersBeyondTheEndingShape()
    {
        var brush = Assert.IsType<RadialGradientBrush>(Resolve(
            "repeating-radial-gradient(circle closest-side, red 0px, blue 20px)", 100, 100));
        var radius = brush.RadiusX * 100;
        Assert.True(radius >= Math.Sqrt(50 * 50 + 50 * 50) - .01);
        Assert.True(brush.GradientStops[^1].Offset >= 1);
    }

    [Fact]
    public void ZeroAndSubpixelPeriods_UseTheSpecifiedAverageColor()
    {
        var zero = Resolve("repeating-linear-gradient(red 0px, white 0px, blue 0px)", 100, 20);
        var average = At(zero, .5);
        Assert.InRange(average.R, (byte)190, (byte)192);
        Assert.InRange(average.G, (byte)127, (byte)129);
        Assert.InRange(average.B, (byte)190, (byte)192);
        var tiny = Resolve("repeating-linear-gradient(red 0px, blue .1px)", 100, 20);
        var tinyAverage = At(tiny, .5);
        Assert.InRange(tinyAverage.R, (byte)127, (byte)129);
        Assert.Equal((byte)0, tinyAverage.G);
        Assert.InRange(tinyAverage.B, (byte)127, (byte)129);
    }

    [Fact]
    public void ZeroPeriodAverage_PremultipliesTransparentStops()
    {
        var brush = Resolve("repeating-linear-gradient(transparent 0px, red 0px)", 100, 20);
        var average = At(brush, .5);
        Assert.InRange(average.A, (byte)127, (byte)129);
        Assert.Equal((byte)255, average.R);
        Assert.Equal((byte)0, average.G);
        Assert.Equal((byte)0, average.B);
    }

    [Theory]
    [InlineData("repeating-radial-gradient(circle -10px, red, blue)")]
    [InlineData("repeating-linear-gradient(red, 25%, 30%, blue)")]
    public void RepeatingForms_KeepTheSameInvalidGrammar(string css)
        => Assert.False(Css.Supports("background-image", css));
}
