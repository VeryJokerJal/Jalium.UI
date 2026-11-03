using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssLineWidthRoundTests
{
    [Theory]
    [InlineData("round(line-width, .2px)", 1)]
    [InlineData("round(line-width, -.2px)", -1)]
    [InlineData("round(line-width, 2.8px)", 2)]
    [InlineData("round(line-width, -2.8px)", -2)]
    [InlineData("round(line-width, 0px)", 0)]
    [InlineData("round(line-width, .2px, 1px)", 1)]
    [InlineData("round(line-width, -.2px, 1px)", -1)]
    [InlineData("round(line-width, 3.4px, 2px)", 4)]
    [InlineData("round(line-width, 2.8px, 1px)", 3)]
    [InlineData("round(line-width, 2px, 1px)", 2)]
    [InlineData("round(line-width, 0px, 1px)", 0)]
    [InlineData("round(line-width, -.2px, -1px)", -1)]
    public void SnapsPositiveAndNegativeLengths(string expression, double expected)
    {
        var parsed = Parse(expression);
        Assert.True(parsed.RequiresElementContext);
        Assert.False(parsed.IsAbsolute);
        Assert.True(parsed.TryEvaluate(CssLengthContext.Default, 100, out var actual));
        Assert.Equal(expected, actual, 8);
    }

    [Fact]
    public void UsesTheStyledElementsDevicePixelRatio()
    {
        var border = new Border();
        var context = new CssLengthContext(14, 14, 14, 0, 0, element: CssNode.Get(border));
        var small = Parse("round(line-width, .2px)");
        var large = Parse("round(line-width, 2.8px)");
        var stepped = Parse("round(line-width, 2.8px, 1px)");

        VisualTreeHelper.SetRootDpi(border, new DpiScale(1.25, 1.25));
        Assert.True(small.TryEvaluate(context, 100, out var smallAt125));
        Assert.True(large.TryEvaluate(context, 100, out var largeAt125));
        Assert.True(stepped.TryEvaluate(context, 100, out var steppedAt125));
        Assert.Equal(.8, smallAt125, 8);
        Assert.Equal(2.4, largeAt125, 8);
        Assert.Equal(2.4, steppedAt125, 8);

        VisualTreeHelper.SetRootDpi(border, new DpiScale(1.5, 1.5));
        Assert.True(small.TryEvaluate(context, 100, out var smallAt150));
        Assert.True(large.TryEvaluate(context, 100, out var largeAt150));
        Assert.True(stepped.TryEvaluate(context, 100, out var steppedAt150));
        Assert.Equal(2d / 3, smallAt150, 8);
        Assert.Equal(8d / 3, largeAt150, 8);
        Assert.Equal(8d / 3, steppedAt150, 8);
    }

    [Theory]
    [InlineData("round(line-width, 1)")]
    [InlineData("round(line-width, 1, 2)")]
    [InlineData("round(line-width, 1deg)")]
    [InlineData("round(line-width, 1%, 2%)")]
    [InlineData("round(line-width, 1px, 2deg)")]
    public void RejectsValuesOtherThanLengths(string expression)
    {
        var reader = new CssTokenReader(expression);
        Assert.True(reader.TryReadFunction(out var name, out var arguments));
        Assert.Null(CssMathExpression.Parse(name.ToString(), arguments.Remaining));
    }

    [Fact]
    public void IsAvailableInCssProperties()
    {
        Assert.True(Css.Supports("width", "round(line-width, .2px)"));
        var border = new Border();
        Css.SetStyle(border, "width:round(line-width, .2px)");
        CssEvaluationScheduler.FlushIfPending(border.Dispatcher);
        Assert.Equal(1, border.Width, 8);
    }

    [Fact]
    public void RecomputesNestedElementWhenRootDpiChanges()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(child, "width:round(line-width, .2px)");
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(1, child.Width, 8);

        VisualTreeHelper.SetRootDpi(root, new DpiScale(1.25, 1.25));
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(.8, child.Width, 8);

        VisualTreeHelper.SetRootDpi(root, new DpiScale(1.5, 1.5));
        CssEvaluationScheduler.FlushIfPending(child.Dispatcher);
        Assert.Equal(2d / 3, child.Width, 8);
    }

    private static CssMathExpression Parse(string expression)
    {
        var reader = new CssTokenReader(expression);
        Assert.True(reader.TryReadFunction(out var name, out var arguments));
        Assert.True(reader.AtEnd);
        return Assert.IsType<CssMathExpression>(CssMathExpression.Parse(name.ToString(), arguments.Remaining));
    }
}
