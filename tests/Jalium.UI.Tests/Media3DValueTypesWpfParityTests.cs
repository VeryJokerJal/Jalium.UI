using System.Globalization;
using System.Reflection;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class Media3DValueTypesWpfParityTests
{
    [Fact]
    public void PublicContractsExposeWpfValueTypeSurface()
    {
        Assert.NotNull(typeof(Matrix3D).GetProperty(nameof(Matrix3D.M11)));
        Assert.Null(typeof(Matrix3D).GetField(nameof(Matrix3D.M11), BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(typeof(Matrix3D).GetMethod(nameof(Matrix3D.Transform), [typeof(Point4D)]));
        Assert.NotNull(typeof(Matrix3D).GetMethod(nameof(Matrix3D.Transform), [typeof(Point4D[])]));
        Assert.NotNull(typeof(Matrix3D).GetMethod(nameof(Matrix3D.RotateAtPrepend), [typeof(Quaternion), typeof(Point3D)]));
        Assert.NotNull(typeof(Point3D).GetMethod(nameof(Point3D.Parse), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(Vector3D).GetMethod(nameof(Vector3D.Multiply), [typeof(Vector3D), typeof(Matrix3D)]));
        Assert.True(typeof(IFormattable).IsAssignableFrom(typeof(Point4D)));
        Assert.True(typeof(IFormattable).IsAssignableFrom(typeof(Matrix3D)));
    }

    [Fact]
    public void DistinguishedDefaultValuesMatchWpfIdentitySemantics()
    {
        Matrix3D matrix = default;
        Assert.True(matrix.IsIdentity);
        Assert.True(matrix.IsAffine);
        Assert.Equal(1.0, matrix.Determinant);
        Assert.Equal("Identity", matrix.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(new Point3D(1, 2, 3), matrix.Transform(new Point3D(1, 2, 3)));

        matrix.OffsetX = 5.0;
        Assert.Equal(1.0, matrix.M11);
        Assert.Equal(1.0, matrix.M22);
        Assert.Equal(1.0, matrix.M33);
        Assert.Equal(1.0, matrix.M44);
        Assert.Equal(new Point3D(6, 2, 3), matrix.Transform(new Point3D(1, 2, 3)));

        Quaternion quaternion = default;
        Assert.True(quaternion.IsIdentity);
        Assert.True(quaternion.IsNormalized);
        Assert.Equal(1.0, quaternion.W);
        Assert.Equal(new Vector3D(0, 1, 0), quaternion.Axis);
        Assert.Equal("Identity", quaternion.ToString(CultureInfo.InvariantCulture));
        Assert.False(new Quaternion(0, 0, 0, 0).IsIdentity);
    }

    [Fact]
    public void SizeAndRectEmptyValuesAreDistinctFromZeroSizedValues()
    {
        Assert.False(default(Size3D).IsEmpty);
        Assert.True(Size3D.Empty.IsEmpty);
        Assert.Equal(double.NegativeInfinity, Size3D.Empty.X);
        Assert.Equal("Empty", Size3D.Empty.ToString(CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentException>(() => new Size3D(-1, 2, 3));
        Assert.Throws<InvalidOperationException>(() =>
        {
            Size3D value = Size3D.Empty;
            value.X = 1;
        });

        Assert.False(default(Rect3D).IsEmpty);
        Assert.True(Rect3D.Empty.IsEmpty);
        Assert.Equal(double.PositiveInfinity, Rect3D.Empty.X);
        Assert.Equal(double.NegativeInfinity, Rect3D.Empty.SizeX);
        Assert.Equal("Empty", Rect3D.Empty.ToString(CultureInfo.InvariantCulture));
        Assert.Throws<ArgumentException>(() => new Rect3D(0, 0, 0, -1, 2, 3));
        Assert.Throws<InvalidOperationException>(() =>
        {
            Rect3D value = Rect3D.Empty;
            value.Location = new Point3D(1, 2, 3);
        });
    }

    [Fact]
    public void RectOperationsHandleEmptyTouchingAndContainmentCases()
    {
        var first = new Rect3D(0, 0, 0, 1, 1, 1);
        var touching = new Rect3D(1, 1, 1, 2, 2, 2);

        Assert.True(first.IntersectsWith(touching));
        Assert.Equal(new Rect3D(1, 1, 1, 0, 0, 0), Rect3D.Intersect(first, touching));
        Assert.True(touching.Contains(2, 2, 2));
        Assert.True(touching.Contains(new Rect3D(1.5, 1.5, 1.5, 0.5, 0.5, 0.5)));

        Rect3D union = Rect3D.Empty;
        union.Union(new Point3D(4, 5, 6));
        Assert.Equal(new Rect3D(4, 5, 6, 0, 0, 0), union);
        union.Union(first);
        Assert.Equal(new Rect3D(0, 0, 0, 4, 5, 6), union);

        union.Offset(new Vector3D(1, 2, 3));
        Assert.Equal(new Point3D(1, 2, 3), union.Location);
        union.Size = new Size3D(8, 9, 10);
        Assert.Equal(new Size3D(8, 9, 10), union.Size);
    }

    [Fact]
    public void MatrixCompositionAndHomogeneousTransformsUseRowVectorSemantics()
    {
        Matrix3D translation = Matrix3D.Identity;
        translation.Translate(new Vector3D(10, 20, 30));

        Matrix3D rotation = Matrix3D.Identity;
        rotation.Rotate(new Quaternion(new Vector3D(0, 0, 1), 90));
        Point3D rotated = rotation.Transform(new Point3D(1, 0, 0));
        Assert.Equal(0.0, rotated.X, 12);
        Assert.Equal(1.0, rotated.Y, 12);

        Matrix3D scaleAt = Matrix3D.Identity;
        scaleAt.ScaleAt(new Vector3D(2, 3, 4), new Point3D(10, 20, 30));
        Assert.Equal(new Point3D(10, 20, 30), scaleAt.Transform(new Point3D(10, 20, 30)));

        Matrix3D composed = Matrix3D.Multiply(rotation, translation);
        Point3D sequential = translation.Transform(rotation.Transform(new Point3D(2, 0, 0)));
        Assert.Equal(sequential, composed.Transform(new Point3D(2, 0, 0)));

        var points = new[] { new Point3D(1, 2, 3), new Point3D(4, 5, 6) };
        translation.Transform(points);
        Assert.Equal(new Point3D(11, 22, 33), points[0]);
        Assert.Equal(new Point3D(14, 25, 36), points[1]);

        Point4D homogeneous = translation.Transform(new Point4D(1, 2, 3, 2));
        Assert.Equal(new Point4D(21, 42, 63, 2), homogeneous);

        var zeroW = new Matrix3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0);
        Point3D infinite = zeroW.Transform(new Point3D(1, 2, 3));
        Assert.True(double.IsPositiveInfinity(infinite.X));
        Assert.True(double.IsPositiveInfinity(infinite.Y));
        Assert.True(double.IsPositiveInfinity(infinite.Z));
    }

    [Fact]
    public void ParsingFormattingConversionsAndEqualityMatchWpfEdgeCases()
    {
        Assert.Equal(new Point3D(1, 2, 3), Point3D.Parse("1 2 3"));
        Assert.Equal(new Point4D(1, 2, 3, 4), Point4D.Parse("1,2,3,4"));
        Assert.Equal(Matrix3D.Identity, Matrix3D.Parse("Identity"));
        Assert.Equal(Size3D.Empty, Size3D.Parse("Empty"));

        var german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("1,5;2,5;3,5", new Point3D(1.5, 2.5, 3.5).ToString(german));

        Assert.Equal(new Vector3D(1, 2, 3), (Vector3D)new Point3D(1, 2, 3));
        Assert.Equal(new Point4D(1, 2, 3, 1), (Point4D)new Point3D(1, 2, 3));
        Assert.Equal(new Point3D(1, 2, 3), (Point3D)new Vector3D(1, 2, 3));
        Assert.Equal(new Point3D(9, 18, 27), new Vector3D(10, 20, 30) - new Point3D(1, 2, 3));

        var nanPoint = new Point3D(double.NaN, 0, 0);
        var sameNanPoint = nanPoint;
        Assert.True(Point3D.Equals(nanPoint, nanPoint));
        Assert.False(nanPoint == sameNanPoint);

        Vector3D zero = default;
        zero.Normalize();
        Assert.True(double.IsNaN(zero.X));
        Assert.True(double.IsNaN(zero.Y));
        Assert.True(double.IsNaN(zero.Z));
    }
}
