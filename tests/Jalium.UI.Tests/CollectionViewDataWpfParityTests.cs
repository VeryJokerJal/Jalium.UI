using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using Jalium.UI.Data;
using ListSortDirection = Jalium.UI.Data.ListSortDirection;
using SortDescription = Jalium.UI.Data.SortDescription;
using GroupDescription = Jalium.UI.Data.GroupDescription;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class CollectionViewDataWpfParityTests
{
    [Fact]
    public void ApiSurface_ExposesCollectionViewAndBindingContracts()
    {
        const BindingFlags nonPublicInstance =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var collectionView = typeof(CollectionView);
        Assert.True(collectionView.GetEvent("CollectionChanged", nonPublicInstance)!.AddMethod!.IsFamily);
        Assert.True(collectionView.GetEvent("PropertyChanged", nonPublicInstance)!.AddMethod!.IsFamily);
        Assert.True(collectionView.GetMethod("GetEnumerator", nonPublicInstance)!.IsVirtual);
        Assert.Equal(typeof(IComparer), collectionView.GetProperty(nameof(CollectionView.Comparer))!.PropertyType);
        Assert.Same(CollectionView.NewItemPlaceholder, CollectionView.NewItemPlaceholder);

        foreach (var name in new[]
                 {
                     "AllowsCrossThreadChanges",
                     "IsCurrentInSync",
                     "IsDynamic",
                     "IsRefreshDeferred",
                     "UpdatedOutsideDispatcher",
                 })
        {
            Assert.True(collectionView.GetProperty(name, nonPublicInstance)!.GetMethod!.IsFamily);
        }

        Assert.NotNull(collectionView.GetMethod(
            "OnPropertyChanged",
            nonPublicInstance,
            null,
            [typeof(PropertyChangedEventArgs)],
            null));
        Assert.NotNull(collectionView.GetMethod(
            "OnCurrentChanging",
            nonPublicInstance,
            null,
            Type.EmptyTypes,
            null));
        Assert.NotNull(collectionView.GetMethod(
            "SetCurrent",
            nonPublicInstance,
            null,
            [typeof(object), typeof(int), typeof(int)],
            null));

        var listView = typeof(ListCollectionView);
        Assert.True(typeof(IComparer).IsAssignableFrom(listView));
        Assert.True(typeof(IEditableCollectionViewAddNewItem).IsAssignableFrom(listView));
        Assert.True(typeof(ICollectionViewLiveShaping).IsAssignableFrom(listView));
        Assert.True(typeof(IItemProperties).IsAssignableFrom(listView));
        Assert.True(listView.GetMethod("Compare", nonPublicInstance)!.IsVirtual);
        Assert.True(listView.GetProperty("ActiveComparer", nonPublicInstance)!.SetMethod!.IsFamily);
        Assert.True(listView.GetProperty("InternalList", nonPublicInstance)!.GetMethod!.IsFamily);
        Assert.Equal(typeof(GroupDescription),
            typeof(GroupDescriptionSelectorCallback).GetMethod("Invoke")!.ReturnType);

        var bindingListView = typeof(BindingListCollectionView);
        Assert.NotNull(bindingListView.GetConstructor([typeof(IBindingList)]));
        Assert.True(typeof(ICollectionViewLiveShaping).IsAssignableFrom(bindingListView));
        Assert.True(bindingListView.GetMethod(
            "RefreshOverride",
            nonPublicInstance)!.GetBaseDefinition().DeclaringType == typeof(CollectionView));

        var source = typeof(CollectionViewSource);
        Assert.True(typeof(IWeakEventListener).IsAssignableFrom(source));
        Assert.NotNull(source.GetField(nameof(CollectionViewSource.CollectionViewTypeProperty)));
        Assert.NotNull(source.GetField(nameof(CollectionViewSource.CanChangeLiveFilteringProperty)));
        Assert.NotNull(source.GetField(nameof(CollectionViewSource.IsLiveSortingProperty)));
        Assert.True(source.GetMethod("ReceiveWeakEvent", nonPublicInstance)!.IsVirtual);
        Assert.NotNull(source.GetMethod("OnSourceChanged", nonPublicInstance));

        Assert.Equal("Item[]", Binding.IndexerName);
        Assert.NotNull(typeof(Binding).GetField(nameof(Binding.XmlNamespaceManagerProperty)));
        Assert.NotNull(typeof(Binding).GetProperty(nameof(Binding.UpdateSourceExceptionFilter)));
        Assert.NotNull(typeof(Binding).GetMethod(nameof(Binding.ShouldSerializeValidationRules)));
    }

    [Fact]
    public void CollectionView_DefersRefreshAndMaintainsCancelableCurrency()
    {
        var source = new ObservableCollection<string> { "c", "a", "b" };
        var view = new CollectionView(source);
        view.SortDescriptions.Add(new SortDescription(string.Empty, ListSortDirection.Ascending));
        view.Filter = item => !Equals(item, "b");

        Assert.Equal(["a", "c"], view.Cast<string>());
        Assert.True(view.MoveCurrentToFirst());
        Assert.Equal("a", view.CurrentItem);

        CurrentChangingEventHandler cancel = (_, args) => args.Cancel = true;
        view.CurrentChanging += cancel;
        Assert.True(view.MoveCurrentTo("c"));
        Assert.Equal("a", view.CurrentItem);
        view.CurrentChanging -= cancel;

        using (view.DeferRefresh())
        {
            source.Add("aa");
            Assert.True(view.NeedsRefresh);
            Assert.Throws<InvalidOperationException>(() => _ = view.Count);
        }

        Assert.False(view.NeedsRefresh);
        Assert.Equal(["a", "aa", "c"], view.Cast<string>());
        Assert.Throws<InvalidOperationException>(
            () => new System.ComponentModel.CurrentChangingEventArgs(false).Cancel = true);
    }

    [Fact]
    public void ListCollectionView_ProvidesLiveShapingGroupingAndAddTransactions()
    {
        IList source = new ObservableCollection<Person>
        {
            new() { Name = "Beta", Score = 2, Team = "B" },
            new() { Name = "Alpha", Score = 1, Team = "A" },
        };
        var view = new ListCollectionView(source);
        view.SortDescriptions.Add(new SortDescription(nameof(Person.Score), ListSortDirection.Ascending));
        view.Filter = item => ((Person)item).Score > 0;
        view.IsLiveSorting = true;

        Assert.Equal(["Alpha", "Beta"], view.Cast<Person>().Select(item => item.Name));
        ((Person)source[1]!).Score = 4;
        Assert.Equal(["Beta", "Alpha"], view.Cast<Person>().Select(item => item.Name));

        view.GroupBySelector = (_, level) =>
            level == 0 ? new PropertyGroupDescription(nameof(Person.Team)) : null;
        Assert.Equal(2, view.Groups!.Count);
        Assert.Equal("B", ((CollectionViewGroup)view.Groups[0]).Name);

        view.NewItemPlaceholderPosition = NewItemPlaceholderPosition.AtBeginning;
        Assert.Same(CollectionView.NewItemPlaceholder, view.GetItemAt(0));
        var pending = new Person { Name = "Pending", Score = 4, Team = "A" };
        Assert.Same(pending, view.AddNewItem(pending));
        Assert.True(view.IsAddingNew);
        Assert.Equal(1, view.IndexOf(pending));
        view.CancelNew();
        Assert.False(view.IsAddingNew);
        Assert.DoesNotContain(pending, source.Cast<Person>());

        var properties = view.ItemProperties!;
        Assert.Contains(properties, property => property.Name == nameof(Person.Score));
    }

    [Fact]
    public void BindingListCollectionView_UsesBindingListTransactionsAndNotifications()
    {
        var source = new BindingList<Person>
        {
            new() { Name = "First", Score = 1, Team = "A" },
        };
        var view = new BindingListCollectionView(source)
        {
            NewItemPlaceholderPosition = NewItemPlaceholderPosition.AtEnd,
        };

        Assert.False(view.CanFilter);
        Assert.False(view.CanCustomFilter);
        Assert.False(view.CanChangeLiveSorting);
        Assert.True(view.CanChangeLiveGrouping);
        Assert.Throws<InvalidOperationException>(() => view.IsLiveSorting = true);
        Assert.Throws<NotSupportedException>(() => view.CustomFilter = "Score > 0");

        var added = Assert.IsType<Person>(view.AddNew());
        added.Name = "Added";
        view.CommitNew();
        Assert.Contains(added, source);
        Assert.Equal(source.Count + 1, view.Count);
        Assert.Same(CollectionView.NewItemPlaceholder, view.GetItemAt(view.Count - 1));

        source.Add(new Person { Name = "External", Score = 3, Team = "B" });
        Assert.Contains(view.Cast<object>(), item => item is Person person && person.Name == "External");
        Assert.Contains(view.ItemProperties!, property => property.Name == nameof(Person.Name));
    }

    [Fact]
    public void CollectionViewSource_ForwardsShapingLiveStateCustomTypeAndProviderChanges()
    {
        var items = new ObservableCollection<Person>
        {
            new() { Name = "Keep", Score = 2, Team = "A" },
            new() { Name = "Drop", Score = 0, Team = "B" },
        };
        var source = new CollectionViewSource();
        source.Filter += (_, args) => args.Accepted = ((Person)args.Item).Score > 0;
        source.SortDescriptions.Add(new SortDescription(nameof(Person.Name), ListSortDirection.Ascending));
        source.LiveSortingProperties.Add(nameof(Person.Name));
        source.IsLiveSortingRequested = true;
        source.Source = items;

        var view = Assert.IsType<ListCollectionView>(source.View);
        Assert.Equal(["Keep"], view.Cast<Person>().Select(item => item.Name));
        Assert.True(source.CanChangeLiveSorting);
        Assert.True(source.IsLiveSorting);
        Assert.Equal([nameof(Person.Name)], view.LiveSortingProperties);

        Assert.Same(CollectionViewSource.GetDefaultView(items), CollectionViewSource.GetDefaultView(items));
        Assert.True(CollectionViewSource.IsDefaultView(CollectionViewSource.GetDefaultView(items)));

        var typedSource = new CollectionViewSource();
        Assert.Throws<InvalidOperationException>(
            () => typedSource.CollectionViewType = typeof(ProbeCollectionView));

        typedSource = new CollectionViewSource();
        typedSource.BeginInit();
        typedSource.CollectionViewType = typeof(ProbeCollectionView);
        typedSource.Source = items;
        typedSource.EndInit();
        Assert.IsType<ProbeCollectionView>(typedSource.View);

        var provider = new ProbeDataSourceProvider();
        var providerSource = new CollectionViewSource { Source = provider };
        Assert.Null(providerSource.View);
        provider.Refresh();
        Assert.NotNull(providerSource.View);
    }

    [Fact]
    public void Binding_UsesXmlNamespacesProviderDataRoutedHandlersAndAsyncDispatch()
    {
        var document = new XmlDocument();
        document.LoadXml("<root xmlns:p='urn:test'><p:item value='ok'/></root>");
        var manager = new XmlNamespaceManager(document.NameTable);
        manager.AddNamespace("p", "urn:test");

        var target = new BindingTarget();
        Binding.SetXmlNamespaceManager(target, manager);
        Assert.Same(manager, Binding.GetXmlNamespaceManager(target));

        BindingOperations.SetBinding(
            target,
            BindingTarget.ValueProperty,
            new Binding { Source = document, XPath = "/root/p:item" });
        Assert.Equal("item", Assert.IsType<XmlElement>(target.Value).LocalName);

        var provider = new ProbeItemDataSourceProvider();
        provider.Refresh();
        BindingOperations.SetBinding(
            target,
            BindingTarget.ValueProperty,
            new Binding(nameof(Person.Name)) { Source = provider });
        Assert.Equal("Provided", target.Value);

        var element = new FrameworkElement();
        var targetUpdatedCount = 0;
        EventHandler<DataTransferEventArgs> handler = (_, _) => targetUpdatedCount++;
        Binding.AddTargetUpdatedHandler(element, handler);
        element.RaiseEvent(new DataTransferEventArgs(element, FrameworkElement.DataContextProperty)
        {
            RoutedEvent = Binding.TargetUpdatedEvent,
        });
        Binding.RemoveTargetUpdatedHandler(element, handler);
        Assert.Equal(1, targetUpdatedCount);
        Assert.Throws<ArgumentException>(
            () => Binding.AddTargetUpdatedHandler(new DependencyObject(), handler));

        var asyncTarget = new BindingTarget();
        BindingOperations.SetBinding(
            asyncTarget,
            BindingTarget.ValueProperty,
            new Binding(nameof(Person.Name))
            {
                Source = new Person { Name = "Async" },
                IsAsync = true,
                AsyncState = "state",
            });
        Assert.Null(asyncTarget.Value);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        Assert.Equal("Async", asyncTarget.Value);

        var binding = new Binding();
        Assert.False(binding.ShouldSerializePath());
        Assert.False(binding.ShouldSerializeSource());
        Assert.False(binding.ShouldSerializeValidationRules());
        binding.Path = new PropertyPath(nameof(Person.Name));
        Assert.True(binding.ShouldSerializePath());
    }

    private sealed class Person : INotifyPropertyChanged, IEditableObject
    {
        private string _name = string.Empty;
        private int _score;
        private string _team = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Score
        {
            get => _score;
            set
            {
                if (_score == value) return;
                _score = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Score)));
            }
        }

        public string Team
        {
            get => _team;
            set
            {
                if (_team == value) return;
                _team = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Team)));
            }
        }

        public void BeginEdit()
        {
        }

        public void CancelEdit()
        {
        }

        public void EndEdit()
        {
        }
    }

    public sealed class ProbeCollectionView : CollectionView
    {
        public ProbeCollectionView(IEnumerable source)
            : base(source)
        {
        }
    }

    private sealed class ProbeDataSourceProvider : DataSourceProvider
    {
        protected override void BeginQuery() =>
            OnQueryFinished(new ObservableCollection<Person>
            {
                new() { Name = "Provided", Score = 1, Team = "A" },
            });
    }

    private sealed class ProbeItemDataSourceProvider : DataSourceProvider
    {
        protected override void BeginQuery() =>
            OnQueryFinished(new Person { Name = "Provided", Score = 1, Team = "A" });
    }

    private sealed class BindingTarget : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(object),
                typeof(BindingTarget),
                new PropertyMetadata(null));

        public object? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
