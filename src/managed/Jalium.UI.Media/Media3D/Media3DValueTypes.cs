using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a point in homogeneous four-dimensional coordinates.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Point4DConverter))]
public struct Point4D : IEquatable<Point4D>, IFormattable
{
    public Point4D(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double W { get; set; }

    public void Offset(double deltaX, double deltaY, double deltaZ, double deltaW)
    {
        X += deltaX;
        Y += deltaY;
        Z += deltaZ;
        W += deltaW;
    }

    public static Point4D Add(Point4D point1, Point4D point2) => point1 + point2;

    public static Point4D Subtract(Point4D point1, Point4D point2) => point1 - point2;

    public static Point4D Multiply(Point4D point, Matrix3D matrix) => matrix.Transform(point);

    public static bool Equals(Point4D point1, Point4D point2) => point1.Equals(point2);

    public static Point4D Parse(string source)
    {
        double[] values = Media3DValueFormatter.ParseNumbers(source, 4);
        return new Point4D(values[0], values[1], values[2], values[3]);
    }

    public bool Equals(Point4D other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override bool Equals(object? obj) => obj is Point4D other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    public override string ToString() => Media3DValueFormatter.Format(null, null, X, Y, Z, W);

    public string ToString(IFormatProvider? provider) => Media3DValueFormatter.Format(null, provider, X, Y, Z, W);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        Media3DValueFormatter.Format(format, formatProvider, X, Y, Z, W);

    public static Point4D operator +(Point4D point1, Point4D point2) =>
        new(point1.X + point2.X, point1.Y + point2.Y, point1.Z + point2.Z, point1.W + point2.W);

    public static Point4D operator -(Point4D point1, Point4D point2) =>
        new(point1.X - point2.X, point1.Y - point2.Y, point1.Z - point2.Z, point1.W - point2.W);

    public static Point4D operator *(Point4D point, Matrix3D matrix) => Multiply(point, matrix);

    public static bool operator ==(Point4D left, Point4D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;

    public static bool operator !=(Point4D left, Point4D right) => !(left == right);
}

internal static class Media3DValueFormatter
{
    public static string Format(string? format, IFormatProvider? provider, params double[] values)
    {
        provider ??= CultureInfo.CurrentCulture;
        string separator = NumberFormatInfo.GetInstance(provider).NumberDecimalSeparator == "," ? ";" : ",";
        return string.Join(separator, values.Select(value => value.ToString(format, provider)));
    }

    public static double[] ParseNumbers(string source, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] tokens = source.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != expectedCount)
        {
            throw new InvalidOperationException($"Expected {expectedCount} numeric values but found {tokens.Length}.");
        }

        var values = new double[expectedCount];
        for (int index = 0; index < expectedCount; index++)
        {
            values[index] = double.Parse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return values;
    }
}
