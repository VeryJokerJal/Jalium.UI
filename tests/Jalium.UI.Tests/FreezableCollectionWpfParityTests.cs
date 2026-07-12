using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class FreezableCollectionWpfParityTests
{
    [Fact]
    public void PublicSurface_MatchesWpfCollectionContract()
    {
        Type type = typeof(FreezableCollection<TrackingFreezable>);

        Assert.False(type.IsSealed);
        Assert.True(typeof(IList).IsAssignableFrom(type));
        Assert.True(typeof(IList<TrackingFreezable>).IsAssignableFrom(type));
        Assert.True(typeof(INotifyCollectionChanged).IsAssignableFrom(type));
        Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(type));
        Assert.Null(type.GetEvent("CollectionChanged", BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(type.GetEvent("PropertyChanged", BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(type.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));

        MethodInfo clone = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(Freezable.Clone));
        MethodInfo cloneCurrentValue = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(Freezable.CloneCurrentValue));
        MethodInfo getEnumerator = Assert.Single(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(FreezableCollection<TrackingFreezable>.GetEnumerator));

        Assert.Equal(type, clone.ReturnType);
        Assert.Equal(type, cloneCurrentValue.ReturnType);

        Type? enumeratorType = type.GetNestedType("Enumerator", BindingFlags.Public);
        Assert.NotNull(enumeratorType);
        Assert.True(enumeratorType.IsValueType);
        Assert.True(getEnumerator.ReturnType.IsGenericType);
        Assert.Equal(enumeratorType, getEnumerator.ReturnType.GetGenericTypeDefinition());

        foreach (string methodName in new[]
                 {
                     "CloneCore",
                     "CloneCurrentValueCore",
                     "GetAsFrozenCore",
                     "GetCurrentValueAsFrozenCore",
                     "FreezeCore",
                 })
        {
            MethodInfo? method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(method);
            Assert.True(method.IsFamily);
        }
    }

    [Fact]
    public void CloneOperations_DeepCopyFreezableItemsUsingMatchingOperation()
    {
        var item = new TrackingFreezable { Value = 42 };
        var source = new FreezableCollection<TrackingFreezable> { item };

        FreezableCollection<TrackingFreezable> clone = source.Clone();
        FreezableCollection<TrackingFreezable> currentClone = source.CloneCurrentValue();
        var frozenClone = (FreezableCollection<TrackingFreezable>)source.GetAsFrozen();
        var currentFrozenClone = (FreezableCollection<TrackingFreezable>)source.GetCurrentValueAsFrozen();

        AssertClone(source, clone, "Clone", isFrozen: false);
        AssertClone(source, currentClone, "CloneCurrentValue", isFrozen: false);
        AssertClone(source, frozenClone, "GetAsFrozen", isFrozen: true);
        AssertClone(source, currentFrozenClone, "GetCurrentValueAsFrozen", isFrozen: true);

        clone[0].Value = 7;
        Assert.Equal(42, source[0].Value);
    }

    [Fact]
    public void Freeze_RecursivelyChecksAndFreezesFreezableItems()
    {
        var item = new TrackingFreezable();
        var collection = new FreezableCollection<TrackingFreezable> { item };

        Assert.True(collection.CanFreeze);
        collection.Freeze();

        Assert.True(collection.IsFrozen);
        Assert.True(item.IsFrozen);
        Assert.True(((ICollection<TrackingFreezable>)collection).IsReadOnly);
        Assert.True(((IList)collection).IsReadOnly);
        Assert.True(((IList)collection).IsFixedSize);
        Assert.Throws<InvalidOperationException>(() => collection.Add(new TrackingFreezable()));
        Assert.Throws<InvalidOperationException>(() => collection.Insert(0, new TrackingFreezable()));
        Assert.Throws<InvalidOperationException>(() => collection.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => collection.Clear());
        Assert.Throws<InvalidOperationException>(() => collection[0] = new TrackingFreezable());

        var blockedItem = new NeverFreezable();
        var blockedCollection = new FreezableCollection<NeverFreezable> { blockedItem };

        Assert.False(blockedCollection.CanFreeze);
        Assert.Throws<InvalidOperationException>(() => blockedCollection.Freeze());
        Assert.False(blockedCollection.IsFrozen);
        Assert.False(blockedItem.IsFrozen);
    }

    [Fact]
    public void Mutations_RaiseWpfCollectionAndPropertyNotifications()
    {
        var collection = new FreezableCollection<TrackingFreezable>();
        var propertyNames = new List<string?>();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        int changedCount = 0;
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, args) => changes.Add(args);
        collection.Changed += (_, _) => ++changedCount;

        var first = new TrackingFreezable();
        collection.Add(first);
        Assert.Equal(new[] { "Count", "Item[]" }, propertyNames);
        NotifyCollectionChangedEventArgs add = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, add.Action);
        Assert.Equal(0, add.NewStartingIndex);
        Assert.Same(first, Assert.Single(add.NewItems!.Cast<TrackingFreezable>()));

        propertyNames.Clear();
        changes.Clear();
        var inserted = new TrackingFreezable();
        collection.Insert(0, inserted);
        Assert.Equal(new[] { "Count", "Item[]" }, propertyNames);
        NotifyCollectionChangedEventArgs insert = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, insert.Action);
        Assert.Equal(0, insert.NewStartingIndex);

        propertyNames.Clear();
        changes.Clear();
        var replacement = new TrackingFreezable();
        collection[0] = replacement;
        Assert.Equal(new[] { "Item[]" }, propertyNames);
        NotifyCollectionChangedEventArgs replace = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Replace, replace.Action);
        Assert.Same(inserted, Assert.Single(replace.OldItems!.Cast<TrackingFreezable>()));
        Assert.Same(replacement, Assert.Single(replace.NewItems!.Cast<TrackingFreezable>()));

        propertyNames.Clear();
        changes.Clear();
        Assert.True(collection.Remove(first));
        Assert.Equal(new[] { "Count", "Item[]" }, propertyNames);
        Assert.Equal(NotifyCollectionChangedAction.Remove, Assert.Single(changes).Action);

        propertyNames.Clear();
        changes.Clear();
        collection.Clear();
        Assert.Equal(new[] { "Count", "Item[]" }, propertyNames);
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(changes).Action);
        Assert.Equal(5, changedCount);
    }

    [Fact]
    public void ChildChanges_PropagateFreezableChangedButNotCollectionNotifications()
    {
        var item = new TrackingFreezable();
        var collection = new FreezableCollection<TrackingFreezable> { item };
        int changedCount = 0;
        int propertyChangedCount = 0;
        int collectionChangedCount = 0;
        collection.Changed += (_, _) => ++changedCount;
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, _) => ++propertyChangedCount;
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, _) => ++collectionChangedCount;

        item.Value = 1;

        Assert.Equal(1, changedCount);
        Assert.Equal(0, propertyChangedCount);
        Assert.Equal(0, collectionChangedCount);
    }

    [Fact]
    public void NotificationHandlers_CannotMutateCollectionReentrantly()
    {
        var collection = new FreezableCollection<TrackingFreezable>();
        int notificationCount = 0;
        ((INotifyCollectionChanged)collection).CollectionChanged += (_, _) =>
        {
            ++notificationCount;
            Assert.Throws<InvalidOperationException>(() => collection.Add(new TrackingFreezable()));
        };

        collection.Add(new TrackingFreezable());

        Assert.Equal(1, notificationCount);
        Assert.Single(collection);
    }

    [Fact]
    public void Enumerator_HasWpfCurrentResetAndVersionSemantics()
    {
        var first = new TrackingFreezable();
        var second = new TrackingFreezable();
        var collection = new FreezableCollection<TrackingFreezable> { first, second };

        FreezableCollection<TrackingFreezable>.Enumerator enumerator = collection.GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Same(first, enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Same(second, enumerator.Current);
        Assert.False(enumerator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => _ = enumerator.Current);
        enumerator.Reset();
        Assert.True(enumerator.MoveNext());
        Assert.Same(first, enumerator.Current);

        IEnumerator<TrackingFreezable> genericEnumerator = ((IEnumerable<TrackingFreezable>)collection).GetEnumerator();
        Assert.IsType<FreezableCollection<TrackingFreezable>.Enumerator>(genericEnumerator);

        FreezableCollection<TrackingFreezable>.Enumerator invalidated = collection.GetEnumerator();
        collection[0] = collection[0];
        Assert.Throws<InvalidOperationException>(() => invalidated.MoveNext());
        Assert.Throws<InvalidOperationException>(() => invalidated.Reset());
    }

    [Fact]
    public void NullAndNonGenericGuards_MatchWpfBehavior()
    {
        var collection = new FreezableCollection<TrackingFreezable>();
        Assert.Throws<ArgumentException>(() => collection.Add(null!));
        Assert.Throws<ArgumentException>(() => collection.Insert(0, null!));
        Assert.Throws<ArgumentException>(() =>
            new FreezableCollection<TrackingFreezable>(new TrackingFreezable[] { null! }));

        collection.Add(new TrackingFreezable());
        Assert.Throws<ArgumentException>(() => collection[0] = null!);

        IList list = collection;
        Assert.Throws<ArgumentNullException>(() => list.Add(null));
        Assert.Throws<ArgumentException>(() => list.Add(new object()));
        Assert.Throws<ArgumentException>(() => list.Insert(0, new object()));
        Assert.Throws<ArgumentException>(() => list[0] = new object());
        Assert.False(list.Contains(new object()));
        Assert.Equal(-1, list.IndexOf(new object()));

        int wpfCompatibleResult = list.Add(new TrackingFreezable());
        Assert.Equal(collection.Count, wpfCompatibleResult);
        Assert.Same(collection, ((ICollection)collection).SyncRoot);
        Assert.True(((ICollection)collection).IsSynchronized);
    }

    private static void AssertClone(
        FreezableCollection<TrackingFreezable> source,
        FreezableCollection<TrackingFreezable> clone,
        string expectedOperation,
        bool isFrozen)
    {
        Assert.NotSame(source, clone);
        TrackingFreezable item = Assert.Single(clone);
        Assert.NotSame(source[0], item);
        Assert.Equal(42, item.Value);
        Assert.Equal(expectedOperation, item.CopyOperation);
        Assert.Equal(isFrozen, clone.IsFrozen);
        Assert.Equal(isFrozen, item.IsFrozen);
    }

    private class TrackingFreezable : Freezable
    {
        private int _value;

        public string? CopyOperation { get; private set; }

        public int Value
        {
            get => _value;
            set
            {
                WritePreamble();
                _value = value;
                WritePostscript();
            }
        }

        protected override Freezable CreateInstanceCore() => new TrackingFreezable();

        protected override void CloneCore(Freezable sourceFreezable)
        {
            base.CloneCore(sourceFreezable);
            CopyFrom((TrackingFreezable)sourceFreezable, "Clone");
        }

        protected override void CloneCurrentValueCore(Freezable sourceFreezable)
        {
            base.CloneCurrentValueCore(sourceFreezable);
            CopyFrom((TrackingFreezable)sourceFreezable, "CloneCurrentValue");
        }

        protected override void GetAsFrozenCore(Freezable sourceFreezable)
        {
            base.GetAsFrozenCore(sourceFreezable);
            CopyFrom((TrackingFreezable)sourceFreezable, "GetAsFrozen");
        }

        protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
        {
            base.GetCurrentValueAsFrozenCore(sourceFreezable);
            CopyFrom((TrackingFreezable)sourceFreezable, "GetCurrentValueAsFrozen");
        }

        private void CopyFrom(TrackingFreezable source, string operation)
        {
            _value = source._value;
            CopyOperation = operation;
        }
    }

    private sealed class NeverFreezable : TrackingFreezable
    {
        protected override Freezable CreateInstanceCore() => new NeverFreezable();

        protected override bool FreezeCore(bool isChecking) => false;
    }
}
