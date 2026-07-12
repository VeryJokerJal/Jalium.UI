using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using MediaTimeline = Jalium.UI.Media.MediaTimeline;

namespace Jalium.UI.Tests;

public sealed class MediaElementRemainingWpfParityTests
{
    [Fact]
    public void TierOneSurface_MatchesWpfMetadataEventsAndAutomationContract()
    {
        var type = typeof(MediaElement);
        Assert.Contains(typeof(IUriContext), type.GetInterfaces());
        Assert.Null(type.GetProperty("BaseUri", BindingFlags.Instance | BindingFlags.Public));
        var interfaceMap = type.GetInterfaceMap(typeof(IUriContext));
        var baseUriGetter = interfaceMap.TargetMethods.Single(method =>
            method.Name.EndsWith("get_BaseUri", StringComparison.Ordinal));
        Assert.True(baseUriGetter.IsPrivate);
        Assert.True(baseUriGetter.IsFinal);
        Assert.True(baseUriGetter.IsVirtual);

        Assert.Equal(typeof(MediaClock), type.GetProperty(nameof(MediaElement.Clock))!.PropertyType);
        Assert.Null(type.GetField("ClockProperty", BindingFlags.Public | BindingFlags.Static));
        Assert.Equal(typeof(double), type.GetProperty(nameof(MediaElement.DownloadProgress))!.PropertyType);
        Assert.Null(type.GetProperty(nameof(MediaElement.DownloadProgress))!.SetMethod);

        AssertRoutedEvent(MediaElement.BufferingStartedEvent, typeof(RoutedEventHandler));
        AssertRoutedEvent(MediaElement.BufferingEndedEvent, typeof(RoutedEventHandler));
        AssertRoutedEvent(
            MediaElement.ScriptCommandEvent,
            typeof(EventHandler<MediaScriptCommandRoutedEventArgs>));
        Assert.Equal(
            typeof(EventHandler<ExceptionRoutedEventArgs>),
            MediaElement.MediaFailedEvent.HandlerType);

        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            MediaElement.SourceProperty.GetMetadata(type));
        Assert.True(metadata.AffectsMeasure);
        Assert.True(metadata.AffectsRender);

        var createPeer = type.GetMethod(
            "OnCreateAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(createPeer);
        Assert.True(createPeer!.IsFamily);
        Assert.True(createPeer.IsVirtual);

        using var element = new MediaElement();
        var peer = Assert.IsType<MediaElementAutomationPeer>(element.GetAutomationPeer());
        Assert.Equal(AutomationControlType.Custom, peer.GetAutomationControlType());
        Assert.Equal(nameof(MediaElement), peer.GetClassName());
        Assert.True(peer.IsContentElement());
    }

    [Fact]
    public void MediaTimeline_CreateClockClonesTimingAndControllerDrivesElement()
    {
        var timeline = new MediaTimeline
        {
            BeginTime = TimeSpan.FromSeconds(2),
            AutoReverse = true,
            SpeedRatio = 1.5,
        };
        var context = (IUriContext)timeline;
        context.BaseUri = new Uri("https://example.test/media/");

        var clock = timeline.CreateClock();

        Assert.NotSame(timeline, clock.Timeline);
        Assert.Equal(timeline.BeginTime, clock.Timeline.BeginTime);
        Assert.Equal(timeline.AutoReverse, clock.Timeline.AutoReverse);
        Assert.Equal(timeline.SpeedRatio, clock.Timeline.SpeedRatio);
        Assert.Equal(context.BaseUri, ((IUriContext)clock.Timeline).BaseUri);
        Assert.NotNull(clock.Controller);

        using var element = new MediaElement { Clock = clock };
        clock.Controller.Begin();
        Assert.True(element.IsPlaying);

        clock.Controller.Pause();
        Assert.False(element.IsPlaying);

        clock.Controller.Resume();
        Assert.True(element.IsPlaying);

        var seek = TimeSpan.FromSeconds(8);
        clock.Controller.Seek(seek, Jalium.UI.Media.Animation.TimeSeekOrigin.BeginTime);
        Assert.Equal(seek, element.Position);

        clock.Controller.Stop();
        Assert.False(element.IsPlaying);
        Assert.Equal(TimeSpan.Zero, element.Position);
    }

    [Fact]
    public void ClockBlocksSourceChangesAndBaseUriResolutionUsesUriSemantics()
    {
        using var element = new MediaElement();
        var context = (IUriContext)element;
        var baseUri = new Uri("https://example.test/media/");
        context.BaseUri = baseUri;
        Assert.Same(baseUri, context.BaseUri);

        var relative = new Uri("clips/intro.mp4", UriKind.Relative);
        Assert.Equal(
            new Uri("https://example.test/media/clips/intro.mp4"),
            MediaElement.ResolveSourceUri(relative, baseUri));

        element.Clock = new MediaTimeline().CreateClock();
        Assert.Throws<InvalidOperationException>(() => element.Source = relative);
        Assert.Equal(relative, element.Source);
        Assert.Equal(0.0, element.DownloadProgress);
    }

    [Fact]
    public void BufferingAndScriptEventsBubbleWithCanonicalArguments()
    {
        var parent = new StackPanel();
        using var element = new ProbeMediaElement();
        parent.Children.Add(element);
        var order = new List<string>();
        MediaScriptCommandRoutedEventArgs? received = null;

        element.BufferingStarted += (_, _) => order.Add("element-start");
        parent.AddHandler(
            MediaElement.BufferingStartedEvent,
            new RoutedEventHandler((_, _) => order.Add("parent-start")));
        element.ScriptCommand += (_, e) =>
        {
            order.Add("element-script");
            received = e;
        };
        parent.AddHandler(
            MediaElement.ScriptCommandEvent,
            new EventHandler<MediaScriptCommandRoutedEventArgs>((_, _) => order.Add("parent-script")));

        element.RaiseBufferingStarted();
        Assert.True(element.IsBuffering);
        Assert.Equal(0.0, element.BufferingProgress);

        element.RaiseScriptCommand("text", "chapter-2");
        element.RaiseBufferingEnded();

        Assert.False(element.IsBuffering);
        Assert.Equal(1.0, element.BufferingProgress);
        Assert.Equal(
            new[] { "element-start", "parent-start", "element-script", "parent-script" },
            order);
        Assert.NotNull(received);
        Assert.Equal("text", received!.ParameterType);
        Assert.Equal("chapter-2", received.ParameterValue);
        Assert.Same(element, received.Source);
        Assert.Same(MediaElement.ScriptCommandEvent, received.RoutedEvent);
    }

    private static void AssertRoutedEvent(RoutedEvent routedEvent, Type handlerType)
    {
        Assert.Equal(RoutingStrategy.Bubble, routedEvent.RoutingStrategy);
        Assert.Equal(handlerType, routedEvent.HandlerType);
    }

    private sealed class ProbeMediaElement : MediaElement
    {
        public void RaiseBufferingStarted() => OnBufferingStarted();

        public void RaiseBufferingEnded() => OnBufferingEnded();

        public void RaiseScriptCommand(string parameterType, string parameterValue) =>
            OnScriptCommand(new MediaScriptCommandEventArgs(parameterType, parameterValue));
    }
}
