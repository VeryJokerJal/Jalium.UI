using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssConicGradientTests
{
    private static BitmapImage Resolve(string gradient, int width, int height)
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:" + gradient);
        var source = Assert.IsType<ImageBrush>(box.Background);
        Assert.NotNull(source.CssGradientLayout);
        var resolved = Assert.IsType<ImageBrush>(source.CssGradientLayout.Resolve(width, height));
        return Assert.IsType<BitmapImage>(resolved.ImageSource);
    }

    private static Color Pixel(BitmapImage bitmap, int x, int y)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var at = (y * bitmap.PixelWidth + x) * 4;
        return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }

    [Fact]
    public void AngularStops_PaintClockwiseFromTheTop()
    {
        const string css = "conic-gradient(red 0deg, lime 90deg, blue 180deg, red 360deg)";
        Assert.True(Css.Supports("background-image", css));
        Assert.True(Css.Supports("background", css));
        var shorthand = new Border(); Css.SetStyle(shorthand, "background:" + css);
        Assert.IsType<ImageBrush>(shorthand.Background);
        var bitmap = Resolve(css, 101, 101);
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 50, 10).ToArgb());
        Assert.Equal(0xFF00FF00u, Pixel(bitmap, 90, 50).ToArgb());
        Assert.Equal(0xFF0000FFu, Pixel(bitmap, 50, 90).ToArgb());
    }

    [Fact]
    public void FromAngleAndCenter_UseTheActualPaintBox()
    {
        var rotated = Resolve("conic-gradient(from 90deg, red 0deg, blue 360deg)", 101, 101);
        Assert.Equal(0xFFFF0000u, Pixel(rotated, 90, 50).ToArgb());

        var box = new Border();
        Css.SetStyle(box, "background-image:conic-gradient(at 20px 20px, red, blue)");
        var source = Assert.IsType<ImageBrush>(box.Background);
        var first = Assert.IsType<ImageBrush>(source.CssGradientLayout!.Resolve(100, 100));
        var second = Assert.IsType<ImageBrush>(source.CssGradientLayout.Resolve(200, 100));
        Assert.Equal(100, Assert.IsType<BitmapImage>(first.ImageSource).PixelWidth);
        Assert.Equal(200, Assert.IsType<BitmapImage>(second.ImageSource).PixelWidth);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void RepeatingConic_UsesTheFirstToLastAngularPeriod()
    {
        const string css = "repeating-conic-gradient(red 0deg, blue 90deg)";
        Assert.True(Css.Supports("background-image", css));
        var bitmap = Resolve(css, 101, 101);
        var first = Pixel(bitmap, 90, 10);
        foreach (var (x, y) in new[] { (90, 90), (10, 90), (10, 10) })
        {
            var repeated = Pixel(bitmap, x, y);
            Assert.InRange(Math.Abs(first.R - repeated.R), 0, 2);
            Assert.InRange(Math.Abs(first.B - repeated.B), 0, 2);
        }
    }

    [Fact]
    public void OmittedInterpolationMethod_DefaultsToOklab()
    {
        var defaults = Resolve("conic-gradient(red 0deg, blue 360deg)", 101, 101);
        var oklab = Resolve("conic-gradient(in oklab, red 0deg, blue 360deg)", 101, 101);
        var srgb = Resolve("conic-gradient(in srgb, red 0deg, blue 360deg)", 101, 101);
        var atRight = Pixel(defaults, 90, 50);
        Assert.Equal(Pixel(oklab, 90, 50).ToArgb(), atRight.ToArgb());
        Assert.NotEqual(Pixel(srgb, 90, 50).ToArgb(), atRight.ToArgb());
    }

    [Fact]
    public void AngularTransitionHint_PlacesTheHalfwayColor()
    {
        var bitmap = Resolve("conic-gradient(red 0deg, 90deg, blue 180deg)", 101, 101);
        var reader = new CssTokenReader("color-mix(in oklab, red, blue)");
        Assert.True(CssColorParser.TryParse(ref reader, out var halfway, out _));
        var atHint = Pixel(bitmap, 90, 50);
        Assert.InRange(Math.Abs(atHint.R - halfway.R), 0, 2);
        Assert.InRange(Math.Abs(atHint.G - halfway.G), 0, 2);
        Assert.InRange(Math.Abs(atHint.B - halfway.B), 0, 2);
    }

    [Fact]
    public void WptConicGradientAngle_UsesTheOriginalFourQuadrants()
    {
        // css/css-images/conic-gradient-angle.html: original 200x200 box and value.
        var bitmap = Resolve("conic-gradient(from 90deg, red 0 25%, green 25% 50%, " +
            "blue 50% 75%, black 75% 100%)", 200, 200);
        Assert.Equal(0xFF0000FFu, Pixel(bitmap, 50, 50).ToArgb());
        Assert.Equal(0xFF000000u, Pixel(bitmap, 150, 50).ToArgb());
        Assert.Equal(0xFF008000u, Pixel(bitmap, 50, 150).ToArgb());
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 150, 150).ToArgb());
    }

    [Fact]
    public void WptConicGradientAngleNegative_UsesTheOriginalFourQuadrants()
    {
        // css/css-images/conic-gradient-angle-negative.html.
        var bitmap = Resolve("conic-gradient(from -90deg, blue 0 25%, black 25% 50%, " +
            "red 50% 75%, green 75% 100%)", 200, 200);
        Assert.Equal(0xFF0000FFu, Pixel(bitmap, 50, 50).ToArgb());
        Assert.Equal(0xFF000000u, Pixel(bitmap, 150, 50).ToArgb());
        Assert.Equal(0xFF008000u, Pixel(bitmap, 50, 150).ToArgb());
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 150, 150).ToArgb());
    }

    [Fact]
    public void WptConicGradientCenter_UsesTheOriginalOffsetCenter()
    {
        // css/css-images/conic-gradient-center.html: original 200x200 box and value.
        var bitmap = Resolve("conic-gradient(at 25% 25%, red 0 25%, green 25% 50%, " +
            "blue 50% 75%, black 75% 100%)", 200, 200);
        Assert.Equal(0xFF000000u, Pixel(bitmap, 20, 20).ToArgb());
        Assert.Equal(0xFFFF0000u, Pixel(bitmap, 150, 20).ToArgb());
        Assert.Equal(0xFF0000FFu, Pixel(bitmap, 20, 150).ToArgb());
        Assert.Equal(0xFF008000u, Pixel(bitmap, 150, 150).ToArgb());
    }

    [Fact]
    public void WptConicGradient001_LargeSecondPositionKeepsTheSingleColor()
    {
        // First box from css/css-images/gradient/conic-gradient-001.html.
        var bitmap = Resolve("conic-gradient(lime 0 999999999%)", 100, 100);
        Assert.Equal(0xFF00FF00u, Pixel(bitmap, 25, 25).ToArgb());
        Assert.Equal(0xFF00FF00u, Pixel(bitmap, 75, 75).ToArgb());
    }

    [Fact]
    public void LargePaintBox_BoundsTheRasterAndRetainsItsAspectRatio()
    {
        var bitmap = Resolve("conic-gradient(red, blue)", 10000, 2500);
        Assert.Equal(2048, bitmap.PixelWidth);
        Assert.Equal(512, bitmap.PixelHeight);
    }

    [Fact]
    public void NativeBackgroundRetainsPrecedenceAndCssRemovalRestoresIt()
    {
        var native = new SolidColorBrush(Colors.CornflowerBlue);
        var box = new Border { Background = native };
        Css.SetStyle(box, "background-image:conic-gradient(red, blue)");
        Assert.Same(native, box.Background);
        box.ClearValue(Border.BackgroundProperty);
        Assert.IsType<ImageBrush>(box.Background);
        Css.SetStyle(box, string.Empty);
        Assert.Null(box.Background);
    }

    [Fact]
    public void ManagedSoftwareRender_UsesTheResolvedConicImageBrush()
    {
        var box = new Border { Width = 101, Height = 101 };
        Css.SetStyle(box, "background-image:conic-gradient(red 0deg 90deg, lime 90deg 180deg, " +
            "blue 180deg 270deg, yellow 270deg 360deg)");
        CssBorderRadiusTests.Layout(box, 101, 101);
        var pixels = CssBorderRadiusTests.Pixels(box, 101, 101);
        uint At(int x, int y)
        {
            var index = (y * 101 + x) * 4;
            return (uint)(pixels[index + 3] << 24 | pixels[index + 2] << 16 |
                pixels[index + 1] << 8 | pixels[index]);
        }
        Assert.Equal(0xFFFF0000u, At(75, 25));
        Assert.Equal(0xFF00FF00u, At(75, 75));
        Assert.Equal(0xFF0000FFu, At(25, 75));
        Assert.Equal(0xFFFFFF00u, At(25, 25));
    }

    [Fact]
    public async Task RecordedDrawing_ResolvesConicBeforeCrossThreadReplay()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:conic-gradient(red 0deg 180deg, blue 180deg 360deg)");
        var source = Assert.IsType<ImageBrush>(box.Background);
        var recorder = new DrawingRecorder();
        recorder.BindWholeFrame();
        recorder.DrawRectangle(source, null, new Rect(0, 0, 101, 101));
        var recorded = recorder.Commit();
        Assert.True(recorded.CommandCount > 0);
        Assert.True(recorded.FullyRecordable);

        var pixels = await Task.Run(() =>
        {
            var target = new RenderTargetBitmap(101, 101, 96, 96, PixelFormat.Bgra32);
            var context = new SoftwareDrawingContext(target);
            DrawingReplayer.Replay(recorded, context);
            context.Close();
            return target.GetPixelBuffer();
        });
        Assert.Equal((byte)255, pixels[(25 * 101 + 75) * 4 + 2]); // red upper half
        Assert.Equal((byte)255, pixels[(75 * 101 + 25) * 4]); // blue lower half
    }

    [Theory]
    [InlineData("conic-gradient(red 20px, blue)")]
    [InlineData("conic-gradient(from 20px, red, blue)")]
    [InlineData("conic-gradient(in srgb longer hue, red, blue)")]
    [InlineData("conic-gradient(from 30deg red, blue)")]
    public void InvalidAngularGrammar_IsRejected(string css)
        => Assert.False(Css.Supports("background-image", css));
}
