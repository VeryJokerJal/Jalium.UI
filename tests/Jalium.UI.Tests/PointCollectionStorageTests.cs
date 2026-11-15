using System.Collections;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class PointCollectionStorageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(32)]
    public void MutationsMatchListAcrossGrowthClearAndReuse(int initialCount)
    {
        var expected = Enumerable.Range(0, initialCount).Select(i => new Point(i, -i)).ToList();
        var actual = new PointCollection(expected);
        Point[] values = [new(1, 2), new(-0.0, 0), new(double.NaN, 3), new(3, double.NaN), new(double.PositiveInfinity, -4)];
        int changes = 0, expectedChanges = 0;
        actual.Changed += (_, _) => changes++;
        var random = new Random(1771 + initialCount);
        for (int step = 0; step < 1200; step++)
        {
            Point value = values[random.Next(values.Length)];
            int index = expected.Count == 0 ? 0 : random.Next(expected.Count);
            switch (random.Next(8))
            {
                case 0: expected.Add(value); actual.Add(value); expectedChanges++; break;
                case 1: expected.Insert(index, value); actual.Insert(index, value); expectedChanges++; break;
                case 2 when expected.Count > 0: expected[index] = value; actual[index] = value; expectedChanges++; break;
                case 3 when expected.Count > 0: expected.RemoveAt(index); actual.RemoveAt(index); expectedChanges++; break;
                case 4:
                    bool removed = expected.Remove(value);
                    Assert.Equal(removed, actual.Remove(value));
                    if (removed) expectedChanges++;
                    break;
                case 5:
                    expected.Clear(); actual.Clear(); expectedChanges++;
                    break;
                case 6:
                    Point[] copy = new Point[actual.Count + 4];
                    actual.CopyTo(copy, 2);
                    EqualContents(expected, copy.Skip(2).Take(actual.Count));
                    break;
                default:
                    Assert.Equal(expected.Contains(value), actual.Contains(value));
                    Assert.Equal(expected.IndexOf(value), actual.IndexOf(value));
                    break;
            }
            EqualContents(expected, actual);
            Assert.Equal(expectedChanges, changes);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(16)]
    public void MutationInvalidatesEnumeratorsIncludingPromotionAndEmptyClear(int count)
    {
        var points = new PointCollection(Enumerable.Range(0, count).Select(i => new Point(i, i)).ToArray());
        var first = points.GetEnumerator();
        var second = first;
        _ = first.MoveNext();
        points.Add(new Point(100, 200));
        Assert.Throws<InvalidOperationException>(() => first.MoveNext());
        Assert.Throws<InvalidOperationException>(() => second.Reset());
        points.Clear();
        var empty = points.GetEnumerator();
        Assert.False(empty.MoveNext());
        points.Clear();
        Assert.Throws<InvalidOperationException>(() => empty.MoveNext());
    }

    [Fact]
    public void EnumeratorCopiesAndResetPreserveExistingCursorSemantics()
    {
        var points = new PointCollection([new Point(10, 20), new Point(30, 40)]);
        var first = points.GetEnumerator();
        Assert.Equal(default, first.Current);
        Assert.True(first.MoveNext());
        var second = first;
        first.Reset();
        Assert.True(first.MoveNext());
        Assert.Equal(new Point(30, 40), first.Current);
        Assert.True(second.MoveNext());
        Assert.Equal(first.Current, second.Current);
        Assert.False(first.MoveNext());
        Assert.Equal(default, first.Current);
        Assert.Equal(default(Point), ((IEnumerator)first).Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(32)]
    public void ConstructorsCloneModesAndFrozenReadsKeepIndependentValues(int count)
    {
        var data = Enumerable.Range(0, count).Select(i => new Point(i, i + .25)).ToArray();
        var expected = data.ToArray();
        var source = new PointCollection(data);
        var copies = new[] { new PointCollection(source), new PointCollection(new EnumerationOnlyCollection(data)),
            source.Clone(), source.CloneCurrentValue(), (PointCollection)source.GetAsFrozen(), (PointCollection)source.GetCurrentValueAsFrozen() };
        if (count != 0) { data[0] = new Point(-3, -4); source[0] = new Point(-5, -6); }
        source.Add(new Point(-7, -8));
        foreach (var copy in copies) EqualContents(expected, copy);
        copies[0].Add(new Point(99, 100));
        foreach (var copy in copies.Skip(1)) EqualContents(expected, copy);
        var frozen = copies[^1];
        EqualContents(expected, OnOtherThread(() => frozen.ToArray()));
        Assert.Throws<InvalidOperationException>(() => OnOtherThread(() => source.Count));
        Assert.Throws<InvalidOperationException>(() => frozen.Add(default));
    }

    [Fact]
    public void SmallClonesReadTheRemainingValuesAfterLargeStorageIsReused()
    {
        var points = new PointCollection(Enumerable.Range(0, 32).Select(i => new Point(i, i)).ToArray());
        points.Clear();
        Point[] expected = [new(17, 23), new(31, 47)];
        foreach (var point in expected) points.Add(point);
        PointCollection[] copies = [new(points), points.Clone(), points.CloneCurrentValue(),
            (PointCollection)points.GetAsFrozen(), (PointCollection)points.GetCurrentValueAsFrozen()];
        points[0] = new Point(-1, -2);
        foreach (var copy in copies) EqualContents(expected, copy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(12)]
    public void CopyToMatchesListForValidAndInvalidArrays(int count)
    {
        var values = Enumerable.Range(0, count).Select(i => new Point(i, -i)).ToList();
        var points = new PointCollection(values);
        foreach (int index in new[] { -1, 0, 1, 4, 5, int.MaxValue })
        {
            foreach (int length in new[] { 0, 2, 4, 20 })
            {
                var expected = new Point[length]; var actual = new Point[length];
                SameFailure(() => values.CopyTo(expected, index), () => points.CopyTo(actual, index));
                EqualContents(expected, actual);
            }
            SameFailure(() => values.CopyTo(null!, index), () => points.CopyTo(null!, index));
            foreach (Func<Array?> factory in new Func<Array?>[] {
                () => null, () => new Point[20], () => new object[20], () => new string[20], () => new int[20],
                () => new Point[2, 2], () => Array.CreateInstance(typeof(Point), [20], [1]) })
            {
                Array? expected = factory(), actual = factory();
                SameFailure(() => {
                    // The existing media storage validates null before delegating
                    // to List<T>, and exposes "array" as its parameter name.
                    if (expected is null) throw new ArgumentNullException("array");
                    ((ICollection)values).CopyTo(expected, index);
                }, () => ((ICollection)points).CopyTo(actual!, index));
                if (expected is not null) Assert.Equal(expected.Cast<object?>(), actual!.Cast<object?>());
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(32)]
    public void ConstructionKeepsTheExistingAllocationBudget(int count)
    {
        Point[] data = Enumerable.Range(0, count).Select(i => new Point(i, i)).ToArray();
        foreach (IEnumerable<Point> source in new IEnumerable<Point>[] { data, data.ToList() })
        {
            object CreateCandidate() => new PointCollection(source);
            object CreateReference() => new ReferencePointList(source);
            for (int i = 0; i < 1000; i++) { GC.KeepAlive(CreateCandidate()); GC.KeepAlive(CreateReference()); }
            long Measure(Func<object> create)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2048; i++) GC.KeepAlive(create());
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long actual = Measure(CreateCandidate), reference = Measure(CreateReference);
            Assert.True(actual <= reference, $"Point count {count}: {actual} candidate bytes exceed {reference} reference bytes.");
        }
    }

    [Fact]
    public void ListSubclassesRetainTheirExplicitEnumerationContract()
    {
        var source = new CustomList { new Point(1, 2), new Point(3, 4) };
        var actual = new PointCollection(source);
        Assert.Equal(1, source.Enumerations);
        EqualContents(source.ToArray(), actual);
    }

    private static void EqualContents(IEnumerable<Point> expected, IEnumerable<Point> actual) =>
        Assert.Equal(expected.Select(p => (p.X, p.Y)), actual.Select(p => (p.X, p.Y)));

    private static void SameFailure(Action expected, Action actual)
    {
        Exception? first = Record.Exception(expected), second = Record.Exception(actual);
        Assert.Equal(first?.GetType(), second?.GetType());
        Assert.Equal((first as ArgumentException)?.ParamName, (second as ArgumentException)?.ParamName);
    }

    private static T OnOtherThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { failure = ex; } });
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private sealed class ReferencePointList : Freezable
    {
        private readonly AnimatableListStorage<Point> _items;
        public ReferencePointList(IEnumerable<Point> points)
        {
            _items = new(this, points is ICollection<Point> collection ? collection.Count : 0);
            _items.AddRange(points);
        }
        protected override Freezable CreateInstanceCore() => new ReferencePointList(Array.Empty<Point>());
    }

    private sealed class CustomList : List<Point>, IEnumerable<Point>
    {
        public int Enumerations;
        IEnumerator<Point> IEnumerable<Point>.GetEnumerator() { Enumerations++; return base.GetEnumerator(); }
    }

    private sealed class EnumerationOnlyCollection(Point[] points) : ICollection<Point>
    {
        public int Count => points.Length;
        public bool IsReadOnly => true;
        public IEnumerator<Point> GetEnumerator() => ((IEnumerable<Point>)points).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void CopyTo(Point[] array, int index) => throw new NotSupportedException("Construction must enumerate unknown ICollection implementations.");
        public bool Contains(Point item) => points.Contains(item);
        public void Add(Point item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Remove(Point item) => throw new NotSupportedException();
    }
}
