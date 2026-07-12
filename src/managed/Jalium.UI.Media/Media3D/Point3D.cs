namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a point in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Point3DConverter))]
public partial struct Point3D : IEquatable<Point3D>, IFormattable
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

    public bool Equals(Point3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Point3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => Media3DValueFormatter.Format(null, null, X, Y, Z);
    public static bool operator ==(Point3D left, Point3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    public static bool operator !=(Point3D left, Point3D right) => !(left == right);
}

/// <summary>
/// Represents a displacement in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Vector3DConverter))]
public partial struct Vector3D : IEquatable<Vector3D>, IFormattable
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

/// <summary>
/// Represents a 3-D size structure.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Size3DConverter))]
public partial struct Size3D : IEquatable<Size3D>, IFormattable
{
    public static Size3D Empty => new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, true);

    public Size3D(double x, double y, double z)
    {
        ValidateSize(x, nameof(x));
        ValidateSize(y, nameof(y));
        ValidateSize(z, nameof(z));
        _x = x;
        _y = y;
        _z = z;
    }

    public double X
    {
        get => _x;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _x = value; }
    }

    public double Y
    {
        get => _y;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _y = value; }
    }

    public double Z
    {
        get => _z;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _z = value; }
    }

    public readonly bool IsEmpty => _x < 0.0;

    public bool Equals(Size3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Size3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Size3D left, Size3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    public static bool operator !=(Size3D left, Size3D right) => !(left == right);
}

/// <summary>
/// Represents an axis-aligned bounding box in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Rect3DConverter))]
public partial struct Rect3D : IEquatable<Rect3D>, IFormattable
{
    public static Rect3D Empty => new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        true);

    public Rect3D(double x, double y, double z, double sizeX, double sizeY, double sizeZ)
    {
        ValidateSize(sizeX, nameof(sizeX));
        ValidateSize(sizeY, nameof(sizeY));
        ValidateSize(sizeZ, nameof(sizeZ));
        _x = x;
        _y = y;
        _z = z;
        _sizeX = sizeX;
        _sizeY = sizeY;
        _sizeZ = sizeZ;
    }

    public Rect3D(Point3D location, Size3D size)
        : this(location.X, location.Y, location.Z, size.X, size.Y, size.Z) { }

    public double X
    {
        get => _x;
        set { VerifyNotEmpty(); _x = value; }
    }

    public double Y
    {
        get => _y;
        set { VerifyNotEmpty(); _y = value; }
    }

    public double Z
    {
        get => _z;
        set { VerifyNotEmpty(); _z = value; }
    }

    public double SizeX
    {
        get => _sizeX;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeX = value; }
    }

    public double SizeY
    {
        get => _sizeY;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeY = value; }
    }

    public double SizeZ
    {
        get => _sizeZ;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeZ = value; }
    }

    public Point3D Location
    {
        get => new(X, Y, Z);
        set { VerifyNotEmpty(); _x = value.X; _y = value.Y; _z = value.Z; }
    }

    public Size3D Size
    {
        get => IsEmpty ? Size3D.Empty : new Size3D(SizeX, SizeY, SizeZ);
        set
        {
            VerifyNotEmpty();
            if (value.IsEmpty)
            {
                throw new ArgumentException("Size cannot be empty.", nameof(value));
            }

            _sizeX = value.X;
            _sizeY = value.Y;
            _sizeZ = value.Z;
        }
    }

    public readonly bool IsEmpty => _sizeX < 0.0;

    public bool Contains(Point3D point) =>
        point.X >= X && point.X <= X + SizeX &&
        point.Y >= Y && point.Y <= Y + SizeY &&
        point.Z >= Z && point.Z <= Z + SizeZ;

    public void Union(Rect3D rect)
    {
        this = Union(this, rect);
    }

    public bool Equals(Rect3D other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) &&
        SizeX.Equals(other.SizeX) && SizeY.Equals(other.SizeY) && SizeZ.Equals(other.SizeZ);
    public override bool Equals(object? obj) => obj is Rect3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, SizeX, SizeY, SizeZ);
    public static bool operator ==(Rect3D left, Rect3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z &&
        left.SizeX == right.SizeX && left.SizeY == right.SizeY && left.SizeZ == right.SizeZ;
    public static bool operator !=(Rect3D left, Rect3D right) => !(left == right);
}

