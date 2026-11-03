using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssMediaResolutionTests
{
    [Theory]
    [InlineData("(resolution: 1.5dppx)", true)]
    [InlineData("(resolution: 1.5x)", true)]
    [InlineData("(resolution: calc(96dpi + .5dppx))", true)]
    [InlineData("(min-resolution: 144dpi)", true)]
    [InlineData("(resolution >= 56dpcm)", true)]
    [InlineData("(2dppx > resolution > 1dppx)", true)]
    [InlineData("(resolution > 2dppx)", false)]
    [InlineData("(resolution: 144px)", false)]
    [InlineData("not (resolution: 144px)", false)]
    [InlineData("(resolution: -1dppx)", false)]
    [InlineData("(resolution > -1dppx)", true)]
    [InlineData("(resolution)", true)]
    [InlineData("(resolution: infinite)", false)]
    [InlineData("(resolution < infinite)", true)]
    public void ResolutionMediaQuery_UsesVerticalDevicePixelRatio(string query, bool expected)
    {
        var root = new Border { Width = 100, Height = 50 };
        VisualTreeHelper.SetRootDpi(root, new DpiScale(1.25, 1.5));

        Assert.Equal(expected, new CssCondition("media", query).Evaluate(root));
    }

    [Fact]
    public void ResolutionMediaQuery_ReevaluatesWhenRootDpiChanges()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .8 }
            @media (min-resolution: 2dppx) { .card { opacity: .4 } }
            """);

        Layout(root);
        Assert.Equal(.8, child.Opacity);

        VisualTreeHelper.SetRootDpi(root, new DpiScale(2, 2));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(.4, child.Opacity);

        VisualTreeHelper.SetRootDpi(root, new DpiScale(1, 1));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
        Assert.Equal(.8, child.Opacity);
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(100, 50));
        root.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);
    }
}
