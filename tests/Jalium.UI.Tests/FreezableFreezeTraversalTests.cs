using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

public sealed class FreezableFreezeTraversalTests
{
    [Fact]
    public void GeometrySnapshot_ChecksAndFreezesItsCloneOnce()
    {
        var source = new CountingGeometry();

        var snapshot = Assert.IsType<CountingGeometry>(DrawInputSnapshotter.SnapshotGeometry(source));

        Assert.NotSame(source, snapshot);
        Assert.True(snapshot.IsFrozen);
        Assert.False(source.IsFrozen);
        Assert.Equal(1, snapshot.Checks);
        Assert.Equal(1, snapshot.Commits);
    }

    [Fact]
    public void GeometrySnapshot_SharesAnAlreadyFrozenGeometry()
    {
        var source = new CountingGeometry();
        source.Freeze();

        Assert.Same(source, DrawInputSnapshotter.SnapshotGeometry(source));
        Assert.Equal(1, source.Checks);
        Assert.Equal(1, source.Commits);
    }

    [Fact]
    public void GeometrySnapshot_UnfreezableCloneMarksTheFrameUnrecordable()
    {
        var source = new UnfreezableGeometry();
        var scope = DrawingContext.BeginWholeFrameRecordingScope();
        bool unrecordable = false;
        try
        {
            Assert.Same(source, DrawInputSnapshotter.SnapshotGeometry(source));
            Assert.False(source.IsFrozen);
        }
        finally
        {
            unrecordable = DrawingContext.EndWholeFrameRecordingScope(scope);
        }
        Assert.True(unrecordable);
    }

    [Fact]
    public void Freeze_DependencyPropertyTreeVisitsEachPhaseOnce()
    {
        var nodes = Enumerable.Range(0, 8).Select(_ => new CountingNode()).ToArray();
        for (int i = 0; i < nodes.Length - 1; i++) nodes[i].Child = nodes[i + 1];

        nodes[0].Freeze();

        foreach (var node in nodes)
        {
            Assert.True(node.IsFrozen);
            Assert.Equal(1, node.Checks);
            Assert.Equal(1, node.Commits);
        }
    }

    [Fact]
    public void Freeze_FreezableCollectionDoesNotRecheckItsItemsDuringCommit()
    {
        var item = new CountingNode();
        var collection = new FreezableCollection<CountingNode> { item };

        collection.Freeze();

        Assert.True(item.IsFrozen);
        Assert.Equal(1, item.Checks);
        Assert.Equal(1, item.Commits);
    }

    [Fact]
    public void Freeze_GeometryCollectionDoesNotRecheckItsItemsDuringCommit()
    {
        var item = new CountingGeometry();
        var collection = new GeometryCollection { item };

        collection.Freeze();

        Assert.True(item.IsFrozen);
        Assert.Equal(1, item.Checks);
        Assert.Equal(1, item.Commits);
    }

    [Fact]
    public void Freeze_RejectedDescendantLeavesEntireTreeMutable()
    {
        var leaf = new CountingNode { RejectFreeze = true };
        var child = new CountingNode { Child = leaf };
        var root = new CountingNode { Child = child };

        Assert.Throws<InvalidOperationException>(() => root.Freeze());

        Assert.All(new[] { root, child, leaf }, node =>
        {
            Assert.False(node.IsFrozen);
            Assert.Equal(0, node.Commits);
        });
    }

    private sealed class CountingNode : Freezable
    {
        private static readonly DependencyProperty ChildProperty = DependencyProperty.Register(
            nameof(Child), typeof(CountingNode), typeof(CountingNode), new PropertyMetadata(null));

        public CountingNode? Child
        {
            get => (CountingNode?)GetValue(ChildProperty);
            set => SetValue(ChildProperty, value);
        }

        public int Checks { get; private set; }
        public int Commits { get; private set; }
        public bool RejectFreeze { get; init; }

        protected override Freezable CreateInstanceCore() => new CountingNode();

        protected override bool FreezeCore(bool isChecking)
        {
            if (isChecking) Checks++; else Commits++;
            return !RejectFreeze && base.FreezeCore(isChecking);
        }
    }

    private sealed class CountingGeometry : Geometry
    {
        public override Rect Bounds => Rect.Empty;
        public int Checks { get; private set; }
        public int Commits { get; private set; }
        protected override Freezable CreateInstanceCore() => new CountingGeometry();

        protected override bool FreezeCore(bool isChecking)
        {
            if (isChecking) Checks++; else Commits++;
            return base.FreezeCore(isChecking);
        }
    }

    private sealed class UnfreezableGeometry : Geometry
    {
        public override Rect Bounds => Rect.Empty;
        protected override Freezable CreateInstanceCore() => new UnfreezableGeometry();
        protected override bool FreezeCore(bool isChecking) => false;
    }
}
