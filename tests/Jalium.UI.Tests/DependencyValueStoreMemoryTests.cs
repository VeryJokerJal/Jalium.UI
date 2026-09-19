using Xunit.Abstractions;

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
        Assert.InRange(allocated, 1, count * 128L);
        foreach (var store in stores)
        {
            Assert.True(store.TryGetEffective(Properties[0], out var actual, out var source));
            Assert.Same(value, actual);
            Assert.Equal(BaseValueSource.Local, source);
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

    private sealed class StoreOwner : DependencyObject;
}
