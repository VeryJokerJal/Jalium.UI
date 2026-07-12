using System.Globalization;

namespace Jalium.UI.Media.Media3D;

public partial struct Point3D
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
}

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

public partial struct Vector3D
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
}

public partial struct Size3D
{
    private double _x;
    private double _y;
    private double _z;

    private Size3D(double x, double y, double z, bool _)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    internal static Size3D CreateUnchecked(double x, double y, double z) => new(x, y, z, true);

    private static void ValidateSize(double value, string parameterName)
    {
        if (value < 0.0)
        {
            throw new ArgumentException("Size dimensions cannot be negative.", parameterName);
        }
    }

    private readonly void VerifyNotEmpty()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot modify an empty Size3D.");
        }
    }

    public static bool Equals(Size3D size1, Size3D size2) => size1.Equals(size2);

    public static Size3D Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Empty", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 3);
        return new Size3D(values[0], values[1], values[2]);
    }

    public override string ToString() =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, null, X, Y, Z);

    public string ToString(IFormatProvider? provider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, provider, X, Y, Z);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z);

    public static explicit operator Vector3D(Size3D size) => new(size.X, size.Y, size.Z);

    public static explicit operator Point3D(Size3D size) => new(size.X, size.Y, size.Z);
}

public partial struct Rect3D
{
    private double _x;
    private double _y;
    private double _z;
    private double _sizeX;
    private double _sizeY;
    private double _sizeZ;

    private Rect3D(double x, double y, double z, double sizeX, double sizeY, double sizeZ, bool _)
    {
        _x = x;
        _y = y;
        _z = z;
        _sizeX = sizeX;
        _sizeY = sizeY;
        _sizeZ = sizeZ;
    }

    private static void ValidateSize(double value, string parameterName)
    {
        if (value < 0.0)
        {
            throw new ArgumentException("Size dimensions cannot be negative.", parameterName);
        }
    }