/// <summary>
/// Represents a 3-D quaternion for rotation.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(QuaternionConverter))]
public partial struct Quaternion : IEquatable<Quaternion>, IFormattable
{
    public Quaternion(double x, double y, double z, double w)
    {
        _x = x;
        _y = y;
        _z = z;
        _w = w;
        _isNotDistinguishedIdentity = true;
    }

    public Quaternion(Vector3D axisOfRotation, double angleInDegrees)
    {
        double length = axisOfRotation.Length;
        if (length == 0.0)
        {
            throw new InvalidOperationException("The axis of rotation cannot be zero.");
        }

        double halfAngle = angleInDegrees * Math.PI / 360.0;
        double scale = Math.Sin(halfAngle) / length;
        _x = axisOfRotation.X * scale;
        _y = axisOfRotation.Y * scale;
        _z = axisOfRotation.Z * scale;
        _w = Math.Cos(halfAngle);
        _isNotDistinguishedIdentity = true;
    }

    public double X
    {
        get => _isNotDistinguishedIdentity ? _x : 0.0;
        set { EnsureNotDistinguishedIdentity(); _x = value; }
    }

    public double Y
    {
        get => _isNotDistinguishedIdentity ? _y : 0.0;
        set { EnsureNotDistinguishedIdentity(); _y = value; }
    }

    public double Z
    {
        get => _isNotDistinguishedIdentity ? _z : 0.0;
        set { EnsureNotDistinguishedIdentity(); _z = value; }
    }

    public double W
    {
        get => _isNotDistinguishedIdentity ? _w : 1.0;
        set { EnsureNotDistinguishedIdentity(); _w = value; }
    }

