using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssGradientInterpolationTests
{
    [Theory]
    [InlineData(false, false, 0)] [InlineData(false, false, 1)] [InlineData(false, false, 2)] [InlineData(false, false, 3)]
    [InlineData(false, true, 0)] [InlineData(false, true, 1)] [InlineData(false, true, 2)] [InlineData(false, true, 3)]
    [InlineData(true, false, 0)] [InlineData(true, false, 1)] [InlineData(true, false, 2)] [InlineData(true, false, 3)]
    [InlineData(true, true, 0)] [InlineData(true, true, 1)] [InlineData(true, true, 2)] [InlineData(true, true, 3)]
    [InlineData(false, false, 4)] [InlineData(false, true, 4)] [InlineData(true, false, 4)] [InlineData(true, true, 4)]
    public void BothModesSampleTheirRequestedColorSpace(bool linearLight, bool radial, int route)
    {
        var first = Color.FromArgb(77, 31, 123, 207);
        var last = Color.FromArgb(207, 211, 74, 16);
        var brush = Gradient(radial, first, last);
        brush.Opacity = .6;
        brush.ColorInterpolationMode = linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation;
        AssertRamp(Paint(brush, route), first, last, linearLight, radial, .6);
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    [InlineData(false, 4)] [InlineData(true, 4)]
    public void LinearLightRetainsExtendedScRgbStopsUntilAfterInterpolation(bool radial, int route)
    {
        var first = Color.FromScRgb(.77f, 1.2f, -.2f, .2f);
        var last = Color.FromScRgb(.43f, -.2f, 1.2f, .8f);
        var brush = Gradient(radial, first, last);
        brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
        AssertRamp(Paint(brush, route), first, last, true, radial, 1);
    }

    [Theory]
    [InlineData("sRGB", false)] [InlineData("linearRGB", true)] [InlineData("auto", false)]
    public void SvgGradientComputesColorInterpolationFromItsDefinition(string value, bool linearLight)
    {
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><style>svg { color-interpolation:" + value + "; } rect { color-interpolation:" + (linearLight ? "sRGB" : "linearRGB") + "; }</style><defs><linearGradient id='g'><stop stop-color='#1f7bcf'/><stop offset='1' stop-color='#d34a10'/></linearGradient></defs><rect width='160' height='48' fill='url(#g)'/></svg>");
        var brush = Assert.IsType<LinearGradientBrush>(Drawings(image.Drawing!).Select(x => x.Brush).First(x => x is GradientBrush));
        Assert.Equal(linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation, brush.ColorInterpolationMode);
        AssertRamp(Paint(Absolute(brush), 1), Color.FromRgb(31, 123, 207), Color.FromRgb(211, 74, 16), linearLight, false, 1);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)]
    public void ModeMutationInvalidatesImagesAndFrozenClonesRetainIt(int route)
    {
        var brush = Gradient(false, Colors.Red, Colors.Blue);
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><rect width='160' height='48' fill='url(#g)'/></svg>");
        var drawing = Assert.Single(Drawings(image.Drawing!)); drawing.Brush = brush;
        var generation = image.ContentGeneration;
        var before = Paint(brush, route);
        brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
        Assert.True(image.ContentGeneration > generation);
        var after = Paint(brush, route); Assert.NotEqual(before[4 * (24 * 160 + 64) + 2], after[4 * (24 * 160 + 64) + 2]);
        var clone = brush.CloneCurrentValue(); clone.Freeze();
        Assert.Equal(ColorInterpolationMode.ScRgbLinearInterpolation, clone.ColorInterpolationMode);
        Assert.Equal(after, Paint(clone, route));
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    [InlineData(false, 4)] [InlineData(true, 4)]
    public void CoincidentStopsUseTheLastColorAtTheBoundary(bool linearLight, int route)
    {
        var brush = new LinearGradientBrush { StartPoint = new(.5, 24), EndPoint = new(128.5, 24), MappingMode = BrushMappingMode.Absolute,
            ColorInterpolationMode = linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation };
        foreach (var stop in new[] { new GradientStop(Colors.Red, 0), new(Colors.Green, 0), new(Colors.Blue, .5), new(Colors.White, .5), new(Colors.Black, 1) }) brush.GradientStops.Add(stop);
        var pixels = Paint(brush, route);
        foreach (var (x, color) in new[] { (0, Colors.Green), (64, Colors.White) })
        {
            var at = (24 * 160 + x) * 4;
            Assert.Equal(new[] { color.B, color.G, color.R, color.A }, pixels[at..(at + 4)]);
        }
    }

    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    [InlineData(false, 4)] [InlineData(true, 4)]
    public void OneStopStillPaintsItsColorAndOpacity(bool linearLight, int route)
    {
        var color = Color.FromArgb(157, 47, 93, 201);
        var brush = Gradient(false, color, color); brush.GradientStops.RemoveAt(1); brush.Opacity = .5;
        brush.ColorInterpolationMode = linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation;
        var pixels = Paint(brush, route); var at = (24 * 160 + 40) * 4;
        Assert.Equal(new byte[] { color.B, color.G, color.R, 79 }, pixels[at..(at + 4)]);
    }

    [Fact]
    public void NativeCacheHashIncludesUnquantizedScRgbComponents()
    {
        var brush = Gradient(false, Color.FromScRgb(.7f, 1.2f, -.2f, .2f), Colors.Blue);
        brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
        var before = brush.ComputeContentHash(); var old = brush.GradientStops[0].Color;
        var changed = Color.FromScRgb(.7f, 1.4f, -.2f, .2f);
        Assert.Equal(old.R, changed.R);
        brush.GradientStops[0].Color = changed;
        Assert.NotEqual(before, brush.ComputeContentHash());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void GradientTemplatesKeepTheDirectlyReferencedDefinitionsOwnMode(bool linearLight, bool radial)
    {
        var tag = radial ? "radialGradient" : "linearGradient";
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><defs><" + tag + " id='base' color-interpolation='" + (linearLight ? "sRGB" : "linearRGB") + "'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></" + tag + "><" + tag + " id='g' href='#base' color-interpolation='" + (linearLight ? "linearRGB" : "sRGB") + "'/></defs><rect width='160' height='48' fill='url(#g)'/></svg>");
        var brush = Assert.IsAssignableFrom<GradientBrush>(Assert.Single(Drawings(image.Drawing!)).Brush);
        Assert.Equal(linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation, brush.ColorInterpolationMode);
        Assert.Equal(2, brush.GradientStops.Count);
    }

    [Theory]
    [InlineData("linearRGB", true)] [InlineData("sRGB", false)]
    public void SvgColorInterpolationUsesCascadeVariablesAndSupports(string value, bool linearLight)
    {
        using var image = SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><style>svg{--space:" + value + ";color-interpolation:sRGB} @supports (color-interpolation:linearRGB){ #g{color-interpolation:var(--space)} }</style><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient><rect width='160' height='48' fill='url(#g)'/></svg>");
        var brush = Assert.IsAssignableFrom<GradientBrush>(Assert.Single(Drawings(image.Drawing!)).Brush);
        Assert.Equal(linearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation, brush.ColorInterpolationMode);
    }

    private static GradientBrush Gradient(bool radial, Color first, Color last)
    {
        GradientBrush brush = radial
            ? new RadialGradientBrush { Center = new(0, 24), GradientOrigin = new(0, 24), RadiusX = 128, RadiusY = 128 }
            : new LinearGradientBrush { StartPoint = new(0, 24), EndPoint = new(128, 24) };
        brush.MappingMode = BrushMappingMode.Absolute;
        brush.GradientStops.Add(new(first, 0)); brush.GradientStops.Add(new(last, 1)); return brush;
    }

    private static LinearGradientBrush Absolute(LinearGradientBrush source)
    {
        var result = source.CloneCurrentValue(); result.MappingMode = BrushMappingMode.Absolute;
        result.StartPoint = new(0, 24); result.EndPoint = new(128, 24); return result;
    }

    private static byte[] Paint(GradientBrush brush, int route)
    {
        var bounds = new Rect(0, 0, 160, 48);
        var drawing = new GeometryDrawing(brush, null, new RectangleGeometry(bounds));
        if (route == 1) return SoftwareVectorRasterizer.Rasterize(drawing, 160, 48, bounds)!;
        var bitmap = new RenderTargetBitmap(160, 48, 96, 96, PixelFormats.Bgra32);
        var dc = new SoftwareDrawingContext(bitmap);
        if (route == 0) dc.DrawRectangle(brush, null, bounds);
        else if (route == 4)
        {
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); recorder.DrawRectangle(brush, null, bounds);
            DrawingReplayer.Replay(recorder.Commit(), dc);
        }
        else if (route == 2) dc.DrawImage(new DrawingImage(drawing), bounds);
        else dc.DrawRectangle(new ImageBrush(new DrawingImage(drawing)) { Stretch = Stretch.Fill }, null, bounds);
        dc.Close(); var bytes = new byte[160 * 48 * 4]; bitmap.CopyPixels(bytes, 160 * 4, 0); return bytes;
    }

    private static void AssertRamp(byte[] pixels, Color first, Color last, bool linearLight, bool radial, double opacity)
    {
        foreach (var x in new[] { 16, 40, 64, 88, 112 })
        {
            var t = radial ? Math.Sqrt((x + .5) * (x + .5) + .25) / 128 : (x + .5) / 128;
            double Channel(byte a, byte b, float sa, float sb) => linearLight
                ? Encode(sa + (sb - sa) * t) : (a + (b - a) * t) / 255;
            var expected = new[] { Channel(first.B, last.B, first.ScB, last.ScB), Channel(first.G, last.G, first.ScG, last.ScG), Channel(first.R, last.R, first.ScR, last.ScR), (first.ScA + (last.ScA - first.ScA) * t) * opacity };
            for (var c = 0; c < 4; c++)
            {
                var value = (byte)Math.Clamp(Math.Round(expected[c] * 255), 0, 255);
                Assert.True(Math.Abs(pixels[(24 * 160 + x) * 4 + c] - value) <= 1, $"x{x} channel{c}: actual{pixels[(24 * 160 + x) * 4 + c]} expected{value}");
            }
        }
    }

    private static double Encode(double value) => value <= .0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
    private static IEnumerable<GeometryDrawing> Drawings(Drawing drawing) => drawing is GeometryDrawing geometry ? [geometry]
        : drawing is DrawingGroup group ? group.Children.SelectMany(Drawings) : [];
}

public class CssGradientParserInterpolationTests
{
    private static LinearGradientBrush Parse(string value)
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:" + value);
        return Assert.IsType<LinearGradientBrush>(box.Background);
    }

    private static Color ParseColor(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.True(CssColorParser.TryParse(ref reader, out var color, out _));
        return color;
    }

    private static Color At(GradientBrush brush, double position)
    {
        var stops = brush.GradientStops;
        for (var i = 0; i < stops.Count - 1; i++)
        {
            var left = stops[i]; var right = stops[i + 1];
            if (position > right.Offset) continue;
            if (Math.Abs(right.Offset - position) < 1e-9) return right.Color;
            if (right.Offset <= left.Offset) continue;
            var t = (float)((position - left.Offset) / (right.Offset - left.Offset));
            return Color.FromScRgb(
                left.Color.ScA + (right.Color.ScA - left.Color.ScA) * t,
                left.Color.ScR + (right.Color.ScR - left.Color.ScR) * t,
                left.Color.ScG + (right.Color.ScG - left.Color.ScG) * t,
                left.Color.ScB + (right.Color.ScB - left.Color.ScB) * t);
        }
        return stops[^1].Color;
    }

    private static void Close(Color actual, Color expected, int tolerance = 3)
    {
        Assert.InRange(Math.Abs(actual.R - expected.R), 0, tolerance);
        Assert.InRange(Math.Abs(actual.G - expected.G), 0, tolerance);
        Assert.InRange(Math.Abs(actual.B - expected.B), 0, tolerance);
        Assert.InRange(Math.Abs(actual.A - expected.A), 0, tolerance);
    }

    [Theory]
    [InlineData("srgb", "color(srgb .2 .4 .6)", "color(srgb .6 .4 .2)", "color(srgb .4 .4 .4)")]
    [InlineData("srgb-linear", "color(srgb-linear .2 .4 .6)", "color(srgb-linear .6 .4 .2)", "color(srgb-linear .4 .4 .4)")]
    [InlineData("display-p3", "color(display-p3 .2 .4 .6)", "color(display-p3 .6 .4 .2)", "color(display-p3 .4 .4 .4)")]
    [InlineData("display-p3-linear", "color(display-p3-linear .2 .4 .6)", "color(display-p3-linear .6 .4 .2)", "color(display-p3-linear .4 .4 .4)")]
    [InlineData("a98-rgb", "color(a98-rgb .2 .4 .6)", "color(a98-rgb .6 .4 .2)", "color(a98-rgb .4 .4 .4)")]
    [InlineData("prophoto-rgb", "color(prophoto-rgb .2 .4 .6)", "color(prophoto-rgb .6 .4 .2)", "color(prophoto-rgb .4 .4 .4)")]
    [InlineData("rec2020", "color(rec2020 .2 .4 .6)", "color(rec2020 .6 .4 .2)", "color(rec2020 .4 .4 .4)")]
    [InlineData("xyz", "color(xyz .2 .4 .6)", "color(xyz .6 .4 .2)", "color(xyz .4 .4 .4)")]
    [InlineData("xyz-d50", "color(xyz-d50 .2 .4 .6)", "color(xyz-d50 .6 .4 .2)", "color(xyz-d50 .4 .4 .4)")]
    [InlineData("xyz-d65", "color(xyz-d65 .2 .4 .6)", "color(xyz-d65 .6 .4 .2)", "color(xyz-d65 .4 .4 .4)")]
    [InlineData("lab", "lab(40 20 10)", "lab(60 0 -10)", "lab(50 10 0)")]
    [InlineData("oklab", "oklab(.4 .1 .05)", "oklab(.6 -.1 -.05)", "oklab(.5 0 0)")]
    [InlineData("lch", "lch(40 20 30)", "lch(60 40 90)", "lch(50 30 60)")]
    [InlineData("oklch", "oklch(.4 .1 30)", "oklch(.6 .1 90)", "oklch(.5 .1 60)")]
    [InlineData("hsl", "hsl(0 100 50)", "hsl(120 100 50)", "hsl(60 100 50)")]
    [InlineData("hwb", "hwb(0 0 0)", "hwb(120 0 0)", "hwb(60 0 0)")]
    public void ExplicitColorSpaces_InterpolateInTheirOwnCoordinates(
        string space, string first, string second, string expected)
    {
        var brush = Parse($"linear-gradient(in {space} to right, {first}, {second})");
        Close(At(brush, .5), ParseColor(expected));
    }

    [Fact]
    public void WptHueDirectionPairs_MatchForHslAndLch()
    {
        foreach (var space in new[] { "hsl", "lch" })
        {
            var increasing = Parse($"linear-gradient(to right in {space} increasing hue, red, orange)");
            var shorter = Parse($"linear-gradient(to right in {space} shorter hue, red, orange)");
            var decreasing = Parse($"linear-gradient(to right in {space} decreasing hue, red, orange)");
            var longer = Parse($"linear-gradient(to right in {space} longer hue, red, orange)");
            Close(At(increasing, .5), At(shorter, .5), 0);
            Close(At(decreasing, .5), At(longer, .5), 0);
            Assert.NotEqual(At(shorter, .5).ToArgb(), At(longer, .5).ToArgb());
        }
    }

    [Fact]
    public void OklchHueDirections_FollowTheSpecifiedShorterAndLongerArcs()
    {
        const string start = "oklch(.6 .24 30)", end = "oklch(.8 .15 90)";
        var shorter = Parse($"linear-gradient(in oklch shorter hue, {start}, {end})");
        var longer = Parse($"linear-gradient(in oklch longer hue, {start}, {end})");
        Close(At(shorter, .5), ParseColor("oklch(.7 .195 60)"));
        Close(At(longer, .5), ParseColor("oklch(.7 .195 240)"));
    }

    [Fact]
    public void PowerlessOklchHue_UsesTheOtherStopsHue()
    {
        var brush = Parse("linear-gradient(in oklch, oklch(.5 0 0), oklch(.5 .2 120))");
        Close(At(brush, .5), ParseColor("oklch(.5 .1 120)"));
    }

    [Fact]
    public void AnalogousMissingSetsAndAlpha_CarryAcrossColorSpaces()
    {
        var lab = Parse("linear-gradient(in lch, lab(50% none none), lch(50% 50 40))");
        Close(At(lab, .5), ParseColor("lch(50% 50 40)"));

        var allRgb = Parse("linear-gradient(in oklab, color(srgb none none none), oklab(.6 .1 .05))");
        Close(At(allRgb, .5), ParseColor("oklab(.6 .1 .05)"));

        var alpha = Parse("linear-gradient(in srgb, color(srgb 0 0 0 / none), color(srgb 1 1 1 / .5))");
        Assert.Equal((byte)128, At(alpha, .5).A);
    }

    [Theory]
    [InlineData("linear-gradient(in srgb longer hue, red, blue)")]
    [InlineData("linear-gradient(in oklch longer, red, blue)")]
    [InlineData("linear-gradient(in unknown, red, blue)")]
    [InlineData("radial-gradient(in lab increasing hue, red, blue)")]
    public void InvalidInterpolationPreludes_AreRejected(string value)
        => Assert.False(Css.Supports("background-image", value));

    [Theory]
    [InlineData("linear-gradient(to right, red 0%, 25%, blue 100%)")]
    [InlineData("linear-gradient(to right, red 0, 25%, blue 100%)")]
    [InlineData("linear-gradient(to right, red 0%, calc(25%), blue 100%)")]
    [InlineData("radial-gradient(circle, red 0%, 25%, blue 100%)")]
    public void TransitionHint_PlacesTheHalfwayColorAtItsPosition(string value)
    {
        var box = new Border(); Css.SetStyle(box, "background-image:" + value);
        var brush = Assert.IsAssignableFrom<GradientBrush>(box.Background);
        Close(At(brush, .25), Color.FromRgb(128, 0, 128), 2);
        Close(At(brush, .5), Color.FromRgb(75, 0, 180), 3);
    }

    [Fact]
    public void TransitionHint_UsesTheChosenColorSpaceForItsHalfwayColor()
    {
        var brush = Parse("linear-gradient(in oklab, red 0%, 25%, lime 100%)");
        Close(At(brush, .25), Color.FromRgb(208, 169, 0), 4);
    }

    [Fact]
    public void HintAndColorStopPositions_UseTheOrderedFixup()
    {
        var brush = Parse("linear-gradient(red 0%, 80%, green, blue 100%)");
        Assert.Contains(brush.GradientStops, stop => Math.Abs(stop.Offset - .9) < 1e-9 &&
            stop.Color.G == 128 && stop.Color.R == 0 && stop.Color.B == 0);
        var doubled = Parse("linear-gradient(red 0% 20%, 30%, blue 100%)");
        Close(At(doubled, .3), Color.FromRgb(128, 0, 128), 2);

        var negative = Parse("linear-gradient(red -50%, white, blue)");
        Assert.Contains(negative.GradientStops, stop => Math.Abs(stop.Offset - .25) < 1e-9 &&
            stop.Color.ToArgb() == 0xFFFFFFFFu);
        var overconstrained = Parse("linear-gradient(red 20%, white 0%, blue 40%)");
        Assert.Contains(overconstrained.GradientStops, stop => Math.Abs(stop.Offset - .2) < 1e-9 &&
            stop.Color.ToArgb() == 0xFFFFFFFFu);
    }

    [Fact]
    public void HintAtEitherBoundary_HasTheSpecifiedLimit()
    {
        var beginning = Parse("linear-gradient(in srgb to right, red 0%, 0%, blue 100%)");
        Close(At(beginning, .25), Colors.Blue, 1);
        var zero = Parse("linear-gradient(in srgb to right, red, 0, blue)");
        Close(At(zero, .25), Colors.Blue, 1);
        var ending = Parse("linear-gradient(in srgb to right, red 0%, 100%, blue 100%)");
        Close(At(ending, .75), Colors.Red, 1);
    }

    [Theory]
    [InlineData("linear-gradient(green)")]
    [InlineData("linear-gradient(in oklch longer hue, green 35%)")]
    [InlineData("radial-gradient(circle, green)")]
    public void WptSingleStopGradient_PaintsOneSolidColor(string value)
    {
        var box = new Border(); Css.SetStyle(box, "background-image:" + value);
        var brush = Assert.IsAssignableFrom<GradientBrush>(box.Background);
        Assert.Equal(0xFF008000u, At(brush, 0).ToArgb());
        Assert.Equal(0xFF008000u, At(brush, .5).ToArgb());
        Assert.Equal(0xFF008000u, At(brush, 1).ToArgb());
    }

    [Theory]
    [InlineData("linear-gradient(25%, red, blue)")]
    [InlineData("linear-gradient(red, blue, 25%)")]
    [InlineData("linear-gradient(red, 20%, 30%, blue)")]
    [InlineData("radial-gradient(red, 25%)")]
    public void HintWithoutTwoNeighboringColors_IsInvalid(string value)
        => Assert.False(Css.Supports("background-image", value));

    [Fact]
    public void LegacyAndModernDefaults_ChooseTheirSpecifiedInterpolationSpaces()
    {
        var legacy = Parse("linear-gradient(to right, rgb(255, 0, 0), rgb(0, 255, 0))");
        var modern = Parse("linear-gradient(to right, rgb(255, 0, 0), color(srgb 0 1 0))");
        var explicitOklab = Parse("linear-gradient(in oklab to right, red, lime)");
        var explicitSrgb = Parse("linear-gradient(to right in srgb, red, color(srgb 0 1 0))");

        // The original WPT 480px references have center pixels #7f8000 in
        // legacy sRGB and #d0a900 in Oklab.
        var legacyCenter = Assert.Single(legacy.GradientStops, stop => Math.Abs(stop.Offset - .5) < 1e-9).Color;
        Assert.InRange(legacyCenter.R, (byte)126, (byte)129);
        Assert.InRange(legacyCenter.G, (byte)126, (byte)129);
        var modernCenter = Assert.Single(modern.GradientStops, stop => Math.Abs(stop.Offset - .5) < 1e-9).Color;
        Assert.InRange(modernCenter.R, (byte)205, (byte)211);
        Assert.InRange(modernCenter.G, (byte)166, (byte)172);
        Assert.Equal(modernCenter.ToArgb(), Assert.Single(explicitOklab.GradientStops,
            stop => Math.Abs(stop.Offset - .5) < 1e-9).Color.ToArgb());
        Assert.Equal(legacyCenter.ToArgb(), Assert.Single(explicitSrgb.GradientStops,
            stop => Math.Abs(stop.Offset - .5) < 1e-9).Color.ToArgb());
    }

    [Fact]
    public void SourceComponentsAndMissingChannels_SurviveUntilGradientInterpolation()
    {
        var source = new CssTokenReader("color(display-p3 1 0.5 none / none)");
        Assert.True(CssColorParser.TryParseWithData(ref source, out _, out _, out var data));
        Assert.Equal("display-p3", data.Space);
        Assert.Equal(1, data.X); Assert.Equal(.5, data.Y);
        Assert.Equal((byte)12, data.Missing);

        var rgb = Parse("linear-gradient(in srgb, rgb(none 0 0), rgb(255 0 0))");
        var midpoint = At(rgb, .5);
        Assert.Equal((byte)255, midpoint.R);
        Assert.Equal((byte)0, midpoint.G);

        var oklab = Parse("linear-gradient(in oklab, oklab(50% none .2), oklab(50% .1 .2))");
        var expected = new CssTokenReader("oklab(50% .1 .2)");
        Assert.True(CssColorParser.TryParse(ref expected, out var target, out _));
        var carried = At(oklab, .5);
        Assert.InRange(Math.Abs(carried.R - target.R), 0, 1);
        Assert.InRange(Math.Abs(carried.G - target.G), 0, 1);
        Assert.InRange(Math.Abs(carried.B - target.B), 0, 1);
    }

    [Fact]
    public void TransparentEndpoint_InterpolatesPremultipliedColor()
    {
        var brush = Parse("linear-gradient(in srgb, rgb(255 0 0 / 0), blue)");
        var middle = At(brush, .5);
        Assert.Equal((byte)128, middle.A);
        Assert.Equal((byte)0, middle.R);
        Assert.Equal((byte)0, middle.G);
        Assert.Equal((byte)255, middle.B);
        var clamped = Parse("linear-gradient(in srgb, rgba(255, 0, 0, -1), blue)");
        var clampedMiddle = At(clamped, .5);
        Assert.Equal(middle.ToArgb(), clampedMiddle.ToArgb());
    }

    [Fact]
    public void WptGradientNoneInterpolation_SplitsTheSharedStopPerNeighbor()
    {
        var brush = Parse("linear-gradient(to right in srgb, " +
            "color(srgb 0.5 0 0), color(srgb none 0.5 0.5), color(srgb 1 1 1))");
        var middle = brush.GradientStops.Where(stop => Math.Abs(stop.Offset - .5) < 1e-9)
            .Select(stop => stop.Color.ToArgb()).ToArray();
        Assert.Contains(0xFF808080u, middle);
        Assert.Contains(0xFFFF8080u, middle);

        var allMissing = Parse("linear-gradient(to right in srgb, " +
            "color(srgb none 0 0), color(srgb none 1 1))");
        var allMiddle = At(allMissing, .5);
        Assert.Equal((byte)0, allMiddle.R);
        Assert.InRange(allMiddle.G, (byte)127, (byte)129);
        Assert.InRange(allMiddle.B, (byte)127, (byte)129);

        var hue = Parse("linear-gradient(to right in oklch, " +
            "oklch(.2 .1 90), oklch(.8 .4 none), oklch(.3 .2 180))");
        var hueStops = hue.GradientStops.Where(stop => Math.Abs(stop.Offset - .5) < 1e-9)
            .Select(stop => stop.Color).ToArray();
        Assert.Contains(hueStops, color => Near(color, ParseColor("oklch(.8 .4 90)")));
        Assert.Contains(hueStops, color => Near(color, ParseColor("oklch(.8 .4 180)")));
    }

    private static bool Near(Color first, Color second) =>
        Math.Abs(first.R - second.R) <= 2 && Math.Abs(first.G - second.G) <= 2 &&
        Math.Abs(first.B - second.B) <= 2 && Math.Abs(first.A - second.A) <= 2;

    [Fact]
    public void RadialAndExplicitLinearInterpolation_AreAccepted()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:radial-gradient(circle in oklab, red, lime)");
        Assert.IsType<RadialGradientBrush>(box.Background);
        Css.SetStyle(box, "background-image:radial-gradient(circle at center in oklab, red, lime)");
        Assert.IsType<RadialGradientBrush>(box.Background);
        var linear = Parse("linear-gradient(in srgb-linear to right, red, lime)");
        Assert.Equal(0, linear.StartPoint.X);
        Assert.Equal(1, linear.EndPoint.X);
        Assert.Equal(0, linear.GradientStops[0].Offset);
        Assert.Equal(1, linear.GradientStops[^1].Offset);
    }
}
