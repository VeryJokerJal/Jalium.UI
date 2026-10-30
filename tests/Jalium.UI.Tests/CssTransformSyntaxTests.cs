using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTransformSyntaxTests
{
    [Theory]
    [InlineData("scale(150%)", 1.5, 1.5)]
    [InlineData("scale(200%, 50%)", 2, .5)]
    [InlineData("scaleX(75%)", .75, 1)]
    [InlineData("scaleY(0%)", 1, 0)]
    [InlineData("scale(calc(100% + 50%))", 1.5, 1.5)]
    public void ScaleFunctionsAcceptPercentages(string value, double x, double y)
    {
        var reader = new CssTokenReader(value);
        Assert.True(CssTransformParser.TryParseTransformList(ref reader, out var transform));
        Assert.True(reader.AtEnd);
        var scale = Assert.IsType<ScaleTransform>(transform);
        Assert.Equal(x, scale.ScaleX, 10);
        Assert.Equal(y, scale.ScaleY, 10);
    }

    [Theory]
    [InlineData("translate(0)")]
    [InlineData("translateX(-0)")]
    [InlineData("rotate(0)")]
    [InlineData("skewX(0)")]
    public void ZeroMayOmitLengthOrAngleUnit(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.True(CssTransformParser.TryParseTransformList(ref reader, out _));
        Assert.True(reader.AtEnd);
    }

    [Theory]
    [InlineData("translate(10)")]
    [InlineData("translateX(-4)")]
    [InlineData("translateY(calc(5))")]
    [InlineData("rotate(10)")]
    [InlineData("skew(20)")]
    [InlineData("skewX(10)")]
    [InlineData("skewY(10)")]
    [InlineData("scale(10px)")]
    [InlineData("matrix(1,0,0,1,0,10%)")]
    public void TransformFunctionsRejectWrongUnits(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.False(CssTransformParser.TryParseTransformList(ref reader, out _));
    }

    [Fact]
    public void PercentageScaleKeyframesInterpolate()
    {
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes grow {from{transform:scale(100%)}to{transform:scale(200%)}}");
        Css.SetStyle(child, "animation: grow 1s linear both");
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));

        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency / 2);
        var scale = Assert.IsType<ScaleTransform>(child.RenderTransform);
        Assert.InRange(scale.ScaleX, 1.499, 1.501);
        Assert.InRange(scale.ScaleY, 1.499, 1.501);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void CalculatedPercentageScaleResolvesInElementContext()
    {
        var child = new Border { Width = 40, Height = 30 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "transform: scale(calc(100% + 50%))");
        root.Measure(new Size(200, 100));
        root.Arrange(new Rect(0, 0, 200, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        var scale = Assert.IsType<ScaleTransform>(child.RenderTransform);
        Assert.Equal(1.5, scale.ScaleX, 10);
        Assert.Equal(1.5, scale.ScaleY, 10);
    }
}
