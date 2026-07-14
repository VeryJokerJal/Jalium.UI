using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using IEditableCollectionView = System.ComponentModel.IEditableCollectionView;
using IEditableCollectionViewAddNewItem = System.ComponentModel.IEditableCollectionViewAddNewItem;
using ICollectionViewLiveShaping = System.ComponentModel.ICollectionViewLiveShaping;
using ListSortDirection = System.ComponentModel.ListSortDirection;
using SortDescription = System.ComponentModel.SortDescription;

namespace Jalium.UI.Tests;

public sealed class ItemCollectionParityTests
{
    [Fact]
    public void ApiSurface_MatchesCollectionViewAndEditingContracts()
    {
        var type = typeof(ItemCollection);

        Assert.Equal(typeof(CollectionView), type.BaseType);
        Assert.True(typeof(IList).IsAssignableFrom(type));
        Assert.False(typeof(IList<object>).IsAssignableFrom(type));
        Assert.True(typeof(IEditableCollectionView).IsAssignableFrom(type));
        Assert.True(typeof(IEditableCollectionViewAddNewItem).IsAssignableFrom(type));
        Assert.True(typeof(ICollectionViewLiveShaping).IsAssignableFrom(type));
        Assert.True(typeof(IItemProperties).IsAssignableFrom(type));
        Assert.True(typeof(IWeakEventListener).IsAssignableFrom(type));

        Assert.Equal(typeof(int), type.GetMethod(nameof(ItemCollection.Add), [typeof(object)])!.ReturnType);
        Assert.Equal(typeof(void), type.GetMethod(nameof(ItemCollection.Remove), [typeof(object)])!.ReturnType);
        Assert.NotNull(type.GetMethod(nameof(ItemCollection.CopyTo), [typeof(Array), typeof(int)]));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name == nameof(ItemCollection.CopyTo)
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual([typeof(object[]), typeof(int)]));
        Assert.Null(type.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(type.GetMethod("AddRange", BindingFlags.Instance | BindingFlags.Public));

        var getEnumerator = type.GetMethod(
            "GetEnumerator",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(getEnumerator);
        Assert.True(getEnumerator!.IsFamily);
        Assert.Equal(typeof(IEnumerator), getEnumerator.ReturnType);
        Assert.Equal(typeof(CollectionView), getEnumerator.GetBaseDefinition().DeclaringType);
    }

    [Fact]
    public void DirectItems_ProvideRealSortingFilteringAndCurrency()
    {
        var control = new ItemsControl();
        var items = control.Items;

        Assert.Same(items, items.SourceCollection);
        Assert.False(items.CanGroup);
        Assert.Null(items.CurrentItem);
        Assert.Equal(-1, items.CurrentPosition);
        Assert.False(items.IsCurrentAfterLast);
        Assert.False(items.IsCurrentBeforeFirst);

        Assert.Equal(0, items.Add("b"));
        Assert.Equal(1, items.Add("a"));
        Assert.True(items.IsCurrentBeforeFirst);

        items.SortDescriptions.Add(new SortDescription(string.Empty, ListSortDirection.Ascending));
        Assert.Equal(["a", "b"], items.Cast<string>());
        Assert.Equal("a", items.GetItemAt(0));
        Assert.Equal(1, items.IndexOf("b"));

        items.Filter = item => !Equals(item, "b");
        Assert.Equal(["a"], items.Cast<string>());
        Assert.True(items.PassesFilter("a"));
        Assert.False(items.PassesFilter("b"));
        Assert.True(items.MoveCurrentToFirst());
        Assert.Equal("a", items.CurrentItem);
        Assert.Equal(0, items.CurrentPosition);
    }

    [Fact]
    public void ItemsSource_SwitchesTheActiveViewAndPreservesShaping()
    {
        var control = new ItemsControl();
        var items = control.Items;
        items.SortDescriptions.Add(new SortDescription(string.Empty, ListSortDirection.Ascending));
        items.Filter = item => !Equals(item, "skip");

        var source = new ObservableCollection<string> { "b", "skip", "a" };
        control.ItemsSource = source;

        Assert.Same(source, items.SourceCollection);
        Assert.True(items.CanGroup);
        Assert.Equal(["a", "b"], items.Cast<string>());

        var notifications = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)items).CollectionChanged +=
            (_, e) => notifications.Add(e.Action);
        source.Add("c");

