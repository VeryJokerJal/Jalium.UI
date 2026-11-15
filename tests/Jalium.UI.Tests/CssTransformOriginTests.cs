using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssTransformOriginTests
{
    [Theory]
    [InlineData("left top", 0, 0)]
    [InlineData("top left", 0, 0)]
    [InlineData("right bottom", 1, 1)]
    [InlineData("top", .5, 0)]
    [InlineData("25%", .25, .5)]
    [InlineData("25px 50%", .25, .5)]
    [InlineData("left 10px", 0, .25)]
    [InlineData("calc(50% + 10px) calc(50% - 5px)", .6, .375)]
    public void PositionValuesResolveAgainstTheElementBox(string origin, double expectedX, double expectedY)
    {
        var child = new Border { Width = 100, Height = 40 };
        var root = new Border { Child = child };
        Css.SetStyle(child, $"transform-origin: {origin}");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(expectedX, child.RenderTransformOrigin.X, 10);
        Assert.Equal(expectedY, child.RenderTransformOrigin.Y, 10);
    }

    [Fact]
    public void PixelOriginTracksElementResize()
    {
        var child = new Border { Width = 100, Height = 40 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "transform-origin: 25px 10px");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(new Point(.25, .25), child.RenderTransformOrigin);

        child.Width = 200;
        child.Height = 80;
        root.InvalidateMeasure();
        root.InvalidateArrange();
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(new Point(.125, .125), child.RenderTransformOrigin);
    }

    [Fact]
    public void FontRelativeOriginTracksFontSize()
    {
        var child = new Border { Width = 100, Height = 40 };
        var root = new Border { Child = child };
        Css.SetStyle(child, "font-size: 20px; transform-origin: 1em 10px");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(new Point(.2, .25), child.RenderTransformOrigin);

        Css.SetStyle(child, "font-size: 30px; transform-origin: 1em 10px");
        Assert.Equal(new Point(.3, .25), child.RenderTransformOrigin);
    }

    [Fact]
    public void PixelOriginMovesRotatedRenderBounds()
    {
        var child = new Border { Width = 100, Height = 40 };
        var root = new Border { Child = child };
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        var before = child.GetRenderBounds();

        Css.SetStyle(child, "transform: rotate(180deg); transform-origin: 25px 20px");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        var after = child.GetRenderBounds();
        Assert.Equal(.25, child.RenderTransformOrigin.X, 10);
        Assert.InRange(after.X - before.X, -50.1, -49.9);
        Assert.InRange(after.Y - before.Y, -.1, .1);
    }

    [Theory]
    [InlineData("10px 20px 30px")]
    [InlineData("left 10px top 20px")]
    [InlineData("10 20px")]
    public void UnsupportedOrInvalidFormsLeaveTheUnderlyingOrigin(string origin)
    {
        var child = new Border { Width = 100, Height = 40, RenderTransformOrigin = new Point(.3, .7) };
        var root = new Border { Child = child };
        Css.SetStyle(child, $"transform-origin: {origin}");
        root.Measure(new Size(300, 100));
        root.Arrange(new Rect(0, 0, 300, 100));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(new Point(.3, .7), child.RenderTransformOrigin);
    }
}
