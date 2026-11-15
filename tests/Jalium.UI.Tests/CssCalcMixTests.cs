using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssCalcMixTests
{
    [Theory]
    [InlineData("calc-mix(1)", 1)]
    [InlineData("calc-mix(1 50%, 3 50%)", 2)]
    [InlineData("calc-mix(1 +50%, 3 50%)", 2)]
    [InlineData("calc-mix(1 50%, 3)", 2)]
    [InlineData("calc-mix(1, 3, 5 50%)", 3.5)]
    [InlineData("calc-mix(1 75%, 3 75%)", 2)]
    [InlineData("calc-mix(1 75%, 3 75%, 5, 7)", 2)]
    [InlineData("calc-mix(1 25%, 3 25%)", 1)]
    [InlineData("calc-mix(1 25%, 3 25%, 5, 7)", 4)]
    [InlineData("calc-mix(1 0%, 3 0%)", 0)]
    [InlineData("calc-mix(1 calc(150%), 3 100%)", 2)]
    [InlineData("calc-mix(1 calc(-50%), 3 100%)", 3)]
    [InlineData("calc-mix(1 calc(50% * sibling-index()), 3 50%)", 2)]
    [InlineData("calc-mix(1 * sibling-index() 0%, 3 100%)", 3)]
    [InlineData("calc(sign(calc-mix(1 50%, 3 50%)) + 10)", 11)]
    public void EvaluatesWeightedAverage(string expression, double expected)
    {
        var reader = new CssTokenReader(expression);
        Assert.True(reader.TryReadFunction(out var name, out var arguments));
        Assert.True(reader.AtEnd);
        var parsed = Assert.IsType<CssMathExpression>(CssMathExpression.Parse(name.ToString(), arguments.Remaining));
        var context = new CssLengthContext(14, 14, 14, 0, 0, provisionalTreeCounts: true);
        Assert.True(parsed.TryEvaluate(context, 100, out var actual));
        Assert.Equal(expected, actual, 8);
    }

    [Theory]
    [InlineData("calc-mix()")]
    [InlineData("calc-mix(1,)")]
    [InlineData("calc-mix(1,, 3)")]
    [InlineData("calc-mix(1 50, 3)")]
    [InlineData("calc-mix(1 150%, 3)")]
    [InlineData("calc-mix(1 -50%, 3)")]
    [InlineData("calc-mix(1px 50%, 3 50%)")]
    [InlineData("calc-mix(1px 50%, 3deg 50%)")]
    public void RejectsInvalidSyntaxAndTypes(string expression)
    {
        Assert.False(Css.Supports("opacity", expression));
        var reader = new CssTokenReader(expression);
        Assert.True(reader.TryReadFunction(out var name, out var arguments));
        Assert.Null(CssMathExpression.Parse(name.ToString(), arguments.Remaining));
    }

    [Fact]
    public void ResolvesRelativeLengthsAndPercentagesInLayout()
    {
        var child = new Border();
        var parent = new Border { Width = 100, Child = child };
        Css.SetStyle(child, "font-size:10px;width:calc-mix(10em 50%, 30px 50%)");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        parent.Measure(new(100, 100));
        parent.Arrange(new(0, 0, 100, 100));
        parent.UpdateLayout();
        Assert.Equal(65, child.ActualWidth, 8);

        Css.SetStyle(child, "width:calc-mix(10% 50%, 30% 50%)");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        parent.UpdateLayout();
        Assert.Equal(20, child.ActualWidth, 8);
    }

    [Fact]
    public void PercentageWeightsDoNotUseThePropertyPercentageBasis()
    {
        var parsed = Assert.IsType<CssMathExpression>(CssMathExpression.Parse("calc-mix", "10px 25%, 30px 75%"));
        Assert.False(parsed.UsesPercent);
        Assert.True(parsed.TryEvaluate(CssLengthContext.Default, 200, out var actual));
        Assert.Equal(25, actual, 8);
        Assert.True(Css.Supports("width", "calc-mix(10px 25%, 30px 75%)"));
    }
}
