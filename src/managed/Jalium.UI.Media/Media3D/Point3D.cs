using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a point in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Point3DConverter))]
public partial struct Point3D : IEquatable<Point3D>, IFormattable
{
    public static Point3D Add(Point3D point, Vector3D vector) => point + vector;

    public static Point3D Subtract(Point3D point, Vector3D vector) => point - vector;

    public static Vector3D Subtract(Point3D point1, Point3D point2) => point1 - point2;

    public static Point3D Multiply(Point3D point, Matrix3D matrix) => matrix.Transform(point);

    public static bool Equals(Point3D point1, Point3D point2) => point1.Equals(point2);

    public static Point3D Parse(string source)
    {
        double[] values = Media3DValueFormatter.ParseNumbers(source, 3);
        return new Point3D(values[0], values[1], values[2]);
    }

    public string ToString(IFormatProvider? provider) => Media3DValueFormatter.Format(null, provider, X, Y, Z);

    string IFormattable.ToString(string? format, IFormatProvider? provider) =>
        Media3DValueFormatter.Format(format, provider, X, Y, Z);

    public static Point3D operator *(Point3D point, Matrix3D matrix) => Multiply(point, matrix);

    public static explicit operator Vector3D(Point3D point) => new(point.X, point.Y, point.Z);

    public static explicit operator Point4D(Point3D point) => new(point.X, point.Y, point.Z, 1.0);

    // --- from Point3D.cs ---
    public Point3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public static Point3D operator +(Point3D point, Vector3D vector) => new(point.X + vector.X, point.Y + vector.Y, point.Z + vector.Z);
    public static Point3D operator -(Point3D point, Vector3D vector) => new(point.X - vector.X, point.Y - vector.Y, point.Z - vector.Z);
    public static Vector3D operator -(Point3D point1, Point3D point2) => new(point1.X - point2.X, point1.Y - point2.Y, point1.Z - point2.Z);
    public static Point3D operator *(Point3D point, double scalar) => new(point.X * scalar, point.Y * scalar, point.Z * scalar);

    public void Offset(double offsetX, double offsetY, double offsetZ) { X += offsetX; Y += offsetY; Z += offsetZ; }
    public double DistanceTo(Point3D other) => (this - other).Length;

    public bool Equals(Point3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => Media3DValueFormatter.Format(null, null, X, Y, Z);
    public static bool operator ==(Point3D left, Point3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    public static bool operator !=(Point3D left, Point3D right) => !(left == right);
}
