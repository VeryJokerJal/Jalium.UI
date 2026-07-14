using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class PointVectorParityTests
{
    [Fact]
    public void Point_IsMutable_AndSupportsWpfOperations()
    {
        var point = new Point(2, 3);
        point.X = 5;
        point.Y = 7;
        point.Offset(1, -2);

        Assert.Equal(new Point(6, 5), point);
        Assert.Equal(new Point(8, 9), Point.Add(point, new Vector(2, 4)));
        Assert.Equal(new Point(4, 1), Point.Subtract(point, new Vector(2, 4)));
        Assert.Equal(new Vector(5, 3), Point.Subtract(point, new Point(1, 2)));

        var matrix = new Matrix(2, 0, 0, 3, 10, 20);
        Assert.Equal(new Point(22, 35), Point.Multiply(point, matrix));
        Assert.Equal(Point.Multiply(point, matrix), point * matrix);
        Assert.Equal(new Size(6, 5), (Size)new Point(-6, -5));
        Assert.Equal(new Vector(6, 5), (Vector)point);
    }

    [Fact]
    public void Vector_IsMutable_AndSupportsWpfOperations()
    {
        var vector = new Vector(3, 4);
        Assert.Equal(5, vector.Length);
        Assert.Equal(25, vector.LengthSquared);

        vector.Normalize();
        Assert.Equal(0.6, vector.X, 12);
        Assert.Equal(0.8, vector.Y, 12);
        vector.Negate();
        Assert.Equal(-0.6, vector.X, 12);
        Assert.Equal(-0.8, vector.Y, 12);

        var x = new Vector(1, 0);
        var y = new Vector(0, 1);
        Assert.Equal(1, Vector.CrossProduct(x, y));
        Assert.Equal(1, Vector.Determinant(x, y));
        Assert.Equal(90, Vector.AngleBetween(x, y), 12);
        Assert.Equal(0, Vector.Multiply(x, y));
        Assert.Equal(new Vector(1, 1), Vector.Add(x, y));
        Assert.Equal(new Vector(0.5, 0), Vector.Divide(x, 2));
        Assert.Equal(new Point(3, 4), Vector.Add(new Vector(2, 2), new Point(1, 2)));

        var matrix = new Matrix(2, 0, 0, 3, 100, 200);
        Assert.Equal(new Vector(2, 3), Vector.Multiply(new Vector(1, 1), matrix));
        Assert.Equal(new Vector(2, 3), new Vector(1, 1) * matrix);
        Assert.Equal(new Size(2, 3), (Size)new Vector(-2, -3));
        Assert.Equal(new Point(2, 3), (Point)new Vector(2, 3));
    }

    [Fact]
    public void ParseAndFormatting_FollowWpfCultureRules()
    {
        Assert.Equal(new Point(1.5, -2.25), Point.Parse("1.5,-2.25"));
        Assert.Equal(new Vector(3, 4), Vector.Parse("3 4"));
        Assert.Throws<FormatException>(() => Point.Parse("1,2,3"));

        var french = CultureInfo.GetCultureInfo("fr-FR");
        Assert.Equal("1,5;-2,25", new Point(1.5, -2.25).ToString(french));
        Assert.Equal("3,4", new Vector(3, 4).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Equality_HasWpfNanSemantics()
    {
        var first = new Point(double.NaN, 1);
        var second = new Point(double.NaN, 1);
        Assert.True(Point.Equals(first, second));
        Assert.True(first.Equals(second));
        Assert.False(first == second);
        Assert.True(first != second);

        var vector1 = new Vector(double.NaN, 1);
        var vector2 = new Vector(double.NaN, 1);
        Assert.True(Vector.Equals(vector1, vector2));
        Assert.False(vector1 == vector2);
    }

    [Fact]
    public void StandardTypeConverters_RoundTripStrings()
    {
        TypeConverter pointConverter = TypeDescriptor.GetConverter(typeof(Point));
        TypeConverter vectorConverter = TypeDescriptor.GetConverter(typeof(Vector));

        Assert.IsType<PointConverter>(pointConverter);
        Assert.IsType<VectorConverter>(vectorConverter);
        Assert.Equal(new Point(1, 2), pointConverter.ConvertFromInvariantString("1,2"));
        Assert.Equal("3,4", pointConverter.ConvertToInvariantString(new Point(3, 4)));
        Assert.Equal(new Vector(5, 6), vectorConverter.ConvertFromInvariantString("5,6"));
        Assert.Equal("7,8", vectorConverter.ConvertToInvariantString(new Vector(7, 8)));
    }
}
