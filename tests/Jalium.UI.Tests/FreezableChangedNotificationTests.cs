using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class FreezableChangedNotificationTests
{
    [Fact]
    public void SingleParentAttachAndDetachDoNotAllocatePerRelationship()
    {
        var parent = new Probe();
        var child = new Probe();
        for (int i = 0; i < 100; i++) { parent.Track(null, child); parent.Track(child, null); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) { parent.Track(null, child); parent.Track(child, null); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 1024);
    }

    [Fact]
    public void MixedParentAndPublicNotificationsMatchFieldLikeEvents()
    {
        Probe[] children = [new(), new()];
        Probe[] parents = [new(), new(), new()];
        Emitter[] models = [new(), new()];
        var actual = new List<int>();
        var expected = new List<int>();
        var parentHandlers = new EventHandler[parents.Length];
        for (int i = 0; i < parents.Length; i++)
        {
            int index = i;
            parents[i].Changed += (sender, args) =>
            {
                Assert.Same(parents[index], sender);
                Assert.Same(EventArgs.Empty, args);
                actual.Add(100 + index);
            };
            parentHandlers[i] = (_, _) => expected.Add(100 + index);
        }

        var actualHandlers = new EventHandler[4];
        var expectedHandlers = new EventHandler[4];
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            actualHandlers[i] = (sender, args) =>
            {
                Assert.Contains(sender, children.Cast<object>());
                Assert.Same(EventArgs.Empty, args);
                actual.Add(index);
            };
            expectedHandlers[i] = (_, _) => expected.Add(index);
        }
        actualHandlers[3] = actualHandlers[0] + actualHandlers[1];
        expectedHandlers[3] = expectedHandlers[0] + expectedHandlers[1];

        var random = new Random(8411);
        for (int step = 0; step < 600; step++)
        {
            int childIndex = random.Next(children.Length);
            int parentIndex = random.Next(parents.Length);
            int handlerIndex = random.Next(actualHandlers.Length);
            switch (random.Next(5))
            {
                case 0:
                    parents[parentIndex].Track(null, children[childIndex]);
                    models[childIndex].Changed += parentHandlers[parentIndex];
                    break;
                case 1:
                    parents[parentIndex].Track(children[childIndex], null);
                    models[childIndex].Changed -= parentHandlers[parentIndex];
                    break;
                case 2:
                    children[childIndex].Changed += actualHandlers[handlerIndex];
                    models[childIndex].Changed += expectedHandlers[handlerIndex];
                    break;
                case 3:
                    children[childIndex].Changed -= actualHandlers[handlerIndex];
                    models[childIndex].Changed -= expectedHandlers[handlerIndex];
                    break;
                case 4:
                    parents[parentIndex].Track(children[childIndex], children[1 - childIndex]);
                    models[childIndex].Changed -= parentHandlers[parentIndex];
                    models[1 - childIndex].Changed += parentHandlers[parentIndex];
                    break;
            }
            for (int i = 0; i < children.Length; i++)
            {
                actual.Clear();
                expected.Clear();
                children[i].Signal();
                models[i].Signal();
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void SubscriptionChangesDuringParentCallbackApplyToTheNextRaise()
    {
        var child = new Probe();
        var first = new Probe();
        var next = new Probe();
        var events = new List<string>();
        EventHandler late = (_, _) => events.Add("late");
        first.Changed += (_, _) =>
        {
            events.Add("first");
            first.Track(child, null);
            next.Track(null, child);
            child.Changed += late;
        };
        next.Changed += (_, _) => events.Add("next");
        first.Track(null, child);
        child.Signal();
        Assert.Equal(["first"], events);
        events.Clear();
        child.Signal();
        Assert.Equal(["next", "late"], events);
        child.Changed -= late;
        events.Clear();
        child.Signal();
        Assert.Equal(["next"], events);
    }

    [Fact]
    public void ThrowingPublicListenerStopsLaterParentsAndListeners()
    {
        var child = new Probe();
        var parent = new Probe();
        int parentCalls = 0, laterCalls = 0;
        EventHandler throwing = (_, _) => throw new InvalidOperationException("listener failed");
        child.Changed += throwing;
        parent.Changed += (_, _) => parentCalls++;
        parent.Track(null, child);
        child.Changed += (_, _) => laterCalls++;
        Assert.Throws<InvalidOperationException>(child.Signal);
        Assert.Equal(0, parentCalls);
        Assert.Equal(0, laterCalls);
        child.Changed -= throwing;
        child.Signal();
        Assert.Equal(1, parentCalls);
        Assert.Equal(1, laterCalls);
    }

    [Fact]
    public void ConcurrentPublicSubscriptionsKeepTheParentAndEveryHandler()
    {
        var child = new Probe();
        var parent = new Probe();
        int parentCalls = 0;
        parent.Changed += (_, _) => parentCalls++;
        parent.Track(null, child);
        var calls = new int[128];
        var handlers = Enumerable.Range(0, calls.Length)
            .Select(index => new EventHandler((_, _) => calls[index]++)).ToArray();
        Parallel.For(0, handlers.Length, i => child.Changed += handlers[i]);
        child.Signal();
        Assert.All(calls, count => Assert.Equal(1, count));
        Assert.Equal(1, parentCalls);
        Parallel.For(0, handlers.Length / 2, i => child.Changed -= handlers[i * 2]);
        child.Signal();
        for (int i = 0; i < calls.Length; i++) Assert.Equal(i % 2 == 0 ? 1 : 2, calls[i]);
        Assert.Equal(2, parentCalls);
    }

    [Fact]
    public void ConcurrentParentSubscriptionsAndRemovalDoNotLoseRelationships()
    {
        var child = new Probe();
        var parents = Enumerable.Range(0, 128).Select(_ => new Probe()).ToArray();
        var calls = new int[parents.Length];
        for (int i = 0; i < parents.Length; i++)
        {
            int index = i;
            parents[i].Changed += (_, _) => calls[index]++;
        }
        Parallel.For(0, parents.Length, i => parents[i].Track(null, child));
        child.Signal();
        Assert.All(calls, count => Assert.Equal(1, count));
        Parallel.For(0, parents.Length / 2, i => parents[i * 2].Track(child, null));
        child.Signal();
        for (int i = 0; i < calls.Length; i++) Assert.Equal(i % 2 == 0 ? 1 : 2, calls[i]);
    }

    [Fact]
    public void FrozenChildrenDoNotAcquireNewParentSubscriptions()
    {
        var child = new Probe();
        var parent = new Probe();
        child.Freeze();
        int parentCalls = 0, directCalls = 0;
        parent.Changed += (_, _) => parentCalls++;
        child.Changed += (_, _) => directCalls++;
        parent.Track(null, child);
        child.RaiseUnchecked();
        Assert.Equal(0, parentCalls);
        Assert.Equal(1, directCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClonesRebuildChildSubscriptionsWithoutCopyingSourceListeners(bool currentValue)
    {
        var sourceChild = new Probe();
        var source = new Probe { Child = sourceChild };
        int sourceCalls = 0, cloneCalls = 0;
        source.Changed += (_, _) => sourceCalls++;
        var clone = (Probe)(currentValue ? source.CloneCurrentValue() : source.Clone());
        clone.Changed += (_, _) => cloneCalls++;
        Assert.NotSame(sourceChild, clone.Child);
        clone.Child!.Signal();
        Assert.Equal(0, sourceCalls);
        Assert.Equal(1, cloneCalls);
        sourceChild.Signal();
        Assert.Equal(1, sourceCalls);
        Assert.Equal(1, cloneCalls);
    }

    private sealed class Emitter
    {
        internal event EventHandler? Changed;
        internal void Signal() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Probe : Freezable
    {
        private static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
            nameof(Child), typeof(Probe), typeof(Probe), new PropertyMetadata(null, (owner, args) =>
                ((Probe)owner).OnFreezablePropertyChanged(args.OldValue as DependencyObject, args.NewValue as DependencyObject)));
        internal Probe? Child { get => (Probe?)GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
        internal void Track(DependencyObject? oldValue, DependencyObject? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
        internal void Signal() { WritePreamble(); WritePostscript(); }
        internal void RaiseUnchecked() => OnChanged();
        protected override Freezable CreateInstanceCore() => new Probe();
    }
}
