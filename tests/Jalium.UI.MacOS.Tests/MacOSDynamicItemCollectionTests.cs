using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDynamicItemCollectionTests
{
    [Theory]
    [InlineData("list", 0)] [InlineData("list", 1)] [InlineData("list", 2)]
    [InlineData("combo", 0)] [InlineData("combo", 1)] [InlineData("combo", 2)]
    [InlineData("grid", 0)] [InlineData("grid", 1)] [InlineData("grid", 2)]
    public void RetainedLogicalSelectionCannotActOnAReinsertedItem(string kind, int action)
    {
        var old = new Entry("obsolete"); var survivor = new Entry("survivor");
        var source = new ObservableCollection<object> { survivor, old };
        var owner = Create(kind); owner.ItemsSource = source;
        var parent = (ItemsControlAutomationPeer)owner.GetAutomationPeer()!;
        var child = parent.GetChildren().OfType<ItemAutomationPeer>().Single(x => ReferenceEquals(x.Item, old));
        var provider = (ISelectionItemProvider)child.GetPattern(PatternInterface.SelectionItem)!;
        source.RemoveAt(1); source.Add(old);
        SetSelected(owner, survivor);
        Assert.False(provider.IsSelected); Assert.False(child.IsEnabled()); Assert.True(child.IsOffscreen());
        if (child is IVirtualizedItemProvider virtualized) Assert.Throws<InvalidOperationException>(() => virtualized.Realize());
        if (action == 0) Assert.Throws<InvalidOperationException>(() => provider.Select());
        else if (action == 1) Assert.Throws<InvalidOperationException>(() => provider.AddToSelection());
        else Assert.Throws<InvalidOperationException>(() => provider.RemoveFromSelection());
        Assert.Same(survivor, Selected(owner));
        var replacement = parent.GetChildren().OfType<ItemAutomationPeer>().Single(x => ReferenceEquals(x.Item, old));
        Assert.NotSame(child, replacement);
        ((ISelectionItemProvider)replacement.GetPattern(PatternInterface.SelectionItem)!).Select();
        Assert.Same(old, Selected(owner));
    }

    [Fact]
    public void ResetInvalidatesAllOldLogicalLeasesEvenForTheSameObjects()
    {
        var entry = new Entry("same object"); var combo = new ComboBox { ItemsSource = new[] { entry } };
        var peer = (ComboBoxAutomationPeer)combo.GetAutomationPeer()!; var old = peer.GetChildren()[0];
        var provider = (ISelectionItemProvider)old.GetPattern(PatternInterface.SelectionItem)!;
        combo.ItemsSource = new[] { entry };
        Assert.Throws<InvalidOperationException>(() => provider.Select());
        Assert.NotSame(old, peer.GetChildren()[0]);
    }

    [Fact]
    public void DirectItemsRetireOldModelsWithoutWaitingForAnotherQuery()
    {
        var owner = new ItemsControl(); var weak = DirectRemove(owner); Collect();
        Assert.False(weak.IsAlive); GC.KeepAlive(owner);
    }

    [Fact]
    public void AThrowingOwnerCallbackDoesNotRetainAnAlreadyRemovedModel()
    {
        var owner = new ThrowingItems(); var weak = RemoveWithThrow(owner); Collect();
        Assert.False(weak.IsAlive); GC.KeepAlive(owner);
    }

    [Fact]
    public void RemovingOneRepeatedObjectCreatesAFreshLogicalLeaseForTheRemainingOccurrence()
    {
        var entry = new Entry("duplicate"); var source = new ObservableCollection<object> { entry, entry };
        var combo = new ComboBox { ItemsSource = source }; var peer = combo.GetAutomationPeer()!;
        var old = peer.GetChildren()[0]; var provider = (ISelectionItemProvider)old.GetPattern(PatternInterface.SelectionItem)!;
        source.RemoveAt(0);
        Assert.Throws<InvalidOperationException>(() => provider.Select());
        var current = Assert.Single(peer.GetChildren()); Assert.NotSame(old, current);
        ((ISelectionItemProvider)current.GetPattern(PatternInterface.SelectionItem)!).Select();
        Assert.Equal(0, combo.SelectedIndex);
    }

    [Fact]
    public void SparseSelectionResetDoesNotEnumerateAMillionLogicalItems()
    {
        var source = new Range(1_000_000); var combo = new ComboBox(); Virtualize(combo);
        combo.ItemsSource = source; combo.SelectedIndex = 999_999;
        var peer = (ComboBoxAutomationPeer)combo.GetAutomationPeer()!; var selected = Assert.Single(peer.GetSelection());
        int reads = source.Reads, enumerations = source.Enumerations;
        source.Reset(2);
        Assert.True(source.Reads - reads < 30); Assert.Equal(enumerations, source.Enumerations);
        Assert.False(selected.IsEnabled()); Assert.Empty(peer.GetSelection());
    }

    [Fact]
    public void APendingStructureNotificationDoesNotKeepAnOtherwiseUnreferencedControlAlive()
    {
        var weak = QueueThenDrop();
        // CSS work can legitimately retain its pending roots after another test
        // activates styling. Leave the weak StructureChanged callback pending.
        Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
        Collect(); Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference QueueThenDrop()
    {
        var owner = new ItemsControl(); _ = owner.GetAutomationPeer(); owner.Items.Add(new Entry("pending"));
        return new WeakReference(owner);
    }

    private static ItemsControl Create(string kind) => kind switch
    { "list" => new ListBox(), "combo" => new ComboBox(), "grid" => new DataGrid(), _ => throw new ArgumentException(kind) };
    private static void SetSelected(ItemsControl owner, object item)
    { if (owner is Jalium.UI.Controls.Primitives.Selector selector) selector.SelectedItem = item; else ((DataGrid)owner).SelectedItem = item; }
    private static object? Selected(ItemsControl owner) => owner is Jalium.UI.Controls.Primitives.Selector selector
        ? selector.SelectedItem : ((DataGrid)owner).SelectedItem;
    private static void Virtualize(ItemsControl owner)
    {
        var template = new ItemsPanelTemplate(); template.SetVisualTree(() => new VirtualizingStackPanel());
        owner.ItemsPanel = template; VirtualizingPanel.SetIsVirtualizing(owner, true);
    }
    private sealed record Entry(string Text) { public override string ToString() => Text; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DirectRemove(ItemsControl owner)
    {
        owner.Items.Add(new Entry("survivor")); var target = new Entry("obsolete"); owner.Items.Add(target);
        _ = owner.GetAutomationPeer()!.GetChildren(); var weak = new WeakReference(target); owner.Items.RemoveAt(1); return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RemoveWithThrow(ThrowingItems owner)
    {
        var target = new Entry("obsolete"); var source = new ObservableCollection<object> { new Entry("survivor"), target };
        owner.ItemsSource = source; _ = owner.GetAutomationPeer()!.GetChildren(); var weak = new WeakReference(target);
        owner.Throw = true; Assert.Throws<ApplicationException>(() => source.RemoveAt(1)); return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed class ThrowingItems : ItemsControl
    {
        internal bool Throw;
        internal ThrowingItems() { Virtualize(this); }
        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        { base.OnItemsChanged(e); if (Throw) throw new ApplicationException("owner callback"); }
    }
    private sealed class Range(int count) : IList, INotifyCollectionChanged
    {
        private int _count = count;
        private readonly Dictionary<int, EntryAt> _created = [];
        internal int Reads, Enumerations;
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        internal void Reset(int size) { _count = size; CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Reset)); }
        public int Count => _count;
        public object? this[int index]
        {
            get { Reads++; if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
                if (!_created.TryGetValue(index, out var item)) _created.Add(index, item = new(index)); return item; }
            set => throw new NotSupportedException();
        }
        public int IndexOf(object? item) => item is EntryAt entry && entry.Index >= 0 && entry.Index < _count ? entry.Index : -1;
        public bool Contains(object? item) => IndexOf(item) >= 0;
        public IEnumerator GetEnumerator() { Enumerations++; throw new InvalidOperationException("A sparse query enumerated the logical source."); }
        public bool IsFixedSize => false;
        public bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array array, int index) => throw new NotSupportedException();
        public int Add(object? value) => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        private sealed record EntryAt(int Index) { public override string ToString() => "entry " + Index; }
    }
}
