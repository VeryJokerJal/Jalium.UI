using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class MediaPlayerParityTests
{
    [Fact]
    public void MediaPlayerExposesWpfStateAndFreezableContract()
    {
        using var player = new MediaPlayer();

        Assert.IsAssignableFrom<Animatable>(player);
        Assert.Null(player.Source);
        Assert.Equal(Duration.Automatic, player.NaturalDuration);
        Assert.False(player.CanPause);
        Assert.False(player.IsBuffering);
        Assert.Equal(0, player.BufferingProgress);
        Assert.Equal(0, player.DownloadProgress);

        var sourceProperty = typeof(MediaPlayer).GetProperty(nameof(MediaPlayer.Source))!;
        Assert.True(sourceProperty.CanRead);
        Assert.False(sourceProperty.CanWrite);

        Assert.NotNull(typeof(MediaPlayer).GetEvent(nameof(MediaPlayer.BufferingStarted)));
        Assert.NotNull(typeof(MediaPlayer).GetEvent(nameof(MediaPlayer.BufferingEnded)));
        Assert.Equal(
            typeof(EventHandler<MediaScriptCommandEventArgs>),
            typeof(MediaPlayer).GetEvent(nameof(MediaPlayer.ScriptCommand))!.EventHandlerType);

        const BindingFlags declaredProtected =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(MediaPlayer).GetMethod("ReadPreamble", declaredProtected));
        Assert.NotNull(typeof(MediaPlayer).GetMethod("WritePreamble", declaredProtected));
        Assert.NotNull(typeof(MediaPlayer).GetMethod("CreateInstanceCore", declaredProtected));
        Assert.NotNull(typeof(MediaPlayer).GetMethod("CloneCore", declaredProtected));
        Assert.NotNull(typeof(MediaPlayer).GetMethod("CloneCurrentValueCore", declaredProtected));
        Assert.NotNull(typeof(MediaPlayer).GetMethod("GetAsFrozenCore", declaredProtected));
    }

    [Fact]
    public void ClonePreservesPlaybackConfigurationWithoutSharingRuntimeResources()
    {
        using var player = new MediaPlayer
        {
            Volume = 0.35,
            Balance = -0.2,
            IsMuted = true,
            SpeedRatio = 1.5,
            ScrubbingEnabled = true,
            Clock = new Jalium.UI.Media.MediaTimeline().CreateClock(),
        };

        using MediaPlayer clone = player.CloneCurrentValue();

        Assert.NotSame(player, clone);
        Assert.Equal(player.Volume, clone.Volume);
        Assert.Equal(player.Balance, clone.Balance);
        Assert.Equal(player.IsMuted, clone.IsMuted);
        Assert.Equal(player.SpeedRatio, clone.SpeedRatio);
        Assert.Equal(player.ScrubbingEnabled, clone.ScrubbingEnabled);
        Assert.Same(player.Clock, clone.Clock);
    }

    [Fact]
    public void MediaTimelineClonesUriContextAndAllocatesMediaClock()
    {
        var timeline = new Jalium.UI.Media.MediaTimeline(new Uri("media/sample.mp4", UriKind.Relative));
        ((Jalium.UI.Markup.IUriContext)timeline).BaseUri = new Uri("https://example.test/assets/");

        var clone = timeline.Clone();
        Clock clock = ((Timeline)timeline).CreateClock();

        Assert.NotSame(timeline, clone);
        Assert.Equal(timeline.Source, clone.Source);
        Assert.Equal(
            ((Jalium.UI.Markup.IUriContext)timeline).BaseUri,
            ((Jalium.UI.Markup.IUriContext)clone).BaseUri);
        Assert.IsType<Jalium.UI.Media.MediaClock>(clock);
        Assert.Equal(Duration.Automatic, clock.NaturalDuration);
    }
}
