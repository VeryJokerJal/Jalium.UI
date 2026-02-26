namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a point in 3-D space.
/// </summary>
public struct Point3D : IEquatable<Point3D>
{
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

    public bool Equals(Point3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"{X},{Y},{Z}";
    public static bool operator ==(Point3D left, Point3D right) => left.Equals(right);
    public static bool operator !=(Point3D left, Point3D right) => !left.Equals(right);
}

/// <summary>
/// Represents a displacement in 3-D space.
/// </summary>
public struct Vector3D : IEquatable<Vector3D>
{
    public Vector3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public void Normalize()
    {
        double len = Length;
        if (len > 0) { X /= len; Y /= len; Z /= len; }
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

    public bool Equals(Vector3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Vector3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"{X},{Y},{Z}";
    public static bool operator ==(Vector3D left, Vector3D right) => left.Equals(right);
    public static bool operator !=(Vector3D left, Vector3D right) => !left.Equals(right);
}

/// <summary>
/// Represents a 3-D size structure.
/// </summary>
public struct Size3D : IEquatable<Size3D>
{
    public static Size3D Empty => new(0, 0, 0);

    public Size3D(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool IsEmpty => X == 0 && Y == 0 && Z == 0;

    public bool Equals(Size3D other) => X == other.X && Y == other.Y && Z == other.Z;
    public override bool Equals(object? obj) => obj is Size3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Size3D left, Size3D right) => left.Equals(right);
    public static bool operator !=(Size3D left, Size3D right) => !left.Equals(right);
}

/// <summary>
/// Represents an axis-aligned bounding box in 3-D space.
/// </summary>
public struct Rect3D : IEquatable<Rect3D>
{
    public static Rect3D Empty => new(0, 0, 0, 0, 0, 0);

    public Rect3D(double x, double y, double z, double sizeX, double sizeY, double sizeZ)
    {
        X = x; Y = y; Z = z; SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
    }

    public Rect3D(Point3D location, Size3D size)
        : this(location.X, location.Y, location.Z, size.X, size.Y, size.Z) { }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double SizeX { get; set; }
    public double SizeY { get; set; }
    public double SizeZ { get; set; }
    public Point3D Location => new(X, Y, Z);
    public Size3D Size => new(SizeX, SizeY, SizeZ);
    public bool IsEmpty => SizeX == 0 && SizeY == 0 && SizeZ == 0;

    public bool Contains(Point3D point) =>
        point.X >= X && point.X <= X + SizeX &&
        point.Y >= Y && point.Y <= Y + SizeY &&
        point.Z >= Z && point.Z <= Z + SizeZ;

    public void Union(Rect3D rect)
    {
        double minX = Math.Min(X, rect.X), minY = Math.Min(Y, rect.Y), minZ = Math.Min(Z, rect.Z);
        double maxX = Math.Max(X + SizeX, rect.X + rect.SizeX);
        double maxY = Math.Max(Y + SizeY, rect.Y + rect.SizeY);
        double maxZ = Math.Max(Z + SizeZ, rect.Z + rect.SizeZ);
        X = minX; Y = minY; Z = minZ;
        SizeX = maxX - minX; SizeY = maxY - minY; SizeZ = maxZ - minZ;
    }

    public bool Equals(Rect3D other) => X == other.X && Y == other.Y && Z == other.Z && SizeX == other.SizeX && SizeY == other.SizeY && SizeZ == other.SizeZ;
    public override bool Equals(object? obj) => obj is Rect3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, SizeX, SizeY, SizeZ);
    public static bool operator ==(Rect3D left, Rect3D right) => left.Equals(right);
    public static bool operator !=(Rect3D left, Rect3D right) => !left.Equals(right);
}

/// <summary>
/// Represents a 3-D quaternion for rotation.
/// </summary>
public struct Quaternion : IEquatable<Quaternion>
{
    public Quaternion(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

    public Quaternion(Vector3D axisOfRotation, double angleInDegrees)
    {
        double halfAngle = angleInDegrees * Math.PI / 360.0;
        double sin = Math.Sin(halfAngle);
        var axis = axisOfRotation;
        double len = axis.Length;
        if (len > 0) { axis = axis / len; }
        X = axis.X * sin; Y = axis.Y * sin; Z = axis.Z * sin;
        W = Math.Cos(halfAngle);
    }

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double W { get; set; }
    public Vector3D Axis => new(X, Y, Z);
    public double Angle => 2.0 * Math.Acos(Math.Clamp(W, -1.0, 1.0)) * (180.0 / Math.PI);
    public bool IsNormalized { get { double n = X * X + Y * Y + Z * Z + W * W; return Math.Abs(n - 1.0) < 1e-10; } }
    public bool IsIdentity => X == 0 && Y == 0 && Z == 0 && W == 1;
    public static Quaternion Identity => new(0, 0, 0, 1);

    public void Normalize()
    {
        double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        if (n > 0) { X /= n; Y /= n; Z /= n; W /= n; }
    }

    public void Conjugate() { X = -X; Y = -Y; Z = -Z; }
    public void Invert() { Conjugate(); double n = X * X + Y * Y + Z * Z + W * W; X /= n; Y /= n; Z /= n; W /= n; }

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quaternion Slerp(Quaternion from, Quaternion to, double t)
    {
        double cosOmega = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
        if (cosOmega < 0) { to = new(-to.X, -to.Y, -to.Z, -to.W); cosOmega = -cosOmega; }
        double s0, s1;
        if (cosOmega > 0.9999) { s0 = 1.0 - t; s1 = t; }
        else
        {
            double omega = Math.Acos(cosOmega);
            double sinOmega = Math.Sin(omega);
            s0 = Math.Sin((1.0 - t) * omega) / sinOmega;
            s1 = Math.Sin(t * omega) / sinOmega;
        }
        return new(s0 * from.X + s1 * to.X, s0 * from.Y + s1 * to.Y, s0 * from.Z + s1 * to.Z, s0 * from.W + s1 * to.W);
    }

    public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);
    public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);
}

/// <summary>
/// Represents a 4x4 matrix used for 3-D transformations.
/// </summary>
public struct Matrix3D : IEquatable<Matrix3D>
{
    public double M11, M12, M13, M14;
    public double M21, M22, M23, M24;
    public double M31, M32, M33, M34;
    public double OffsetX, OffsetY, OffsetZ, M44;

