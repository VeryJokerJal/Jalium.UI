using System.Collections;
using System.Collections.ObjectModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ListBoxOccurrenceTests
{
    public static IEnumerable<object[]> Shapes()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        foreach (bool source in new[] { false, true }) yield return [kind, source];
    }

    [Theory, MemberData(nameof(Shapes))]
    public void SelectingByIndexSelectsOnlyThatOccurrenceBeforeAndAfterRealization(int kind, bool source)
    {
        var f = new Fixture(kind, source); f.List.SelectedIndex = 1;
        var rows = f.Rows(); Assert.NotSame(rows[0], rows[1]);
        Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection()));
        Assert.False(rows[0].IsSelected); Assert.True(rows[1].IsSelected);
        f.Generate(); Assert.False(f.Container(0).IsSelected); Assert.True(f.Container(1).IsSelected);
        f.List.SelectedIndex = 0; Assert.True(f.Container(0).IsSelected); Assert.False(f.Container(1).IsSelected);
        f.List.SelectedIndex = 1; Assert.False(f.Container(0).IsSelected); Assert.True(f.Container(1).IsSelected);
        Assert.Same(f.Item(1), f.List.SelectedItem);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void SelectingADataPeerUsesItsOwnRowIndex(int kind, bool source)
    {
        var f = new Fixture(kind, source); var rows = f.Rows(); rows[1].Select();
        Assert.Equal(1, f.List.SelectedIndex); Assert.True(rows[1].IsSelected); Assert.False(rows[0].IsSelected);
        f.Generate(); Assert.True(f.Container(1).IsSelected); Assert.False(f.Container(0).IsSelected);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void MultipleSelectionAddsAndRemovesIndividualEqualRows(int kind, bool source)
    {
        var f = new Fixture(kind, source); f.List.SelectionMode = SelectionMode.Multiple; var rows = f.Rows();
        rows[1].Select(); rows[0].AddToSelection();
        Assert.Equal(2, f.List.SelectedItems.Count); Assert.Equal(2, f.Peer.GetSelection().Length);
        Assert.True(rows[0].IsSelected); Assert.True(rows[1].IsSelected);
        rows[1].RemoveFromSelection(); Assert.Single(f.List.SelectedItems); Assert.Same(rows[0], Assert.Single(f.Peer.GetSelection()));
        f.Generate(); Assert.True(f.Container(0).IsSelected); Assert.False(f.Container(1).IsSelected);
    }

    [Theory, MemberData(nameof(Shapes))]
    public void SelectAllAndRemoveAtKeepEachOccurrenceIndependent(int kind, bool source)
    {
        var f = new Fixture(kind, source); f.List.SelectionMode = SelectionMode.Extended; var rows = f.Rows();
        f.List.SelectAll(); Assert.Equal(2, f.List.SelectedItems.Count); Assert.Equal(rows, f.Peer.GetSelection());
        f.List.SelectedItems.RemoveAt(0); Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection()));
        Assert.False(rows[0].IsSelected); Assert.True(rows[1].IsSelected);
        f.List.UnselectAll(); Assert.Empty(f.Peer.GetSelection());
    }

    [Theory, MemberData(nameof(Shapes))]
    public void ItemContainerPatternCanAdvancePastTheFirstEqualOccurrence(int kind, bool source)
    {
        var f = new Fixture(kind, source); var rows = f.Rows();
        var pattern = Assert.IsAssignableFrom<IItemContainerProvider>(f.Peer.GetPattern(PatternInterface.ItemContainer));
        var access = new PeerAccess(f.List);
        var one = pattern.FindItemByProperty(null, 0, null); var two = pattern.FindItemByProperty(one, 0, null);
        Assert.Same(rows[0], access.FromProvider(one)); Assert.Same(rows[1], access.FromProvider(two));
        Assert.Null(pattern.FindItemByProperty(two, 0, null));
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void InsertingBeforeASelectedRowPreservesItsPeerAndSelection(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        f.Source.Insert(0, new Row(9)); var current = f.Rows();
        Assert.Same(rows[0], current[1]); Assert.Same(rows[1], current[2]); Assert.Equal(2, f.List.SelectedIndex);
        Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection()));
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void MovingAnEqualRowPreservesBothOccurrenceIdentities(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        f.Source.Move(1, 0); var current = f.Rows();
        Assert.Same(rows[1], current[0]); Assert.Same(rows[0], current[1]); Assert.Equal(0, f.List.SelectedIndex);
        Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection()));
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void RemovingAnEqualSiblingDoesNotRetireOrSelectTheSurvivingPeer(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        f.Source.RemoveAt(0); Assert.Same(rows[1], Assert.Single(f.Rows()));
        Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection())); Assert.Equal(0, f.List.SelectedIndex);
        Assert.Throws<InvalidOperationException>(() => rows[0].Select()); Assert.False(rows[0].IsSelected);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void RemovingTheSelectedOccurrenceDoesNotFallBackToAnEqualSibling(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        f.Source.RemoveAt(1); Assert.Same(rows[0], Assert.Single(f.Rows()));
        Assert.Empty(f.Peer.GetSelection()); Assert.Empty(f.List.SelectedItems); Assert.Equal(-1, f.List.SelectedIndex);
        Assert.Throws<InvalidOperationException>(() => rows[1].Select()); Assert.False(rows[1].IsSelected);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ReplacingTheSelectedOccurrenceRetiresOnlyItsOldPeer(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        object replacement = kind == 2 ? new EqualContainer { Content = "重复条目🙂" } : new Row(7);
        f.Source[1] = replacement; var current = f.Rows(); Assert.Same(rows[0], current[0]); Assert.NotSame(rows[1], current[1]);
        Assert.Same(current[1], Assert.Single(f.Peer.GetSelection())); Assert.Equal(1, f.List.SelectedIndex);
        Assert.Throws<InvalidOperationException>(() => rows[1].Select());
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ACommittedSelectionCallbackCanSelectAnotherEqualOccurrence(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); bool nested = false;
        f.List.SelectionChanged += (_, _) => { if (!nested) { nested = true; rows[0].Select(); } };
        rows[1].Select(); Assert.True(nested); Assert.Equal(0, f.List.SelectedIndex);
        Assert.Same(rows[0], Assert.Single(f.Peer.GetSelection())); Assert.False(rows[1].IsSelected);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void SpaceAndArrowKeysAddressTheFocusedOccurrence(int kind)
    {
        var f = new Fixture(kind, false); f.List.SelectedIndex = 0;
        f.List.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Down, ModifierKeys.None, true, false, 1));
        Assert.Equal(1, f.List.SelectedIndex); var rows = f.Rows(); Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection()));
    }

    [Theory, InlineData(0, false), InlineData(0, true), InlineData(2, false), InlineData(2, true)]
    public void SelectedItemUsesReferenceIdentityWhenTwoObjectsCompareEqual(int kind, bool source)
    {
        var f = new Fixture(kind, source); f.List.SelectedItem = f.Item(0); f.List.SelectedItem = f.Item(1);
        Assert.Equal(1, f.List.SelectedIndex); Assert.Same(f.Item(1), f.List.SelectedItem);
        Assert.Same(f.Rows()[1], Assert.Single(f.Peer.GetSelection()));
    }

    [Fact]
    public void SwitchingRepeatedReferencesPublishesTheOccurrenceChange()
    {
        var f = new Fixture(1, true); f.List.SelectedIndex = 0; int events = 0, resets = 0;
        f.List.SelectionChanged += (_, e) => { events++; Assert.Same(f.Item(0), Assert.Single(e.RemovedItems)); Assert.Same(f.Item(1), Assert.Single(e.AddedItems)); };
        ((System.Collections.Specialized.INotifyCollectionChanged)f.List.SelectedItems).CollectionChanged += (_, _) => resets++;
        f.List.SelectedIndex = 1; Assert.Equal(1, events); Assert.Equal(1, resets);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ProtectedBatchAndPublicCollectionPreserveIndependentRows(int kind)
    {
        var f = new Fixture(kind, true); f.List.SelectionMode = SelectionMode.Multiple; var rows = f.Rows();
        Assert.True(f.List.SetSelection(new[] { f.Item(1), f.Item(0) })); Assert.Equal(2, f.List.SelectedItems.Count);
        Assert.Equal(rows, f.Peer.GetSelection());
        f.List.SelectedItems.RemoveAt(0); Assert.Single(f.List.SelectedItems);
        Assert.Equal(kind == 1 ? 1 : 0, f.List.SelectedIndex);
        f.List.UnselectAll(); f.List.SelectedItems.Add(f.Item(1));
        if (kind != 1) Assert.Equal(1, f.List.SelectedIndex);
    }

    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ResetRetiresOldPeersAndRetainsTheSelectedSurvivingOccurrence(int kind)
    {
        var f = new Fixture(kind, true); var rows = f.Rows(); f.List.SelectedIndex = 1;
        f.List.ItemsSource = new ObservableCollection<object>(new[] { f.Item(0), f.Item(1) });
        var current = f.Rows(); Assert.NotSame(rows[0], current[0]); Assert.NotSame(rows[1], current[1]);
        Assert.Equal(1, f.List.SelectedIndex); Assert.Same(current[1], Assert.Single(f.Peer.GetSelection()));
        Assert.Throws<InvalidOperationException>(() => rows[0].Select()); Assert.Throws<InvalidOperationException>(() => rows[1].Select());
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ItemMutationDuringASelectionPropertyCallbackCannotPublishAStaleRow(bool insert)
    {
        var f = new Fixture(1, true); var rows = f.Rows(); bool changed = false;
        f.List.IndexChanged = () =>
        {
            if (changed || f.List.SelectedIndex != 1) return; changed = true;
            if (insert) f.Source.Insert(0, new Row(9)); else f.Source.RemoveAt(1);
        };
        rows[1].Select(); Assert.True(changed);
        Assert.Equal(insert ? 2 : -1, f.List.SelectedIndex);
        if (insert) Assert.Same(rows[1], Assert.Single(f.Peer.GetSelection())); else { Assert.Empty(f.Peer.GetSelection()); Assert.Empty(f.List.SelectedItems); }
    }

    [Fact]
    public void AThrowingPropertyCallbackDoesNotKeepADeletedSelectionOrBlockRetry()
    {
        var f = new Fixture(0, true); var rows = f.Rows(); bool changed = false;
        f.List.IndexChanged = () =>
        {
            if (changed || f.List.SelectedIndex != 1) return; changed = true; f.Source.RemoveAt(1); throw new InvalidOperationException("callback");
        };
        Assert.Throws<InvalidOperationException>(() => rows[1].Select()); Assert.Empty(f.Peer.GetSelection());
        Assert.Empty(f.List.SelectedItems); rows[0].Select(); Assert.Equal(0, f.List.SelectedIndex);
        Assert.Same(rows[0], Assert.Single(f.Peer.GetSelection()));
    }

    [Fact]
    public void SelectingAndQueryingALargeVirtualSourceDoesNotEnumerateEveryRow()
    {
        var source = new CountingRange(); var list = new ListBox { ItemsSource = source }; source.Reads = 0;
        list.SelectedIndex = source.Count - 1;
        var peer = Assert.IsType<ListBoxAutomationPeer>(list.GetAutomationPeer()); Assert.Single(peer.GetSelection());
        Assert.InRange(source.Reads, 0, 20); Assert.Equal(source.Count - 1, list.SelectedIndex);
    }

    [Fact]
    public void EqualVisualChildrenRetainDistinctMembershipAndDetachByReference()
    {
        var panel = new StackPanel(); var first = new EqualContainer(); var second = new EqualContainer(); var absent = new EqualContainer();
        panel.Children.Add(first); panel.Children.Add(second);
        Assert.Equal(1, panel.Children.IndexOf(second)); Assert.False(panel.Children.Contains(absent));
        Assert.Same(panel, LogicalTreeHelper.GetParent(first)); Assert.Same(panel, LogicalTreeHelper.GetParent(second));
        panel.Children.Remove(second);
        Assert.Same(first, Assert.Single(panel.Children.Cast<UIElement>()));
        Assert.Same(panel, first.VisualParent); Assert.Null(second.VisualParent);
        Assert.Same(panel, LogicalTreeHelper.GetParent(first)); Assert.Null(LogicalTreeHelper.GetParent(second));
        panel.Children.Add(second); panel.Children.Remove(first);
        Assert.Same(second, Assert.Single(panel.Children.Cast<UIElement>()));
        Assert.Same(panel, second.VisualParent); Assert.Null(first.VisualParent);
        Assert.Same(panel, LogicalTreeHelper.GetParent(second)); Assert.Null(LogicalTreeHelper.GetParent(first));
    }

    [Fact]
    public void EqualVisualCollectionMembersDetachTheRequestedReference()
    {
        var owner = new VisualOwner(); var children = new Jalium.UI.Media.VisualCollection(owner);
        var first = new EqualContainer(); var second = new EqualContainer(); var absent = new EqualContainer();
        children.Add(first); children.Add(second); Assert.Equal(1, children.IndexOf(second)); Assert.False(children.Contains(absent));
        children.Remove(second); Assert.Same(first, children[0]); Assert.Null(second.VisualParent); Assert.Same(owner, first.VisualParent);
        children.Add(second); children.Remove(first); Assert.Same(second, children[0]); Assert.Null(first.VisualParent); Assert.Same(owner, second.VisualParent);
    }

    [Fact]
    public void SelectingANestedTreeItemKeepsTheGenericSelectionProviderUsable()
    {
        var tree = new TreeView(); var parent = new TreeViewItem { Header = "parent", IsExpanded = true }; var child = new TreeViewItem { Header = "child" };
        parent.Items.Add(child); tree.Items.Add(parent); tree.SelectItem(child);
        var provider = Assert.IsAssignableFrom<ISelectionProvider>(tree.GetAutomationPeer()!.GetPattern(PatternInterface.Selection));
        Assert.Single(provider.GetSelection()); Assert.Single(provider.GetSelection());
    }

    [Fact]
    public void AFactoryThatReplacesTheViewCannotPublishItsOldContainerUnderTheNewRow()
    {
        var list = new GeneratingList(); var old = new Row(1); var replacement = new Row(2); list.Items.Add(old);
        list.Create = () => { list.Create = null; list.Items[0] = replacement; };
        var stale = list.ItemContainerGenerator.GetOrCreateContainerForIndex(0, out _);
        Assert.Same(replacement, list.Items[0]); Assert.Null(stale);
        Assert.Null(list.ItemContainerGenerator.ContainerFromIndex(0));
        var current = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.GetOrCreateContainerForIndex(0, out _));
        list.ItemContainerGenerator.PrepareItemContainer(current);
        Assert.Same(replacement, list.ItemContainerGenerator.ItemFromContainer(current)); Assert.Same(replacement, current.Content);
    }

    private sealed class GeneratingList : ListBox
    {
        internal Action? Create;
        protected override FrameworkElement GetContainerForItem(object item) { Create?.Invoke(); return new ListBoxItem(); }
    }

    private sealed class VisualOwner : Jalium.UI.Media.Visual { }

    private sealed class PeerAccess(ListBox owner) : ListBoxAutomationPeer(owner)
    {
        internal AutomationPeer? FromProvider(IRawElementProviderSimple? provider) => provider is null ? null : PeerFromProvider(provider);
    }

    private sealed class Fixture
    {
        internal readonly TestListBox List = new();
        internal readonly ObservableCollection<object> Source = new();
        internal ListBoxAutomationPeer Peer => Assert.IsType<ListBoxAutomationPeer>(List.GetAutomationPeer());
        internal Fixture(int kind, bool source)
        {
            object first = kind == 2 ? new EqualContainer { Content = "重复条目🙂" } : new Row(7);
            object second = kind == 1 ? first : kind == 2 ? new EqualContainer { Content = "重复条目🙂" } : new Row(7);
            Source.Add(first); Source.Add(second);
            if (source) List.ItemsSource = Source; else { List.Items.Add(first); List.Items.Add(second); }
        }
        internal object Item(int index) => List.Items[index]!;
        internal ListBoxItemAutomationPeer[] Rows() => Peer.GetChildren().Cast<ListBoxItemAutomationPeer>().ToArray();
        internal ListBoxItem Container(int index) => Assert.IsAssignableFrom<ListBoxItem>(List.ItemContainerGenerator.ContainerFromIndex(index));
        internal void Generate()
        {
            for (int i = 0; i < List.Items.Count; i++)
            {
                var container = List.ItemContainerGenerator.GetOrCreateContainerForIndex(i, out _)!;
                List.ItemContainerGenerator.PrepareItemContainer(container);
            }
        }
    }
    private sealed class TestListBox : ListBox
    {
        internal Action? IndexChanged;
        internal bool SetSelection(IEnumerable items) => SetSelectedItems(items);
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        { base.OnPropertyChanged(e); if (e.Property == SelectedIndexProperty) IndexChanged?.Invoke(); }
    }
    private sealed class CountingRange : IList
    {
        internal int Reads;
        public object? this[int index] { get { Reads++; return index; } set => throw new NotSupportedException(); }
        public int Count => 1_000_000;
        public bool IsReadOnly => true; public bool IsFixedSize => true; public bool IsSynchronized => false; public object SyncRoot => this;
        public IEnumerator GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]!; }
        public int Add(object? value) => throw new NotSupportedException(); public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException(); public void RemoveAt(int index) => throw new NotSupportedException(); public void Clear() => throw new NotSupportedException();
        public bool Contains(object? value) => value is int i && (uint)i < (uint)Count; public int IndexOf(object? value) => Contains(value) ? (int)value! : -1;
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
    }
    private sealed class Row(int value)
    {
        public override bool Equals(object? other) => other is Row row && value == row.Value;
        private int Value => value;
        public override int GetHashCode() => value;
        public override string ToString() => "重复条目🙂";
    }
    private sealed class EqualContainer : ListBoxItem
    {
        public override bool Equals(object? other) => other is EqualContainer;
        public override int GetHashCode() => 7;
    }
}
