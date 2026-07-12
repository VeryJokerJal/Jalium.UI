using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using CanonicalCollectionChangedEventManager = System.Collections.Specialized.CollectionChangedEventManager;
using CanonicalDependencyPropertyDescriptor = System.ComponentModel.DependencyPropertyDescriptor;
using CanonicalDesignerProperties = System.ComponentModel.DesignerProperties;
using CanonicalGroupDescription = System.ComponentModel.GroupDescription;
using CanonicalCollectionViewLiveShaping = System.ComponentModel.ICollectionViewLiveShaping;
using CanonicalCurrentChangingEventArgs = System.ComponentModel.CurrentChangingEventArgs;
using CanonicalEditableCollectionView = System.ComponentModel.IEditableCollectionView;
using CanonicalEditableCollectionViewAddNewItem = System.ComponentModel.IEditableCollectionViewAddNewItem;
using CanonicalListSortDirection = System.ComponentModel.ListSortDirection;
using CanonicalPropertyChangedEventManager = System.ComponentModel.PropertyChangedEventManager;
using CanonicalSortDescription = System.ComponentModel.SortDescription;
using CanonicalSortDescriptionCollection = System.ComponentModel.SortDescriptionCollection;
using LegacySortDescription = Jalium.UI.Data.SortDescription;
using LegacySortDirection = Jalium.UI.Data.ListSortDirection;

namespace Jalium.UI.Tests;

