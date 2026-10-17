using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSItemPeerLifetimeTests
{
    [Theory]
    [InlineData("items", "remove")] [InlineData("items", "replace")] [InlineData("items", "reset")] [InlineData("items", "rebind")]
    [InlineData("list", "remove")] [InlineData("list", "replace")] [InlineData("list", "reset")] [InlineData("list", "rebind")]
    [InlineData("combo", "remove")] [InlineData("combo", "replace")] [InlineData("combo", "reset")] [InlineData("combo", "rebind")]
    public void RemovedDataAndItsPeerAreReleasedWithoutAnotherAutomationQuery(string kind, string mutation)
    {
        var owner = Create(kind);
        var weak = CacheThenRemove(owner, mutation);
        Collect();
        Assert.False(weak.Item.IsAlive); Assert.False(weak.Peer.IsAlive);
        GC.KeepAlive(owner);
    }

    [Theory] [InlineData("items")] [InlineData("list")] [InlineData("combo")]
    public void MovingLiveItemsPreservesPeersAndChangesTheirOrder(string kind)
    {
        var first = new Entry("first"); var second = new Entry("second");
        var source = new ObservableCollection<object> { first, second };
        var owner = Create(kind); owner.ItemsSource = source;
        var peer = owner.GetAutomationPeer()!; var old = peer.GetChildren().ToArray();
        source.Move(1, 0);
        Assert.Same(old[1], peer.GetChildren()[0]); Assert.Same(old[0], peer.GetChildren()[1]);
        source.Add(new Entry("third"));
        Assert.Equal(3, peer.GetChildren().Count); Assert.Same(old[1], peer.GetChildren()[0]);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ParentStructureNotificationSeesTheCurrentCollection(bool external)
    {
        var source = new ObservableCollection<object> { new Entry("first") };
        var owner = new ItemsControl();
        if (external) owner.ItemsSource = source; else owner.Items.Add(source[0]);
        var peer = owner.GetAutomationPeer()!; _ = peer.GetChildren();
        var previous = EventSinkRegistry.Sink; var sink = new Sink(peer, owner);
        EventSinkRegistry.Sink = sink;
        try
        {
            if (external) source.Add(new Entry("second")); else owner.Items.Add(new Entry("second"));
            Assert.Single(sink.Children); Assert.Equal(new[] { "first", "second" }, sink.Children[0]);
        }
        finally { EventSinkRegistry.Sink = previous; }
    }

    [Fact]
    public void AFactoryCannotPublishAnItemAcrossAReentrantSourceReset()
    {
        var old = new Entry("old"); var current = new Entry("new");
        var owner = new FactoryItems(); owner.Items.Add(old); var peer = owner.Peer;
        ItemAutomationPeer? nested = null;
        peer.Factory = item =>
        {
            peer.Factory = null; owner.Items.Clear(); owner.Items.Add(current); nested = peer.Request(current);
            return new ItemAutomationPeer(item, peer);
        };
        Assert.Throws<InvalidOperationException>(() => peer.Request(old));
        Assert.Same(nested, peer.Request(current)); Assert.Single(peer.GetChildren());
    }

    [Fact]
    public void AReentrantFactoryUsesTheAlreadyCreatedCurrentPeer()
    {
        var item = new Entry("current"); var owner = new FactoryItems(); owner.Items.Add(item); var peer = owner.Peer;
        ItemAutomationPeer? nested = null;
        peer.Factory = value =>
        {
            peer.Factory = null; nested = peer.Request(value); return new ItemAutomationPeer(value, peer);
        };
        Assert.Same(peer.Request(item), nested); Assert.Same(nested, peer.Request(item));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Item, WeakReference Peer) CacheThenRemove(ItemsControl owner, string mutation)
    {
        var target = new Entry("obsolete");
        var source = new ObservableCollection<object> { new Entry("survivor"), target }; owner.ItemsSource = source;
        var peer = owner.GetAutomationPeer()!; var child = peer.GetChildren()[1];
        var weak = (new WeakReference(target), new WeakReference(child));
        switch (mutation)
        {
            case "remove": source.RemoveAt(1); break;
            case "replace": source[1] = new Entry("replacement"); break;
            case "reset": source.Clear(); break;
            case "rebind": owner.ItemsSource = new ObservableCollection<object> { new Entry("replacement") }; break;
            default: throw new ArgumentException(mutation);
        }
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static ItemsControl Create(string kind) => kind switch
    { "items" => new ItemsControl(), "list" => new ListBox(), "combo" => new ComboBox(), _ => throw new ArgumentException(kind) };
    private sealed record Entry(string Text) { public override string ToString() => Text; }

    private sealed class FactoryItems : ItemsControl
    {
        internal FactoryPeer Peer => (FactoryPeer)GetAutomationPeer()!;
        protected override AutomationPeer? OnCreateAutomationPeer() => new FactoryPeer(this);
    }
    private sealed class FactoryPeer(ItemsControl owner) : ItemsControlAutomationPeer(owner)
    {
        internal Func<object, ItemAutomationPeer>? Factory;
        internal ItemAutomationPeer Request(object item) => FindOrCreateItemAutomationPeer(item);
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => Factory?.Invoke(item) ?? new(item, this);
    }
    private sealed class Sink(AutomationPeer target, ItemsControl owner) : IAutomationEventSink
    {
        internal List<string[]> Children = [];
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        {
            if (!ReferenceEquals(peer, target) || eventId != AutomationEvents.StructureChanged) return;
            var children = peer.GetChildren().Select(x => x.GetName()).ToArray();
            Assert.Equal(owner.Items.Cast<object>().Select(x => x.ToString()), children); Children.Add(children);
        }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue) { }
        public void OnFocusChanged(AutomationPeer peer) { }
    }
}