    private readonly void VerifyNotEmpty()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot modify an empty Rect3D.");
        }
    }

    public bool Contains(double x, double y, double z) => Contains(new Point3D(x, y, z));

    public bool Contains(Rect3D rect) =>
        !IsEmpty && !rect.IsEmpty &&
        X <= rect.X && Y <= rect.Y && Z <= rect.Z &&
        X + SizeX >= rect.X + rect.SizeX &&
        Y + SizeY >= rect.Y + rect.SizeY &&
        Z + SizeZ >= rect.Z + rect.SizeZ;

    public bool IntersectsWith(Rect3D rect) =>
        !IsEmpty && !rect.IsEmpty &&
        rect.X <= X + SizeX && rect.X + rect.SizeX >= X &&
        rect.Y <= Y + SizeY && rect.Y + rect.SizeY >= Y &&
        rect.Z <= Z + SizeZ && rect.Z + rect.SizeZ >= Z;

    public void Intersect(Rect3D rect) => this = Intersect(this, rect);

    public static Rect3D Intersect(Rect3D rect1, Rect3D rect2)
    {
        if (!rect1.IntersectsWith(rect2))
        {
            return Empty;
        }

        double x = Math.Max(rect1.X, rect2.X);
        double y = Math.Max(rect1.Y, rect2.Y);
        double z = Math.Max(rect1.Z, rect2.Z);
        double maxX = Math.Min(rect1.X + rect1.SizeX, rect2.X + rect2.SizeX);
        double maxY = Math.Min(rect1.Y + rect1.SizeY, rect2.Y + rect2.SizeY);
        double maxZ = Math.Min(rect1.Z + rect1.SizeZ, rect2.Z + rect2.SizeZ);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public static Rect3D Union(Rect3D rect1, Rect3D rect2)
    {
        if (rect1.IsEmpty)
        {
            return rect2;
        }

        if (rect2.IsEmpty)
        {
            return rect1;
        }

        double x = Math.Min(rect1.X, rect2.X);
        double y = Math.Min(rect1.Y, rect2.Y);
        double z = Math.Min(rect1.Z, rect2.Z);
        double maxX = Math.Max(rect1.X + rect1.SizeX, rect2.X + rect2.SizeX);
        double maxY = Math.Max(rect1.Y + rect1.SizeY, rect2.Y + rect2.SizeY);
        double maxZ = Math.Max(rect1.Z + rect1.SizeZ, rect2.Z + rect2.SizeZ);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public void Union(Point3D point) => this = Union(this, point);

    public static Rect3D Union(Rect3D rect, Point3D point)
    {
        if (rect.IsEmpty)
        {
            return new Rect3D(point, new Size3D(0.0, 0.0, 0.0));
        }

        double x = Math.Min(rect.X, point.X);
        double y = Math.Min(rect.Y, point.Y);
        double z = Math.Min(rect.Z, point.Z);
        double maxX = Math.Max(rect.X + rect.SizeX, point.X);
        double maxY = Math.Max(rect.Y + rect.SizeY, point.Y);
        double maxZ = Math.Max(rect.Z + rect.SizeZ, point.Z);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public void Offset(Vector3D offsetVector) => Offset(offsetVector.X, offsetVector.Y, offsetVector.Z);

    public void Offset(double offsetX, double offsetY, double offsetZ)
    {
        VerifyNotEmpty();
        _x += offsetX;
        _y += offsetY;
        _z += offsetZ;
    }

    public static Rect3D Offset(Rect3D rect, Vector3D offsetVector)
    {
        rect.Offset(offsetVector);
        return rect;
    }

    public static Rect3D Offset(Rect3D rect, double offsetX, double offsetY, double offsetZ)
    {
        rect.Offset(offsetX, offsetY, offsetZ);
        return rect;
    }

    public static bool Equals(Rect3D rect1, Rect3D rect2) => rect1.Equals(rect2);

    public static Rect3D Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Empty", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 6);
        return new Rect3D(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public override string ToString() =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, null, X, Y, Z, SizeX, SizeY, SizeZ);

    public string ToString(IFormatProvider? provider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, provider, X, Y, Z, SizeX, SizeY, SizeZ);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z, SizeX, SizeY, SizeZ);
}

public partial struct Quaternion
{
    private double _x;
    private double _y;
    private double _z;
    private double _w;
    private bool _isNotDistinguishedIdentity;

    private void EnsureNotDistinguishedIdentity()
    {
        if (_isNotDistinguishedIdentity)
        {
            return;
        }

        _x = 0.0;
        _y = 0.0;
        _z = 0.0;
        _w = 1.0;
        _isNotDistinguishedIdentity = true;
    }

    public static Quaternion Add(Quaternion left, Quaternion right) => left + right;

    public static Quaternion Subtract(Quaternion left, Quaternion right) => left - right;

    public static Quaternion Multiply(Quaternion left, Quaternion right) => left * right;

    public static bool Equals(Quaternion quaternion1, Quaternion quaternion2) => quaternion1.Equals(quaternion2);

    public static Quaternion Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Identity", StringComparison.OrdinalIgnoreCase))
        {
            return Identity;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 4);
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    public override string ToString() =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(null, null, X, Y, Z, W);

    public string ToString(IFormatProvider? provider) =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(null, provider, X, Y, Z, W);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z, W);

    public static Quaternion operator +(Quaternion left, Quaternion right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    public static Quaternion operator -(Quaternion left, Quaternion right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
}

public partial struct Matrix3D
{
    private double _m11;
    private double _m12;
    private double _m13;
    private double _m14;
    private double _m21;
    private double _m22;
    private double _m23;
    private double _m24;
    private double _m31;
    private double _m32;
    private double _m33;
    private double _m34;
    private double _offsetX;
    private double _offsetY;
    private double _offsetZ;
    private double _m44;
    private bool _isNotDistinguishedIdentity;

    private void EnsureNotDistinguishedIdentity()
    {
        if (_isNotDistinguishedIdentity)
        {
            return;
        }

        _m11 = 1.0;
        _m22 = 1.0;
        _m33 = 1.0;
        _m44 = 1.0;
        _isNotDistinguishedIdentity = true;
    }

    public void SetIdentity() => this = default;

    public void Rotate(Quaternion quaternion) => Append(CreateRotationMatrix(quaternion));

    public void RotatePrepend(Quaternion quaternion) => Prepend(CreateRotationMatrix(quaternion));

    public void RotateAt(Quaternion quaternion, Point3D center) => Append(CreateRotationMatrix(quaternion, center));

    public void RotateAtPrepend(Quaternion quaternion, Point3D center) => Prepend(CreateRotationMatrix(quaternion, center));

    public void Scale(Vector3D scale) => Append(CreateScaleMatrix(scale, default));

    public void ScalePrepend(Vector3D scale) => Prepend(CreateScaleMatrix(scale, default));

    public void ScaleAt(Vector3D scale, Point3D center) => Append(CreateScaleMatrix(scale, center));

    public void ScaleAtPrepend(Vector3D scale, Point3D center) => Prepend(CreateScaleMatrix(scale, center));

    public void Translate(Vector3D offset) => Append(CreateTranslationMatrix(offset));

    public void TranslatePrepend(Vector3D offset) => Prepend(CreateTranslationMatrix(offset));

    public static Matrix3D Multiply(Matrix3D matrix1, Matrix3D matrix2) => matrix1 * matrix2;

    public Point4D Transform(Point4D point) => new(
        point.X * M11 + point.Y * M21 + point.Z * M31 + point.W * OffsetX,
        point.X * M12 + point.Y * M22 + point.Z * M32 + point.W * OffsetY,
        point.X * M13 + point.Y * M23 + point.Z * M33 + point.W * OffsetZ,
        point.X * M14 + point.Y * M24 + point.Z * M34 + point.W * M44);

    public void Transform(Point3D[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = Transform(points[index]);
        }
    }

    public void Transform(Point4D[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = Transform(points[index]);
        }
    }

    public void Transform(Vector3D[] vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        for (int index = 0; index < vectors.Length; index++)
        {
            vectors[index] = Transform(vectors[index]);
        }
    }

    public static bool Equals(Matrix3D matrix1, Matrix3D matrix2) => matrix1.Equals(matrix2);

    public static Matrix3D Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Identity", StringComparison.OrdinalIgnoreCase))
        {
            return Identity;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 16);
        return new Matrix3D(
            values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7],
            values[8], values[9], values[10], values[11],
            values[12], values[13], values[14], values[15]);
    }

    public override string ToString() => IsIdentity
        ? "Identity"
        : Media3DValueFormatter.Format(
            null, null,
            M11, M12, M13, M14,
            M21, M22, M23, M24,
            M31, M32, M33, M34,
            OffsetX, OffsetY, OffsetZ, M44);

    public string ToString(IFormatProvider? provider) => IsIdentity
        ? "Identity"
        : Media3DValueFormatter.Format(
            null, provider,
            M11, M12, M13, M14,
            M21, M22, M23, M24,
            M31, M32, M33, M34,
            OffsetX, OffsetY, OffsetZ, M44);

    string IFormattable.ToString(string? format, IFormatProvider? provider) => IsIdentity
        ? "Identity"
        : Media3DValueFormatter.Format(
            format, provider,
            M11, M12, M13, M14,
            M21, M22, M23, M24,
            M31, M32, M33, M34,
            OffsetX, OffsetY, OffsetZ, M44);

    private static Matrix3D CreateTranslationMatrix(Vector3D offset) => new(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        offset.X, offset.Y, offset.Z, 1.0);

    private static Matrix3D CreateScaleMatrix(Vector3D scale, Point3D center) => new(
        scale.X, 0.0, 0.0, 0.0,
        0.0, scale.Y, 0.0, 0.0,
        0.0, 0.0, scale.Z, 0.0,
        center.X * (1.0 - scale.X),
        center.Y * (1.0 - scale.Y),
        center.Z * (1.0 - scale.Z),
        1.0);

    private static Matrix3D CreateRotationMatrix(Quaternion quaternion) => CreateRotationMatrix(quaternion, default);

    private static Matrix3D CreateRotationMatrix(Quaternion quaternion, Point3D center)
    {
        double x = quaternion.X;
        double y = quaternion.Y;
        double z = quaternion.Z;
        double w = quaternion.W;
        double norm = x * x + y * y + z * z + w * w;
        if (norm == 0.0)
        {
            return Identity;
        }

        double scale = 2.0 / norm;
        double xx = x * x * scale;
        double yy = y * y * scale;
        double zz = z * z * scale;
        double xy = x * y * scale;
        double xz = x * z * scale;
        double yz = y * z * scale;
        double xw = x * w * scale;
        double yw = y * w * scale;
        double zw = z * w * scale;

        var matrix = new Matrix3D(
            1.0 - yy - zz, xy + zw, xz - yw, 0.0,
            xy - zw, 1.0 - xx - zz, yz + xw, 0.0,
            xz + yw, yz - xw, 1.0 - xx - yy, 0.0,
            0.0, 0.0, 0.0, 1.0);

        matrix.OffsetX = center.X - (center.X * matrix.M11 + center.Y * matrix.M21 + center.Z * matrix.M31);
        matrix.OffsetY = center.Y - (center.X * matrix.M12 + center.Y * matrix.M22 + center.Z * matrix.M32);
        matrix.OffsetZ = center.Z - (center.X * matrix.M13 + center.Y * matrix.M23 + center.Z * matrix.M33);
        return matrix;
    }
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
