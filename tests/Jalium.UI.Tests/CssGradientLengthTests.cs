using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssGradientLengthTests
{
    private static LinearGradientBrush Resolve(GradientBrush brush, double width, double height)
    {
        var layout = brush.CssGradientLayout;
        Assert.NotNull(layout);
        return Assert.IsType<LinearGradientBrush>(layout.Resolve(width, height));
    }

    [Fact]
    public void AbsoluteStopsAndHints_ResolveAgainstThePaintedLineOnResize()
    {
        Assert.True(Css.Supports("background-image", "linear-gradient(to right, red 0px, blue 100px)"));
        var box = new Border();
        Css.SetStyle(box, "background-image:linear-gradient(to right, red 0px, 25px, blue 100px)");
        var brush = Assert.IsType<LinearGradientBrush>(box.Background);
        var at100 = Resolve(brush, 100, 20);
        var at200 = Resolve(brush, 200, 20);
        Assert.Equal(0, at100.GradientStops[0].Offset);
        Assert.Equal(1, at100.GradientStops[^1].Offset);
        Assert.Equal(.5, at200.GradientStops[^1].Offset, 9);
        Assert.Contains(at100.GradientStops, stop => Math.Abs(stop.Offset - .25) < 1e-9 &&
            Math.Abs(stop.Color.R - 128) <= 2 && Math.Abs(stop.Color.B - 128) <= 2);
        Assert.Contains(at200.GradientStops, stop => Math.Abs(stop.Offset - .125) < 1e-9 &&
            Math.Abs(stop.Color.R - 128) <= 2 && Math.Abs(stop.Color.B - 128) <= 2);
        Assert.Same(at200, Resolve(brush, 200, 20));
        Assert.NotSame(at100, at200);
    }

    [Fact]
    public void MixedCalcLengthAndPercentage_UsesTheCurrentLineLength()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:linear-gradient(to right, red 0%, blue calc(50% + 10px))");
        var brush = Assert.IsType<LinearGradientBrush>(box.Background);
        Assert.Equal(.6, Resolve(brush, 100, 20).GradientStops[^1].Offset, 8);
        Assert.Equal(.55, Resolve(brush, 200, 20).GradientStops[^1].Offset, 8);
    }

    [Fact]
    public void WptCalcEmStop_UsesEachElementsNativeFontSize()
    {
        static LinearGradientBrush Styled(double size)
        {
            var text = new TextBlock { FontSize = size, Width = 100, Height = 100 };
            Css.SetStyle(text, "background-image:linear-gradient(blue calc(2em), yellow)");
            return Assert.IsType<LinearGradientBrush>(text.Background);
        }
        Assert.Equal(.2, Resolve(Styled(10), 100, 100).GradientStops[0].Offset, 8);
        Assert.Equal(.6, Resolve(Styled(30), 100, 100).GradientStops[0].Offset, 8);
    }

    [Fact]
    public void NativeFontSizeChange_RecomputesTheSameElementsGradientRuler()
    {
        var text = new TextBlock { FontSize = 10, Width = 100, Height = 100 };
        Css.SetStyle(text, "background-image:linear-gradient(blue 2em, yellow)");
        var first = Assert.IsType<LinearGradientBrush>(text.Background);
        Assert.Equal(.2, Resolve(first, 100, 100).GradientStops[0].Offset, 8);
        text.FontSize = 30;
        text.Measure(new(100, 100)); text.Arrange(new(0, 0, 100, 100)); text.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(text.Dispatcher);
        var updated = Assert.IsType<LinearGradientBrush>(text.Background);
        Assert.Equal(.6, Resolve(updated, 100, 100).GradientStops[0].Offset, 8);
    }

    [Fact]
    public void NonSquareCornerDirection_PassesThroughTheSpecCornerProjections()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:linear-gradient(to right bottom, black 50%, lightgray 50%)");
        var resolved = Resolve(Assert.IsType<LinearGradientBrush>(box.Background), 200, 100);
        var sx = resolved.StartPoint.X * 200; var sy = resolved.StartPoint.Y * 100;
        var ex = resolved.EndPoint.X * 200; var ey = resolved.EndPoint.Y * 100;
        var dx = ex - sx; var dy = ey - sy;
        double Position(double x, double y) => ((x - sx) * dx + (y - sy) * dy) / (dx * dx + dy * dy);
        Assert.Equal(.5, Position(200, 0), 8);
        Assert.Equal(.5, Position(0, 100), 8);
        Assert.True(Position(120, 35) < .5);
        Assert.True(Position(80, 65) > .5);
    }

    [Fact]
    public void RadialLengthStops_ResolveAgainstTheRightwardEndingRadius()
    {
        var box = new Border();
        Css.SetStyle(box, "background-image:radial-gradient(circle, red 0px, blue 50px)");
        var brush = Assert.IsType<RadialGradientBrush>(box.Background);
        Assert.NotNull(brush.CssGradientLayout);
        var at100 = Assert.IsType<RadialGradientBrush>(brush.CssGradientLayout.Resolve(100, 100));
        var at200 = Assert.IsType<RadialGradientBrush>(brush.CssGradientLayout.Resolve(200, 200));
        Assert.Equal(50 / Math.Sqrt(50 * 50 + 50 * 50), at100.GradientStops[^1].Offset, 8);
        Assert.Equal(50 / Math.Sqrt(100 * 100 + 100 * 100), at200.GradientStops[^1].Offset, 8);
    }

    [Fact]
    public void NativeBackgroundValue_RetainsPrecedenceOverDeferredCssGradient()
    {
        var native = new SolidColorBrush(Colors.CornflowerBlue);
        var box = new Border { Background = native };
        Css.SetStyle(box, "background-image:linear-gradient(red 10px, blue 50px)");
        Assert.Same(native, box.Background);
        box.ClearValue(Border.BackgroundProperty);
        Assert.IsType<LinearGradientBrush>(box.Background);
        Css.SetStyle(box, string.Empty);
        Assert.Null(box.Background);
    }

    [Fact]
    public void ManagedSoftwarePixels_KeepAnAbsoluteStopFixedAcrossResizes()
    {
        var box = new Border { Width = 100, Height = 20 };
        Css.SetStyle(box, "background-image:linear-gradient(to right, red 0px, blue 100px)");
        CssBorderRadiusTests.Layout(box, 100, 20);
        var narrow = CssBorderRadiusTests.Pixels(box, 200, 20);
        box.Width = 200;
        CssBorderRadiusTests.Layout(box, 200, 20);
        var wide = CssBorderRadiusTests.Pixels(box, 200, 20);
        var at = (10 * 200 + 50) * 4;
        Assert.InRange(Math.Abs(narrow[at] - wide[at]), 0, 3); // blue
        Assert.InRange(Math.Abs(narrow[at + 2] - wide[at + 2]), 0, 3); // red
        Assert.InRange(wide[at], (byte)120, (byte)136);
        Assert.InRange(wide[at + 2], (byte)120, (byte)136);
    }
}
