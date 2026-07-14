using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a displacement in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Vector3DConverter))]
public partial struct Vector3D : IEquatable<Vector3D>, IFormattable
{
    public static Vector3D Add(Vector3D vector1, Vector3D vector2) => vector1 + vector2;

    public static Vector3D Subtract(Vector3D vector1, Vector3D vector2) => vector1 - vector2;

    public static Point3D Add(Vector3D vector, Point3D point) => vector + point;

    public static Point3D Subtract(Vector3D vector, Point3D point) => vector - point;

    public static Vector3D Multiply(Vector3D vector, double scalar) => vector * scalar;

    public static Vector3D Multiply(double scalar, Vector3D vector) => scalar * vector;

    public static Vector3D Divide(Vector3D vector, double scalar) => vector / scalar;

    public static Vector3D Multiply(Vector3D vector, Matrix3D matrix) => matrix.Transform(vector);

    public static bool Equals(Vector3D vector1, Vector3D vector2) => vector1.Equals(vector2);

    public static Vector3D Parse(string source)
    {
        double[] values = Media3DValueFormatter.ParseNumbers(source, 3);
        return new Vector3D(values[0], values[1], values[2]);
    }

    public string ToString(IFormatProvider? provider) => Media3DValueFormatter.Format(null, provider, X, Y, Z);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        Media3DValueFormatter.Format(format, formatProvider, X, Y, Z);

    public static Point3D operator +(Vector3D vector, Point3D point) =>
        new(vector.X + point.X, vector.Y + point.Y, vector.Z + point.Z);

    public static Point3D operator -(Vector3D vector, Point3D point) =>
        new(vector.X - point.X, vector.Y - point.Y, vector.Z - point.Z);

    public static Vector3D operator *(Vector3D vector, Matrix3D matrix) => Multiply(vector, matrix);

    public static explicit operator Point3D(Vector3D vector) => new(vector.X, vector.Y, vector.Z);

    public static explicit operator Size3D(Vector3D vector) => Size3D.CreateUnchecked(vector.X, vector.Y, vector.Z);

    // --- from Point3D.cs ---
    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public void Normalize()
    {
        double len = Length;
        X /= len;
        Y /= len;
        Z /= len;
    }

    public void Negate() { X = -X; Y = -Y; Z = -Z; }

    public static Vector3D operator +(Vector3D a, Vector3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3D operator -(Vector3D a, Vector3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3D operator -(Vector3D v) => new(-v.X, -v.Y, -v.Z);
    public static Vector3D operator *(Vector3D v, double scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vector3D operator *(double scalar, Vector3D v) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);
    public static Vector3D operator /(Vector3D v, double scalar) => new(v.X / scalar, v.Y / scalar, v.Z / scalar);

    public static double DotProduct(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3D CrossProduct(Vector3D a, Vector3D b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
    public static double AngleBetween(Vector3D a, Vector3D b) =>
        Math.Acos(Math.Clamp(DotProduct(a, b) / (a.Length * b.Length), -1.0, 1.0)) * (180.0 / Math.PI);

    public bool Equals(Vector3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => Media3DValueFormatter.Format(null, null, X, Y, Z);
    public static bool operator ==(Vector3D left, Vector3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    public static bool operator !=(Vector3D left, Vector3D right) => !(left == right);
}