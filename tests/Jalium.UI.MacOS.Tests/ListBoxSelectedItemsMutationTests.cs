using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ListBoxSelectedItemsMutationTests
{
    [Theory]
    [InlineData(SelectionMode.Multiple, false)] [InlineData(SelectionMode.Multiple, true)]
    [InlineData(SelectionMode.Extended, false)] [InlineData(SelectionMode.Extended, true)]
    public void AddRemoveAndClearKeepPublicPropertiesAndRowsInSync(SelectionMode mode, bool data)
    {
        using var f = new Fixture(mode, data); IList items = f.List.SelectedItems;
        Assert.Equal(0, items.Add(f.Item(1))); f.AssertSelection(1);
        Assert.Equal(1, items.Add(f.Item(2))); f.AssertSelection(1, 2);
        Assert.Equal(1, f.List.SelectedIndex);
        items.Remove(f.Item(1)); f.AssertSelection(2); Assert.Equal(2, f.List.SelectedIndex);
        items.Clear(); f.AssertSelection(); Assert.Equal(-1, f.List.SelectedIndex); Assert.Null(f.List.SelectedItem);
    }

    [Theory]
    [InlineData(SelectionMode.Multiple, false)] [InlineData(SelectionMode.Multiple, true)]
    [InlineData(SelectionMode.Extended, false)] [InlineData(SelectionMode.Extended, true)]
    public void InsertReplaceAndRemoveAtUseTheSameSelectionPipeline(SelectionMode mode, bool data)
    {
        using var f = new Fixture(mode, data); IList items = f.List.SelectedItems;
        items.Add(f.Item(1)); items.Insert(0, f.Item(0)); f.AssertSelection(0, 1);
        Assert.Same(f.Item(1), f.List.SelectedItem);
        items[1] = f.Item(2); f.AssertSelection(0, 2);
        items.RemoveAt(0); f.AssertSelection(2);
        Assert.Same(f.Item(2), Assert.Single(items.Cast<object>()));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void DuplicateAddsAreIdempotentAndPreserveTheActiveItem(bool data)
    {
        using var f = new Fixture(SelectionMode.Multiple, data);
        f.List.SelectedItems.Add(f.Item(0)); f.List.SelectedItems.Add(f.Item(2)); f.List.SelectedItem = f.Item(2);
        int changed = 0; f.List.SelectionChanged += (_, _) => changed++;
        Assert.Equal(0, f.List.SelectedItems.Add(f.Item(2)));
        f.AssertSelection(2); Assert.Equal(2, f.List.SelectedIndex); Assert.Equal(0, changed);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SingleModeCollectionRemainsReadableButCannotContainTwoItems(bool data)
    {
        using var f = new Fixture(SelectionMode.Single, data); f.List.SelectedIndex = 1;
        Assert.Single(f.List.SelectedItems); Assert.Equal(f.Item(1), f.List.SelectedItems[0]);
        Assert.Throws<InvalidOperationException>(() => f.List.SelectedItems.Add(f.Item(2)));
        f.AssertSelection(1);
        f.List.SelectedItems.Clear(); f.AssertSelection(); Assert.Equal(-1, f.List.SelectedIndex);
        f.List.SelectedItems.Add(f.Item(0)); f.AssertSelection(0);
    }

    [Theory] [InlineData("add")] [InlineData("insert")] [InlineData("replace")]
    public void ForeignAndNullItemsAreRejectedBeforeChangingSelection(string operation)
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1;
        void Change(object? item)
        {
            if (operation == "add") f.List.SelectedItems.Add(item);
            else if (operation == "insert") f.List.SelectedItems.Insert(0, item);
            else f.List.SelectedItems[0] = item;
        }
        Assert.Throws<ArgumentException>(() => Change(new object())); f.AssertSelection(1);
        Assert.Throws<ArgumentException>(() => Change(null)); f.AssertSelection(1);
    }

    [Fact]
    public void InvalidIndicesAndMissingRemovalRetainTheOldSelection()
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1; var items = f.List.SelectedItems;
        Assert.Throws<ArgumentOutOfRangeException>(() => items.Insert(-1, f.Item(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => items.Insert(2, f.Item(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => items.RemoveAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => items[2] = f.Item(0));
        items.Remove(new object()); f.AssertSelection(1);
    }

    [Fact]
    public void EverySelectionEntryPointKeepsOneLiveObservablePropertyValue()
    {
        using var f = new Fixture(); var items = f.List.SelectedItems;
        Assert.Same(items, f.List.GetValue(ListBox.SelectedItemsProperty)); Assert.Same(items, f.List.SharedItems);
        var observable = Assert.IsAssignableFrom<INotifyCollectionChanged>(items);
        var properties = Assert.IsAssignableFrom<INotifyPropertyChanged>(items);
        var snapshots = new List<int[]>(); var names = new List<string?>();
        observable.CollectionChanged += (_, _) => { f.AssertCoherent(); snapshots.Add(f.Indices()); };
        properties.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        f.List.SelectedIndex = 1; Assert.Equal(new[] { 1 }, Assert.Single(snapshots));
        snapshots.Clear(); Assert.True(f.List.Choose(f.Item(0), f.Item(2)));
        Assert.Equal(new[] { 0, 2 }, Assert.Single(snapshots));
        snapshots.Clear(); f.List.Items.Remove(f.Item(0));
        Assert.Single(snapshots); Assert.Single(items);
        Assert.Contains("Count", names); Assert.Contains("Item[]", names);
        Assert.Same(items, f.List.SelectedItems); Assert.Same(items, f.List.GetValue(ListBox.SelectedItemsProperty));
    }

    [Fact]
    public void CollectionObserversSeeOnlyCommittedSelectionAndMayMakeANewerChoice()
    {
        using var f = new Fixture();
        var observer = Assert.IsAssignableFrom<INotifyCollectionChanged>(f.List.SelectedItems);
        int notifications = 0;
        observer.CollectionChanged += (_, _) =>
        {
            notifications++; f.AssertCoherent();
            if (f.List.SelectedIndex == 1 && f.List.SelectedItems.Count == 1) f.List.SelectedItems.Add(f.Item(2));
        };
        f.List.SelectedItems.Add(f.Item(1));
        f.AssertSelection(1, 2); Assert.Equal(2, notifications);
    }

    [Fact]
    public void SelectionHandlersCanReplaceACollectionMutation()
    {
        using var f = new Fixture();
        f.List.SelectionChanged += (_, _) =>
        {
            if (f.List.SelectedIndex == 1) { f.List.SelectedItems.Clear(); f.List.SelectedItems.Add(f.Item(2)); }
        };
        f.List.SelectedItems.Add(f.Item(1)); f.AssertSelection(2);
    }

    [Fact]
    public void ThrowingSelectionHandlerDoesNotSuppressCommittedCollectionNotification()
    {
        using var f = new Fixture(); int notifications = 0;
        Assert.IsAssignableFrom<INotifyCollectionChanged>(f.List.SelectedItems).CollectionChanged += (_, _) => { notifications++; f.AssertCoherent(); };
        f.List.SelectionChanged += (_, _) => throw new ApplicationException("observer failed");
        Assert.Throws<ApplicationException>(() => f.List.SelectedItems.Add(f.Item(1)));
        f.AssertSelection(1); Assert.Equal(1, notifications);
    }

    [Fact]
    public void PublicCollectionEditsPublishTheFinalTwoWaySelectionBinding()
    {
        using var f = new Fixture(); var source = new SelectionSource();
        f.List.SetBinding(Selector.SelectedIndexProperty, new Binding(nameof(SelectionSource.Index))
        { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        f.List.SelectedItems.Add(f.Item(2)); Assert.Equal(2, source.Index);
        f.List.SelectedItems.Clear(); Assert.Equal(-1, source.Index);
    }

    [Fact]
    public void AliasOnlyBatchChangesStillNotifyTheStableCollection()
    {
        using var f = new Fixture(); f.List.SelectedIndex = 1; int changes = 0;
        Assert.IsAssignableFrom<INotifyCollectionChanged>(f.List.SelectedItems).CollectionChanged += (_, _) => { changes++; f.AssertCoherent(); };
        Assert.True(f.List.Choose(f.Item(1)));
        Assert.Equal("Beta", Assert.Single(f.List.SelectedItems.Cast<object>())); Assert.Equal(1, changes); f.AssertSelection(1);
    }

    [Fact]
    public void PropertyObserverFailureStillPublishesTheCollectionAndBinding()
    {
        using var f = new Fixture(); var source = new SelectionSource(); int changes = 0;
        f.List.SetBinding(Selector.SelectedIndexProperty, new Binding(nameof(SelectionSource.Index))
        { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Assert.IsAssignableFrom<INotifyCollectionChanged>(f.List.SelectedItems).CollectionChanged += (_, _) => changes++;
        Assert.IsAssignableFrom<INotifyPropertyChanged>(f.List.SelectedItems).PropertyChanged += (_, e) =>
        { if (e.PropertyName == "Count") throw new ApplicationException("count observer failed"); };
        Assert.Throws<ApplicationException>(() => f.List.SelectedItems.Add(f.Item(2)));
        f.AssertSelection(2); Assert.Equal(2, source.Index); Assert.Equal(1, changes);
    }

    [Fact]
    public void GenericListInterfacesPreserveMutableCollectionCompatibility()
    {
        using var f = new Fixture(); var items = Assert.IsAssignableFrom<IList<object>>(f.List.SelectedItems);
        Assert.IsAssignableFrom<IReadOnlyList<object>>(items);
        items.Add(f.Item(0)); items.Insert(1, f.Item(2)); f.AssertSelection(0, 2);
        object[] copy = new object[2]; items.CopyTo(copy, 0); Assert.Equal(new[] { f.Item(0), f.Item(2) }, copy);
        Assert.True(items.Remove(f.Item(0))); Assert.False(items.Remove(new object())); f.AssertSelection(2);
    }

    [Fact]
    public void ModeChangesNotifyTheSameLiveCollectionAfterTrimmingSelection()
    {
        using var f = new Fixture(); f.List.SelectedItems.Add(f.Item(0)); f.List.SelectedItems.Add(f.Item(2));
        var items = f.List.SelectedItems; int changes = 0;
        Assert.IsAssignableFrom<INotifyCollectionChanged>(items).CollectionChanged += (_, _) => { changes++; f.AssertCoherent(); };
        f.List.SelectionMode = SelectionMode.Single;
        Assert.Single(items); Assert.Equal(1, changes); Assert.Same(items, f.List.SelectedItems);
    }

    private sealed class SelectionSource { public int Index { get; set; } = -1; }

    [Fact]
    public void EqualValuedReplacementStillPublishesTheNewItemReference()
    {
        var first = new EqualValue("same"); var replacement = new EqualValue("same");
        var list = new ListBox { SelectionMode = SelectionMode.Multiple }; list.Items.Add(first); list.SelectedItems.Add(first);
        int changes = 0;
        Assert.IsAssignableFrom<INotifyCollectionChanged>(list.SelectedItems).CollectionChanged += (_, _) => changes++;
        list.SelectedItems[0] = replacement;
        Assert.Same(replacement, list.SelectedItems[0]); Assert.Equal(1, changes);
    }
    private sealed record EqualValue(string Value);
    private sealed class SelectionList : ListBox
    {
        internal Panel? Host => ItemsHost;
        internal IList SharedItems => SelectedItemsImpl;
        internal bool Choose(params object[] items) => SetSelectedItems(items);
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly SelectionList List;
        private readonly bool _data;
        private readonly string[] _labels = ["Alpha", "Beta", "Gamma"];
        private readonly ListBoxItem[] _rows = [new() { Content = "Alpha" }, new() { Content = "Beta" }, new() { Content = "Gamma" }];
        private readonly DisplayedTestWindow _window;
        internal Fixture(SelectionMode mode = SelectionMode.Multiple, bool data = false)
        {
            _data = data; List = new SelectionList { SelectionMode = mode, Height = 200 };
            for (int i = 0; i < 3; i++) List.Items.Add(Item(i));
            _window = new DisplayedTestWindow { Content = List, TitleBarStyle = WindowTitleBarStyle.Native, Width = 400, Height = 240 };
            _window.Measure(new Size(400, 240)); _window.Arrange(new Rect(0, 0, 400, 240));
            if (List.Host is { } host) { host.Measure(new Size(400, 200)); host.Arrange(new Rect(0, 0, 400, 200)); }
        }
        internal object Item(int i) => _data ? _labels[i] : _rows[i];
        private static object Value(object item) => item is ListBoxItem row ? row.Content ?? row : item;
        internal int[] Indices() => Enumerable.Range(0, 3).Where(i => List.SelectedItems.Cast<object>().Any(item => Equals(Value(item), Value(Item(i))))).ToArray();
        internal void AssertSelection(params int[] indices) { Assert.Equal(indices, Indices()); AssertCoherent(); }
        internal void AssertCoherent()
        {
            var items = List.SelectedItems.Cast<object>().ToArray();
            Assert.Equal(items.Length == 0, List.SelectedItem is null);
            if (List.SelectedItem is not null) Assert.Contains(items, item => Equals(Value(item), Value(List.SelectedItem)));
            for (int i = 0; i < List.Items.Count; i++)
            {
                var row = List.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                Assert.NotNull(row);
                Assert.Equal(items.Any(item => Equals(Value(item), Value(List.Items[i]))), row.IsSelected);
            }
        }
        public void Dispose() => _window.Close();
    }
}
