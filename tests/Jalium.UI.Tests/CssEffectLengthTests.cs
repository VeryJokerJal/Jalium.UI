using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssEffectLengthTests
{
    private static void Layout(FrameworkElement element, double width = 200, double height = 100)
    {
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(element.Dispatcher));
    }

    [Fact]
    public void BoxShadow_ResolvesEmLengthsWhenNativeFontSizeChanges()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var button = new Button { FontSize = 20, Foreground = foreground };
        Css.SetStyle(button, "box-shadow:1em -0.5em .25em .1em currentColor");
        Layout(button);

        var first = Assert.IsType<DropShadowEffect>(button.Effect);
        Assert.Equal(20, first.OffsetX, 5);
        Assert.Equal(-10, first.OffsetY, 5);
        Assert.Equal(5, first.BlurRadius, 5);
        Assert.Equal(2, first.SpreadRadius, 5);
        Assert.Equal(Colors.Red.ToArgb(), first.Color.ToArgb());
        Layout(button);
        Assert.Same(first, button.Effect);

        button.FontSize = 30;
        Layout(button);
        var updated = Assert.IsType<DropShadowEffect>(button.Effect);
        Assert.NotSame(first, updated);
        Assert.Equal(30, updated.OffsetX, 5);
        Assert.Equal(-15, updated.OffsetY, 5);
        Assert.Equal(7.5, updated.BlurRadius, 5);
        Assert.Equal(3, updated.SpreadRadius, 5);
        Assert.Equal(Colors.Red.ToArgb(), updated.Color.ToArgb());

        foreground.Color = Colors.Blue;
        Layout(button);
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<DropShadowEffect>(button.Effect).Color.ToArgb());
    }

    [Fact]
    public void FilterBlurAndDropShadow_ResolveCalculatedLengthsPerElement()
    {
        var button = new Button { FontSize = 20 };
        Css.SetStyle(button, "filter:blur(calc(.25em + 1px)) " +
            "drop-shadow(calc(1em + 2px) 0 calc(.1em + 1px) red)");
        Layout(button);

        var first = Assert.IsType<EffectGroup>(button.Effect);
        var blur = Assert.IsType<BlurEffect>(first.Children[0]);
        var shadow = Assert.IsType<DropShadowEffect>(first.Children[1]);
        Assert.Equal(6, blur.Radius, 5);
        Assert.Equal(6, blur.CssGaussianSigma, 5);
        Assert.Equal(22, shadow.OffsetX, 5);
        Assert.Equal(3, shadow.BlurRadius, 5);
        Layout(button);
        Assert.Same(first, button.Effect);

        button.FontSize = 30;
        Layout(button);
        var updated = Assert.IsType<EffectGroup>(button.Effect);
        Assert.NotSame(first, updated);
        Assert.Equal(8.5, Assert.IsType<BlurEffect>(updated.Children[0]).Radius, 5);
        shadow = Assert.IsType<DropShadowEffect>(updated.Children[1]);
        Assert.Equal(32, shadow.OffsetX, 5);
        Assert.Equal(4, shadow.BlurRadius, 5);
        Assert.Equal(4, shadow.CssGaussianSigma, 5);
    }

    [Fact]
    public void ShadowAndBlur_RespondToViewportMetricChanges()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetViewportMetrics(root, new(new Size(200, 100), new Size(200, 100), new Size(200, 100)));
        Css.SetStyle(child, "box-shadow:10vw 0 red;filter:blur(5vh)");
        Layout(root);

        var first = Assert.IsType<EffectGroup>(child.Effect);
        Assert.Equal(20, Assert.IsType<DropShadowEffect>(first.Children[0]).OffsetX, 5);
        Assert.Equal(5, Assert.IsType<BlurEffect>(first.Children[1]).Radius, 5);

        Css.SetViewportMetrics(root, new(new Size(400, 200), new Size(400, 200), new Size(400, 200)));
        Layout(root);
        var updated = Assert.IsType<EffectGroup>(child.Effect);
        Assert.NotSame(first, updated);
        Assert.Equal(40, Assert.IsType<DropShadowEffect>(updated.Children[0]).OffsetX, 5);
        Assert.Equal(10, Assert.IsType<BlurEffect>(updated.Children[1]).Radius, 5);
    }

    [Fact]
    public void NegativeCalculatedBlur_ClampsToZeroAndRecoversAfterFontSizeChanges()
    {
        var button = new Button { FontSize = 20 };
        Css.SetStyle(button, "box-shadow:0 0 calc(1em - 30px) red;" +
            "filter:blur(calc(1em - 30px))");
        Layout(button);
        var initial = Assert.IsType<EffectGroup>(button.Effect);
        Assert.Equal(0, Assert.IsType<DropShadowEffect>(initial.Children[0]).BlurRadius);
        Assert.Equal(0, Assert.IsType<BlurEffect>(initial.Children[1]).Radius);

        button.FontSize = 40;
        Layout(button);
        var group = Assert.IsType<EffectGroup>(button.Effect);
        Assert.NotSame(initial, group);
        Assert.Equal(10, Assert.IsType<DropShadowEffect>(group.Children[0]).BlurRadius, 5);
        Assert.Equal(10, Assert.IsType<BlurEffect>(group.Children[1]).Radius, 5);
    }

    [Fact]
    public void EffectContainerUnits_TrackTheNearestQueryContainer()
    {
        var child = new Border();
        var container = new Border { Width = 400, Height = 200, Child = child };
        var root = new Border { Width = 800, Height = 400, Child = container };
        Css.SetStyle(container, "container-type:size");
        Css.SetStyle(child, "box-shadow:10cqw 0 red;filter:blur(10cqh)");
        Layout(root, 800, 400);

        var first = Assert.IsType<EffectGroup>(child.Effect);
        Assert.Equal(40, Assert.IsType<DropShadowEffect>(first.Children[0]).OffsetX, 5);
        Assert.Equal(20, Assert.IsType<BlurEffect>(first.Children[1]).Radius, 5);

        container.Width = 500;
        container.Height = 300;
        Layout(root, 800, 400);
        var updated = Assert.IsType<EffectGroup>(child.Effect);
        Assert.NotSame(first, updated);
        Assert.Equal(50, Assert.IsType<DropShadowEffect>(updated.Children[0]).OffsetX, 5);
        Assert.Equal(30, Assert.IsType<BlurEffect>(updated.Children[1]).Radius, 5);
    }

    [Theory]
    [InlineData("box-shadow", "1em 2rem calc(1px + 2vw) -2px red", true)]
    [InlineData("filter", "blur(calc(1em + 2px))", true)]
    [InlineData("filter", "drop-shadow(1em 2rem 3px red)", true)]
    [InlineData("filter", "blur(calc(-5px))", true)]
    [InlineData("box-shadow", "0 0 calc(-5px) red", true)]
    [InlineData("box-shadow", "1 2px red", false)]
    [InlineData("filter", "blur(5)", false)]
    [InlineData("filter", "blur(-1em)", false)]
    [InlineData("filter", "blur(10%)", false)]
    [InlineData("box-shadow", "0 0 10% red", false)]
    [InlineData("box-shadow", "0 0 -1em red", false)]
    public void EffectLengths_UseCssLengthGrammar(string property, string value, bool supported)
        => Assert.Equal(supported, Css.Supports(property, value));
}
