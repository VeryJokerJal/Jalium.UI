using Xunit.Abstractions;
using Layer = Jalium.UI.DependencyValueStore.Layer;

namespace Jalium.UI.Tests;

public sealed class DependencyValueStoreMemoryTests(ITestOutputHelper output)
{
    private static readonly DependencyProperty[] Properties = Enumerable.Range(0, 12)
        .Select(index => DependencyProperty.Register(
            "StoreValue" + index,
            typeof(object),
            typeof(StoreOwner),
            new PropertyMetadata(null)))
        .ToArray();

    [Fact]
    public void FirstRegisteredProperty_DoesNotAppearInEmptyOrClearedStore()
    {
        var firstRegistered = DependencyProperty.FromGlobalIndex(1);
        var others = Properties.Where(property => !ReferenceEquals(property, firstRegistered)).ToArray();
        var store = new DependencyValueStore();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Assert.Equal(0, store.Count);
            Assert.False(store.TryGetEffective(firstRegistered, out _, out _));
            Assert.False(store.ContainsLayer(firstRegistered, Layer.Local));
            Assert.False(store.RemoveLayer(firstRegistered, Layer.Local));
            Assert.Empty(store.SnapshotEffectiveProperties());
            store.SetLayer(firstRegistered, Layer.Local, null);
            Assert.Equal(1, store.Count);
            Assert.True(store.TryGetEffective(firstRegistered, out var value, out var source));
            Assert.Null(value);
            Assert.Equal(BaseValueSource.Local, source);
            for (int index = 0; index < others.Length; index++)
                store.SetLayer(others[index], Layer.Local, null);
            for (int index = others.Length - 1; index >= 0; index--)
                Assert.True(store.RemoveLayer(others[index], Layer.Local));
            Assert.Equal(1, store.Count);
            Assert.True(store.RemoveLayer(firstRegistered, Layer.Local));
        }
        Assert.Equal(0, store.Count);
        Assert.False(store.TryGetEffective(firstRegistered, out _, out _));
    }

    [Fact]
    public void SinglePropertyStorage_AllocatesLessThanAnArrayBackedStore()
    {
        var value = new object();
        var warmup = new DependencyValueStore();
        warmup.SetLayer(Properties[0], DependencyValueStore.Layer.Local, value);

        const int count = 1_000;
        var stores = new DependencyValueStore[count];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            var store = new DependencyValueStore();
            store.SetLayer(Properties[0], DependencyValueStore.Layer.Local, value);
            stores[index] = store;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine("Single-property allocation: {0} bytes for {1} stores ({2} bytes/store).",
            allocated, count, allocated / count);

        // Allow alignment differences while keeping the single-property budget
        // below the previous store plus a separate four-entry array.
        Assert.InRange(allocated, 1, count * 64L);
        foreach (var store in stores)
        {
            Assert.True(store.TryGetEffective(Properties[0], out var actual, out var source));
            Assert.Same(value, actual);
            Assert.Equal(BaseValueSource.Local, source);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SmallMultiPropertyStorage_StaysWithinItsAllocationBudget(int propertyCount)
    {
        var value = new object();
        var warmup = new DependencyValueStore();
        for (int property = 0; property < propertyCount; property++)
            warmup.SetLayer(Properties[property], DependencyValueStore.Layer.Local, value);

        const int count = 1_000;
        var stores = new DependencyValueStore[count];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < count; index++)
        {
            var store = new DependencyValueStore();
            for (int property = 0; property < propertyCount; property++)
                store.SetLayer(Properties[property], DependencyValueStore.Layer.Local, value);
            stores[index] = store;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine("{0} properties: {1} bytes/store.", propertyCount, allocated / count);
        // Two properties no longer need an array. Larger stores retain the
        // previous one-inline/overflow allocation budget.
        long perStoreBudget = propertyCount == 2 ? 64 : 136;
        Assert.InRange(allocated, 1, count * perStoreBudget);
        foreach (var store in stores)
        {
            Assert.Equal(propertyCount, store.Count);
            foreach (var property in Properties.Take(propertyCount))
            {
                Assert.True(store.TryGetEffective(property, out var actual, out var source));
                Assert.Same(value, actual);
                Assert.Equal(BaseValueSource.Local, source);
            }
        }
    }

    [Fact]
    public void SingleProperty_MaintainsEveryPrecedenceLayer()
    {
        DependencyValueStore.Layer[] precedence =
        [
            DependencyValueStore.Layer.Local,
            DependencyValueStore.Layer.ParentTemplateTrigger,
            DependencyValueStore.Layer.ParentTemplate,
            DependencyValueStore.Layer.CssState,
            DependencyValueStore.Layer.StyleTrigger,
            DependencyValueStore.Layer.TemplateTrigger,
            DependencyValueStore.Layer.CssBase,
            DependencyValueStore.Layer.StyleSetter,
            DependencyValueStore.Layer.Current,
        ];
        var values = precedence.Select(_ => new object()).ToArray();
        var store = new DependencyValueStore();
        for (int index = precedence.Length - 1; index >= 0; index--)
            store.SetLayer(Properties[0], precedence[index], values[index], BaseValueSource.Inherited);

        for (int index = 0; index < precedence.Length; index++)
        {
            Assert.Equal(1, store.Count);
            Assert.True(store.TryGetEffective(Properties[0], out var effective, out _));
            Assert.Same(values[index], effective);
            Assert.True(store.TryGetEffectiveLayer(Properties[0], out var layer));
            Assert.Equal(precedence[index], layer);
            Assert.True(store.TryGetLayer(Properties[0], precedence[index], out var stored, out var source));
            Assert.Same(values[index], stored);
            if (layer == DependencyValueStore.Layer.Current)
                Assert.Equal(BaseValueSource.Inherited, source);
            Assert.True(store.RemoveLayer(Properties[0], layer));
        }

        Assert.Equal(0, store.Count);
        Assert.False(store.TryGetEffective(Properties[0], out _, out _));
        Assert.False(store.RemoveLayer(Properties[0], DependencyValueStore.Layer.Local));
    }

    [Fact]
    public void GrowingAndRemovingProperties_PreservesValuesLayersAndIndexUpdates()
    {
        var values = Properties.Select(_ => new object()).ToArray();
        var local = new object();
        var store = new DependencyValueStore();
        store.SetLayer(Properties[0], DependencyValueStore.Layer.StyleSetter, values[0]);
        store.SetLayer(Properties[0], DependencyValueStore.Layer.Local, local);

        // Cross both the single-entry transition and the indexed lookup threshold.
        for (int index = 1; index < Properties.Length; index++)
            store.SetLayer(Properties[index], DependencyValueStore.Layer.StyleSetter, values[index]);

        Assert.Equal(Properties.Length, store.Count);
        Assert.True(store.TryGetEffective(Properties[0], out var actual, out _));
        Assert.Same(local, actual);
        Assert.True(store.RemoveLayer(Properties[0], DependencyValueStore.Layer.Local));

        // Removing the first, middle and last entries exercises swap-removal and
        // index removal before the store falls back to linear lookup again.
        int[] removalOrder = [0, 5, 11, 1, 8, 3, 10, 2, 9, 4, 7, 6];
        var live = new HashSet<int>(Enumerable.Range(0, Properties.Length));
        foreach (int removed in removalOrder)
        {
            Assert.True(store.RemoveLayer(Properties[removed], DependencyValueStore.Layer.StyleSetter));
            live.Remove(removed);
            Assert.Equal(live.Count, store.Count);
            Assert.False(store.ContainsLayer(Properties[removed], DependencyValueStore.Layer.StyleSetter));
            foreach (int index in live)
            {
                Assert.True(store.TryGetEffective(Properties[index], out actual, out var source));
                Assert.Same(values[index], actual);
                Assert.Equal(BaseValueSource.Style, source);
            }
        }

        Assert.Empty(store.SnapshotEffectiveProperties());
        Assert.Empty(store.SnapshotLayer(DependencyValueStore.Layer.StyleSetter));
        store.SetLayer(Properties[3], DependencyValueStore.Layer.Local, local);
        var restored = Assert.Single(store.SnapshotLayer(DependencyValueStore.Layer.Local));
        Assert.Same(Properties[3], restored.Key);
        Assert.Same(local, restored.Value);
    }

    [Fact]
    public void LocalNull_RemainsAValueAcrossPromotionAndReuse()
    {
        var store = new DependencyValueStore();
        store.SetLayer(Properties[0], DependencyValueStore.Layer.Local, null);
        store.SetLayer(Properties[1], DependencyValueStore.Layer.Current, "current", BaseValueSource.Inherited);

        Assert.True(store.TryGetEffective(Properties[0], out var value, out var source));
        Assert.Null(value);
        Assert.Equal(BaseValueSource.Local, source);
        var local = Assert.Single(store.SnapshotLayer(DependencyValueStore.Layer.Local));
        Assert.Null(local.Value);
        Assert.Equal(new[] { Properties[0] }, store.SnapshotEffectiveProperties());

        Assert.True(store.RemoveLayer(Properties[0], DependencyValueStore.Layer.Local));
        Assert.True(store.RemoveLayer(Properties[1], DependencyValueStore.Layer.Current));
        store.SetLayer(Properties[1], DependencyValueStore.Layer.Local, null);
        Assert.True(store.ContainsLayer(Properties[1], DependencyValueStore.Layer.Local));
        Assert.False(store.ContainsLayer(Properties[0], DependencyValueStore.Layer.Local));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(12)]
    public void MixedLayerMutationsMatchIndependentPrecedenceModel(int propertyCount)
    {
        var precedence = new (Layer Layer, BaseValueSource Source)[]
        {
            (Layer.Local, BaseValueSource.Local),
            (Layer.ParentTemplateTrigger, BaseValueSource.ParentTemplateTrigger),
            (Layer.ParentTemplate, BaseValueSource.ParentTemplate),
            (Layer.CssState, BaseValueSource.StyleTrigger),
            (Layer.StyleTrigger, BaseValueSource.StyleTrigger),
            (Layer.TemplateTrigger, BaseValueSource.TemplateTrigger),
            (Layer.CssBase, BaseValueSource.Style),
            (Layer.StyleSetter, BaseValueSource.Style),
            (Layer.Current, BaseValueSource.Unknown),
        };
        object?[] payloads = [null, new object(), "value", 42, new Point(1, 2), Array.Empty<object>()];
        BaseValueSource[] currentSources = [BaseValueSource.Inherited, BaseValueSource.Local,
            (BaseValueSource)254, (BaseValueSource)255, (BaseValueSource)777,
            (BaseValueSource)(-1), (BaseValueSource)int.MinValue, (BaseValueSource)int.MaxValue];
        var model = new Dictionary<DependencyProperty, Dictionary<Layer, (object? Value, BaseValueSource Source)>>();
        var store = new DependencyValueStore();
        var random = new Random(3119 + propertyCount);

        void Remove(DependencyProperty property, Layer layer)
        {
            bool expected = model.TryGetValue(property, out var layers) && layers.Remove(layer);
            if (layers?.Count == 0) model.Remove(property);
            Assert.Equal(expected, store.RemoveLayer(property, layer));
        }

        void Verify()
        {
            Assert.Equal(model.Count, store.Count);
            foreach (var property in Properties.Take(propertyCount))
            {
                bool exists = model.TryGetValue(property, out var layers);
                Assert.Equal(exists, store.TryGetEffective(property, out var value, out var source));
                Assert.Equal(exists, store.TryGetEffectiveLayer(property, out var winningLayer));
                if (exists)
                {
                    var winner = precedence.First(pair => layers!.ContainsKey(pair.Layer));
                    Assert.Equal(winner.Layer, winningLayer);
                    Assert.Same(layers![winner.Layer].Value, value);
                    Assert.Equal(layers[winner.Layer].Source, source);
                }
                foreach (var pair in precedence)
                {
                    bool hasLayer = layers?.ContainsKey(pair.Layer) == true;
                    Assert.Equal(hasLayer, store.TryGetLayer(property, pair.Layer, out value, out source));
                    if (hasLayer)
                    {
                        Assert.Same(layers![pair.Layer].Value, value);
                        Assert.Equal(layers[pair.Layer].Source, source);
                    }
                }
                var native = precedence.FirstOrDefault(pair => pair.Layer is not (Layer.CssBase or Layer.CssState)
                    && layers?.ContainsKey(pair.Layer) == true);
                bool hasNative = layers?.ContainsKey(native.Layer) == true;
                Assert.Equal(hasNative, store.TryGetWithoutCss(property, out var nativeValue));
                if (hasNative) Assert.Same(layers![native.Layer].Value, nativeValue);
            }
            var expectedKeys = model.Where(pair => pair.Value.Keys.Any(layer => layer != Layer.Current)
                || pair.Value[Layer.Current].Source != BaseValueSource.Inherited).Select(pair => pair.Key);
            Assert.Equal(expectedKeys.OrderBy(property => property.GlobalIndex),
                store.SnapshotEffectiveProperties().OrderBy(property => property.GlobalIndex));
            foreach (var pair in precedence)
            {
                var snapshot = store.SnapshotLayer(pair.Layer);
                Assert.Equal(model.Count(property => property.Value.ContainsKey(pair.Layer)), snapshot.Length);
                foreach (var entry in snapshot) Assert.Same(model[entry.Key][pair.Layer].Value, entry.Value);
            }
        }

        for (int step = 0; step < 320; step++)
        {
            if (step % 32 == 0)
            {
                foreach (var property in Properties.Take(propertyCount))
                foreach (var pair in precedence)
                {
                    Remove(property, pair.Layer);
                    Verify();
                }
            }
            var selectedProperty = Properties[random.Next(propertyCount)];
            var selected = precedence[random.Next(precedence.Length)];
            if (random.Next(100) < 45) Remove(selectedProperty, selected.Layer);
            else
            {
                object? value = payloads[random.Next(payloads.Length)];
                var source = selected.Layer == Layer.Current
                    ? currentSources[random.Next(currentSources.Length)] : selected.Source;
                store.SetLayer(selectedProperty, selected.Layer, value, source);
                if (!model.TryGetValue(selectedProperty, out var layers)) model[selectedProperty] = layers = new();
                layers[selected.Layer] = (value, source);
            }
            Verify();
        }
    }

    [Theory]
    [InlineData(254)]
    [InlineData(255)]
    [InlineData(777)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void CurrentSource_RetainsFullEnumDomainAcrossLayerPromotionAndReuse(int sourceValue)
    {
        var store = new DependencyValueStore();
        var current = new object();
        var local = new object();
        var source = (BaseValueSource)sourceValue;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            store.SetLayer(Properties[0], Layer.Current, current, source);
            Assert.True(store.TryGetEffective(Properties[0], out var value, out var actualSource));
            Assert.Same(current, value);
            Assert.Equal(source, actualSource);
            Assert.Equal(new[] { Properties[0] }, store.SnapshotEffectiveProperties());
            store.SetLayer(Properties[0], Layer.Local, local);
            Assert.True(store.TryGetLayer(Properties[0], Layer.Current, out value, out actualSource));
            Assert.Same(current, value);
            Assert.Equal(source, actualSource);
            Assert.True(store.RemoveLayer(Properties[0], Layer.Local));
            Assert.True(store.TryGetEffective(Properties[0], out value, out actualSource));
            Assert.Same(current, value);
            Assert.Equal(source, actualSource);

            store.SetLayer(Properties[0], Layer.Current, null, BaseValueSource.Inherited);
            Assert.True(store.TryGetEffective(Properties[0], out value, out actualSource));
            Assert.Null(value);
            Assert.Equal(BaseValueSource.Inherited, actualSource);
            Assert.Empty(store.SnapshotEffectiveProperties());
            store.SetLayer(Properties[0], Layer.Current, current, source);
            Assert.True(store.RemoveLayer(Properties[0], Layer.Current));
            Assert.Equal(0, store.Count);
        }
    }

    [Fact]
    public void PayloadWithInternalStorageType_RemainsAnOrdinaryValue()
    {
        var storageType = typeof(DependencyValueStore).GetNestedType("LayerValues",
            System.Reflection.BindingFlags.NonPublic)!;
        var payload = Activator.CreateInstance(storageType, nonPublic: true)!;
        var store = new DependencyValueStore();
        store.SetLayer(Properties[0], Layer.Local, payload);
        store.SetLayer(Properties[1], Layer.Current, payload, (BaseValueSource)777);
        store.SetLayer(Properties[0], Layer.StyleSetter, new object());
        Assert.True(store.RemoveLayer(Properties[0], Layer.StyleSetter));
        foreach (var property in Properties.Take(2))
        {
            Assert.True(store.TryGetEffective(property, out var value, out _));
            Assert.Same(payload, value);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArrayAndIndexPayloads_SurviveSmallStoragePromotionAndReuse(int arraySlot)
    {
        var entryType = typeof(DependencyValueStore).GetNestedType("Entry",
            System.Reflection.BindingFlags.NonPublic)!;
        object array = Array.CreateInstance(entryType, 4);
        object indices = new Dictionary<int, int> { [Properties[0].GlobalIndex] = 99 };
        var store = new DependencyValueStore();

        void VerifyPayloads()
        {
            Assert.True(store.TryGetEffective(Properties[arraySlot], out var value, out var source));
            Assert.Same(array, value);
            Assert.Equal(BaseValueSource.Local, source);
            Assert.True(store.TryGetEffective(Properties[1 - arraySlot], out value, out source));
            Assert.Same(indices, value);
            Assert.Equal((BaseValueSource)int.MaxValue, source);
        }

        for (int repeat = 0; repeat < 3; repeat++)
        {
            store.SetLayer(Properties[arraySlot], Layer.Local, array);
            store.SetLayer(Properties[1 - arraySlot], Layer.Current, indices, (BaseValueSource)int.MaxValue);
            VerifyPayloads();
            for (int property = 2; property < Properties.Length; property++)
            {
                store.SetLayer(Properties[property], Layer.Local, new object());
                VerifyPayloads();
            }
            for (int property = 2; property < Properties.Length; property++)
            {
                Assert.True(store.RemoveLayer(Properties[property], Layer.Local));
                VerifyPayloads();
            }
            Assert.True(store.RemoveLayer(Properties[arraySlot], Layer.Local));
            Assert.True(store.TryGetEffective(Properties[1 - arraySlot], out var remaining, out _));
            Assert.Same(indices, remaining);
            Assert.True(store.RemoveLayer(Properties[1 - arraySlot], Layer.Current));
            Assert.Equal(0, store.Count);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(12)]
    public void RemovedPayloads_AreCollectibleWhileTheStoreRemainsAlive(int propertyCount)
    {
        var store = new DependencyValueStore();
        var removed = AddAndRemovePayloads(store, propertyCount);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(removed, reference => Assert.False(reference.IsAlive));
        Assert.Equal(0, store.Count);
        GC.KeepAlive(store);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference[] AddAndRemovePayloads(DependencyValueStore store, int propertyCount)
    {
        var references = new WeakReference[propertyCount];
        for (int property = 0; property < propertyCount; property++)
        {
            var value = new object();
            references[property] = new WeakReference(value);
            store.SetLayer(Properties[property], Layer.Local, value);
        }
        for (int property = 0; property < propertyCount; property++)
            Assert.True(store.RemoveLayer(Properties[property], Layer.Local));
        return references;
    }

    [Fact]
    public void ConcurrentRegistrationAndSnapshots_PreservePropertyIdentity()
    {
        string prefix = "ConcurrentStore" + Guid.NewGuid().ToString("N");
        Parallel.For(0, 4096, index =>
        {
            var property = DependencyProperty.Register(prefix + index, typeof(object), typeof(StoreOwner));
            var shared = DependencyProperty.Register(prefix + "Shared" + index % 16, typeof(object), typeof(StoreOwner));
            var store = new DependencyValueStore();
            var value = new object();
            store.SetLayer(property, Layer.Local, value);
            store.SetLayer(shared, Layer.Current, null, BaseValueSource.Local);
            var snapshot = store.SnapshotLayer(Layer.Local);
            Assert.Same(property, Assert.Single(snapshot).Key);
            Assert.Same(value, snapshot[0].Value);
            Assert.Equal(new[] { property, shared }, store.SnapshotEffectiveProperties());
            Assert.Same(property, DependencyProperty.FromGlobalIndex(property.GlobalIndex));
            Assert.Same(shared, DependencyProperty.FromGlobalIndex(shared.GlobalIndex));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateRegistration_DoesNotRetainDiscardedMetadata(bool readOnly)
    {
        string name = "DiscardedMetadata" + Guid.NewGuid().ToString("N");
        if (readOnly) DependencyProperty.RegisterReadOnly(name, typeof(object), typeof(StoreOwner));
        else DependencyProperty.Register(name, typeof(object), typeof(StoreOwner));
        var discarded = CreateDuplicateMetadata(name, readOnly);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(discarded.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateDuplicateMetadata(string name, bool readOnly)
    {
        var value = new object();
        var metadata = new PropertyMetadata(value);
        if (readOnly) DependencyProperty.RegisterReadOnly(name, typeof(object), typeof(StoreOwner), metadata);
        else DependencyProperty.Register(name, typeof(object), typeof(StoreOwner), metadata);
        return new WeakReference(value);
    }

    private sealed class StoreOwner : DependencyObject;
}
