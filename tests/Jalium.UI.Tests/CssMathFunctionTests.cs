using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssMathFunctionTests
{
    private static CssMathExpression Parse(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.True(reader.TryReadFunction(out var name, out var args));
        Assert.True(reader.AtEnd);
        return Assert.IsType<CssMathExpression>(CssMathExpression.Parse(name.ToString(), args.Remaining));
    }

    [Theory]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(-2.5)", -2)]
    [InlineData("round(up, 21px, -10px)", 30)]
    [InlineData("round(down, -21px, 10px)", -30)]
    [InlineData("round(to-zero, -21px, 10px)", -20)]
    [InlineData("mod(-18px, 5px)", 2)]
    [InlineData("mod(18px, -5px)", -2)]
    [InlineData("rem(-18px, 5px)", -3)]
    [InlineData("rem(18px, -5px)", 3)]
    [InlineData("abs(-12px)", 12)]
    [InlineData("sign(-12px)", -1)]
    [InlineData("sin(90deg)", 1)]
    [InlineData("cos(.5turn)", -1)]
    [InlineData("tan(45deg)", 1)]
    [InlineData("asin(1)", 90)]
    [InlineData("acos(-1)", 180)]
    [InlineData("atan(1)", 45)]
    [InlineData("atan2(1px, -1px)", 135)]
    [InlineData("pow(2, 5)", 32)]
    [InlineData("sqrt(81)", 9)]
    [InlineData("hypot(3px, 4px, 12px)", 13)]
    [InlineData("log(8, 2)", 3)]
    [InlineData("log(e)", 1)]
    [InlineData("exp(0)", 1)]
    [InlineData("calc(10px * 20px / 5px)", 40)]
    [InlineData("calc((1s * 10px + 500ms * 20px) / 2s)", 10)]
    [InlineData("calc(2kHz / 500Hz)", 4)]
    [InlineData("calc(2fr / 4fr)", .5)]
    [InlineData("calc(192dpi / 1dppx)", 2)]
    [InlineData("clamp(none, 20px, 10px)", 10)]
    [InlineData("clamp(30px, 20px, none)", 30)]
    [InlineData("clamp(none, 20px, none)", 20)]
    [InlineData("calc(1px /* a */ + /* b */ 2px)", 3)]
    [InlineData("ROUND(UP, 10px, 3px)", 12)]
    [InlineData("calc(sin(pi / 2) * 20px)", 20)]
    public void FunctionsAndUnitAlgebra_ProduceTheExpectedValue(string text, double expected)
    {
        Assert.True(Parse(text).TryEvaluate(CssLengthContext.Default, 100, out var actual));
        Assert.Equal(expected, actual, 9);
    }

    [Theory]
    [InlineData("round(1px)")]
    [InlineData("round(up 1, 2)")]
    [InlineData("round(sideways, 1, 2)")]
    [InlineData("mod(1px, 1s)")]
    [InlineData("sin(1px)")]
    [InlineData("asin(1deg)")]
    [InlineData("atan2(1px, 1s)")]
    [InlineData("pow(2px, 2)")]
    [InlineData("sqrt(4px)")]
    [InlineData("hypot(1px, 2s)")]
    [InlineData("log(1px)")]
    [InlineData("exp(1px)")]
    [InlineData("sign(1,2)")]
    [InlineData("clamp(1px, none, 2px)")]
    [InlineData("calc(1px/**/+ 2px)")]
    [InlineData("calc(1px +/**/2px)")]
    [InlineData("calc(1px + 1)")]
    public void InvalidSyntaxOrTypes_AreRejected(string text)
    {
        var reader = new CssTokenReader(text);
        Assert.True(reader.TryReadFunction(out var name, out var args));
        Assert.Null(CssMathExpression.Parse(name.ToString(), args.Remaining));
    }

    [Theory]
    [InlineData("calc(1)")]
    [InlineData("sign(1px)")]
    [InlineData("sin(90deg)")]
    [InlineData("calc(1px * 1px)")]
    [InlineData("calc(1px / 1s)")]
    public void MathResult_MustMatchTheLengthGrammar(string text)
    {
        var element = new Border();
        Css.SetStyle(element, "width:17px; width:" + text);
        Assert.Equal(17, element.Width);
    }

    [Theory]
    [InlineData("calc(0 / 0)", 0)]
    [InlineData("sqrt(-1)", 0)]
    [InlineData("pow(NaN, 0)", 0)]
    [InlineData("hypot(infinity, NaN)", 0)]
    [InlineData("log(10, 1)", 0)]
    [InlineData("log(-1)", 0)]
    [InlineData("calc(1 / infinity)", 0)]
    [InlineData("min(infinity, 20)", 20)]
    [InlineData("max(-infinity, 20)", 20)]
    [InlineData("atan(infinity)", 90)]
    [InlineData("calc(1 / round(to-zero, -1, infinity))", -double.MaxValue)]
    [InlineData("calc(1 / min(0, -1 * 0))", -double.MaxValue)]
    [InlineData("calc(1 / max(0, -1 * 0))", double.MaxValue)]
    [InlineData("calc(1 / -0)", double.MaxValue)]
    [InlineData("mod(-1, infinity)", 0)]
    [InlineData("rem(-1, infinity)", -1)]
    public void SpecialValues_ArePreservedInsideTheTreeAndCensoredAtItsBoundary(string text, double expected)
    {
        Assert.True(Parse(text).TryEvaluate(CssLengthContext.Default, 100, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Hypot_AvoidsOverflowAndUnderflowInIntermediateSquares()
    {
        Assert.True(Parse("hypot(3e200, 4e200)").TryEvaluate(CssLengthContext.Default, 100, out var large));
        Assert.Equal(5, large / 1e200, 12);
        Assert.True(Parse("hypot(3e-200, 4e-200)").TryEvaluate(CssLengthContext.Default, 100, out var small));
        Assert.Equal(5, small / 1e-200, 12);
    }

    private static void Layout(FrameworkElement element, double width = 400)
    {
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        element.Measure(new Size(width, 200));
        element.Arrange(new Rect(0, 0, width, 200));
        element.UpdateLayout();
    }

    [Fact]
    public void Percentages_RemainDependentOnTheirLayoutBasisAfterUnitCancellation()
    {
        var child = new Border { HorizontalAlignment = HorizontalAlignment.Left }; var root = new Border { Child = child };
        Css.SetStyle(child, "width:round(33%, 10px); height:calc(sign(100% - 300px) * 10px + 20px); margin-left:mod(10%, 30px)");
        Layout(root);
        Assert.Equal(130, child.ActualWidth);
        Assert.Equal(10, child.ActualHeight);
        Assert.Equal(10, child.VisualBounds.X);
        Layout(root, 600);
        Assert.Equal(200, child.ActualWidth);
        Assert.Equal(0, child.VisualBounds.X);
    }

    [Fact]
    public void DynamicScalarMath_UsesActualFontContextAndExistingShorthandConverters()
    {
        var element = new TextBlock { FontSize = 20 };
        var root = new Border { Child = element };
        Css.SetStyle(element, "opacity:calc(1em / 100px); transform:rotate(atan2(1em, 20px)); transition:width calc(1em / 1px * 1ms) linear");
        Layout(root);
        Assert.Equal(.2, element.Opacity, 9);
        Assert.Equal(45, Assert.IsType<RotateTransform>(element.RenderTransform).Angle, 8);
        Assert.Equal(TimeSpan.FromMilliseconds(20), element.TransitionDuration.TimeSpan);
        element.FontSize = 40; Layout(root);
        Assert.Equal(.4, element.Opacity, 9);
        Assert.Equal(Math.Atan2(40, 20) * 180 / Math.PI, Assert.IsType<RotateTransform>(element.RenderTransform).Angle, 8);
        Assert.Equal(TimeSpan.FromMilliseconds(40), element.TransitionDuration.TimeSpan);
    }

    [Fact]
    public void RegisteredMath_NormalizesUnitsButRetainsNonlinearPercentageExpressions()
    {
        var child = new Border(); var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --step {syntax:'<length-percentage>'; inherits:true; initial-value:10px} @property --scalar {syntax:'<number>'; inherits:true; initial-value:1}");
        Css.SetStyle(root, "font-size:20px; --step:round(20% + sin(90deg) * 1em, 10px); --scalar:calc(1em / 100px)");
        Css.SetStyle(child, "width:var(--step); opacity:var(--scalar)");
        Layout(root);
        Assert.Equal(100, child.ActualWidth);
        Assert.Equal(.2, child.Opacity, 9);
        Css.SetStyle(root, "font-size:40px; --step:round(20% + sin(90deg) * 1em, 10px); --scalar:calc(1em / 100px)");
        Layout(root);
        Assert.Equal(120, child.ActualWidth);
        Assert.Equal(.4, child.Opacity, 9);
    }

    [Fact]
    public void NativeBindings_RemainAuthoritativeAcrossMathUpdatesAndRemoval()
    {
        var element = new Border(); var source = new Border { Width = 70 };
        var binding = element.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(Border.Width)) { Source = source });
        Css.SetStyle(element, "width:hypot(30px,40px) !important; margin:abs(-4px)");
        Layout(element);
        Assert.Equal(70, element.Width);
        source.Width = 80;
        Css.SetStyle(element, "width:round(99px,20px) !important");
        Assert.Equal(80, element.Width);
        Css.SetStyle(element, string.Empty);
        Assert.Equal(80, element.Width);
        Assert.Same(binding, element.GetBindingExpression(FrameworkElement.WidthProperty));
    }

    [Fact]
    public void GridShorthands_ResolveDynamicFractionsAndRoundIntegerMath()
    {
        var panel = new StackPanel(); var first = new Border(); var second = new Border();
        panel.Children.Add(first); panel.Children.Add(second);
        Css.SetStyle(panel, "font-size:20px; display:grid; grid:20px / calc(1em / 10px * 1fr) 1fr");
        Layout(panel, 300);
        Assert.Equal(200, first.ActualWidth, 6);
        Assert.Equal(100, second.ActualWidth, 6);
        Css.SetStyle(panel, "font-size:10px; display:grid; grid:20px / calc(1em / 10px * 1fr) 1fr");
        Layout(panel, 300);
        Assert.Equal(150, first.ActualWidth, 6);
        Css.SetStyle(panel, "display:grid; grid-template-columns:repeat(calc(1.5), 70px)");
        Layout(panel, 300);
        Assert.Equal(70, second.VisualBounds.X, 6);
    }

    [Fact]
    public void NumericRanges_ClampMathResultsAndRoundIntegerTiesUpward()
    {
        var element = new Border();
        Css.SetStyle(element, "order:calc(2.5); z-index:calc(-2.5); flex-grow:calc(-2); transition-duration:calc(-1s); width:calc(20px / 0)");
        Assert.Equal(3, FlexPanel.GetOrder(element));
        Assert.Equal(-2, Panel.GetZIndex(element));
        Assert.Equal(0, FlexPanel.GetGrow(element));
        Assert.Equal(TimeSpan.Zero, element.TransitionDuration.TimeSpan);
        Assert.Equal(double.MaxValue, element.Width);
        Css.SetStyle(element, "width:calc(0px / 0); opacity:sqrt(-1)");
        Assert.Equal(0, element.Width); Assert.Equal(0, element.Opacity);
    }

    [Fact]
    public void RegisteredNonlinearExpressions_DoNotLoseNaNPropagationDuringSerialization()
    {
        var element = new Border(); var root = new Border { Child = element };
        Css.SetStyleSheet(root, "@property --length { syntax:'<length-percentage>'; inherits:true; initial-value:20px }");
        Css.SetStyle(root, "--length:calc(1px * NaN + 10%)");
        Css.SetStyle(element, "width:var(--length)");
        Layout(root);
        Assert.Equal(0, element.ActualWidth);
    }

    [Fact]
    public void MathSyntax_SupportsRequiredComplexityAndRejectsExcessiveTerms()
    {
        Assert.True(Parse("min(" + string.Join(',', Enumerable.Repeat("10px", 32)) + ")")
            .TryEvaluate(CssLengthContext.Default, 100, out var value));
        Assert.Equal(10, value);
        var expression = "1px";
        for (var i = 0; i < 32; i++) expression = "calc(" + expression + ")";
        Assert.True(Parse(expression).TryEvaluate(CssLengthContext.Default, 100, out value));
        Assert.Equal(1, value);
        var reader = new CssTokenReader("calc(" + string.Join(" + ", Enumerable.Repeat("1px", 600)) + ")");
        Assert.True(reader.TryReadFunction(out var name, out var args));
        Assert.Null(CssMathExpression.Parse(name.ToString(), args.Remaining));
    }
}
