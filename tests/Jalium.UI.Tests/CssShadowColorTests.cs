using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssShadowColorTests
{
    private static void Layout(FrameworkElement root)
    {
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));
        root.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.False(CssEvaluationScheduler.HasPending(root.Dispatcher));
    }

    [Fact]
    public void OmittedBoxShadowColor_UsesTheWinningForeground()
    {
        var border = new Border();
        Css.SetStyle(border, "color:red;box-shadow:0 2px 4px");
        Layout(border);
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<DropShadowEffect>(border.Effect).Color.ToArgb());

        Css.SetStyle(border, "color:blue;box-shadow:0 2px 4px");
        Layout(border);
        Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<DropShadowEffect>(border.Effect).Color.ToArgb());
    }

    [Fact]
    public void ShadowList_ResolvesDirectAndMixedCurrentColorPerElement()
    {
        var border = new Border();
        Css.SetStyle(border, "color:blue;box-shadow:1px 2px currentColor, " +
            "inset 0 0 2px color-mix(in srgb,currentColor,white)");
        Layout(border);
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.True(group.CssShadowsFrontToBack);
        Assert.Equal(2, group.Children.Count);
        Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<DropShadowEffect>(group.Children[0]).Color.ToArgb());
        Assert.Equal(0xFF8080FFu, Assert.IsType<InnerShadowEffect>(group.Children[1]).Color.ToArgb());
    }

    [Fact]
    public void FilterDropShadow_UsesCurrentColorAndCombinesWithOtherFilters()
    {
        var border = new Border();
        Css.SetStyle(border, "color:green;filter:brightness(1) drop-shadow(2px 3px)");
        Layout(border);
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.Equal(Colors.Green.ToArgb(), Assert.IsType<DropShadowEffect>(group.Children[1]).Color.ToArgb());

        Css.SetStyle(border, "color:blue;filter:drop-shadow(2px 3px color-mix(in srgb,currentColor,white))");
        Layout(border);
        Assert.Equal(0xFF8080FFu, Assert.IsType<DropShadowEffect>(border.Effect).Color.ToArgb());
    }

    [Fact]
    public void MutableNativeForeground_UpdatesOmittedAndMixedShadowColors()
    {
        var foreground = new SolidColorBrush(Colors.Red);
        var button = new Button { Foreground = foreground };
        Css.SetStyle(button, "box-shadow:0 2px 3px;" +
            "filter:drop-shadow(2px 3px color-mix(in srgb,currentColor,white))");
        Layout(button);
        var first = Assert.IsType<EffectGroup>(button.Effect);
        Assert.Equal(Colors.Red.ToArgb(), Assert.IsType<DropShadowEffect>(first.Children[0]).Color.ToArgb());
        Assert.Equal(0xFFFF8080u, Assert.IsType<DropShadowEffect>(first.Children[1]).Color.ToArgb());

        foreground.Color = Colors.Blue;
        Layout(button);
        var updated = Assert.IsType<EffectGroup>(button.Effect);
        Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<DropShadowEffect>(updated.Children[0]).Color.ToArgb());
        Assert.Equal(0xFF8080FFu, Assert.IsType<DropShadowEffect>(updated.Children[1]).Color.ToArgb());
    }

    [Fact]
    public void MixedCurrentColor_PreservesShadowAlpha()
    {
        var border = new Border();
        Css.SetStyle(border, "color:red;box-shadow:0 0 color-mix(in srgb,transparent,currentColor)");
        Layout(border);

        var shadow = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(Colors.Red.ToArgb(), shadow.Color.ToArgb());
        Assert.InRange(shadow.Opacity, 0.49, 0.51);
    }

    [Fact]
    public void NativeEffect_TakesPriorityUntilItsLocalValueIsCleared()
    {
        var native = new BlurEffect { Radius = 7 };
        var border = new Border { Effect = native };
        Css.SetStyle(border, "color:red;box-shadow:0 2px;filter:drop-shadow(1px 2px currentColor)");
        Layout(border);
        Assert.Same(native, border.Effect);

        Css.SetStyle(border, "color:blue;box-shadow:0 2px;filter:drop-shadow(1px 2px currentColor)");
        Layout(border);
        Assert.Same(native, border.Effect);

        border.ClearValue(UIElement.EffectProperty);
        Layout(border);
        var group = Assert.IsType<EffectGroup>(border.Effect);
        Assert.Equal(2, group.Children.Count);
        Assert.All(group.Children, effect =>
            Assert.Equal(Colors.Blue.ToArgb(), Assert.IsType<DropShadowEffect>(effect).Color.ToArgb()));
    }

    [Fact]
    public void ShadowList_KeepsFrontToBackOrderWhenCombinedWithFilter()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow:1px 0 red,1px 0 blue;filter:brightness(.9)");
        Layout(border);
        var combined = Assert.IsType<EffectGroup>(border.Effect);
        Assert.True(combined.CssCombinedShadowAndFilter);
        Assert.True(Assert.IsType<EffectGroup>(combined.Clone()).CssCombinedShadowAndFilter);
        var shadows = Assert.IsType<EffectGroup>(combined.Children[0]);
        Assert.True(shadows.CssShadowsFrontToBack);
        Assert.True(Assert.IsType<EffectGroup>(shadows.Clone()).CssShadowsFrontToBack);
        Assert.Equal(Colors.Red.ToArgb(),
            Assert.IsType<DropShadowEffect>(shadows.Children[0]).Color.ToArgb());
        Assert.Equal(Colors.Blue.ToArgb(),
            Assert.IsType<DropShadowEffect>(shadows.Children[1]).Color.ToArgb());
        Assert.IsType<ColorMatrixEffect>(combined.Children[1]);
    }

    [Fact]
    public void OmittedBlurRadius_UsesZero()
    {
        var border = new Border();
        Css.SetStyle(border, "filter:blur()");
        Layout(border);
        Assert.Equal(0, Assert.IsType<BlurEffect>(border.Effect).Radius);
        Assert.True(Css.Supports("filter", "blur()"));
    }

    [Theory]
    [InlineData("0 0 0 8px red", 8.0)]
    [InlineData("20px 0 0 -8px red", -8.0)]
    public void OuterShadowSpread_IsRetainedOnTheEffect(string value, double spread)
    {
        var border = new Border();
        Css.SetStyle(border, $"box-shadow:{value}");
        Layout(border);

        var effect = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(spread, effect.SpreadRadius);
        Assert.True(effect.HasEffect);
        Assert.True(Css.Supports("box-shadow", value));
        if (spread > 0) Assert.True(effect.EffectPadding.Left >= spread);
    }

    [Fact]
    public void BoxAndFilterShadowLengths_KeepTheirDistinctGaussianMeaningThroughCloning()
    {
        var border = new Border();
        Css.SetStyle(border, "box-shadow:0 0 12px red");
        Layout(border);
        var box = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(12, box.BlurRadius);
        Assert.Equal(6, box.CssGaussianSigma);
        Assert.False(box.CssUsesAlphaMask);
        Assert.Equal(6, box.Clone().CssGaussianSigma);

        Css.SetStyle(border, "filter:drop-shadow(0 0 6px red)");
        Layout(border);
        var filter = Assert.IsType<DropShadowEffect>(border.Effect);
        Assert.Equal(6, filter.BlurRadius);
        Assert.Equal(6, filter.CssGaussianSigma);
        Assert.True(filter.CssUsesAlphaMask);
        Assert.True(filter.Clone().CssUsesAlphaMask);
        Assert.True(filter.EffectPadding.Left >= 18);
    }

    [Fact]
    public void FilterBlurLength_IsGaussianSigmaAndIncludesItsPaintApron()
    {
        var border = new Border();
        Css.SetStyle(border, "filter:blur(6px)");
        Layout(border);

        var blur = Assert.IsType<BlurEffect>(border.Effect);
        Assert.Equal(6, blur.Radius);
        Assert.Equal(6, blur.CssGaussianSigma);
        Assert.Equal(6, blur.Clone().CssGaussianSigma);
        Assert.True(blur.EffectPadding.Left >= 18);
    }

    [Theory]
    [InlineData("box-shadow", "0 0 -1px red")]
    [InlineData("filter", "drop-shadow(2px 3px 4px 5px)")]
    [InlineData("filter", "drop-shadow(inset 2px 3px)")]
    public void InvalidShadowForms_AreRejected(string property, string value)
        => Assert.False(Css.Supports(property, value));
}
