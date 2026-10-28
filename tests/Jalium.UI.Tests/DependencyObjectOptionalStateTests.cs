using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

public sealed class DependencyObjectOptionalStateTests(ITestOutputHelper output)
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "OptionalStateValue", typeof(double), typeof(Probe), new PropertyMetadata(0d));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectsWithoutOptionalFeatures_StayWithinAllocationBudget(bool query)
    {
        var previousEpoch = DependencyObject.BeginBindingReactivationEpoch();
        try
        {
            var warmup = new Probe();
            if (query) QueryAndClearAbsentFeatures(warmup);
            const int count = 2_000;
            var retained = new Probe[count];
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < count; index++)
            {
                var instance = new Probe();
                if (query) QueryAndClearAbsentFeatures(instance);
                retained[index] = instance;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            output.WriteLine("Query={0}: {1} bytes/object.", query, allocated / count);
            Assert.InRange(allocated, 1, count * 80L);
            foreach (var instance in retained)
            {
                Assert.Equal(0d, instance.GetValue(ValueProperty));
                if (query) Assert.True(instance.WasBindingReactivationDoneInCurrentEpoch());
            }
        }
        finally { DependencyObject.EndBindingReactivationEpoch(previousEpoch); }
    }

    private static void QueryAndClearAbsentFeatures(Probe instance)
    {
        _ = instance.GetValue(ValueProperty);
        _ = instance.GetValueSourceInternal(ValueProperty);
        _ = instance.HasLocalOrAnimatedValue(ValueProperty);
        _ = instance.GetBindingExpression(ValueProperty);
        _ = instance.GetBindingExpressionsInternal();
        instance.ClearBinding(ValueProperty);
        instance.ClearAllBindings();
        instance.ClearAnimatedValue(ValueProperty);
        instance.DiscardAllAnimatedValues();
        instance.ClearAllCssAnimatedValues();
        instance.SetBindingMentor(null);
        instance.ReactivateBindings();
        instance.PropertyChangedInternal -= null;
        instance.CssCornerRadiusPresentationChanged -= null;
        instance.CssBorderPresentationChanged -= null;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Events_PreserveDuplicatesOrderRemovalAndReentrantSubscriptions(int kind)
    {
        var owner = new Probe();
        var calls = new List<string>();
        var third = new Listener(() => calls.Add("third"));
        var second = new Listener(() => calls.Add("second"));
        bool update = true;
        var first = new Listener(() =>
        {
            calls.Add("first");
            if (!update) return;
            update = false;
            Subscribe(owner, kind, third);
            Unsubscribe(owner, kind, second);
        });
        Subscribe(owner, kind, first);
        Subscribe(owner, kind, first);
        Subscribe(owner, kind, second);
        Unsubscribe(owner, kind, first);
        Raise(owner, kind, 1);
        Assert.Equal(new[] { "first", "second" }, calls);
        calls.Clear();
        Raise(owner, kind, 2);
        Assert.Equal(new[] { "first", "third" }, calls);
        Unsubscribe(owner, kind, first);
        Unsubscribe(owner, kind, third);
        calls.Clear();
        Raise(owner, kind, 3);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConcurrentEventSubscription_DoesNotLoseHandlers(int kind)
    {
        for (int repeat = 0; repeat < 8; repeat++)
        {
            var owner = new Probe();
            int calls = 0;
            var listeners = Enumerable.Range(0, 64)
                .Select(_ => new Listener(() => Interlocked.Increment(ref calls))).ToArray();
            Parallel.For(0, listeners.Length, index => Subscribe(owner, kind, listeners[index]));
            Raise(owner, kind, 1);
            Assert.Equal(listeners.Length, calls);
            Parallel.For(0, listeners.Length, index => Unsubscribe(owner, kind, listeners[index]));
            Raise(owner, kind, 2);
            Assert.Equal(listeners.Length, calls);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RemovedEventHandler_DoesNotKeepSubscriberAlive(int kind)
    {
        var owner = new Probe();
        var subscriber = AttachAndRemove(owner, kind);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(subscriber.IsAlive);
        GC.KeepAlive(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachAndRemove(Probe owner, int kind)
    {
        var subscriber = new Subscriber();
        var listener = new Listener(subscriber.Signal);
        Subscribe(owner, kind, listener);
        Unsubscribe(owner, kind, listener);
        return new WeakReference(subscriber);
    }

    private static void Subscribe(Probe owner, int kind, Listener listener)
    {
        if (kind == 0) owner.PropertyChangedInternal += listener.PropertyCallback;
        else if (kind == 1) owner.CssCornerRadiusPresentationChanged += listener.Callback;
        else owner.CssBorderPresentationChanged += listener.Callback;
    }

    private static void Unsubscribe(Probe owner, int kind, Listener listener)
    {
        if (kind == 0) owner.PropertyChangedInternal -= listener.PropertyCallback;
        else if (kind == 1) owner.CssCornerRadiusPresentationChanged -= listener.Callback;
        else owner.CssBorderPresentationChanged -= listener.Callback;
    }

    private static void Raise(Probe owner, int kind, int value)
    {
        if (kind == 0) owner.SetValue(ValueProperty, (double)value);
        else if (kind == 1) owner.NotifyCssCornerRadiusPresentationChanged();
        else owner.NotifyCssBorderPresentationChanged();
    }

    private sealed class Listener
    {
        public readonly Action Callback;
        public readonly Action<DependencyProperty, object?, object?> PropertyCallback;
        public Listener(Action callback)
        {
            Callback = callback;
            PropertyCallback = (_, _, _) => callback();
        }
    }

    private sealed class Subscriber { public void Signal() { } }
    private sealed class Probe : DependencyObject;
}
