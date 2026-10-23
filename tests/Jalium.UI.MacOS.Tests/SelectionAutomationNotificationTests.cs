using Jalium.UI.Automation;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SelectionAutomationNotificationTests
{
    [Fact]
    public void BatchSelectionNotifiesTheCachedContainerAfterTheCollectionCommits()
    {
        var list = Create(); var peer = list.GetAutomationPeer()!;
        using var sink = new Recorder(() => list.SelectedItems.Cast<object>().ToArray());
        Assert.True(list.Choose("Alpha", "Gamma"));
        var entry = Assert.Single(sink.Events);
        Assert.Same(peer, entry.Peer);
        Assert.Equal(new object[] { "Alpha", "Gamma" }, entry.State);
    }

    [Fact]
    public void ClearingSelectionPublishesTheEmptyCollection()
    {
        var list = Create(); Assert.True(list.Choose("Beta")); _ = list.GetAutomationPeer();
        using var sink = new Recorder(() => list.SelectedItems.Cast<object>().ToArray());
        list.UnselectAll();
        Assert.NotEmpty(sink.Events);
        Assert.All(sink.Events, entry => Assert.Empty(entry.State));
    }

    [Fact]
    public void ThrowingUserHandlerDoesNotSuppressNotificationOfACommittedSelection()
    {
        var list = Create(); _ = list.GetAutomationPeer();
        using var sink = new Recorder(() => list.SelectedItems.Cast<object>().ToArray());
        list.SelectionChanged += (_, _) => throw new InvalidOperationException("user callback");
        Assert.Throws<InvalidOperationException>(() => list.Choose("Beta"));
        Assert.Equal(new object[] { "Beta" }, Assert.Single(sink.Events).State);
    }

    [Fact]
    public void TreeSelectionNotifiesItsContainerAfterSelectedItemCommits()
    {
        var tree = new TreeView(); var child = new TreeViewItem { Header = "中文🙂" };
        tree.Items.Add(child); var peer = tree.GetAutomationPeer()!;
        using var sink = new Recorder(() => tree.SelectedItem is { } item ? [item] : []);
        tree.SelectItem(child);
        var entry = Assert.Single(sink.Events); Assert.Same(peer, entry.Peer);
        Assert.Same(tree.SelectedItem, Assert.Single(entry.State));
    }

    [Fact]
    public void ChangesWithoutAnExistingPeerDoNotCreateAnAutomationTree()
    {
        var list = Create(); Assert.Null(list.GetExistingAutomationPeer());
        using var sink = new Recorder(() => list.SelectedItems.Cast<object>().ToArray());
        Assert.True(list.Choose("Beta"));
        Assert.Null(list.GetExistingAutomationPeer()); Assert.Empty(sink.Events);
    }

    [Fact]
    public void ATreeSelectionHandlerCanChooseAnotherExclusiveRow()
    {
        var tree = new TreeView();
        var first = new TreeViewItem { Header = "first" };
        var second = new TreeViewItem { Header = "second" };
        tree.Items.Add(first); tree.Items.Add(second);
        int callbacks = 0;
        tree.SelectedItemChanged += (_, _) => { if (++callbacks == 1) tree.SelectItem(second); };
        tree.SelectItem(first);
        Assert.Equal(2, callbacks); Assert.False(first.IsSelected); Assert.True(second.IsSelected);
    }

    private static SelectionList Create() => new()
    { SelectionMode = SelectionMode.Multiple, Items = { "Alpha", "Beta", "Gamma" } };
    private sealed class SelectionList : ListBox
    {
        internal bool Choose(params object[] values) => SetSelectedItems(values);
    }
    private sealed class Recorder : IAutomationEventSink, IDisposable
    {
        private readonly IAutomationEventSink? _previous;
        private readonly Func<object[]> _state;
        internal List<(AutomationPeer Peer, object[] State)> Events { get; } = [];
        internal Recorder(Func<object[]> state)
        {
            _state = state; _previous = AutomationPeer.EventSink;
            AutomationPeer.EventSink = this;
            Assert.True(AutomationPeer.ListenerExists(AutomationEvents.SelectionPatternOnInvalidated));
        }
        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId)
        {
            if (eventId == AutomationEvents.SelectionPatternOnInvalidated) Events.Add((peer, _state()));
        }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue) { }
        public void OnFocusChanged(AutomationPeer peer) { }
        public void Dispose() => AutomationPeer.EventSink = _previous;
    }
}
