using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssRelativeUnitWptTests
{
    [Fact]
    [Trait("WPT", "css/css-values/lh-unit-001.html")]
    public void LhInLineHeight_ProducesTheOriginalGreenSquare()
    {
        var line = new Border { VerticalAlignment = VerticalAlignment.Top };
        var parent = new Border { Child = line };
        Css.SetStyle(parent, "font-size:50px; line-height:1; width:100px; height:100px; background-color:red");
        // The source's single NBSP line is represented by a native line box.
        Css.SetStyle(line, "font-size:42px; line-height:2lh; height:1lh; background-color:green");
        Layout(parent, 100, 100);
        Assert.Equal(100, line.ActualHeight);
        var reference = new Border { Width = 100, Height = 100, Background = new SolidColorBrush(Colors.Green) };
        Layout(reference, 100, 100);
        Assert.Equal(Pixels(reference, 100, 100), Pixels(parent, 100, 100));
    }

    private static IEnumerable<(string Unit, bool Horizontal)> Units()
    {
        foreach (var prefix in new[] { "", "s", "l", "d" })
        {
            foreach (var axis in new[] { "vw", "vi", "vmax" }) yield return (prefix + axis, true);
            foreach (var axis in new[] { "vh", "vb", "vmin" }) yield return (prefix + axis, false);
        }
    }

    [Fact]
    [Trait("WPT", "css/css-values/viewport-units-compute.html")]
    public void ViewportUnitComputation_MatchesAllOriginalNumericCases()
    {
        var cases = Units().Select(item => ("100" + item.Unit, item.Horizontal ? 200d : 100d)).ToList();
        cases.AddRange(new (string, double)[]
        {
            ("1dvw", 2), ("10dvw", 20), ("1dvh", 1), ("10dvh", 10),
            ("calc(1dvw + 1dvw)", 4), ("calc(1dvw + 1dvh)", 3),
            ("calc(1dvw + 100px)", 102), ("max(1svw,1svh)", 2),
            ("min(1lvw,1lvh)", 1), ("calc(1dvw + 10%)", 12)
        });
        foreach (var (expression, expected) in cases)
        {
            var target = new Border { Width = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var root = new Border { Child = target };
            Css.SetStyle(target, "background-color:green; height:" + expression);
            Layout(root, 200, 100);
            Assert.Equal(expected, target.ActualHeight, 6);
            var reference = new Border { Child = new Border { Width = 8, Height = expected,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Colors.Green) } };
            Layout(reference, 200, 100);
            Assert.Equal(Pixels(reference, 200, 100), Pixels(root, 200, 100));
        }
    }

    [Fact]
    [Trait("WPT", "css/css-values/viewport-units-invalidation.html")]
    public void ViewportResize_InvalidatesAllOriginalUnitVariants()
    {
        foreach (var (unit, horizontal) in Units())
        {
            var target = new Border(); var root = new Border { Child = target };
            Css.SetStyle(target, "height:100" + unit);
            Layout(root, 200, 100); Assert.Equal(horizontal ? 200 : 100, target.ActualHeight, 6);
            Layout(root, 400, 300); Assert.Equal(horizontal ? 400 : 300, target.ActualHeight, 6);
        }
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
    }

    private static byte[] Pixels(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormat.Bgra32);
        bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        return pixels;
    }
}
