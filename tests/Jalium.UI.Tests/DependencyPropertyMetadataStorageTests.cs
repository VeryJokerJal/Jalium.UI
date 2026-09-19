using System.Reflection;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

public sealed class DependencyPropertyMetadataStorageTests(ITestOutputHelper output)
{
    [Fact]
    public void ColdProperties_DoNotAllocatePerPropertyMetadataDictionaries()
    {
        // Isolate the property itself from the global registration dictionary's
        // resize schedule. Reflection setup and the argument array are warmed
        // before measuring, identically for every construction.
        var constructor = typeof(DependencyProperty).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic).Single();
        object?[] arguments = ["MemoryProbe", typeof(object), typeof(Owner), null, false, null];
        _ = constructor.Invoke(arguments);
        const int count = 1024;
        var properties = new DependencyProperty[count];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
            properties[i] = (DependencyProperty)constructor.Invoke(arguments);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        output.WriteLine("Cold metadata allocation: {0} B, {1} B/property.", allocated, allocated / count);
        Assert.InRange(allocated, 1, count * 512L);
        foreach (var property in properties)
        {
            Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(Owner)));
            Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(FirstChild)));
        }
    }

    [Fact]
    public void SparseOverrides_PreserveRegistrationOwnerBarrierAndAddedOwners()
    {
        var property = Register(typeof(FirstChild));
        var ancestorMetadata = new PropertyMetadata("ancestor");
        property.OverrideMetadata(typeof(Owner), ancestorMetadata);

        Assert.Same(ancestorMetadata, property.GetMetadata(typeof(Owner)));
        Assert.Same(ancestorMetadata, property.GetMetadata(typeof(SecondChild)));
        Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(FirstChild)));
        Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(Grandchild)));
        Assert.Throws<ArgumentException>(() =>
            property.OverrideMetadata(typeof(FirstChild), new PropertyMetadata("invalid")));

        property.AddOwner(typeof(UnrelatedOwner));
        Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(UnrelatedOwner)));
        var unrelatedMetadata = new PropertyMetadata("unrelated");
        property.OverrideMetadata(typeof(UnrelatedOwner), unrelatedMetadata);
        Assert.Same(unrelatedMetadata, property.GetMetadata(typeof(UnrelatedOwner)));
    }

    [Fact]
    public void FailedOverride_DiscardsProvisionalDescendantLookupAndAllowsRetry()
    {
        var property = Register(typeof(Owner));
        var provisional = new ProvisionalMetadata();

        Assert.Throws<InvalidOperationException>(() =>
            property.OverrideMetadata(typeof(FirstChild), provisional));

        Assert.True(provisional.SawOwnMetadata);
        Assert.Same(property.DefaultMetadata, property.GetMetadata(typeof(Grandchild)));
        var replacement = new PropertyMetadata("replacement");
        property.OverrideMetadata(typeof(FirstChild), replacement);
        Assert.Same(replacement, property.GetMetadata(typeof(Grandchild)));
    }

    [Fact]
    public void ConcurrentDistinctTypes_NeverMixTheirMetadata()
    {
        var property = Register(typeof(Owner));
        var first = new PropertyMetadata("first");
        var second = new PropertyMetadata("second");
        property.OverrideMetadata(typeof(FirstChild), first);
        property.OverrideMetadata(typeof(SecondChild), second);
        // Prime both answers so the concurrent loop exercises the fast caches.
        Assert.Same(first, property.GetMetadata(typeof(FirstChild)));
        Assert.Same(second, property.GetMetadata(typeof(SecondChild)));

        Parallel.For(0, 8, worker =>
        {
            for (int i = 0; i < 20000; i++)
            {
                bool useFirst = ((i + worker) & 1) == 0;
                Assert.Same(useFirst ? first : second,
                    property.GetMetadata(useFirst ? typeof(FirstChild) : typeof(SecondChild)));
            }
        });
    }

    private static DependencyProperty Register(Type owner) => DependencyProperty.Register(
        "SparseMetadata_" + Guid.NewGuid().ToString("N"), typeof(string), owner, new PropertyMetadata("default"));

    private sealed class ProvisionalMetadata() : PropertyMetadata("provisional")
    {
        public bool SawOwnMetadata { get; private set; }

        protected override void OnApply(DependencyProperty property, Type targetType)
        {
            SawOwnMetadata = ReferenceEquals(this, property.GetMetadata(typeof(Grandchild)));
            throw new InvalidOperationException("Injected metadata application failure.");
        }
    }

    private class Owner : DependencyObject { }
    private class FirstChild : Owner { }
    private sealed class Grandchild : FirstChild { }
    private sealed class SecondChild : Owner { }
    private sealed class UnrelatedOwner : DependencyObject { }
}