    public Vector3D Axis
    {
        get
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z);
            return length > 0.0 ? new Vector3D(X / length, Y / length, Z / length) : new Vector3D(0.0, 1.0, 0.0);
        }
    }

    public double Angle
    {
        get
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return length > 0.0
                ? 2.0 * Math.Acos(Math.Clamp(W / length, -1.0, 1.0)) * (180.0 / Math.PI)
                : 0.0;
        }
    }

    public bool IsNormalized { get { double n = X * X + Y * Y + Z * Z + W * W; return Math.Abs(n - 1.0) < 1e-10; } }
    public bool IsIdentity => X == 0 && Y == 0 && Z == 0 && W == 1;
    public static Quaternion Identity => default;

    public void Normalize()
    {
        double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        X /= n;
        Y /= n;
        Z /= n;
        W /= n;
    }

    public void Conjugate() { X = -X; Y = -Y; Z = -Z; }
    public void Invert() { Conjugate(); double n = X * X + Y * Y + Z * Z + W * W; X /= n; Y /= n; Z /= n; W /= n; }

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quaternion Slerp(Quaternion from, Quaternion to, double t) =>
        Slerp(from, to, t, useShortestPath: true);

    public static Quaternion Slerp(Quaternion from, Quaternion to, double t, bool useShortestPath)
    {
        double lengthFrom = Math.Sqrt(from.X * from.X + from.Y * from.Y + from.Z * from.Z + from.W * from.W);
        double lengthTo = Math.Sqrt(to.X * to.X + to.Y * to.Y + to.Z * to.Z + to.W * to.W);

        // Jalium's compact Quaternion representation has no distinguished-identity bit;
        // treat the all-zero default value as WPF treats its distinguished identity value.
        if (lengthFrom == 0.0)
        {
            from = Identity;
            lengthFrom = 1.0;
        }
        if (lengthTo == 0.0)
        {
            to = Identity;
            lengthTo = 1.0;
        }

        from = new(from.X / lengthFrom, from.Y / lengthFrom, from.Z / lengthFrom, from.W / lengthFrom);
        to = new(to.X / lengthTo, to.Y / lengthTo, to.Z / lengthTo, to.W / lengthTo);

        double cosOmega = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
        if (useShortestPath && cosOmega < 0.0)
        {
            cosOmega = -cosOmega;
            to = new(-to.X, -to.Y, -to.Z, -to.W);
        }

        cosOmega = Math.Clamp(cosOmega, -1.0, 1.0);

        double scaleFrom;
        double scaleTo;
        if (cosOmega > 1.0 - 1e-6)
        {
            scaleFrom = 1.0 - t;
            scaleTo = t;
        }
        else if (cosOmega < 1e-10 - 1.0)
        {
            // Nearly antipodal quaternions have infinitely many great-circle paths.
            // Match WPF by selecting a stable perpendicular quaternion.
            to = new(-from.Y, from.X, -from.W, from.Z);
            double theta = t * Math.PI;
            scaleFrom = Math.Cos(theta);
            scaleTo = Math.Sin(theta);
        }
        else
        {
            double omega = Math.Acos(cosOmega);
            double sinOmega = Math.Sqrt(1.0 - cosOmega * cosOmega);
            scaleFrom = Math.Sin((1.0 - t) * omega) / sinOmega;
            scaleTo = Math.Sin(t * omega) / sinOmega;
        }

        double lengthOut = lengthFrom * Math.Pow(lengthTo / lengthFrom, t);
        scaleFrom *= lengthOut;
        scaleTo *= lengthOut;
        return new(
            scaleFrom * from.X + scaleTo * to.X,
            scaleFrom * from.Y + scaleTo * to.Y,
            scaleFrom * from.Z + scaleTo * to.Z,
            scaleFrom * from.W + scaleTo * to.W);
    }

    public bool Equals(Quaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static bool operator ==(Quaternion left, Quaternion right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
    public static bool operator !=(Quaternion left, Quaternion right) => !(left == right);
}

/// <summary>
/// Represents a 4x4 matrix used for 3-D transformations.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Matrix3DConverter))]
public partial struct Matrix3D : IEquatable<Matrix3D>, IFormattable
{
    public double M11 { get => _isNotDistinguishedIdentity ? _m11 : 1.0; set { EnsureNotDistinguishedIdentity(); _m11 = value; } }
    public double M12 { get => _isNotDistinguishedIdentity ? _m12 : 0.0; set { EnsureNotDistinguishedIdentity(); _m12 = value; } }
    public double M13 { get => _isNotDistinguishedIdentity ? _m13 : 0.0; set { EnsureNotDistinguishedIdentity(); _m13 = value; } }
    public double M14 { get => _isNotDistinguishedIdentity ? _m14 : 0.0; set { EnsureNotDistinguishedIdentity(); _m14 = value; } }
    public double M21 { get => _isNotDistinguishedIdentity ? _m21 : 0.0; set { EnsureNotDistinguishedIdentity(); _m21 = value; } }
    public double M22 { get => _isNotDistinguishedIdentity ? _m22 : 1.0; set { EnsureNotDistinguishedIdentity(); _m22 = value; } }
    public double M23 { get => _isNotDistinguishedIdentity ? _m23 : 0.0; set { EnsureNotDistinguishedIdentity(); _m23 = value; } }
    public double M24 { get => _isNotDistinguishedIdentity ? _m24 : 0.0; set { EnsureNotDistinguishedIdentity(); _m24 = value; } }
    public double M31 { get => _isNotDistinguishedIdentity ? _m31 : 0.0; set { EnsureNotDistinguishedIdentity(); _m31 = value; } }
    public double M32 { get => _isNotDistinguishedIdentity ? _m32 : 0.0; set { EnsureNotDistinguishedIdentity(); _m32 = value; } }
    public double M33 { get => _isNotDistinguishedIdentity ? _m33 : 1.0; set { EnsureNotDistinguishedIdentity(); _m33 = value; } }
    public double M34 { get => _isNotDistinguishedIdentity ? _m34 : 0.0; set { EnsureNotDistinguishedIdentity(); _m34 = value; } }
    public double OffsetX { get => _isNotDistinguishedIdentity ? _offsetX : 0.0; set { EnsureNotDistinguishedIdentity(); _offsetX = value; } }
    public double OffsetY { get => _isNotDistinguishedIdentity ? _offsetY : 0.0; set { EnsureNotDistinguishedIdentity(); _offsetY = value; } }
    public double OffsetZ { get => _isNotDistinguishedIdentity ? _offsetZ : 0.0; set { EnsureNotDistinguishedIdentity(); _offsetZ = value; } }
    public double M44 { get => _isNotDistinguishedIdentity ? _m44 : 1.0; set { EnsureNotDistinguishedIdentity(); _m44 = value; } }

    public Matrix3D(double m11, double m12, double m13, double m14,
                    double m21, double m22, double m23, double m24,
                    double m31, double m32, double m33, double m34,
                    double offsetX, double offsetY, double offsetZ, double m44)
    {
        _m11 = m11; _m12 = m12; _m13 = m13; _m14 = m14;
        _m21 = m21; _m22 = m22; _m23 = m23; _m24 = m24;
        _m31 = m31; _m32 = m32; _m33 = m33; _m34 = m34;
        _offsetX = offsetX; _offsetY = offsetY; _offsetZ = offsetZ; _m44 = m44;
        _isNotDistinguishedIdentity = true;
    }

    public static Matrix3D Identity => default;
    public bool IsIdentity => M11 == 1 && M12 == 0 && M13 == 0 && M14 == 0 &&
                               M21 == 0 && M22 == 1 && M23 == 0 && M24 == 0 &&
                               M31 == 0 && M32 == 0 && M33 == 1 && M34 == 0 &&
                               OffsetX == 0 && OffsetY == 0 && OffsetZ == 0 && M44 == 1;
    public bool IsAffine => M14 == 0.0 && M24 == 0.0 && M34 == 0.0 && M44 == 1.0;
    public bool HasInverse => Math.Abs(Determinant) > 1e-15;
    public double Determinant =>
        M11 * (M22 * (M33 * M44 - M34 * OffsetZ) - M23 * (M32 * M44 - M34 * OffsetY) + M24 * (M32 * OffsetZ - M33 * OffsetY)) -
        M12 * (M21 * (M33 * M44 - M34 * OffsetZ) - M23 * (M31 * M44 - M34 * OffsetX) + M24 * (M31 * OffsetZ - M33 * OffsetX)) +
        M13 * (M21 * (M32 * M44 - M34 * OffsetY) - M22 * (M31 * M44 - M34 * OffsetX) + M24 * (M31 * OffsetY - M32 * OffsetX)) -
        M14 * (M21 * (M32 * OffsetZ - M33 * OffsetY) - M22 * (M31 * OffsetZ - M33 * OffsetX) + M23 * (M31 * OffsetY - M32 * OffsetX));

    public void Invert()
    {
        double det = Determinant;
        if (Math.Abs(det) <= 1e-15) throw new InvalidOperationException("Matrix is not invertible.");

        double invDet = 1.0 / det;

        // Cofactor expansion for 4x4 matrix inversion
        // Using row4 = (OffsetX, OffsetY, OffsetZ, M44)
        double a11 = M11, a12 = M12, a13 = M13, a14 = M14;
        double a21 = M21, a22 = M22, a23 = M23, a24 = M24;
        double a31 = M31, a32 = M32, a33 = M33, a34 = M34;
        double a41 = OffsetX, a42 = OffsetY, a43 = OffsetZ, a44 = M44;

        M11 = (a22 * (a33 * a44 - a34 * a43) - a23 * (a32 * a44 - a34 * a42) + a24 * (a32 * a43 - a33 * a42)) * invDet;
        M12 = -(a12 * (a33 * a44 - a34 * a43) - a13 * (a32 * a44 - a34 * a42) + a14 * (a32 * a43 - a33 * a42)) * invDet;
        M13 = (a12 * (a23 * a44 - a24 * a43) - a13 * (a22 * a44 - a24 * a42) + a14 * (a22 * a43 - a23 * a42)) * invDet;
        M14 = -(a12 * (a23 * a34 - a24 * a33) - a13 * (a22 * a34 - a24 * a32) + a14 * (a22 * a33 - a23 * a32)) * invDet;

        M21 = -(a21 * (a33 * a44 - a34 * a43) - a23 * (a31 * a44 - a34 * a41) + a24 * (a31 * a43 - a33 * a41)) * invDet;
        M22 = (a11 * (a33 * a44 - a34 * a43) - a13 * (a31 * a44 - a34 * a41) + a14 * (a31 * a43 - a33 * a41)) * invDet;
        M23 = -(a11 * (a23 * a44 - a24 * a43) - a13 * (a21 * a44 - a24 * a41) + a14 * (a21 * a43 - a23 * a41)) * invDet;
        M24 = (a11 * (a23 * a34 - a24 * a33) - a13 * (a21 * a34 - a24 * a31) + a14 * (a21 * a33 - a23 * a31)) * invDet;

        M31 = (a21 * (a32 * a44 - a34 * a42) - a22 * (a31 * a44 - a34 * a41) + a24 * (a31 * a42 - a32 * a41)) * invDet;
        M32 = -(a11 * (a32 * a44 - a34 * a42) - a12 * (a31 * a44 - a34 * a41) + a14 * (a31 * a42 - a32 * a41)) * invDet;
        M33 = (a11 * (a22 * a44 - a24 * a42) - a12 * (a21 * a44 - a24 * a41) + a14 * (a21 * a42 - a22 * a41)) * invDet;
        M34 = -(a11 * (a22 * a34 - a24 * a32) - a12 * (a21 * a34 - a24 * a31) + a14 * (a21 * a32 - a22 * a31)) * invDet;

        OffsetX = -(a21 * (a32 * a43 - a33 * a42) - a22 * (a31 * a43 - a33 * a41) + a23 * (a31 * a42 - a32 * a41)) * invDet;
        OffsetY = (a11 * (a32 * a43 - a33 * a42) - a12 * (a31 * a43 - a33 * a41) + a13 * (a31 * a42 - a32 * a41)) * invDet;
        OffsetZ = -(a11 * (a22 * a43 - a23 * a42) - a12 * (a21 * a43 - a23 * a41) + a13 * (a21 * a42 - a22 * a41)) * invDet;
        M44 = (a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31)) * invDet;
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
        if (w != 1.0) { x /= w; y /= w; z /= w; }
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
        M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14) &&
        M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24) &&
        M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34) &&
        OffsetX.Equals(other.OffsetX) && OffsetY.Equals(other.OffsetY) && OffsetZ.Equals(other.OffsetZ) && M44.Equals(other.M44);
    public override bool Equals(object? obj) => obj is Matrix3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M22, M33, M44, OffsetX, OffsetY, OffsetZ);
    public static bool operator ==(Matrix3D left, Matrix3D right) =>
        left.M11 == right.M11 && left.M12 == right.M12 && left.M13 == right.M13 && left.M14 == right.M14 &&
        left.M21 == right.M21 && left.M22 == right.M22 && left.M23 == right.M23 && left.M24 == right.M24 &&
        left.M31 == right.M31 && left.M32 == right.M32 && left.M33 == right.M33 && left.M34 == right.M34 &&
        left.OffsetX == right.OffsetX && left.OffsetY == right.OffsetY && left.OffsetZ == right.OffsetZ && left.M44 == right.M44;
    public static bool operator !=(Matrix3D left, Matrix3D right) => !(left == right);
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
