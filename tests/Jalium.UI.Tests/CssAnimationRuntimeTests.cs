using System.Diagnostics;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssAnimationRuntimeTests
{
    [Theory]
    [InlineData("animation", "fade 1s linear both", true)]
    [InlineData("animation", "fade 1s, spin 2s alternate infinite", true)]
    [InlineData("animation-duration", "-1s", false)]
    [InlineData("animation-duration", "calc(-1s)", true)]
    [InlineData("animation-delay", "-250ms", true)]
    [InlineData("animation-iteration-count", "-1", false)]
    [InlineData("animation-iteration-count", "calc(-1)", true)]
    [InlineData("animation-direction", "sideways", false)]
    public void SupportsReportsAnimationGrammar(string property, string value, bool expected)
        => Assert.Equal(expected, Css.Supports(property, value));

    [Fact]
    public void ShorthandStartsSamplesAndRestoresUnderlyingValue()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "opacity: .2; animation: fade 1s linear both");

        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(0, child.Opacity);
        Assert.True(runner.Render(runner.StartTimestamp + Stopwatch.Frequency / 2));
        Assert.InRange(child.Opacity, .49, .51);
        Assert.False(runner.Render(runner.StartTimestamp + Stopwatch.Frequency));
        Assert.Equal(1, child.Opacity);

        Css.SetStyle(child, "opacity: .2");
        Assert.Equal(.2, child.Opacity);
        Assert.Null(child.CssRuntimeState?.AnimationRunner);
    }

    [Fact]
    public void PausingKeepsProgressAndImportantDeclarationWins()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "animation: fade 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        var start = runner.StartTimestamp;
        AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency / 4, _ =>
            Css.SetStyle(child, "animation: fade 1s linear both; animation-play-state: paused; opacity: .7 !important"));
        Assert.Same(runner, child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(.7, child.Opacity);
        AnimationManager.ProcessFrame(start + Stopwatch.Frequency);
        Assert.Equal(.7, child.Opacity);

        AnimationEngineTests.RunInsideFrame(start + Stopwatch.Frequency, _ =>
            Css.SetStyle(child, "animation: fade 1s linear both; animation-play-state: running"));
        Assert.Same(runner, child.CssRuntimeState?.AnimationRunner);
        Assert.InRange(child.Opacity, .24, .26);
        AnimationManager.ProcessFrame(start + Stopwatch.Frequency * 5 / 4);
        Assert.InRange(child.Opacity, .49, .51);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void ListsRepeatTimingValuesAndFirstAnimationWinsOverLaterOne()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, """
            @keyframes first { from { opacity: 0 } to { opacity: 1 } }
            @keyframes second { from { opacity: .2 } to { opacity: .2 } }
            """);
        Css.SetStyle(child, "animation-name: first, second; animation-duration: 1s; " +
            "animation-timing-function: linear; animation-delay: -250ms; animation-fill-mode: both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        Assert.InRange(child.Opacity, .24, .27);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .74, .76);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void NoneListEntryDoesNotRestartAnUnchangedAnimation()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "animation-name: none, fade; animation-duration: 1s; opacity: .2");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        var start = runner.StartTimestamp;

        Css.SetStyle(child, "animation-name: none, fade; animation-duration: 1s; opacity: .3");
        Assert.Same(runner, child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(start, runner.StartTimestamp);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void CentralAnimationManagerAdvancesCssKeyframes()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "animation: fade 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        AnimationManager.ProcessFrame(runner.StartTimestamp + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .49, .51);
        AnimationManager.ProcessFrame(runner.StartTimestamp + Stopwatch.Frequency);
        Assert.Equal(1, child.Opacity);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void ReplacingKeyframesRuleUsesTheNewTrack()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "animation: fade 1s linear forwards");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: .4 } }");
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Same(runner, child.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency);
        Assert.InRange(child.Opacity, .39, .41);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void RecyclingDiscardsCssAnimationAndReevaluationRestartsIt()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(child, "opacity: .2; animation: fade 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(0, child.Opacity);

        child.StopAnimationsForRecycleRecursive();
        Assert.Null(child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(.2, child.Opacity);
        Css.SetStyle(child, "opacity: .3; animation: fade 1s linear both");
        Assert.NotSame(runner, child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(0, child.Opacity);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void CssWideInheritCopiesParentConfigurationAndInitialStopsPlayback()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } to { opacity: 1 } }");
        Css.SetStyle(root, "animation: fade 2s linear both");
        Css.SetStyle(child, "animation: inherit; opacity: .3");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency);
        Assert.InRange(child.Opacity, .49, .51);

        Css.SetStyle(child, "animation: initial; opacity: .3");
        Assert.Null(child.CssRuntimeState?.AnimationRunner);
        Assert.Equal(.3, child.Opacity);
        Css.SetStyle(root, "");
    }

    [Fact]
    public void QuotedNoneCanNameAKeyframesRule()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes \"none\" { from {opacity:0} to {opacity:.8} }");
        Css.SetStyle(child, "animation: \"none\" 1s linear forwards");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency);
        Assert.InRange(child.Opacity, .79, .81);

        Css.SetStyle(child, "animation-name: none; animation-duration: 1s");
        Assert.Null(child.CssRuntimeState?.AnimationRunner);
    }

    [Fact]
    public void AnimatedInheritedFontSizeReachesInlineChild()
    {
        var run = new Run("hello");
        var block = new TextBlock(run);
        Css.SetStyleSheet(block, "@keyframes grow { from {font-size:10px} to {font-size:30px} }");
        Css.SetStyle(block, "animation: grow 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(block.CssRuntimeState?.AnimationRunner);
        runner.Render(runner.StartTimestamp + Stopwatch.Frequency / 2);

        Assert.InRange(block.FontSize, 19.9, 20.1);
        Assert.Equal(block.FontSize, run.FontSize);
        Css.SetStyle(block, "");
    }

    [Fact]
    public void ChangingUnderlyingValueRefreshesImplicitEndpointWithoutRestartingClock()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade { from { opacity: 0 } }");
        Css.SetStyle(child, "opacity: .2; animation: fade 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        var start = runner.StartTimestamp;
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .09, .11);

        Css.SetStyle(child, "opacity: .4; animation: fade 1s linear both");
        Assert.Equal(start, runner.StartTimestamp);
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .19, .21);
        Css.SetStyle(child, "");
    }

    [Fact]
    public void ChangingCustomPropertyRefreshesKeyframeValuesWithoutRestartingClock()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@keyframes fade {from{opacity:0}to{opacity:var(--target)}}");
        Css.SetStyle(child, "--target: .4; animation: fade 1s linear both");
        var runner = Assert.IsType<CssAnimationRunner>(child.CssRuntimeState?.AnimationRunner);
        var start = runner.StartTimestamp;
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .19, .21);

        Css.SetStyle(child, "--target: .8; animation: fade 1s linear both");
        Assert.Equal(start, runner.StartTimestamp);
        runner.Render(start + Stopwatch.Frequency / 2);
        Assert.InRange(child.Opacity, .39, .41);
        Css.SetStyle(child, "");
    }
}
