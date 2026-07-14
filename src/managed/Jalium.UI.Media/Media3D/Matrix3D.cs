using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a 4x4 matrix used for 3-D transformations.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Matrix3DConverter))]
public partial struct Matrix3D : IEquatable<Matrix3D>, IFormattable
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

    // --- from Point3D.cs ---
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