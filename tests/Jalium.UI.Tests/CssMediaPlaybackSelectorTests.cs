using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssMediaPlaybackSelectorTests
{
    [Theory]
    [InlineData(":playing", true)]
    [InlineData(":PAUSED", true)]
    [InlineData(":buffering", true)]
    [InlineData(":muted", true)]
    [InlineData(":seeking", false)]
    [InlineData(":stalled", false)]
    [InlineData(":volume-locked", false)]
    [InlineData(":playing()", false)]
    [InlineData(":muted()", false)]
    public void MediaSelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void MediaStates_TrackClockBufferingAndMute()
    {
        using var media = new ProbeMediaElement();
        var playing = Parse(":playing");
        var paused = Parse(":paused");
        var buffering = Parse(":buffering");
        var muted = Parse(":muted");

        Assert.False(CssMatcher.Matches(media, playing, evaluateStates: true));
        Assert.True(CssMatcher.Matches(media, paused, evaluateStates: true));
        Assert.False(CssMatcher.Matches(media, buffering, evaluateStates: true));
        Assert.False(CssMatcher.Matches(media, muted, evaluateStates: true));
        Assert.False(CssMatcher.Matches(new Border(), paused, evaluateStates: true));
        media.Volume = 0;
        Assert.False(CssMatcher.Matches(media, muted, evaluateStates: true));

        var clock = new Jalium.UI.Media.MediaTimeline().CreateClock();
        media.Clock = clock;
        clock.Controller.Begin();
        Assert.True(CssMatcher.Matches(media, playing, evaluateStates: true));
        Assert.False(CssMatcher.Matches(media, paused, evaluateStates: true));

        media.RaiseBufferingStarted();
        Assert.True(CssMatcher.Matches(media, buffering, evaluateStates: true));
        media.IsMuted = true;
        Assert.True(CssMatcher.Matches(media, muted, evaluateStates: true));

        clock.Controller.Pause();
        Assert.False(CssMatcher.Matches(media, buffering, evaluateStates: true));
        Assert.True(CssMatcher.Matches(media, paused, evaluateStates: true));
        media.RaiseBufferingEnded();
        clock.Controller.Resume();
        Assert.False(CssMatcher.Matches(media, buffering, evaluateStates: true));
        Assert.True(CssMatcher.Matches(media, playing, evaluateStates: true));
    }

    [Fact]
    public void MediaStates_RefreshOwnAncestorAndSiblingStyles()
    {
        var root = new StackPanel();
        using var media = new ProbeMediaElement();
        var sibling = new Border();
        root.Children.Add(media);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "MediaElement:paused { opacity:.4 } MediaElement:playing { opacity:.6 } " +
            "MediaElement:buffering { opacity:.8 } MediaElement:muted + Border { opacity:.3 } " +
            "StackPanel:has(> MediaElement:buffering) { opacity:.5 }");
        Flush(root);
        Assert.Equal(.4, media.Opacity);
        Assert.Equal(1, root.Opacity);

        var clock = new Jalium.UI.Media.MediaTimeline().CreateClock();
        media.Clock = clock;
        clock.Controller.Begin();
        Flush(root);
        Assert.Equal(.6, media.Opacity);

        media.RaiseBufferingStarted();
        Flush(root);
        Assert.Equal(.8, media.Opacity);
        Assert.Equal(.5, root.Opacity);

        media.IsMuted = true;
        Flush(root);
        Assert.Equal(.3, sibling.Opacity);

        clock.Controller.Pause();
        Flush(root);
        Assert.Equal(.4, media.Opacity);
        Assert.Equal(1, root.Opacity);

        media.RaiseBufferingEnded();
        media.IsMuted = false;
        Flush(root);
        Assert.Equal(1, sibling.Opacity);
    }

    private static CssSelector Parse(string selector) =>
        Assert.Single(CssSelectorParser.ParseGroup(selector, out _)!);

    private static void Flush(FrameworkElement element) =>
        CssEvaluationScheduler.FlushIfPending(element.Dispatcher);

    private sealed class ProbeMediaElement : MediaElement
    {
        public void RaiseBufferingStarted() => OnBufferingStarted();
        public void RaiseBufferingEnded() => OnBufferingEnded();
    }
}
