using Jalium.UI.Controls;
using Jalium.UI.Navigation;

namespace Jalium.UI.Tests;

public class NavigationServiceTests
{
    [Fact]
    public void ObjectNavigation_BackAndForward_ShouldReplayCapturedContent()
    {
        var navigationService = new NavigationService();
        var first = new Border();
        var second = new Grid();

        Assert.True(navigationService.Navigate(first));
        Assert.True(navigationService.Navigate(second));

        navigationService.GoBack();
        Assert.Same(first, navigationService.Content);

        navigationService.GoForward();
        Assert.Same(second, navigationService.Content);
    }

    [Fact]
    public void UriNavigation_WithContentLoader_ShouldUseCapturedEntryForBackReplay()
    {
        var loaderCalls = new List<Uri>();
        var navigationService = new NavigationService
        {
            ContentLoader = uri =>
            {
                loaderCalls.Add(uri);
                return new NavigationToken(uri);
            }
        };

        var firstUri = new Uri("https://example.com/first");
        var secondUri = new Uri("https://example.com/second");

        Assert.True(navigationService.Navigate(firstUri));
        var firstContent = Assert.IsType<NavigationToken>(navigationService.Content);

        Assert.True(navigationService.Navigate(secondUri));
        Assert.IsType<NavigationToken>(navigationService.Content);

        navigationService.GoBack();

        // Back replay should use captured journal content, not resolve URI again.
        Assert.Same(firstContent, navigationService.Content);
        Assert.Equal(2, loaderCalls.Count);
    }

    [Fact]
    public void BackReplay_ShouldApplyCustomContentState_ForNonPageContent()
    {
        var state = new ReplayTrackingState();
        var replayable = new ReplayTrackingElement(state);
        var other = new Border();
        var navigationService = new NavigationService();

        Assert.True(navigationService.Navigate(replayable));
        Assert.True(navigationService.Navigate(other));

        navigationService.GoBack();

        Assert.Same(replayable, navigationService.Content);
        Assert.Same(replayable, state.LastReplayContent);
        Assert.Equal(NavigationMode.Back, state.LastReplayMode);
    }

    [Fact]
    public void Navigate_Uri_WhenLoaderThrows_ShouldRaiseNavigationFailed()
    {
        var navigationService = new NavigationService
        {
            ContentLoader = _ => throw new InvalidOperationException("boom")
        };

        var failureRaised = false;
        navigationService.NavigationFailed += (_, e) =>
        {
            failureRaised = true;
            Assert.NotNull(e.Exception);
        };

        var success = navigationService.Navigate(new Uri("https://example.com/fail"));
        Assert.False(success);
        Assert.True(failureRaised);
    }

    private sealed record NavigationToken(Uri Source);

    private sealed class ReplayTrackingElement : ContentControl, IProvideCustomContentState
    {
        private readonly ReplayTrackingState _state;

        public ReplayTrackingElement(ReplayTrackingState state)
        {
            _state = state;
        }

        public CustomContentState? GetContentState() => _state;
    }

    private sealed class ReplayTrackingState : CustomContentState
    {
        public object? LastReplayContent { get; private set; }
        public NavigationMode? LastReplayMode { get; private set; }

        public override string JournalEntryName => "ReplayTrackingState";

        public override void Replay(NavigationService navigationService, NavigationMode mode)
        {
            LastReplayContent = navigationService.Content;
            LastReplayMode = mode;
        }
    }
}
