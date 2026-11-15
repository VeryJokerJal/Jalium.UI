using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DependencyObjectSingleLocalValueTests
{
    private static readonly object First = new();
    private static readonly object Second = new();
    private static readonly object Third = new();

    [Fact]
    public void ExplicitNull_RemainsLocalUntilCleared()
    {
        var owner = new Owner();
        owner.SetValue(Owner.ValueProperty, null);
        Assert.True(owner.HasLocalValue(Owner.ValueProperty));
        Assert.Null(owner.ReadLocalValue(Owner.ValueProperty));
        Assert.True(owner.TryGetValueWithoutCss(Owner.ValueProperty, out var value));
        Assert.Null(value);
        Assert.True(owner.Serializes(Owner.ValueProperty));
        Assert.Single(owner.GetLocalValueEntriesInternal());
        owner.ClearValue(Owner.ExtraProperty);
        Assert.True(owner.HasLocalValue(Owner.ValueProperty));
        owner.ClearValue(Owner.ValueProperty);
        Assert.False(owner.HasLocalValue(Owner.ValueProperty));
        Assert.Same(DependencyProperty.UnsetValue, owner.ReadLocalValue(Owner.ValueProperty));
        Assert.Empty(owner.GetLocalValueEntriesInternal());
    }

    [Fact]
    public void PublicPayloadMayItselfBeAValueStore()
    {
        var payload = new DependencyValueStore();
        var owner = new Owner();
        owner.SetValue(Owner.ValueProperty, payload);
        Assert.Same(payload, owner.GetValue(Owner.ValueProperty));
        Assert.Same(payload, owner.GetLocalValueEntriesInternal()[0].Value);
        owner.SetValue(Owner.ExtraProperty, Second);
        Assert.Same(payload, owner.GetValue(Owner.ValueProperty));
        owner.ClearValue(Owner.ExtraProperty);
        Assert.Same(payload, owner.GetValue(Owner.ValueProperty));
        owner.ClearValue(Owner.ValueProperty);
        Assert.False(owner.HasLocalValue(Owner.ValueProperty));
    }

    [Fact]
    public void SnapshotsRemainStableWhenOtherPropertiesArrive()
    {
        var owner = new Owner();
        owner.SetValue(Owner.ValueProperty, First);
        var locals = owner.GetLocalValueEnumerator();
        var entries = owner.GetLocalValueEntriesInternal();
        var keys = owner.GetEffectiveSetPropertiesInternal();
        owner.SetValue(Owner.ExtraProperty, Second);
        owner.SetValue(Owner.ValueProperty, Third);
        Assert.Equal(1, locals.Count);
        Assert.True(locals.MoveNext());
        Assert.Same(Owner.ValueProperty, locals.Current.Property);
        Assert.Same(First, locals.Current.Value);
        Assert.False(locals.MoveNext());
        Assert.Single(entries);
        Assert.Same(First, entries[0].Value);
        Assert.Equal([Owner.ValueProperty], keys.ToArray());
        Assert.Same(Third, owner.GetValue(Owner.ValueProperty));
    }

    public static IEnumerable<object[]> LayerCases()
    {
        foreach (var layer in Enum.GetValues<DependencyObject.LayerValueSource>())
        foreach (bool nullLocal in new[] { false, true })
            yield return [(int)layer, nullLocal];
    }

    [Theory]
    [MemberData(nameof(LayerCases))]
    public void LayerTransitionsMatchAnEquivalentOwnerWithEarlierValues(int layer, bool nullLocal)
    {
        var source = (DependencyObject.LayerValueSource)layer;
        var fresh = new Owner();
        var history = new Owner();
        object? initial = nullLocal ? null : First;
        fresh.SetValue(Owner.ValueProperty, initial);
        history.SetValue(Owner.ExtraProperty, Second);
        history.SetValue(Owner.ValueProperty, initial);
        history.ClearValue(Owner.ExtraProperty);
        Check();
        fresh.SetCurrentValue(Owner.ValueProperty, Third);
        history.SetCurrentValue(Owner.ValueProperty, Third);
        Check();
        fresh.SetLayerValue(Owner.ValueProperty, Second, source);
        history.SetLayerValue(Owner.ValueProperty, Second, source);
        Check();
        fresh.ClearValue(Owner.ValueProperty);
        history.ClearValue(Owner.ValueProperty);
        Check();
        fresh.SetCurrentValue(Owner.ValueProperty, First);
        history.SetCurrentValue(Owner.ValueProperty, First);
        Check();
        fresh.ClearLayerValue(Owner.ValueProperty, source);
        history.ClearLayerValue(Owner.ValueProperty, source);
        Check();

        void Check()
        {
            Assert.Same(history.GetValue(Owner.ValueProperty), fresh.GetValue(Owner.ValueProperty));
            Assert.Equal(history.HasLocalValue(Owner.ValueProperty), fresh.HasLocalValue(Owner.ValueProperty));
            Assert.Same(history.ReadLocalValue(Owner.ValueProperty), fresh.ReadLocalValue(Owner.ValueProperty));
            Assert.Equal(history.GetValueSourceInternal(Owner.ValueProperty), fresh.GetValueSourceInternal(Owner.ValueProperty));
            Assert.Equal(history.GetLocalValueEntriesInternal(), fresh.GetLocalValueEntriesInternal());
            bool expected = history.TryGetValueWithoutCss(Owner.ValueProperty, out var expectedValue);
            Assert.Equal(expected, fresh.TryGetValueWithoutCss(Owner.ValueProperty, out var actualValue));
            Assert.Same(expectedValue, actualValue);
        }
    }

    [Theory]
    [MemberData(nameof(LayerCases))]
    public void PromotingLocalValuesPreservesSourceAndExplicitNull(int layer, bool nullLocal)
    {
        var owner = new Owner();
        object? value = nullLocal ? null : First;
        owner.SetValue(Owner.ValueProperty, value);
        owner.PromoteLocalValuesToLayer((DependencyObject.LayerValueSource)layer);
        Assert.False(owner.HasLocalValue(Owner.ValueProperty));
        Assert.True(owner.TryGetLayerValue(Owner.ValueProperty, (DependencyObject.LayerValueSource)layer, out var stored));
        Assert.Same(value, stored);
        Assert.Same(value, owner.GetValue(Owner.ValueProperty));
    }

    [Fact]
    public void ReentrantNotificationsCanAddClearAndReplaceValues()
    {
        var owner = new CallbackOwner();
        bool entered = false;
        owner.Callback = args =>
        {
            if (entered || args.Property != Owner.ValueProperty) return;
            entered = true;
            owner.SetValue(Owner.ExtraProperty, Second);
            owner.ClearValue(Owner.ValueProperty);
            owner.SetValue(Owner.ValueProperty, Third);
        };
        owner.SetValue(Owner.ValueProperty, First);
        Assert.Same(Third, owner.GetValue(Owner.ValueProperty));
        Assert.Same(Second, owner.GetValue(Owner.ExtraProperty));
    }

    [Fact]
    public void ReadOnlyKeysKeepTheirWriteAndClearGuards()
    {
        var owner = new Owner();
        owner.SetValue(Owner.ReadOnlyKey, First);
        Assert.Same(First, owner.GetValue(Owner.ReadOnlyKey.DependencyProperty));
        Assert.Throws<InvalidOperationException>(() => owner.SetValue(Owner.ReadOnlyKey.DependencyProperty, Second));
        owner.ClearValue(Owner.ReadOnlyKey);
        Assert.False(owner.HasLocalValue(Owner.ReadOnlyKey.DependencyProperty));
    }

    [Fact]
    public void SingleValueFreezableGraphsCloneFreezeAndReadAcrossThreads()
    {
        var child = new FrozenOwner();
        child.SetValue(FrozenOwner.ValueProperty, First);
        var parent = new FrozenOwner();
        parent.SetValue(FrozenOwner.ValueProperty, child);
        var clone = (FrozenOwner)parent.Clone();
        var clonedChild = Assert.IsType<FrozenOwner>(clone.GetValue(FrozenOwner.ValueProperty));
        Assert.NotSame(child, clonedChild);
        Assert.Same(First, clonedChild.GetValue(FrozenOwner.ValueProperty));
        parent.Freeze();
        Assert.True(child.IsFrozen);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Same(child, parent.GetValue(FrozenOwner.ValueProperty));
                Assert.Same(First, child.GetValue(FrozenOwner.ValueProperty));
            }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(10000));
        Assert.Null(failure);
        Assert.Throws<InvalidOperationException>(() => parent.SetValue(FrozenOwner.ValueProperty, Second));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingTheLastLocalValueReleasesItsPayload(bool earlierExtraValue)
    {
        var owner = new Owner();
        var weak = AttachPayload(owner, earlierExtraValue);
        owner.ClearValue(Owner.ValueProperty);
        for (int retry = 0; retry < 3 && weak.IsAlive; retry++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(weak.IsAlive);
        GC.KeepAlive(owner);
    }

    [Fact]
    public void EmptyAndTwoValueOwnerAllocationBudgetsStayBounded()
    {
        if (IntPtr.Size != 8) return;
        Assert.InRange(AllocateOwners(0), 0, 2000 * 72L + 1024);
        Assert.InRange(AllocateOwners(2), 0, 2000 * 128L + 1024);
    }

    private static long AllocateOwners(int count)
    {
        for (int i = 0; i < 2000; i++) CreateOwner(count);
        var owners = new Owner[2000];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < owners.Length; i++) owners[i] = CreateOwner(count);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(owners);
        return allocated;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Owner CreateOwner(int count)
    {
        var owner = new Owner();
        if (count > 0) owner.SetValue(Owner.ValueProperty, First);
        if (count > 1) owner.SetValue(Owner.ExtraProperty, Second);
        return owner;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachPayload(Owner owner, bool earlierExtraValue)
    {
        object value = new();
        owner.SetValue(Owner.ValueProperty, value);
        if (earlierExtraValue)
        {
            owner.SetValue(Owner.ExtraProperty, Second);
            owner.ClearValue(Owner.ExtraProperty);
        }
        return new WeakReference(value);
    }

    private class Owner : DependencyObject
    {
        internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(object), typeof(Owner), new PropertyMetadata(null));
        internal static readonly DependencyProperty ExtraProperty = DependencyProperty.Register("Extra", typeof(object), typeof(Owner), new PropertyMetadata(null));
        internal static readonly DependencyPropertyKey ReadOnlyKey = DependencyProperty.RegisterReadOnly("ReadOnlyValue", typeof(object), typeof(Owner), new PropertyMetadata(null));
        internal bool Serializes(DependencyProperty property) => ShouldSerializeProperty(property);
    }

    private sealed class CallbackOwner : Owner
    {
        internal Action<DependencyPropertyChangedEventArgs>? Callback;
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
        {
            base.OnPropertyChanged(args);
            Callback?.Invoke(args);
        }
    }

    private sealed class FrozenOwner : Freezable
    {
        internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(object), typeof(FrozenOwner), new PropertyMetadata(null));
        protected override Freezable CreateInstanceCore() => new FrozenOwner();
    }
}
