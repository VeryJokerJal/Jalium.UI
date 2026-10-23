using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSExplicitListItemOwnershipTests
{
    [Fact]
    public void ADirectPeerReportsItsOwningListAsItsParent()
    {
        var row = new ListBoxItem(); var source = new Resettable { row }; var list = Create(source);
        var peer = Peer(row); Assert.Same(list.GetAutomationPeer(), peer.GetParent());
        source.Clear(); Assert.Null(peer.GetParent());
    }

    [Fact]
    public void ADirectPeerResolvesItsParentAgainAfterReusingTheContainer()
    {
        var row = new ListBoxItem(); var source = new Resettable { row }; var old = Create(source);
        var peer = Peer(row); _ = peer.GetParent(); source.Clear(); var current = Create(new Resettable { row });
        Assert.Same(current.GetAutomationPeer(), peer.GetParent()); GC.KeepAlive(old);
    }

    [Theory]
    [InlineData("clear")] [InlineData("replace")] [InlineData("rebind")] [InlineData("reset")] [InlineData("remove")]
    public void ADiscardedExplicitItemStopsExposingItsOldSelectionContainer(string action)
    {
        var row = new ListBoxItem { Content = "中文🙂" };
        var source = new Resettable { row, new ListBoxItem { Content = "保留" } };
        var list = Create(source); var peer = Peer(row);
        Assert.Same(list.GetAutomationPeer(), peer.SelectionContainer);
        Mutate(list, source, action);
        Assert.Null(peer.SelectionContainer);
        Assert.Null(list.ItemContainerGenerator.ContainerFromItem(row));
    }

    [Theory]
    [InlineData("clear")] [InlineData("replace")] [InlineData("rebind")] [InlineData("reset")] [InlineData("remove")]
    public void RetainedDirectProviderAndClickCannotSelectANewItemWithEqualContent(string action)
    {
        var row = new ListBoxItem { Content = "同名" };
        var source = new Resettable { row, new ListBoxItem { Content = "保留" } };
        var list = Create(source); var old = Peer(row);
        Mutate(list, source, action);
        var current = new ListBoxItem { Content = "同名" }; list.ItemsSource = null;
        list.Items.Clear(); list.Items.Add(new ListBoxItem { Content = "保留" }); list.Items.Add(current);
        list.SelectedIndex = 0;
        old.Select(); old.AddToSelection(); old.RemoveFromSelection();
        row.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent, new Point(2, 2), MouseButton.Left,
            MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released,
            MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Same(list.Items[0], list.SelectedItem);
        Assert.False(current.IsSelected);
    }

    [Fact]
    public void ResetKeepsSurvivingExplicitItemsAndAttachesNewOnes()
    {
        var removed = new ListBoxItem { Content = "旧" }; var retained = new ListBoxItem { Content = "保留" };
        var source = new Resettable { removed, retained }; var list = Create(source);
        var added = new ListBoxItem { Content = "新" }; source.Reset(retained, added);
        Assert.Null(Peer(removed).SelectionContainer);
        Assert.Same(list.GetAutomationPeer(), Peer(retained).SelectionContainer);
        Assert.Same(list.GetAutomationPeer(), Peer(added).SelectionContainer);
        Peer(added).Select(); Assert.Equal("新", list.SelectedItem);
    }

    [Fact]
    public void ResetDetachesByIdentityWhenTwoContainersCompareEqual()
    {
        var old = new EqualRow { Content = "旧" }; var current = new EqualRow { Content = "新" };
        var source = new Resettable { old }; var list = Create(source); source.Reset(current);
        Assert.Null(Peer(old).SelectionContainer);
        Assert.Same(list.GetAutomationPeer(), Peer(current).SelectionContainer);
    }

    [Fact]
    public void RemovingOneUnrealizedDuplicateKeepsTheRemainingOccurrenceOwned()
    {
        var row = new ListBoxItem { Content = "重复" }; var source = new Resettable { row, row }; var list = Create(source);
        Assert.Null(list.ItemContainerGenerator.ContainerFromItem(row));
        source.RemoveAt(0); Assert.Same(list.GetAutomationPeer(), Peer(row).SelectionContainer);
        source.RemoveAt(0); Assert.Null(Peer(row).SelectionContainer);
    }

    [Fact]
    public void ReplacingOneDuplicateDoesNotDetachTheOtherOccurrence()
    {
        var row = new ListBoxItem { Content = "重复" }; var source = new Resettable { row, row }; var list = Create(source);
        source[0] = new ListBoxItem { Content = "新" };
        Assert.Same(list.GetAutomationPeer(), Peer(row).SelectionContainer);
        source.Clear(); Assert.Null(Peer(row).SelectionContainer);
    }

    [Fact]
    public void MovePreservesTheExplicitItemOwnerAndItsDirectPeer()
    {
        var row = new ListBoxItem { Content = "移动" }; var source = new Resettable { row, new ListBoxItem() };
        var list = Create(source); var peer = Peer(row); source.Move(0, 1);
        Assert.Same(peer, Peer(row)); Assert.Same(list.GetAutomationPeer(), peer.SelectionContainer);
        peer.Select(); Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void ASelectionChangedCallbackObservesRetiredOwnership()
    {
        var row = new ListBoxItem { Content = "旧" }; var source = new Resettable { row }; var list = Create(source);
        list.SelectedIndex = 0; bool? detachedDuringCallback = null;
        list.SelectionChanged += (_, _) => detachedDuringCallback = Peer(row).SelectionContainer == null;
        source.Clear(); Assert.True(detachedDuringCallback); Assert.Null(Peer(row).SelectionContainer);
    }

    [Fact]
    public void ReentrantSelectionCallbackCanInstallANewSourceWithoutResurrectingTheOldOwner()
    {
        var row = new ListBoxItem { Content = "旧" }; var current = new ListBoxItem { Content = "新" };
        var source = new Resettable { row }; var list = Create(source); list.SelectedIndex = 0;
        bool replaced = false;
        list.SelectionChanged += (_, _) => { if (!replaced) { replaced = true; list.ItemsSource = new[] { current }; } };
        source.Clear();
        Assert.Null(Peer(row).SelectionContainer);
        Assert.Same(list.GetAutomationPeer(), Peer(current).SelectionContainer);
        Peer(current).Select(); Assert.Equal("新", list.SelectedItem);
    }

    [Fact]
    public void ThrowingSelectionCallbackDoesNotLeaveTheRemovedItemOwned()
    {
        var row = new ListBoxItem { Content = "旧" }; var source = new Resettable { row }; var list = Create(source);
        list.SelectedIndex = 0; var error = new InvalidOperationException("owner callback");
        list.SelectionChanged += (_, _) => throw error;
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => source.Clear()));
        Assert.Null(Peer(row).SelectionContainer);
    }

    [Theory]
    [InlineData("clear")] [InlineData("replace")] [InlineData("rebind")] [InlineData("reset")] [InlineData("remove")]
    public void ARemovedContainerCanBeReusedByAnotherList(string action)
    {
        var row = new ListBoxItem { Content = "中文🙂" }; var source = new Resettable { row };
        var old = Create(source); Mutate(old, source, action);
        var next = Create(new Resettable { row }); source.Clear();
        Assert.Same(next.GetAutomationPeer(), Peer(row).SelectionContainer);
        Peer(row).Select(); Assert.Equal("中文🙂", next.SelectedItem); Assert.Equal(-1, old.SelectedIndex);
    }

    [Fact]
    public void RetiringAnOldMembershipDoesNotClearAContainerNowOwnedByAnotherList()
    {
        var row = new ListBoxItem { Content = "shared container" }; var source = new Resettable { row };
        var old = Create(source); var next = Create(new Resettable { row }); source.Clear();
        Assert.Same(next.GetAutomationPeer(), Peer(row).SelectionContainer);
        GC.KeepAlive(old);
    }

    [Fact]
    public void TheMembershipRegistryDoesNotKeepADiscardedContainerAlive()
    {
        var list = Create(new Resettable()); var weak = AddThenClear(list);
        Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Collect(); Assert.False(weak.IsAlive); GC.KeepAlive(list);
    }

    [Fact]
    public void RetainingADiscardedContainerDoesNotKeepTheFormerListAlive()
    {
        var (row, weak) = ClearThenDropList();
        Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Collect(); Assert.False(weak.IsAlive); GC.KeepAlive(row);
    }

    private static ListBoxItemAutomationPeer Peer(ListBoxItem row) => (ListBoxItemAutomationPeer)row.GetAutomationPeer()!;
    private static ListBox Create(Resettable source)
    {
        var list = new ListBox(); var panel = new ItemsPanelTemplate(); panel.SetVisualTree(() => new VirtualizingStackPanel());
        list.ItemsPanel = panel; VirtualizingPanel.SetIsVirtualizing(list, true); list.ItemsSource = source; return list;
    }
    private static void Mutate(ListBox list, Resettable source, string action)
    {
        switch (action)
        {
            case "clear": source.Clear(); break;
            case "replace": source[0] = new ListBoxItem { Content = "替换" }; break;
            case "rebind": list.ItemsSource = new[] { new ListBoxItem { Content = "新源" } }; break;
            case "reset": source.Reset(new ListBoxItem { Content = "重置" }); break;
            case "remove": source.RemoveAt(0); break;
            default: throw new ArgumentException(action);
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddThenClear(ListBox list)
    { var source = (Resettable)list.ItemsSource!; var row = new ListBoxItem(); source.Add(row); source.Clear(); return new(row); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ListBoxItem, WeakReference) ClearThenDropList()
    { var row = new ListBoxItem(); var source = new Resettable { row }; var list = Create(source); _ = Peer(row).GetParent(); source.Clear(); return (row, new(list)); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed class EqualRow : ListBoxItem
    { public override bool Equals(object? obj) => obj is EqualRow; public override int GetHashCode() => 1; }
    private sealed class Resettable : ObservableCollection<object>
    {
        internal void Reset(params object[] current)
        { Items.Clear(); foreach (var item in current) Items.Add(item); OnPropertyChanged(new("Count")); OnPropertyChanged(new("Item[]")); OnCollectionChanged(new(NotifyCollectionChangedAction.Reset)); }
    }
}
