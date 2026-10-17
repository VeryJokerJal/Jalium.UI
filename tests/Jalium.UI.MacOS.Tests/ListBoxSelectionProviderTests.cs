using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ListBoxSelectionProviderTests
{
    [Theory]
    [InlineData(SelectionMode.Single)] [InlineData(SelectionMode.Multiple)] [InlineData(SelectionMode.Extended)]
    public void DataSelectionReturnsTheCachedLogicalPeersBeforeContainersExist(SelectionMode mode)
    {
        object first = new Entry("首项"), second = new Entry("中文🙂"), third = new Entry("末项");
        var list = new TestList { SelectionMode = mode, ItemsSource = new[] { first, second, third } };
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());
        var children = peer.GetChildren()!;
        Assert.Empty(peer.GetSelection());
        Assert.True(list.SetSelection(mode == SelectionMode.Single ? [second] : [second, third]));
        var selected = peer.GetSelection();
        Assert.Equal(mode == SelectionMode.Single ? 1 : 2, selected.Length);
        Assert.Same(children[1], selected[0]);
        Assert.Equal("中文🙂", selected[0].GetName());
        var item = Assert.IsAssignableFrom<ISelectionItemProvider>(selected[0]);
        Assert.True(item.IsSelected);
        Assert.Same(peer, Assert.IsAssignableFrom<IAutomationPeerRawProvider>(item.SelectionContainer).Peer);
        var provider = Assert.IsAssignableFrom<ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
        Assert.Equal(selected, provider.GetSelection().Select(value => Assert.IsAssignableFrom<IAutomationPeerRawProvider>(value).Peer));
        if (selected.Length == 2) Assert.Same(children[2], selected[1]);
        Assert.True(list.SetSelection([])); Assert.Empty(peer.GetSelection());
    }

    [Fact]
    public void OwnContainersAreMatchedBySelectionValueAndKeepTheirLogicalIdentity()
    {
        var first = new ListBoxItem { Content = "首项" };
        var second = new ListBoxItem { Content = "中文🙂" };
        var list = new TestList { SelectionMode = SelectionMode.Multiple, Items = { first, second } };
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());
        var children = peer.GetChildren()!;
        Assert.True(list.SetSelection([second.Content!]));
        Assert.Same(children[1], Assert.Single(peer.GetSelection()));
        Assert.Equal("中文🙂", peer.GetSelection()[0].GetName());
        Assert.True(((ListBoxItemAutomationPeer)peer.GetSelection()[0]).IsSelected);
    }

    [Fact]
    public void SelectionActionsOnUnrealizedDataKeepScalarAndMultipleSelectionInSync()
    {
        object first = new Entry("首项"), second = new Entry("末项");
        var list = new TestList { SelectionMode = SelectionMode.Multiple, Items = { first, second } };
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());
        var children = peer.GetChildren()!.Cast<ListBoxItemAutomationPeer>().ToArray();
        children[0].Select(); children[1].AddToSelection();
        Assert.Equal(new[] { first, second }, list.SelectedItems.Cast<object>());
        Assert.Equal(2, peer.GetSelection().Length);
        children[0].RemoveFromSelection();
        Assert.Equal(second, list.SelectedItem); Assert.Equal(1, list.SelectedIndex);
        Assert.Same(children[1], Assert.Single(peer.GetSelection()));
        children[0].Select();
        Assert.Equal(new[] { first }, list.SelectedItems.Cast<object>());
        Assert.Same(children[0], Assert.Single(peer.GetSelection()));
    }

    [Fact]
    public void RemovedItemsAreExcludedWhileSurvivingSelectedPeersRemainStable()
    {
        object first = new Entry("首项"), second = new Entry("末项");
        var items = new ObservableCollection<object> { first, second };
        var list = new TestList { SelectionMode = SelectionMode.Multiple, ItemsSource = items };
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer());
        Assert.True(list.SetSelection([first, second]));
        var old = peer.GetSelection(); Assert.Equal(2, old.Length);
        items.Remove(first);
        Assert.Same(old[1], Assert.Single(peer.GetSelection()));
        list.IsEnabled = false;
        Assert.Same(old[1], Assert.Single(peer.GetSelection()));
        items.Clear(); Assert.Empty(peer.GetSelection());
    }

    private sealed class TestList : ListBox
    {
        internal bool SetSelection(object[] values) => SetSelectedItems(values);
    }
    private sealed record Entry(string Text)
    {
        public override string ToString() => Text;
    }
}