public sealed class ComponentModelNamespaceWpfParityTests
{
    [Fact]
    public void CanonicalTypesAndItemCollectionShapingContractsHaveWpfIdentity()
    {
        Assert.Equal("System.ComponentModel", typeof(CanonicalCurrentChangingEventArgs).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalDependencyPropertyDescriptor).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalDesignerProperties).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalGroupDescription).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalCollectionViewLiveShaping).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalEditableCollectionView).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalEditableCollectionViewAddNewItem).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalPropertyChangedEventManager).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalSortDescription).Namespace);
        Assert.Equal("System.ComponentModel", typeof(CanonicalSortDescriptionCollection).Namespace);
        Assert.Equal("System.Collections.Specialized", typeof(CanonicalCollectionChangedEventManager).Namespace);

        Assert.Equal(
            typeof(ObservableCollection<CanonicalGroupDescription>),
            typeof(ItemCollection).GetProperty(nameof(ItemCollection.GroupDescriptions))!.PropertyType);
        Assert.Equal(
            typeof(CanonicalSortDescriptionCollection),
            typeof(ItemCollection).GetProperty(nameof(ItemCollection.SortDescriptions))!.PropertyType);
        Assert.True(typeof(CanonicalCollectionViewLiveShaping).IsAssignableFrom(typeof(ItemCollection)));
        Assert.True(typeof(CanonicalEditableCollectionView).IsAssignableFrom(typeof(ListCollectionView)));
        Assert.True(typeof(CanonicalEditableCollectionViewAddNewItem).IsAssignableFrom(typeof(ListCollectionView)));
    }

    [Fact]
    public void CanonicalSortDescriptionsAreMutableUntilCollectionSealsThem()
    {
        var sort = new CanonicalSortDescription("Name", CanonicalListSortDirection.Ascending);
        sort.PropertyName = "DisplayName";
        sort.Direction = CanonicalListSortDirection.Descending;
        Assert.False(sort.IsSealed);

        var collection = new CanonicalSortDescriptionCollection();
        var changes = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) => changes.Add(args.Action);
        collection.Add(sort);

        CanonicalSortDescription sealedSort = collection[0];
        Assert.True(sealedSort.IsSealed);
        Assert.Throws<InvalidOperationException>(() => sealedSort.PropertyName = "Other");

        collection[0] = new CanonicalSortDescription("Score", CanonicalListSortDirection.Ascending);
        Assert.Equal(
            [
                NotifyCollectionChangedAction.Add,
                NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Add,
            ],
            changes);
        Assert.Throws<NotSupportedException>(() => CanonicalSortDescriptionCollection.Empty.Add(sort));
    }

    [Fact]
    public void CanonicalGroupingOwnsSortAndCustomComparerSemantics()
    {
        var group = new CanonicalProbeGroup();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)group).PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        group.GroupNames.Add("known");
        group.CustomSort = Comparer.DefaultInvariant;
        group.SortDescriptions.Add(new CanonicalSortDescription("Name", CanonicalListSortDirection.Ascending));

        Assert.Null(group.CustomSort);
        Assert.True(group.ShouldSerializeGroupNames());
        Assert.True(group.ShouldSerializeSortDescriptions());
        Assert.Contains(nameof(CanonicalGroupDescription.GroupNames), changed);
        Assert.Contains(nameof(CanonicalGroupDescription.CustomSort), changed);
        Assert.Contains(nameof(CanonicalGroupDescription.SortDescriptions), changed);

        var view = new CollectionView(new[] { "b", "a" });
        view.GroupDescriptions.Add(group);
        view.Refresh();
        Assert.NotNull(view.Groups);

        var legacyGroup = new PropertyGroupDescription();
        Assert.IsAssignableFrom<CanonicalGroupDescription>(legacyGroup);
        view.GroupDescriptions[0] = legacyGroup;
    }

    [Fact]
    public void LegacySortDescriptionFlowsIntoCanonicalCollections()
    {
        var view = new CollectionView(new[] { "b", "a" });
        var legacy = new LegacySortDescription(string.Empty, LegacySortDirection.Descending);
        view.SortDescriptions.Add(legacy);

        Assert.Equal(CanonicalListSortDirection.Descending, view.SortDescriptions[0].Direction);
        Assert.Equal(new[] { "b", "a" }, view.Cast<string>());

        LegacySortDescription roundTrip = view.SortDescriptions[0];
        Assert.Equal(LegacySortDirection.Descending, roundTrip.Direction);
    }

    [Fact]
    public void CanonicalWeakEventManagersFilterAndDetachHandlers()
    {
        var propertySource = new PropertySource();
        var listener = new RecordingWeakListener();
        int named = 0;
        int all = 0;
        EventHandler<PropertyChangedEventArgs> namedHandler = (_, _) => named++;
        EventHandler<PropertyChangedEventArgs> allHandler = (_, _) => all++;

        CanonicalPropertyChangedEventManager.AddListener(propertySource, listener, nameof(PropertySource.Name));
        CanonicalPropertyChangedEventManager.AddHandler(propertySource, namedHandler, nameof(PropertySource.Name));
        CanonicalPropertyChangedEventManager.AddHandler(propertySource, allHandler, string.Empty);
        propertySource.Raise(nameof(PropertySource.Other));
        propertySource.Raise(nameof(PropertySource.Name));

        Assert.Equal(1, named);
        Assert.Equal(2, all);
        Assert.Equal(1, listener.Deliveries);

        CanonicalPropertyChangedEventManager.RemoveListener(propertySource, listener, nameof(PropertySource.Name));
        CanonicalPropertyChangedEventManager.RemoveHandler(propertySource, namedHandler, nameof(PropertySource.Name));
        CanonicalPropertyChangedEventManager.RemoveHandler(propertySource, allHandler, string.Empty);
        propertySource.Raise(nameof(PropertySource.Name));
        Assert.Equal(1, named);
        Assert.Equal(2, all);

        var collection = new ObservableCollection<int>();
        int collectionChanges = 0;
        EventHandler<NotifyCollectionChangedEventArgs> collectionHandler = (_, _) => collectionChanges++;
        CanonicalCollectionChangedEventManager.AddHandler(collection, collectionHandler);
        collection.Add(1);
        CanonicalCollectionChangedEventManager.RemoveHandler(collection, collectionHandler);
        collection.Add(2);
        Assert.Equal(1, collectionChanges);
    }

    [Fact]
    public void CanonicalDependencyDescriptorAndDesignerPropertyAreFunctional()
    {
        var element = new DescriptorProbe();
        CanonicalDependencyPropertyDescriptor descriptor =
            CanonicalDependencyPropertyDescriptor.FromProperty(DescriptorProbe.ValueProperty, typeof(DescriptorProbe));
        Assert.Same(
            descriptor,
            CanonicalDependencyPropertyDescriptor.FromName(
                nameof(DescriptorProbe.Value),
                typeof(DescriptorProbe),
                typeof(DescriptorProbe),
                ignorePropertyType: true));
        Assert.Same(DescriptorProbe.ValueProperty, descriptor.DependencyProperty);
        Assert.False(descriptor.IsAttached);
        Assert.Equal(typeof(int), descriptor.PropertyType);

        int changes = 0;
        EventHandler handler = (sender, _) =>
        {
            Assert.Same(element, sender);
            changes++;
        };
        descriptor.AddValueChanged(element, handler);
        descriptor.SetValue(element, 42);
        descriptor.RemoveValueChanged(element, handler);
        descriptor.SetValue(element, 7);
        Assert.Equal(1, changes);
        Assert.Equal(7, descriptor.GetValue(element));
        Assert.True(descriptor.CanResetValue(element));
        descriptor.ResetValue(element);
        Assert.Equal(0, descriptor.GetValue(element));

        CanonicalDesignerProperties.SetIsInDesignMode(element, true);
        Assert.True(CanonicalDesignerProperties.GetIsInDesignMode(element));
    }

    private sealed class CanonicalProbeGroup : CanonicalGroupDescription
    {
        public override object GroupNameFromItem(object item, int level, System.Globalization.CultureInfo culture) =>
            item.ToString()![0].ToString();
    }

    private sealed class PropertySource : INotifyPropertyChanged
    {
        public string? Name { get; set; }
        public string? Other { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class RecordingWeakListener : IWeakEventListener
    {
        public int Deliveries { get; private set; }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            Deliveries++;
            return true;
        }
    }

    private sealed class DescriptorProbe : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(int), typeof(DescriptorProbe), new PropertyMetadata(0));

        public int Value
        {
            get => (int)(GetValue(ValueProperty) ?? 0);
            set => SetValue(ValueProperty, value);
        }
    }
}
