using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssProgressFunctionTests
{
    [Fact]
    public void ParsesConsistentNumberAndDimensionArguments()
    {
        Assert.True(Css.Supports("opacity", "progress(25, 0, 100)"));
        Assert.True(Css.Supports("width", "calc(progress(50px, 0px, 100px) * 20px)"));
        Assert.True(Css.Supports("width", "calc(progress(50%, 0px, 100px) * 10px)"));
        Assert.True(Css.Supports("opacity", "progress(50px, 0px, 100px)"));
        Assert.True(Css.Supports("opacity", "progress(no-clamp 25, 0, 100)"));
        Assert.False(Css.Supports("opacity", "progress(1, 0)"));
        Assert.False(Css.Supports("opacity", "progress(1, 0, 1, 2)"));
        Assert.False(Css.Supports("opacity", "progress(1, 0px, 1px)"));
        Assert.False(Css.Supports("opacity", "progress(no-clamp, 1, 0, 1)"));
        Assert.False(Css.Supports("opacity", "progress(1 no-clamp, 0, 1)"));
        Assert.False(Css.Supports("opacity", "progress(10px * 10px, 0px * 0px, 20px * 20px)"));
    }

    [Theory]
    [InlineData("progress(25, 0, 100)", .25)]
    [InlineData("progress(150, 0, 100)", 1)]
    [InlineData("progress(-10, 0, 100)", 0)]
    [InlineData("progress(no-clamp 150, 0, 100)", 1.5)]
    [InlineData("progress(no-clamp -10, 0, 100)", -.1)]
    [InlineData("progress(75, 100, 0)", .25)]
    [InlineData("progress(50%, 0%, 100%)", .5)]
    [InlineData("progress(1s, 0s, 2s)", .5)]
    [InlineData("progress(90deg, 0deg, 180deg)", .5)]
    [InlineData("progress(20, 10, 10)", 0)]
    [InlineData("progress(no-clamp 10, 10, 10)", 0)]
    [InlineData("min(progress(no-clamp 20, 10, 10), .7)", .7)]
    [InlineData("max(progress(no-clamp 0, 10, 10), .2)", .2)]
    public void EvaluatesClampingReversedRangesAndEqualEndpoints(string expression, double expected)
    {
        var reader = new CssTokenReader(expression);
        Assert.True(reader.TryReadFunction(out var name, out var arguments));
        Assert.True(reader.AtEnd);
        var parsed = Assert.IsType<CssMathExpression>(CssMathExpression.Parse(name.ToString(), arguments.Remaining));
        Assert.True(parsed.TryEvaluate(CssLengthContext.Default, 100, out var actual));
        Assert.Equal(expected, actual, 8);
    }

    [Fact]
    public void WorksInsideLengthCalculationsAndUpdatesWithFontSize()
    {
        var element = new Border();
        Css.SetStyle(element, "font-size:10px;width:calc(progress(50px, 0px, 10em) * 100px)");
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        Assert.Equal(50, element.Width, 8);
        Css.SetStyle(element, "font-size:20px;width:calc(progress(50px, 0px, 10em) * 100px)");
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
        Assert.Equal(25, element.Width, 8);
    }

    [Fact]
    public void CombinesWithNestedMathAndLengthPercentages()
    {
        var child = new Border();
        var parent = new Border { Width = 100, Child = child };
        Css.SetStyle(child, "width:calc(progress(50%, 0px, 100px) * 10px)");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        parent.Measure(new(100, 100));
        parent.Arrange(new(0, 0, 100, 100));
        parent.UpdateLayout();
        Assert.Equal(5, child.ActualWidth, 8);
        Css.SetStyle(child, "width:calc(progress(progress(1, 0, 1), progress(0px, 0px, 1px), progress(1deg, 0deg, 1deg)) * 10px)");
        CssEvaluationScheduler.FlushIfPending(parent.Dispatcher);
        parent.UpdateLayout();
        Assert.Equal(10, child.ActualWidth, 8);
    }
}
