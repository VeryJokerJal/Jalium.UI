using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSAutomationNotificationTests
{
    [Theory]
    [InlineData(Visibility.Hidden)]
    [InlineData(Visibility.Collapsed)]
    public void CachedTextPeerAnnouncesHideAndRestore(Visibility hidden)
    {
        var text = new TextBlock { Text = "可变说明🙂" };
        var peer = text.GetAutomationPeer()!;
        using var sink = new RecordingSink();
        text.Visibility = hidden;
        Assert.False(text.IsVisible);
        text.Visibility = Visibility.Visible;
        Assert.True(text.IsVisible);
        Assert.Equal(2, sink.StructureChanges.Count(p => ReferenceEquals(p, peer)));
        Assert.Same(peer, text.GetAutomationPeer());
        text.Visibility = Visibility.Visible;
        Assert.Equal(2, sink.StructureChanges.Count(p => ReferenceEquals(p, peer)));
    }

    [Fact]
    public void AncestorVisibilityAnnouncesTheCachedDescendant()
    {
        var text = new TextBlock { Text = "祖先隐藏的说明" };
        var parent = new Border { Child = text };
        var peer = text.GetAutomationPeer()!;
        using var sink = new RecordingSink();
        parent.Visibility = Visibility.Collapsed;
        Assert.False(text.IsVisible);
        parent.Visibility = Visibility.Visible;
        Assert.True(text.IsVisible);
        Assert.Equal(2, sink.StructureChanges.Count(p => ReferenceEquals(p, peer)));
    }

    [Fact]
    public void UnqueriedChildAnnouncesItsExistingAncestor()
    {
        var text = new TextBlock { Text = "尚未查询的说明" };
        var parent = new Border { Child = text };
        var parentPeer = parent.GetAutomationPeer()!;
        Assert.Null(text.GetExistingAutomationPeer());
        using var sink = new RecordingSink();
        text.Visibility = Visibility.Hidden;
        text.Visibility = Visibility.Visible;
        Assert.Equal(2, sink.StructureChanges.Count(p => ReferenceEquals(p, parentPeer)));
        Assert.Null(text.GetExistingAutomationPeer());
    }

    [Fact]
    public void VisibilityWithoutAnExistingPeerDoesNotCreateOne()
    {
        var text = new TextBlock { Text = "没有外部查询的说明" };
        using var sink = new RecordingSink();
        text.Visibility = Visibility.Collapsed;
        text.Visibility = Visibility.Visible;
        Assert.Empty(sink.StructureChanges);
        Assert.Null(text.GetExistingAutomationPeer());
    }

    [Fact]
    public void ExplicitOffscreenMetadataDoesNotSuppressTreeVisibilityChanges()
    {
        var text = new TextBlock { Text = "显式几何状态" };
        AutomationProperties.SetIsOffscreenBehavior(text, IsOffscreenBehavior.Onscreen);
        var peer = text.GetAutomationPeer()!;
        using var sink = new RecordingSink();
        text.Visibility = Visibility.Hidden;
        Assert.False(peer.IsOffscreen());
        text.Visibility = Visibility.Visible;
        Assert.Equal(2, sink.StructureChanges.Count(p => ReferenceEquals(p, peer)));
    }

    private sealed class RecordingSink : IAutomationEventSink, IDisposable
    {
        private readonly IAutomationEventSink? _previous = AutomationPeer.EventSink;
        internal List<AutomationPeer> StructureChanges { get; } = [];
        internal RecordingSink() => AutomationPeer.EventSink = this;
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        {
            _previous?.OnAutomationEventRaised(peer, eventId);
            if (eventId == AutomationEvents.StructureChanged) StructureChanges.Add(peer);
        }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
            => _previous?.OnPropertyChangedRaised(peer, property, oldValue, newValue);
        public void OnFocusChanged(AutomationPeer peer) => _previous?.OnFocusChanged(peer);
        public void Dispose() => AutomationPeer.EventSink = _previous;
    }
}
