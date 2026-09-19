using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssConditionAndColorTests
{
    static CssConditionAndColorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private static void Layout(FrameworkElement root, double width)
    {
        root.Measure(new Size(width, 200)); root.Arrange(new Rect(0, 0, width, 200));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
    }

    [Fact]
    public void MediaQueries_RespondToViewportChangesAndNestedSupports()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, ".card {opacity:.8} @media screen and (min-width:500px) { @supports (width:calc(50% - 1px)) { .card {opacity:.4} } }");
        Layout(root, 400); Assert.Equal(.8, child.Opacity);
        Layout(root, 600); Assert.Equal(.4, child.Opacity);
        Layout(root, 300); Assert.Equal(.8, child.Opacity);
    }

    [Theory]
    [InlineData("screen and (width >= 400px)", true)]
    [InlineData("(max-width:300px)", false)]
    [InlineData("print, (orientation:landscape)", true)]
    [InlineData("not screen", false)]
    public void MediaQueries_EvaluateRangesAndBooleanComposition(string query, bool expected)
    {
        var target = new Border { Width = 400, Height = 200 };
        Assert.Equal(expected, new CssCondition("media", query).Evaluate(target));
    }

    [Theory]
    [InlineData("width", "calc(50% - 10px)", true)]
    [InlineData("width", "invalid", false)]
    [InlineData("font-weight", "bold", true)]
    [InlineData("animation-name", "missing-animation", false)]
    [InlineData("made-up-property", "true", false)]
    public void Supports_ReportsRegisteredValueGrammars(string property, string value, bool expected)
        => Assert.Equal(expected, Css.Supports(property, value));

    [Fact]
    public void CurrentColor_UsesSameDeclarationColorRegardlessOfPropertyOrder()
    {
        var target = new Border();
        Css.SetStyle(target, "background:currentcolor; border:2px solid currentcolor; outline:1px solid currentcolor; color:#336699");
        var expected = Color.FromRgb(0x33, 0x66, 0x99);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(target.Background).Color);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(target.BorderBrush).Color);
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(target.OutlineBrush).Color);
    }

    [Fact]
    public void ColorAndFonts_InheritThroughPanelsWithoutTheirOwnTypographyProperties()
    {
        var text = new TextBlock(); var root = new Border { Child = text };
        Css.SetStyle(root, "color:red; font-size:24px");
        Layout(root, 400);
        Assert.Equal(24, text.FontSize);
        Assert.Equal(Color.FromRgb(255, 0, 0), Assert.IsType<SolidColorBrush>(text.Foreground).Color);
    }

    [Theory]
    [InlineData("brightness(120%)")]
    [InlineData("contrast(.7)")]
    [InlineData("grayscale()")]
    [InlineData("invert(40%)")]
    [InlineData("opacity(.3)")]
    [InlineData("saturate(2)")]
    [InlineData("sepia(.8)")]
    [InlineData("hue-rotate(.5turn)")]
    public void ColorFilters_MapToTheSharedNativeColorMatrixEffect(string filter)
    {
        var target = new Border(); Css.SetStyle(target, "filter:" + filter);
        Assert.IsType<ColorMatrixEffect>(target.Effect);
    }
}
