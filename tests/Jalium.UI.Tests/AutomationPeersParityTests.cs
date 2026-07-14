using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("AutomationEventSink")]
public sealed class AutomationPeersParityTests
{
    [Fact]
    public void PeerTypes_AreExposedFromWpfMappedNamespace()
    {
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(AutomationPeer).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(UIElementAutomationPeer).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(FrameworkElementAutomationPeer).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(ContentElementAutomationPeer).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(GenericRootAutomationPeer).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(AutomationControlType).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(PatternInterface).Namespace);
        Assert.Equal("Jalium.UI.Automation.Peers", typeof(AutomationEvents).Namespace);
        Assert.Null(typeof(AutomationPeer).Assembly.GetType("Jalium.UI.Automation.AutomationPeer"));
    }

    [Fact]
    public void PeerEnums_UseWpfNumericContracts()
    {
        Assert.Equal(15, (int)PatternInterface.Toggle);
        Assert.Equal(16, (int)PatternInterface.Transform);
        Assert.Equal(17, (int)PatternInterface.Text);
        Assert.Equal(19, (int)AutomationEvents.Notification);
        Assert.Equal(20, (int)AutomationEvents.ActiveTextPositionChanged);
        Assert.Equal(1, (int)AutomationOrientation.Horizontal);
        Assert.Equal(2, (int)AutomationOrientation.Vertical);
    }

    [Fact]
    public void UIElementPeer_ExposesOwnerAndAttachedAutomationMetadata()
    {
        var owner = new Button { Name = "fallback" };
        AutomationProperties.SetName(owner, "Accessible button");
        AutomationProperties.SetAutomationId(owner, "submit");
        AutomationProperties.SetHelpText(owner, "Submits the form");
        AutomationProperties.SetAcceleratorKey(owner, "Ctrl+Enter");
        AutomationProperties.SetAccessKey(owner, "S");
        AutomationProperties.SetItemStatus(owner, "Ready");
        AutomationProperties.SetItemType(owner, "Action");
        AutomationProperties.SetIsRequiredForForm(owner, true);
        AutomationProperties.SetIsDialog(owner, true);
        AutomationProperties.SetPositionInSet(owner, 2);
        AutomationProperties.SetSizeOfSet(owner, 5);
        AutomationProperties.SetHeadingLevel(owner, AutomationHeadingLevel.Level2);
        AutomationProperties.SetLiveSetting(owner, AutomationLiveSetting.Polite);

        AutomationPeer peer = owner.GetAutomationPeer()!;

        Assert.Same(owner, Assert.IsAssignableFrom<UIElementAutomationPeer>(peer).Owner);
        Assert.Equal("Accessible button", peer.GetName());
        Assert.Equal("submit", peer.GetAutomationId());
        Assert.Equal("Submits the form", peer.GetHelpText());
        Assert.Equal("Ctrl+Enter", peer.GetAcceleratorKey());
        Assert.Equal("S", peer.GetAccessKey());
        Assert.Equal("Ready", peer.GetItemStatus());
        Assert.Equal("Action", peer.GetItemType());
        Assert.True(peer.IsRequiredForForm());
        Assert.True(peer.IsDialog());
        Assert.Equal(2, peer.GetPositionInSet());
        Assert.Equal(5, peer.GetSizeOfSet());
        Assert.Equal(AutomationHeadingLevel.Level2, peer.GetHeadingLevel());
        Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
    }

    [Fact]
    public void VisualTreeMutation_ResetsNearestPeerChildrenCacheAndRaisesStructureChanged()
    {
        var root = new PeerRoot();
        var peerlessContainer = new Grid();
        peerlessContainer.Children.Add(new Button());
        root.Add(peerlessContainer);
        AutomationPeer peer = root.GetAutomationPeer()!;
        var sink = new RecordingSink(peer);
        AutomationPeer.EventSink = sink;
        try
        {
            Assert.Single(peer.GetChildren());

            // Grid has no peer of its own, so the root peer exposes its
            // descendants as a flattened accessibility child list.
            peerlessContainer.Children.Add(new Button());

            Assert.Equal(2, peer.GetChildren().Count);
            Assert.Contains(AutomationEvents.StructureChanged, sink.Events);
        }
        finally
        {
            AutomationPeer.EventSink = null;
        }
    }

    [Fact]
    public void ContentElementPeer_UsesContentElementOwnerContract()
    {
        var owner = new PeerContentElement();
        AutomationProperties.SetName(owner, "inline content");

        AutomationPeer peer = ContentElementAutomationPeer.CreatePeerForElement(owner)!;

        var contentPeer = Assert.IsType<ContentElementAutomationPeer>(peer);
        Assert.Same(owner, contentPeer.Owner);
        Assert.Same(peer, ContentElementAutomationPeer.FromElement(owner));
        Assert.Equal("inline content", peer.GetName());
        Assert.True(peer.IsContentElement());
    }

    [Fact]
    public void ProviderConversion_RoundTripsPeerIdentity()
    {
        var peer = new TestFrameworkPeer(new FrameworkElement());

        IRawElementProviderSimple? provider = peer.ToProvider(peer);

        Assert.NotNull(provider);
        Assert.Same(peer, peer.FromProvider(provider));
    }

    [Fact]
    public void NotificationAndAsyncEvents_AreForwardedToPlatformSink()
    {
        var peer = new TestFrameworkPeer(new FrameworkElement());
        var sink = new RecordingSink(peer);
        AutomationPeer.EventSink = sink;
        try
        {
            Assert.True(AutomationPeer.ListenerExists(AutomationEvents.Notification));
            peer.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                "Saved",
                "save");
            peer.RaiseAsyncContentLoadedEvent(
                new AsyncContentLoadedEventArgs(AsyncContentLoadedState.Completed, 100));

            Assert.Equal(
                new[] { AutomationEvents.Notification, AutomationEvents.AsyncContentLoaded },
                sink.Events);
        }
        finally
        {
            AutomationPeer.EventSink = null;
        }
    }

    private sealed class PeerContentElement : ContentElement
    {
        protected override AutomationPeer? OnCreateAutomationPeer() => new ContentElementAutomationPeer(this);
    }

    private sealed class PeerRoot : FrameworkElement
    {
        internal void Add(UIElement child) => AddVisualChild(child);

        protected override AutomationPeer? OnCreateAutomationPeer() => new GenericRootAutomationPeer(this);
    }

    private sealed class TestFrameworkPeer : FrameworkElementAutomationPeer
    {
        internal TestFrameworkPeer(FrameworkElement owner)
            : base(owner)
        {
        }

        internal IRawElementProviderSimple? ToProvider(AutomationPeer peer) => ProviderFromPeer(peer);
        internal AutomationPeer FromProvider(IRawElementProviderSimple provider) => PeerFromProvider(provider);
    }

    private sealed class RecordingSink : IAutomationEventSink
    {
        private readonly AutomationPeer _expectedPeer;

        internal RecordingSink(AutomationPeer expectedPeer)
        {
            _expectedPeer = expectedPeer;
        }

        internal List<AutomationEvents> Events { get; } = [];

        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        {
            if (ReferenceEquals(peer, _expectedPeer))
            {
                Events.Add(eventId);
            }
        }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue) { }
        public void OnFocusChanged(AutomationPeer peer) { }
    }
}
