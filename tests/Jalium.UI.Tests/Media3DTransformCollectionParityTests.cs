using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class Media3DTransformCollectionParityTests
{
    [Fact]
    public void TransformTypesExposeWpfFreezableAndDependencyPropertyContracts()
    {
        Assert.True(typeof(Animatable).IsAssignableFrom(typeof(Transform3D)));
        Assert.True(typeof(Transform3D).IsAssignableFrom(typeof(AffineTransform3D)));
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(GeneralTransform3DTo2D)));
        Assert.True(typeof(Freezable).IsAssignableFrom(typeof(GeneralTransform2DTo3D)));

        Assert.Same(TranslateTransform3D.OffsetXProperty, new TranslateTransform3D().GetType()
            .GetField(nameof(TranslateTransform3D.OffsetXProperty))!.GetValue(null));
        Assert.Same(ScaleTransform3D.ScaleZProperty, new ScaleTransform3D().GetType()
            .GetField(nameof(ScaleTransform3D.ScaleZProperty))!.GetValue(null));
        Assert.Same(RotateTransform3D.RotationProperty, typeof(RotateTransform3D)
            .GetField(nameof(RotateTransform3D.RotationProperty))!.GetValue(null));
        Assert.Same(MatrixTransform3D.MatrixProperty, typeof(MatrixTransform3D)
            .GetField(nameof(MatrixTransform3D.MatrixProperty))!.GetValue(null));
        Assert.True(Transform3D.Identity.IsFrozen);
        Assert.True(Rotation3D.Identity.IsFrozen);
    }

    [Fact]
    public void AffineTransformsPerformFunctionalRowVectorMatrixMath()
    {
        var translation = new TranslateTransform3D(new Vector3D(2, 3, 4));
        Assert.Equal(new Point3D(3, 4, 5), translation.Transform(new Point3D(1, 1, 1)));

        var scale = new ScaleTransform3D(new Vector3D(2, 3, 4), new Point3D(10, 20, 30));
        Assert.Equal(new Point3D(10, 20, 30), scale.Transform(new Point3D(10, 20, 30)));
        Assert.Equal(new Point3D(12, 23, 34), scale.Transform(new Point3D(11, 21, 31)));

        var rotation = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(0, 0, 1), 90));
        Point3D rotated = rotation.Transform(new Point3D(1, 0, 0));
        Assert.Equal(0, rotated.X, 12);
        Assert.Equal(1, rotated.Y, 12);

        Assert.True(translation.IsAffine);
        Assert.False(new MatrixTransform3D(new Matrix3D(
            1, 0, 0, 1,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1)).IsAffine);
    }

    [Fact]
    public void TransformOverloadsBoundsAndInverseRoundTrip()
    {
        Transform3D transform = new TranslateTransform3D(5, 6, 7);
        var points = new[] { new Point3D(0, 0, 0), new Point3D(1, 2, 3) };
        var vectors = new[] { new Vector3D(1, 2, 3) };
        var homogeneous = new[] { new Point4D(1, 2, 3, 2) };

        transform.Transform(points);
        transform.Transform(vectors);
        transform.Transform(homogeneous);
        Assert.Equal(new Point3D(5, 6, 7), points[0]);
        Assert.Equal(new Point3D(6, 8, 10), points[1]);
        Assert.Equal(new Vector3D(1, 2, 3), vectors[0]);
        Assert.Equal(new Point4D(11, 14, 17, 2), homogeneous[0]);

        Rect3D bounds = transform.TransformBounds(new Rect3D(1, 2, 3, 4, 5, 6));
        Assert.Equal(new Rect3D(6, 8, 10, 4, 5, 6), bounds);
        Assert.Equal(new Point3D(1, 2, 3), transform.Inverse!.Transform(points[1]));
    }

    [Fact]
    public void TransformGroupsComposeCloneFreezeAndInvertChildren()
    {
        var group = new Transform3DGroup();
        group.Children.Add(new TranslateTransform3D(1, 0, 0));
        group.Children.Add(new ScaleTransform3D(2, 2, 2));
        Assert.Equal(new Point3D(4, 0, 0), group.Transform(new Point3D(1, 0, 0)));

        Transform3DGroup clone = group.Clone();
        Assert.NotSame(group.Children, clone.Children);
        Assert.NotSame(group.Children[0], clone.Children[0]);
        Assert.Equal(group.Value, clone.Value);

        group.Freeze();
        Assert.True(group.Children.IsFrozen);
        Assert.True(group.Children[0].IsFrozen);
        Assert.Throws<InvalidOperationException>(() => group.Children.Add(new MatrixTransform3D()));

        var general = new GeneralTransform3DGroup();
        general.Children.Add(new TranslateTransform3D(1, 0, 0));
        general.Children.Add(new ScaleTransform3D(2, 2, 2));
        Point3D transformed = general.Transform(new Point3D(1, 0, 0));
        Assert.Equal(new Point3D(4, 0, 0), transformed);
        Assert.Equal(new Point3D(1, 0, 0), general.Inverse!.Transform(transformed));
    }

    [Fact]
    public void ValueCollectionsParseFormatCloneFreezeAndInvalidateEnumerators()
    {
        Point3DCollection points = Point3DCollection.Parse("1,2,3 4,5,6");
        Vector3DCollection vectors = Vector3DCollection.Parse("7 8 9");
        Int32Collection indices = Int32Collection.Parse("0, 1 2");

        Assert.Equal("1,2,3 4,5,6", points.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("7,8,9", vectors.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("0 1 2", indices.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(points.ToArray(), points.Clone().ToArray());

        Point3DCollection.Enumerator enumerator = points.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        points.Add(new Point3D(10, 11, 12));
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());

        indices.Freeze();
        Assert.Throws<InvalidOperationException>(() => indices.Add(3));
        Assert.True(((IList)indices).IsReadOnly);
    }

    [Fact]
    public void CollectionConvertersAndValueSerializersRoundTripStrings()
    {
        var pointConverter = new Point3DCollectionConverter();
        Assert.True(pointConverter.CanConvertFrom(typeof(string)));
        var points = Assert.IsType<Point3DCollection>(pointConverter.ConvertFromInvariantString("1,2,3 4,5,6"));
        Assert.Equal(2, points.Count);

        TypeConverter vectorConverter = TypeDescriptor.GetConverter(typeof(Vector3DCollection));
        var vectors = Assert.IsType<Vector3DCollection>(vectorConverter.ConvertFromInvariantString("1,2,3"));
        Assert.Equal("1,2,3", vectorConverter.ConvertToInvariantString(vectors));

        var serializer = new Jalium.UI.Media.Converters.Int32CollectionValueSerializer();
        Assert.True(serializer.CanConvertToString(new Int32Collection([1, 2, 3]), null));
        Assert.Equal("1 2 3", serializer.ConvertToString(new Int32Collection([1, 2, 3]), null));
        Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<Int32Collection>(serializer.ConvertFromString("1,2,3", null)).ToArray());
    }

    [Fact]
    public void ProjectionTransformsMapBoundsAndCloneTheirState()
    {
        Matrix3D matrix = Matrix3D.Identity;
        matrix.Translate(new Vector3D(10, 20, 30));
        var to2D = new GeneralTransform3DTo2D(matrix);
        Assert.Equal(new Point(11, 22), to2D.Transform(new Point3D(1, 2, 3)));
        Assert.Equal(new Rect(10, 20, 4, 5), to2D.TransformBounds(new Rect3D(0, 0, 0, 4, 5, 6)));

        var to3D = new GeneralTransform2DTo3D(new TranslateTransform3D(1, 2, 3));
        Assert.Equal(new Point3D(5, 7, 3), to3D.Transform(new Point(4, 5)));
        Assert.Equal(to2D.Transform(new Point3D(2, 3, 4)),
            ((GeneralTransform3DTo2D)to2D.Clone()).Transform(new Point3D(2, 3, 4)));
    }
}
