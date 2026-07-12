using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Navigation;
using WpfNavigationMode = Jalium.UI.Navigation.NavigationMode;

namespace Jalium.UI.Tests;

public sealed class FrameWpfParityTests
{
    [Fact]
    public void ApiSurface_ExposesWpfNavigationContractsAndReadOnlyState()
    {
        var type = typeof(Frame);

        Assert.Equal(typeof(NavigationService), type.GetProperty(nameof(Frame.NavigationService))!.PropertyType);
        Assert.Equal(typeof(Jalium.UI.Navigation.JournalOwnership), type.GetProperty(nameof(Frame.JournalOwnership))!.PropertyType);
        Assert.Equal(typeof(Jalium.UI.Navigation.NavigationUIVisibility), type.GetProperty(nameof(Frame.NavigationUIVisibility))!.PropertyType);
        Assert.Equal(typeof(IEnumerable), type.GetProperty(nameof(Frame.BackStack))!.PropertyType);
        Assert.Equal(typeof(void), type.GetMethod(nameof(Frame.GoBack), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(void), type.GetMethod(nameof(Frame.GoForward), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(typeof(NavigatingCancelEventHandler), type.GetEvent(nameof(Frame.Navigating))!.EventHandlerType);
        Assert.Equal(typeof(NavigatedEventHandler), type.GetEvent(nameof(Frame.Navigated))!.EventHandlerType);
        Assert.Equal("Jalium.UI.Navigation", typeof(NavigationService).Namespace);
        Assert.Equal("Jalium.UI.Navigation", typeof(JournalEntry).Namespace);

        var frame = new Frame();
        Assert.Throws<InvalidOperationException>(() => frame.SetValue(Frame.CanGoBackProperty, true));
        Assert.Throws<InvalidOperationException>(() => frame.SetValue(Frame.BackStackProperty, Array.Empty<object>()));
    }

    [Fact]
    public void ObjectNavigation_UpdatesJournalContentAndExactEvents()
    {
        var frame = new Frame();
        var first = new object();
        var second = new object();
        var modes = new List<WpfNavigationMode>();
        var rendered = 0;
        frame.Navigating += (_, e) => modes.Add(e.NavigationMode);
        frame.ContentRendered += (_, _) => rendered++;

        Assert.True(frame.Navigate(first, "first-data"));
        Assert.True(frame.Navigate(second, "second-data"));
        Assert.Same(second, frame.Content);
        Assert.True(frame.CanGoBack);
        Assert.False(frame.CanGoForward);
        Assert.Single(frame.BackStack.Cast<JournalEntry>());

        frame.GoBack();
        Assert.Same(first, frame.Content);
        Assert.False(frame.CanGoBack);
        Assert.True(frame.CanGoForward);

        frame.GoForward();
        Assert.Same(second, frame.Content);
        Assert.Equal(
            [WpfNavigationMode.New, WpfNavigationMode.New, WpfNavigationMode.Back, WpfNavigationMode.Forward],
            modes);
        Assert.Equal(4, rendered);
    }

    [Fact]
    public void UriNavigation_UsesBaseUriLoaderProgressFragmentAndFailureEvents()
    {
        var frame = new ProbeFrame
        {
            ExposedBaseUri = new Uri("https://example.test/root/"),
        };
        var loaded = new object();
        var progress = 0;
        string? fragment = null;
        NavigationFailedEventArgs? failure = null;
        frame.ContentLoader = uri => uri.AbsolutePath.EndsWith("page", StringComparison.Ordinal)
            ? loaded
            : null;
        frame.NavigationProgress += (_, _) => progress++;
        frame.FragmentNavigation += (_, e) => fragment = e.Fragment;
        frame.NavigationFailed += (_, e) =>
        {
            failure = e;
            e.Handled = true;
        };

        frame.Source = new Uri("page#section", UriKind.Relative);

        Assert.Same(loaded, frame.Content);
        Assert.Equal(new Uri("https://example.test/root/page#section"), frame.CurrentSource);
        Assert.Equal("section", fragment);
        Assert.Equal(1, progress);

        Assert.False(frame.Navigate(new Uri("https://example.test/missing")));
        Assert.NotNull(failure);
        Assert.Equal(new Uri("https://example.test/missing"), failure!.Uri);
        Assert.Same(loaded, frame.Content);
    }

    [Fact]
    public void CancellationAndCustomContentState_AreAppliedTransactionally()
    {
        var replay = new ReplayState();
        var first = new StatefulContent(replay);
        var second = new object();
        var frame = new Frame();

        Assert.True(frame.Navigate(first));
        Assert.True(frame.Navigate(second));

        var cancelBack = true;
        NavigatingCancelEventHandler handler = (_, e) =>
        {
            if (e.NavigationMode == WpfNavigationMode.Back && cancelBack)
            {
                e.Cancel = true;
            }
        };
        frame.Navigating += handler;

        frame.GoBack();
        Assert.Same(second, frame.Content);
        Assert.True(frame.CanGoBack);
        Assert.Equal(0, replay.Count);

        cancelBack = false;
        frame.GoBack();
        Assert.Same(first, frame.Content);
        Assert.Equal(1, replay.Count);
        Assert.Same(frame.NavigationService, replay.Service);
        Assert.Equal(WpfNavigationMode.Back, replay.Mode);
        frame.Navigating -= handler;
    }

    [Fact]
    public void TypedPageNavigation_UsesCacheAndPageLifecycle()
    {
        var frame = new Frame();

#pragma warning disable IL2026
        Assert.True(frame.Navigate(typeof(ProbePage), "alpha"));
        var first = Assert.IsType<ProbePage>(frame.Content);
        Assert.Equal("alpha", first.NavigationParameter);
        Assert.Same(frame, first.Frame);

        Assert.True(frame.Navigate(typeof(OtherProbePage), "beta"));
        frame.GoBack();
#pragma warning restore IL2026

        Assert.Same(first, frame.Content);
        Assert.Equal("alpha", first.NavigationParameter);
    }

    private sealed class ProbeFrame : Frame
    {
        public Uri? ExposedBaseUri
        {
            get => BaseUri;
            set => BaseUri = value;
        }
    }

    private sealed class StatefulContent : IProvideCustomContentState
    {
        private readonly CustomContentState _state;
        public StatefulContent(CustomContentState state) => _state = state;
        public CustomContentState? GetContentState() => _state;
    }

    private sealed class ReplayState : CustomContentState
    {
        public override string JournalEntryName => "Replay state";
        public int Count { get; private set; }
        public NavigationService? Service { get; private set; }
        public WpfNavigationMode Mode { get; private set; }

        public override void Replay(NavigationService navigationService, WpfNavigationMode mode)
        {
            Count++;
            Service = navigationService;
            Mode = mode;
        }
    }

    private sealed class ProbePage : Page
    {
        public ProbePage() => NavigationCacheMode = NavigationCacheMode.Required;
    }

    private sealed class OtherProbePage : Page
    {
    }
}
