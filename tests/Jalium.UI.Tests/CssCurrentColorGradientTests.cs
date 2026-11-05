using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssCurrentColorGradientTests
{
    private static void Layout(FrameworkElement element, double width = 101, double height = 101)
    {
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
    }

    [Fact]
    public void LinearGradient_ResolvesCurrentColorAfterTheColorPrepass()
    {
        const string gradient = "linear-gradient(currentColor 0%, blue 100%)";
        Assert.True(Css.Supports("background-image", gradient));
        var text = new TextBlock { Text = "X" };
        Css.SetStyle(text, "color:red;background-image:" + gradient);
        Layout(text);
        var source = Assert.IsType<LinearGradientBrush>(text.Background);
        Assert.Equal(0xFFFF0000u,
            Assert.IsType<LinearGradientBrush>(source.CssGradientLayout!.Resolve(101, 101))
                .GradientStops[0].Color.ToArgb());

        Css.SetStyle(text, "color:green;background-image:" + gradient);
        Layout(text);
        source = Assert.IsType<LinearGradientBrush>(text.Background);
        Assert.Equal(0xFF008000u,
            Assert.IsType<LinearGradientBrush>(source.CssGradientLayout!.Resolve(101, 101))
                .GradientStops[0].Color.ToArgb());
    }

    [Fact]
    public void NativeForegroundChange_RecomputesAContextualRadialGradient()
    {
        var button = new Button { Foreground = new SolidColorBrush(Colors.Red) };
        Css.SetStyle(button, "background-image:radial-gradient(circle, color-mix(in srgb,currentColor,white), blue)");
        Layout(button);
        var first = Assert.IsType<RadialGradientBrush>(button.Background);
        Assert.Equal(0xFFFF8080u,
            Assert.IsType<RadialGradientBrush>(first.CssGradientLayout!.Resolve(101, 101))
                .GradientStops[0].Color.ToArgb());

        button.Foreground = new SolidColorBrush(Colors.Blue);
        Layout(button);
        var next = Assert.IsType<RadialGradientBrush>(button.Background);
        Assert.Equal(0xFF8080FFu,
            Assert.IsType<RadialGradientBrush>(next.CssGradientLayout!.Resolve(101, 101))
                .GradientStops[0].Color.ToArgb());
    }

    [Fact]
    public void ConicGradient_UsesCurrentColorInDirectAndMixedStops()
    {
        const string css = "conic-gradient(currentColor 0deg 180deg, " +
            "color-mix(in srgb,currentColor,white) 180deg 360deg)";
        Assert.True(Css.Supports("background-image", css));
        var text = new TextBlock { Text = "X" };
        Css.SetStyle(text, "color:blue;background-image:" + css);
        Layout(text);
        var source = Assert.IsType<ImageBrush>(text.Background);
        var used = Assert.IsType<ImageBrush>(source.CssGradientLayout!.Resolve(101, 101));
        var bitmap = Assert.IsType<BitmapImage>(used.ImageSource);
        var pixels = new byte[101 * 101 * 4];
        bitmap.CopyPixels(pixels, 101 * 4, 0);
        uint At(int x, int y)
        {
            var at = (y * 101 + x) * 4;
            return (uint)(pixels[at + 3] << 24 | pixels[at + 2] << 16 |
                pixels[at + 1] << 8 | pixels[at]);
        }
        Assert.Equal(0xFF0000FFu, At(75, 25));
        Assert.Equal(0xFF8080FFu, At(25, 75));
    }

    [Fact]
    public void MutableForegroundBrush_RecomputesContextualConicStops()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var button = new Button { Foreground = foreground };
        Css.SetStyle(button, "background-image:conic-gradient(currentColor 0deg 180deg,blue 180deg 360deg)");
        Layout(button);
        var first = Assert.IsType<ImageBrush>(button.Background);
        var firstBitmap = Assert.IsType<BitmapImage>(Assert.IsType<ImageBrush>(
            first.CssGradientLayout!.Resolve(101, 101)).ImageSource);
        var firstPixels = new byte[101 * 101 * 4];
        firstBitmap.CopyPixels(firstPixels, 101 * 4, 0);
        Assert.Equal((byte)255, firstPixels[(25 * 101 + 75) * 4 + 2]);

        foreground.Color = Colors.Green;
        Layout(button);
        var updated = Assert.IsType<ImageBrush>(button.Background);
        var nextBitmap = Assert.IsType<BitmapImage>(Assert.IsType<ImageBrush>(
            updated.CssGradientLayout!.Resolve(101, 101)).ImageSource);
        var nextPixels = new byte[101 * 101 * 4];
        nextBitmap.CopyPixels(nextPixels, 101 * 4, 0);
        Assert.True(nextPixels[(25 * 101 + 75) * 4 + 1] > 100);
        Assert.True(nextPixels[(25 * 101 + 75) * 4 + 2] < 100);
    }
}
