using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssMediaDisplayModeTests
{
    [Theory]
    [InlineData(":modal", true)]
    [InlineData(":FULLSCREEN", true)]
    [InlineData(":modal()", false)]
    [InlineData(":fullscreen()", false)]
    public void PresentationSelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void FullscreenSelector_FollowsWindowStateAndUpdatesDescendants()
    {
        var child = new Border();
        var root = new Border { Child = child };
        var window = new Window { Content = root, Width = 100, Height = 50 };
        Css.SetStyleSheet(window,
            "Window:fullscreen { opacity:.4 } Window:fullscreen Border { opacity:.6 } " +
            "Window:modal > Border { opacity:.8 }");

        window.Measure(new Size(100, 50));
        window.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(1, window.Opacity);
        Assert.Equal(1, root.Opacity);
        Assert.Equal(1, child.Opacity);

        window.WindowState = WindowState.FullScreen;
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(.4, window.Opacity);
        Assert.Equal(.8, root.Opacity);
        Assert.Equal(.6, child.Opacity);
        var fullscreen = Assert.Single(CssSelectorParser.ParseGroup(":fullscreen", out _)!);
        Assert.True(CssMatcher.Matches(CssNode.Get(window), fullscreen, evaluateStates: true));
        Assert.False(CssMatcher.Matches(CssNode.Get(root), fullscreen, evaluateStates: true));
        Assert.False(CssMatcher.Matches(CssNode.Get(child), fullscreen, evaluateStates: true));

        window.WindowState = WindowState.Normal;
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(1, window.Opacity);
        Assert.Equal(1, root.Opacity);
        Assert.Equal(1, child.Opacity);
    }

    [Fact]
    public void ModalSelector_FollowsWindowShowDialog()
    {
        var window = new Window { Width = 100, Height = 50 };
        Css.SetStyleSheet(window, "Window:modal { opacity:.6 }");
        double opacityDuringDialog = 0;
        window.Dispatcher.BeginInvokeCritical(() =>
        {
            try
            {
                CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
                opacityDuringDialog = window.Opacity;
            }
            finally
            {
                window.Close();
            }
        });

        window.ShowDialog();
        Assert.Equal(.6, opacityDuringDialog);
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "standalone", true)]
    [InlineData(false, "fullscreen", false)]
    [InlineData(false, "minimal-ui", false)]
    [InlineData(false, "browser", false)]
    [InlineData(false, "picture-in-picture", false)]
    [InlineData(true, null, true)]
    [InlineData(true, "standalone", false)]
    [InlineData(true, "fullscreen", true)]
    [InlineData(true, "minimal-ui", false)]
    [InlineData(true, "browser", false)]
    [InlineData(true, "picture-in-picture", false)]
    public void DisplayMode_BooleanAndKeywordSemantics(bool fullscreen, string? value, bool expected)
        => Assert.Equal(expected ? CssQueryResult.True : CssQueryResult.False,
            CssMediaQuery.EvaluateDisplayMode(fullscreen, value));

    [Fact]
    public void DisplayMode_InvalidKeywordIsUnknown()
        => Assert.Equal(CssQueryResult.Unknown,
            CssMediaQuery.EvaluateDisplayMode(false, "invalid"));

    [Fact]
    public void DisplayMode_FollowsHostWindowState()
    {
        var child = new Border();
        var root = new Border { Child = child };
        var window = new Window { Content = root, Width = 100, Height = 50 };
        Css.SetClass(child, "card");
        Css.SetStyleSheet(root, """
            .card { opacity: .3 }
            @media (display-mode: standalone) { .card { opacity: .4 } }
            @media (display-mode: fullscreen) { .card { opacity: .8 } }
            """);

        window.Measure(new Size(100, 50));
        window.Arrange(new Rect(0, 0, 100, 50));
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(.4, child.Opacity);

        window.WindowState = WindowState.FullScreen;
        Assert.True(CssEvaluationScheduler.HasPending(window.Dispatcher));
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(.8, child.Opacity);
        Assert.True(new CssCondition("media", "(display-mode: fullscreen)").Evaluate(child));

        window.WindowState = WindowState.Normal;
        CssEvaluationScheduler.FlushIfPending(window.Dispatcher);
        Assert.Equal(.4, child.Opacity);
        Assert.True(new CssCondition("media", "(display-mode)").Evaluate(child));
        Assert.False(new CssCondition("media", "not (display-mode: invalid)").Evaluate(child));
        Assert.False(new CssCondition("media", "not (display-mode > fullscreen)").Evaluate(child));
    }

    [Fact]
    public void DisplayMode_DetachedNativeTreeUsesStandalone()
    {
        var root = new Border();
        Assert.True(new CssCondition("media", "(display-mode: standalone)").Evaluate(root));
        Assert.False(new CssCondition("media", "(display-mode: browser)").Evaluate(root));
    }
}
