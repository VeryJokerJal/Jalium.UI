using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Tests.TextEffects;

namespace Jalium.UI.Tests;

public sealed class CssBackdropFilterTests
{
    private static void Layout(FrameworkElement element, double width = 200, double height = 100)
    {
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
    }

    [Fact]
    public void BorderBlur_UsesCssSigmaAndDrawsBeforeItsBackground()
    {
        var border = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8) };
        Css.SetStyle(border, "backdrop-filter: blur(4px)");
        Layout(border);

        var effect = Assert.IsType<BackdropBlurEffect>(border.BackdropEffect);
        Assert.Equal(4f, effect.BlurSigma);
        Assert.Equal(12f, effect.BlurRadius);

        var drawing = new RecordingDrawingContext();
        border.Render(drawing);
        var backdropIndex = drawing.Events.IndexOf("DrawBackdropEffect");
        var backgroundIndex = drawing.Events.IndexOf("DrawRoundedRectangle");
        Assert.True(backdropIndex >= 0);
        Assert.True(backgroundIndex > backdropIndex);
    }

    [Fact]
    public void BlurViewportLength_UpdatesWhenViewportChangesAndReusesStableEffect()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(200, 100), new Size(200, 100), new Size(200, 100)));
        Css.SetStyle(child, "backdrop-filter: blur(5vw)");
        Layout(root);
        var first = Assert.IsType<BackdropBlurEffect>(child.BackdropEffect);
        Assert.Equal(10f, first.BlurSigma);

        Layout(root);
        Assert.Same(first, child.BackdropEffect);

        Css.SetViewportMetrics(root, new(new Size(400, 100), new Size(400, 100), new Size(400, 100)));
        Layout(root);
        var changed = Assert.IsType<BackdropBlurEffect>(child.BackdropEffect);
        Assert.NotSame(first, changed);
        Assert.Equal(20f, changed.BlurSigma);
    }

    [Theory]
    [InlineData("blur()", true)]
    [InlineData("blur(0)", true)]
    [InlineData("blur(calc(-5px))", true)]
    [InlineData("blur(-1px)", false)]
    [InlineData("blur(10%)", false)]
    [InlineData("blur(5)", false)]
    [InlineData("blur(4px) brightness(2)", false)]
    [InlineData("brightness(2) contrast(50%)", false)]
    [InlineData("hue-rotate(20)", false)]
    [InlineData("grayscale(-1)", false)]
    [InlineData("invert(10px)", false)]
    [InlineData("opacity(-10%)", false)]
    public void SupportedBlurAndInvalidValues_AreDistinguished(string value, bool valid)
    {
        var border = new Border();
        Css.SetStyle(border, $"backdrop-filter: {value}; width: 40px");
        Layout(border);

        Assert.Equal(40, border.Width);
        if (valid)
            Assert.NotNull(border.BackdropEffect);
        else
            Assert.Null(border.BackdropEffect);
    }

    [Theory]
    [InlineData("brightness(150%)", "brightness", 1.5f)]
    [InlineData("contrast(0.5)", "contrast", 0.5f)]
    [InlineData("saturate(200%)", "saturate", 2f)]
    [InlineData("grayscale()", "grayscale", 1f)]
    [InlineData("grayscale(150%)", "grayscale", 1f)]
    [InlineData("sepia(25%)", "sepia", 0.25f)]
    [InlineData("invert()", "invert", 1f)]
    [InlineData("invert(200%)", "invert", 1f)]
    [InlineData("opacity(25%)", "opacity", 0.25f)]
    [InlineData("opacity(200%)", "opacity", 1f)]
    [InlineData("opacity()", "opacity", 1f)]
    [InlineData("hue-rotate(0.5turn)", "hue-rotate", 180f)]
    [InlineData("hue-rotate(0)", "hue-rotate", 0f)]
    public void SingleColorFunction_MapsToBackdropMaterial(string css, string function, float expected)
    {
        var border = new Border();
        Css.SetStyle(border, $"backdrop-filter: {css}");
        Layout(border);

        var effect = Assert.IsType<ColorAdjustmentEffect>(border.BackdropEffect);
        var actual = function switch
        {
            "brightness" => effect.Brightness,
            "contrast" => effect.Contrast,
            "saturate" => effect.Saturation,
            "grayscale" => effect.Grayscale,
            "sepia" => effect.Sepia,
            "invert" => effect.Invert,
            "opacity" => effect.Opacity,
            _ => effect.HueRotation * 180f / MathF.PI,
        };
        Assert.InRange(actual, expected - 0.001f, expected + 0.001f);

        Layout(border);
        Assert.Same(effect, border.BackdropEffect);
    }

    [Fact]
    public void NoneAndRemovedStyle_ClearTheCssBackdropEffect()
    {
        var border = new Border();
        Css.SetStyle(border, "backdrop-filter: blur(4px)");
        Layout(border);
        Assert.NotNull(border.BackdropEffect);

        Css.SetStyle(border, "backdrop-filter: none");
        Layout(border);
        Assert.Null(border.BackdropEffect);

        Css.SetStyle(border, "backdrop-filter: blur(4px)");
        Layout(border);
        Assert.NotNull(border.BackdropEffect);
        Css.SetStyle(border, "");
        Layout(border);
        Assert.Null(border.BackdropEffect);
    }

    [Fact]
    public void NativeLocalBackdropEffectKeepsPrecedenceOverCss()
    {
        var native = new BackdropBlurEffect(7);
        var border = new Border { BackdropEffect = native };
        Css.SetStyle(border, "backdrop-filter: blur(4px)");
        Layout(border);
        Assert.Same(native, border.BackdropEffect);

        border.ClearValue(UIElement.BackdropEffectProperty);
        Layout(border);
        Assert.Equal(4f, Assert.IsType<BackdropBlurEffect>(border.BackdropEffect).BlurSigma);
    }

    [Fact]
    public void TextBlockPaintsBackdropBeforeItsBackground()
    {
        var text = new TextBlock { Text = "Backdrop", Background = Brushes.White };
        Css.SetStyle(text, "backdrop-filter: blur(4px)");
        Layout(text);
        Assert.Equal(4f, Assert.IsType<BackdropBlurEffect>(text.BackdropEffect).BlurSigma);

        var drawing = new RecordingDrawingContext();
        text.Render(drawing);
        var backdropIndex = drawing.Events.IndexOf("DrawBackdropEffect");
        var backgroundIndex = drawing.Events.IndexOf("DrawRectangle");
        Assert.True(backdropIndex >= 0);
        Assert.True(backgroundIndex > backdropIndex);
    }

    [Fact]
    public void PanelBackdropUsesTheCssBorderBoxContour()
    {
        var panel = new Grid { Background = Brushes.White };
        Css.SetStyle(panel, "border-radius: 50px / 20px; backdrop-filter: invert(100%)");
        Layout(panel);
        Assert.IsType<ColorAdjustmentEffect>(panel.BackdropEffect);

        var drawing = new RecordingDrawingContext();
        panel.Render(drawing);
        var clipIndex = drawing.Events.IndexOf("PushClip(CssRoundedRectangleGeometry)");
        var backdropIndex = drawing.Events.IndexOf("DrawBackdropEffect");
        var backgroundIndex = drawing.Events.IndexOf("DrawGeometry");
        Assert.True(clipIndex >= 0);
        Assert.True(backdropIndex > clipIndex);
        Assert.True(backgroundIndex > backdropIndex);
    }

    [Fact]
    public void ScrollBarCssBackdropPaintsOnceAtItsBorderBox()
    {
        var scrollBar = new Jalium.UI.Controls.Primitives.ScrollBar
        {
            Width = 20,
            Height = 80,
            Padding = new Thickness(3),
        };
        Css.SetStyle(scrollBar, "backdrop-filter: invert(1)");
        Layout(scrollBar, 20, 80);

        var drawing = new RecordingDrawingContext();
        scrollBar.Render(drawing);
        Assert.Equal(1, drawing.Events.Count(static entry => entry == "DrawBackdropEffect"));
    }
}
