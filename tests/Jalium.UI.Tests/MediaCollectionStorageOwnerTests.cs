using System.Collections;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MediaCollectionStorageOwnerTests
{
    [Theory]
    [InlineData("Point")]
    [InlineData("Geometry")]
    [InlineData("Figure")]
    [InlineData("Segment")]
    [InlineData("Transform")]
    [InlineData("GeneralTransform")]
    [InlineData("Drawing")]
    [InlineData("Vector")]
    [InlineData("TextEffect")]
    [InlineData("TextDecoration")]
    [InlineData("Model3D")]
    [InlineData("Material")]
    public void CloneFreezeAndThreadGuardsRemainIntact(string kind)
    {
        var (owner, items, value) = Create(kind);
        items.Add(value);
        var clone = owner.Clone();
        Assert.Single((IList)clone);
        Assert.False(clone.IsFrozen);
        items[0] = value;
        Assert.Equal(value, items[0]);
        Assert.Throws<InvalidOperationException>(() => OnOtherThread(() => items.Count));
        Assert.Throws<InvalidOperationException>(() => OnOtherThread(() => { items[0] = value; return 0; }));

        owner.Freeze();
        Assert.Equal(1, OnOtherThread(() => items.Count));
        Assert.True(items.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => items.Add(value));
        Assert.Throws<InvalidOperationException>(() => items.Clear());
        Assert.Throws<InvalidOperationException>(() => items[0] = value);
        ((IList)clone).Clear();
        Assert.Single(items);
    }

    [Fact]
    public void ReplacingItemsRetargetsChildNotificationsAndPreservesCloneIndependence()
    {
        var first = new LineGeometry();
        var second = new LineGeometry();
        var collection = new GeometryCollection { first };
        int changes = 0;
        collection.Changed += (_, _) => changes++;
        collection[0] = second;
        int afterReplacement = changes;
        first.EndPoint = new Point(10, 20);
        Assert.Equal(afterReplacement, changes);
        second.EndPoint = new Point(30, 40);
        Assert.True(changes > afterReplacement);
        var clone = collection.CloneCurrentValue();
        clone[0] = first;
        Assert.Same(second, collection[0]);
        Assert.Same(first, clone[0]);
        collection.Freeze();
        Assert.Throws<InvalidOperationException>(() => collection[0] = first);
    }

    [Fact]
    public void ChildChangesReachOwnerOnlyWhileAttached()
    {
        var owner = new GeometryCollection();
        var child = new LineGeometry();
        int changes = 0;
        owner.Changed += (_, _) => changes++;
        owner.Add(child);
        int afterAdd = changes;
        child.EndPoint = new Point(10, 20);
        Assert.True(changes > afterAdd);
        owner.Remove(child);
        int afterRemove = changes;
        child.EndPoint = new Point(30, 40);
        Assert.Equal(afterRemove, changes);
    }

    private static (Freezable Owner, IList Items, object Item) Create(string kind)
    {
        (Freezable Owner, object Item) result = kind switch
        {
            "Point" => (new PointCollection(), new Point(1, 2)),
            "Geometry" => (new GeometryCollection(), new LineGeometry()),
            "Figure" => (new PathFigureCollection(), new PathFigure()),
            "Segment" => (new PathSegmentCollection(), new LineSegment()),
            "Transform" => (new TransformCollection(), new TranslateTransform()),
            "GeneralTransform" => (new GeneralTransformCollection(), new TranslateTransform()),
            "Vector" => (new VectorCollection(), new Vector(1, 2)),
            "TextEffect" => (new TextEffectCollection(), new TextEffect()),
            "TextDecoration" => (new TextDecorationCollection(), new TextDecoration()),
            "Model3D" => (new Model3DCollection(), new GeometryModel3D()),
            "Material" => (new MaterialCollection(), new DiffuseMaterial()),
            _ => (new DrawingCollection(), new GeometryDrawing()),
        };
        return (result.Owner, (IList)result.Owner, result.Item);
    }

    private static T OnOtherThread<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}
