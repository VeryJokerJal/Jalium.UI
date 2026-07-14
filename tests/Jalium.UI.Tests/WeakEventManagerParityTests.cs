using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Tests;

public sealed class WeakEventManagerParityTests
{
    [Fact]
    public void BaseAndProtectedSurfaceMatchWpfShape()
    {
        Type managerType = typeof(WeakEventManager);
        const BindingFlags protectedInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Equal(typeof(Jalium.UI.Threading.DispatcherObject), managerType.BaseType);
        Assert.Equal(typeof(IDisposable), managerType.GetProperty("ReadLock", protectedInstance)!.PropertyType);
        Assert.Equal(typeof(IDisposable), managerType.GetProperty("WriteLock", protectedInstance)!.PropertyType);

        PropertyInfo indexer = managerType.GetProperty("Item", protectedInstance)!;
        Assert.Equal(typeof(object), indexer.PropertyType);
        Assert.True(indexer.GetMethod!.IsFamily);
        Assert.True(indexer.SetMethod!.IsFamily);

        Assert.True(managerType.GetMethod("DeliverEventToList", protectedInstance)!.IsFamily);
        Assert.True(managerType.GetMethod("Remove", protectedInstance)!.IsFamily);
        Assert.True(managerType.GetMethod("ScheduleCleanup", protectedInstance)!.IsFamily);

        MethodInfo newListenerList = managerType.GetMethod("NewListenerList", protectedInstance)!;
        MethodInfo purge = managerType.GetMethod("Purge", protectedInstance)!;
        Assert.True(newListenerList.IsFamily && newListenerList.IsVirtual);
        Assert.True(purge.IsFamily && purge.IsVirtual);
    }

