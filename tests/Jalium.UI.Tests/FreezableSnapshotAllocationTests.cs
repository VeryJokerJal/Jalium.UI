using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class FreezableSnapshotAllocationTests
{
    private static readonly DependencyProperty[] Properties = Enumerable.Range(0, 20)
        .Select(i => DependencyProperty.Register($"SnapshotValue{i}", typeof(int), typeof(Probe),
            new PropertyMetadata(0))).ToArray();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(19)]
    public void PropertySnapshotSurvivesLaterStoreChanges(int count)
    {
        var source = new Probe();
        for (int i = 0; i < count; i++) source.SetValue(Properties[i], i + 1);
        var keys = source.GetEffectiveSetPropertiesInternal();
        for (int i = 0; i < count; i++) source.ClearValue(Properties[i]);
        source.SetValue(Properties[19], 999);
        Assert.Equal(Properties.Take(count), keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(19)]
    public void CloneAndFreezePreserveAllEffectiveValues(int count)
    {
        var source = new Probe();
        for (int i = 0; i < count; i++) source.SetValue(Properties[i], i + 1);
        var clone = source.CloneCurrentValue();
        clone.Freeze();
        for (int i = 0; i < count; i++) source.SetValue(Properties[i], -1);
        for (int i = 0; i < count; i++) Assert.Equal(i + 1, clone.GetValue(Properties[i]));
        Assert.True(clone.IsFrozen);
    }

    [Fact]
    public void ChildNotificationPreservesDuplicateRelationshipsAndRetargeting()
    {
        var parent = new Probe();
        var first = new Probe();
        var second = new Probe();
        int changes = 0;
        parent.Changed += (_, _) => changes++;
        parent.Track(null, first);
        parent.Track(null, first);
        first.Signal();
        Assert.Equal(2, changes);
        parent.Track(first, second);
        first.Signal();
        Assert.Equal(3, changes);
        second.Signal();
        Assert.Equal(4, changes);
        parent.Track(first, null);
        first.Signal();
        Assert.Equal(4, changes);
        parent.Track(second, null);
        second.Signal();
        Assert.Equal(4, changes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(31)]
    [InlineData(80)]
    public void BulkPointClonesRemainIndependentInAllCloneModes(int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new Point(i, i + 0.5)).ToArray();
        var source = new PointCollection(points);
        PointCollection[] clones = [(PointCollection)source.Clone(), (PointCollection)source.CloneCurrentValue(),
            (PointCollection)source.GetAsFrozen(), (PointCollection)source.GetCurrentValueAsFrozen()];
        if (count > 0) source[0] = new Point(-100, -200);
        source.Add(new Point(1000, 1000));
        foreach (var clone in clones) Assert.Equal(points, clone.ToArray());
        Assert.False(clones[0].IsFrozen);
        Assert.False(clones[1].IsFrozen);
        Assert.True(clones[2].IsFrozen);
        Assert.True(clones[3].IsFrozen);
    }

    [Fact]
    public void BulkReferenceClonesKeepDeepCopiesAndChildNotifications()
    {
        var source = new GeometryCollection();
        for (int i = 0; i < 12; i++) source.Add(new LineGeometry(new Point(i, i), new Point(i + 1, i + 1)));
        var clone = (GeometryCollection)source.CloneCurrentValue();
        int changes = 0;
        clone.Changed += (_, _) => changes++;
        for (int i = 0; i < source.Count; i++)
        {
            Assert.NotSame(source[i], clone[i]);
            ((LineGeometry)source[i]).EndPoint = new Point(99, 99);
            Assert.Equal(new Point(i + 1, i + 1), ((LineGeometry)clone[i]).EndPoint);
            ((LineGeometry)clone[i]).EndPoint = new Point(80, 80);
        }
        Assert.Equal(12, changes);
    }

    private sealed class Probe : Freezable
    {
        public void Track(DependencyObject? oldValue, DependencyObject? newValue) =>
            OnFreezablePropertyChanged(oldValue, newValue);
        public void Signal() { WritePreamble(); WritePostscript(); }
        protected override Freezable CreateInstanceCore() => new Probe();
    }
}