    public Matrix3D(double m11, double m12, double m13, double m14,
                    double m21, double m22, double m23, double m24,
                    double m31, double m32, double m33, double m34,
                    double offsetX, double offsetY, double offsetZ, double m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        OffsetX = offsetX; OffsetY = offsetY; OffsetZ = offsetZ; M44 = m44;
    }

    public static Matrix3D Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    public bool IsIdentity => M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 &&
                               M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 &&
                               M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 &&
                               OffsetX == 0 && OffsetY == 0 && OffsetZ == 0 && M44 == 1;
    public bool HasInverse => Math.Abs(Determinant) > 1e-15;
    public double Determinant =>
        M11 * (M22 * (M33 * M44 - M34 * OffsetZ) - M23 * (M32 * M44 - M34 * OffsetY) + M24 * (M32 * OffsetZ - M33 * OffsetY)) -
        M12 * (M21 * (M33 * M44 - M34 * OffsetZ) - M23 * (M31 * M44 - M34 * OffsetX) + M24 * (M31 * OffsetZ - M33 * OffsetX)) +
        M13 * (M21 * (M32 * M44 - M34 * OffsetY) - M22 * (M31 * M44 - M34 * OffsetX) + M24 * (M31 * OffsetY - M32 * OffsetX)) -
        M14 * (M21 * (M32 * OffsetZ - M33 * OffsetY) - M22 * (M31 * OffsetZ - M33 * OffsetX) + M23 * (M31 * OffsetY - M32 * OffsetX));

    public void Invert()
    {
        if (!HasInverse) throw new InvalidOperationException("Matrix is not invertible.");
        // Simple adjugate/determinant approach (stub - real impl would be more extensive)
        this = Identity; // Placeholder
    }