    [Fact]
    public void ListenerListExposesWpfCollectionAndCopyOnWriteSurface()
    {
        Type managerType = typeof(WeakEventManager);
        Type listenerListType = managerType.GetNestedType("ListenerList", BindingFlags.NonPublic)!;
        Type genericListenerListType = managerType.GetNestedType("ListenerList`1", BindingFlags.NonPublic)!;

        Assert.True(listenerListType.IsNestedFamily);
        Assert.True(genericListenerListType.IsNestedFamily);
        Assert.NotNull(listenerListType.GetConstructor(Type.EmptyTypes));
        Assert.NotNull(listenerListType.GetConstructor(new[] { typeof(int) }));
        Assert.NotNull(listenerListType.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(listenerListType.GetProperty("Count"));
        Assert.NotNull(listenerListType.GetProperty("IsEmpty"));
        Assert.NotNull(listenerListType.GetProperty("Item"));

        foreach (string methodName in new[]
                 {
                     "Add", "AddHandler", "BeginUse", "Clone", "DeliverEvent", "EndUse",
                     "PrepareForWriting", "Purge", "Remove", "RemoveHandler",
                 })
        {
            Assert.Contains(listenerListType.GetMethods(), method => method.Name == methodName);
        }

        MethodInfo copyTo = listenerListType.GetMethod("CopyTo", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(copyTo.IsFamily);

        MethodInfo genericFactory = typeof(WeakEventManager<TestSource, EventArgs>)
            .GetMethod("NewListenerList", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(WeakEventManager<TestSource, EventArgs>), genericFactory.DeclaringType);
    }

    [Fact]
    public async Task ReadAndWriteLocksSerializeTableAccess()
    {
        var manager = new TestManager();
        using IDisposable readLock = manager.AcquireReadLock();
        using var writerStarted = new ManualResetEventSlim();
        using var writerEntered = new ManualResetEventSlim();

        Task writer = Task.Run(() =>
        {
            writerStarted.Set();
            using (manager.AcquireWriteLock())
            {
                writerEntered.Set();
            }
        });

        Assert.True(writerStarted.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(writerEntered.Wait(TimeSpan.FromMilliseconds(50)));

        readLock.Dispose();
        Assert.True(writerEntered.Wait(TimeSpan.FromSeconds(2)));
        await writer.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void IndexerStoresAndRemoveDiscardsManagerData()
    {
        var manager = new TestManager();
        var source = new TestSource();
        var data = new object();

        manager.SetData(source, data);

        Assert.Same(data, manager.GetData(source));
        manager.DropData(source);
        Assert.Null(manager.GetData(source));
    }

    [Fact]
    public void ListenerListUsesCopyOnWriteWhenDeliveryIsInProgress()
    {
        var manager = new TestManager();
        var listener = new CountingListener();

        Assert.True(manager.VerifyCopyOnWrite(listener));
    }

    [Fact]
    public void DeliveryUsesSnapshotWhenListenerMutatesRegistration()
    {
        var manager = new TestManager();
        var source = new TestSource();
        var second = new CountingListener();
        var addedDuringDelivery = new CountingListener();
        bool changed = false;

        var first = new CountingListener(() =>
        {
            if (changed)
            {
                return;
            }

            changed = true;
            manager.RemoveListener(source, second);
            manager.AddListener(source, addedDuringDelivery);
        });

        manager.AddListener(source, first);
        manager.AddListener(source, second);

        source.Raise();

        Assert.Equal(1, first.Count);
        Assert.Equal(1, second.Count);
        Assert.Equal(0, addedDuringDelivery.Count);

        source.Raise();

        Assert.Equal(2, first.Count);
        Assert.Equal(1, second.Count);
        Assert.Equal(1, addedDuringDelivery.Count);
    }

    [Fact]
    public void HandlerDelegateStaysAliveForLifetimeOfTarget()
    {
        var manager = new TestManager();
        var source = new TestSource();
        var target = new HandlerTarget();

        manager.AddHandler(source, target.OnRaised);
        CollectGarbage();
        source.Raise();

        Assert.Equal(1, target.Count);

        manager.RemoveHandler(source, target.OnRaised);
        Assert.Equal(0, source.SubscriptionCount);
        GC.KeepAlive(target);
    }

    [Fact]
    public void GenericManagerUsesTypedListenerListForDelivery()
    {
        var source = new GenericTestSource();
        var target = new HandlerTarget();
        EventHandler<EventArgs> handler = target.OnRaised;

        WeakEventManager<GenericTestSource, EventArgs>.AddHandler(source, nameof(GenericTestSource.Raised), handler);
        source.Raise();

        Assert.Equal(1, target.Count);

        WeakEventManager<GenericTestSource, EventArgs>.RemoveHandler(source, nameof(GenericTestSource.Raised), handler);
        Assert.Equal(0, source.SubscriptionCount);
    }

    [Fact]
    public void ExistingCollectionChangedManagerRetainsGenericHandlerBehavior()
    {
        var source = new ObservableCollection<int>();
        int calls = 0;
        EventHandler<NotifyCollectionChangedEventArgs> handler = (_, _) => calls++;

        CollectionChangedEventManager.AddHandler(source, handler);
        source.Add(1);

        Assert.Equal(1, calls);

        CollectionChangedEventManager.RemoveHandler(source, handler);
        source.Add(2);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void PurgeRemovesCollectedListenerAndStopsListening()
    {
        var manager = new TestManager();
        var source = new TestSource();
        WeakReference listenerReference = AddTemporaryListener(manager, source);

        CollectGarbage(listenerReference);

        Assert.False(listenerReference.IsAlive);
        bool purged = manager.PurgeSource(source);
        Assert.True(purged || manager.GetData(source) is null);
        Assert.Equal(1, manager.StopCount);
        Assert.Equal(0, source.SubscriptionCount);
        Assert.Null(manager.GetData(source));
        GC.KeepAlive(source);
        GC.KeepAlive(manager);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddTemporaryListener(TestManager manager, TestSource source)
    {
        var listener = new CountingListener();
        manager.AddListener(source, listener);
        return new WeakReference(listener);
    }

    private static void CollectGarbage(WeakReference? reference = null)
    {
        for (int i = 0; i < 5 && (reference is null || reference.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class TestManager : WeakEventManager
    {
        public int StopCount { get; private set; }

        public IDisposable AcquireReadLock() => ReadLock;

        public IDisposable AcquireWriteLock() => WriteLock;

        public void AddListener(TestSource source, IWeakEventListener listener) =>
            ProtectedAddListener(source, listener);

        public void RemoveListener(TestSource source, IWeakEventListener listener) =>
            ProtectedRemoveListener(source, listener);

        public void AddHandler(TestSource source, EventHandler handler) =>
            ProtectedAddHandler(source, handler);

        public void RemoveHandler(TestSource source, EventHandler handler) =>
            ProtectedRemoveHandler(source, handler);

        public object? GetData(object source)
        {
            using (ReadLock)
            {
                return this[source];
            }
        }

        public void SetData(object source, object data)
        {
            using (WriteLock)
            {
                this[source] = data;
            }
        }

        public void DropData(object source)
        {
            using (WriteLock)
            {
                Remove(source);
            }
        }

        public bool PurgeSource(object source)
        {
            using (WriteLock)
            {
                object? data = this[source];
                return data is not null && Purge(source, data, purgeAll: false);
            }
        }

        public bool VerifyCopyOnWrite(IWeakEventListener listener)
        {
            ListenerList original = NewListenerList();
            original.Add(listener);
            original.BeginUse();

            try
            {
                ListenerList writable = original;
                bool cloned = ListenerList.PrepareForWriting(ref writable);
                writable.Add(new CountingListener());
                return cloned
                    && !ReferenceEquals(original, writable)
                    && original.Count == 1
                    && writable.Count == 2;
            }
            finally
            {
                original.EndUse();
            }
        }

        protected override void StartListening(object source)
        {
            ((TestSource)source).Raised += OnRaised;
        }

        protected override void StopListening(object source)
        {
            ((TestSource)source).Raised -= OnRaised;
            StopCount++;
        }

        private void OnRaised(object? sender, EventArgs args) => DeliverEvent(sender!, args);
    }

    private sealed class TestSource
    {
        private EventHandler? _raised;

        public int SubscriptionCount { get; private set; }

        public event EventHandler Raised
        {
            add
            {
                _raised += value;
                SubscriptionCount++;
            }
            remove
            {
                _raised -= value;
                SubscriptionCount--;
            }
        }

        public void Raise() => _raised?.Invoke(this, EventArgs.Empty);
    }

    private sealed class GenericTestSource
    {
        private EventHandler<EventArgs>? _raised;

        public int SubscriptionCount { get; private set; }

        public event EventHandler<EventArgs> Raised
        {
            add
            {
                _raised += value;
                SubscriptionCount++;
            }
            remove
            {
                _raised -= value;
                SubscriptionCount--;
            }
        }

        public void Raise() => _raised?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CountingListener : IWeakEventListener
    {
        private readonly Action? _onEvent;

        public CountingListener(Action? onEvent = null)
        {
            _onEvent = onEvent;
        }

        public int Count { get; private set; }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            Count++;
            _onEvent?.Invoke();
            return true;
        }
    }

    private sealed class HandlerTarget
    {
        public int Count { get; private set; }

        public void OnRaised(object? sender, EventArgs args)
        {
            Count++;
        }
    }
}