        Assert.Equal(["a", "b", "c"], items.Cast<string>());
        Assert.Contains(NotifyCollectionChangedAction.Reset, notifications);

        control.ItemsSource = null;
        Assert.Same(items, items.SourceCollection);
        Assert.Empty(items);
        Assert.Single(items.SortDescriptions);
        Assert.NotNull(items.Filter);
    }

    [Fact]
    public void DeferRefresh_BatchesShapingAndMarksTheViewStale()
    {
        var items = new ItemsControl().Items;
        items.Add("b");
        items.Add("a");

        using (items.DeferRefresh())
        {
            items.Filter = item => Equals(item, "a");
            items.SortDescriptions.Add(
                new SortDescription(string.Empty, ListSortDirection.Ascending));

            Assert.True(items.NeedsRefresh);
            Assert.Throws<InvalidOperationException>(() => items.GetItemAt(0));
            Assert.Throws<InvalidOperationException>(() => items.Contains("a"));
        }

        Assert.False(items.NeedsRefresh);
        Assert.Equal(["a"], items.Cast<string>());
    }

    [Fact]
    public void EditableView_AddNewItemCancelCommitAndRemoveMutateTheSource()
    {
        var source = new ObservableCollection<MutableItem>
        {
            new() { Name = "existing" },
        };
        var control = new ItemsControl { ItemsSource = source };
        var editable = (IEditableCollectionView)control.Items;
        var addNew = (IEditableCollectionViewAddNewItem)control.Items;

        Assert.False(new ItemsControl().Items is IEditableCollectionView direct && direct.CanAddNew);
        Assert.True(addNew.CanAddNewItem);
        Assert.True(editable.CanRemove);

        var canceled = new MutableItem { Name = "cancel" };
        Assert.Same(canceled, addNew.AddNewItem(canceled));
        Assert.Same(canceled, editable.CurrentAddItem);
        editable.CancelNew();
        Assert.DoesNotContain(canceled, source);

        var committed = new MutableItem { Name = "commit" };
        addNew.AddNewItem(committed);
        editable.CommitNew();
        Assert.Contains(committed, source);

        editable.Remove(committed);
        Assert.DoesNotContain(committed, source);
    }

    [Fact]
    public void LiveSorting_RefreshesWhenAConfiguredItemPropertyChanges()
    {
        var first = new MutableItem { Name = "b" };
        var second = new MutableItem { Name = "a" };
        var source = new ObservableCollection<MutableItem> { first, second };
        var control = new ItemsControl { ItemsSource = source };
        var items = control.Items;

        items.SortDescriptions.Add(
            new SortDescription(nameof(MutableItem.Name), ListSortDirection.Ascending));
        items.LiveSortingProperties.Add(nameof(MutableItem.Name));
        items.IsLiveSorting = true;

        Assert.True(items.CanChangeLiveSorting);
        Assert.True(items.IsLiveSorting);
        Assert.Equal([second, first], items.Cast<MutableItem>());

        first.Name = "0";
        Assert.Equal([first, second], items.Cast<MutableItem>());
    }

    [Fact]
    public void ItemProperties_DescribeTheDeclaredSourceItemType()
    {
        var source = new ObservableCollection<MutableItem>
        {
            new() { Name = "item" },
        };
        var control = new ItemsControl { ItemsSource = source };

        var properties = ((IItemProperties)control.Items).ItemProperties;

        Assert.NotNull(properties);
        Assert.Contains(properties!, property =>
            property.Name == nameof(MutableItem.Name) &&
            property.PropertyType == typeof(string));
    }

    private sealed class MutableItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }

                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
}