    public static Matrix3D operator *(Matrix3D a, Matrix3D b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.OffsetX,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.OffsetY,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.OffsetZ,
        a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.OffsetX,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.OffsetY,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.OffsetZ,
        a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.OffsetX,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.OffsetY,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.OffsetZ,
        a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,
        a.OffsetX * b.M11 + a.OffsetY * b.M21 + a.OffsetZ * b.M31 + a.M44 * b.OffsetX,
        a.OffsetX * b.M12 + a.OffsetY * b.M22 + a.OffsetZ * b.M32 + a.M44 * b.OffsetY,
        a.OffsetX * b.M13 + a.OffsetY * b.M23 + a.OffsetZ * b.M33 + a.M44 * b.OffsetZ,
        a.OffsetX * b.M14 + a.OffsetY * b.M24 + a.OffsetZ * b.M34 + a.M44 * b.M44);

    public Point3D Transform(Point3D point)
    {
        double x = point.X * M11 + point.Y * M21 + point.Z * M31 + OffsetX;
        double y = point.X * M12 + point.Y * M22 + point.Z * M32 + OffsetY;
        double z = point.X * M13 + point.Y * M23 + point.Z * M33 + OffsetZ;
        double w = point.X * M14 + point.Y * M24 + point.Z * M34 + M44;
        if (w != 1.0 && w != 0.0) { x /= w; y /= w; z /= w; }
        return new(x, y, z);
    }

    public Vector3D Transform(Vector3D vector)
    {
        return new(
            vector.X * M11 + vector.Y * M21 + vector.Z * M31,
            vector.X * M12 + vector.Y * M22 + vector.Z * M32,
            vector.X * M13 + vector.Y * M23 + vector.Z * M33);
    }

    public void Prepend(Matrix3D matrix) { this = matrix * this; }
    public void Append(Matrix3D matrix) { this = this * matrix; }

    public bool Equals(Matrix3D other) =>
        M11 == other.M11 && M12 == other.M12 && M13 == other.M13 && M14 == other.M14 &&
        M21 == other.M21 && M22 == other.M22 && M23 == other.M23 && M24 == other.M24 &&
        M31 == other.M31 && M32 == other.M32 && M33 == other.M33 && M34 == other.M34 &&
        OffsetX == other.OffsetX && OffsetY == other.OffsetY && OffsetZ == other.OffsetZ && M44 == other.M44;
    public override bool Equals(object? obj) => obj is Matrix3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M22, M33, M44, OffsetX, OffsetY, OffsetZ);
    public static bool operator ==(Matrix3D left, Matrix3D right) => left.Equals(right);
    public static bool operator !=(Matrix3D left, Matrix3D right) => !left.Equals(right);
}

/// <summary>
/// Represents a collection of Point3D values.
/// </summary>
public sealed class Point3DCollection : List<Point3D>
{
    public Point3DCollection() { }
    public Point3DCollection(IEnumerable<Point3D> collection) : base(collection) { }
    public Point3DCollection(int capacity) : base(capacity) { }
}

/// <summary>
/// Represents a collection of Vector3D values.
/// </summary>
public sealed class Vector3DCollection : List<Vector3D>
{
    public Vector3DCollection() { }
    public Vector3DCollection(IEnumerable<Vector3D> collection) : base(collection) { }
    public Vector3DCollection(int capacity) : base(capacity) { }
}

/// <summary>
/// Represents a collection of integer values used for mesh triangle indices.
/// </summary>
public sealed class Int32Collection : List<int>
{
    public Int32Collection() { }
    public Int32Collection(IEnumerable<int> collection) : base(collection) { }
    public Int32Collection(int capacity) : base(capacity) { }
}

/// <summary>
/// Represents a collection of Point values used for texture coordinates.
/// </summary>
public sealed class PointCollection : List<Point>
{
    public PointCollection() { }
    public PointCollection(IEnumerable<Point> collection) : base(collection) { }
    public PointCollection(int capacity) : base(capacity) { }
}
