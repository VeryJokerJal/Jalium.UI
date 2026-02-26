namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Provides a parent class for all three-dimensional transformations.
/// </summary>
public abstract class Transform3D
{
    /// <summary>
    /// Gets the identity transform.
    /// </summary>
    public static Transform3D Identity { get; } = new MatrixTransform3D(Matrix3D.Identity);

    /// <summary>
    /// Gets the Matrix3D that represents the value of the transformation.
    /// </summary>
    public abstract Matrix3D Value { get; }

    /// <summary>
    /// Gets a value indicating whether this is an identity transform.
    /// </summary>
    public bool IsIdentity => Value.IsIdentity;

    /// <summary>
    /// Transforms the specified point.
    /// </summary>
    public Point3D Transform(Point3D point) => Value.Transform(point);

    /// <summary>
    /// Transforms the specified vector.
    /// </summary>
    public Vector3D Transform(Vector3D vector) => Value.Transform(vector);
}

/// <summary>
/// Creates a translation transformation along the direction of the 3-D vector.
/// </summary>
public sealed class TranslateTransform3D : Transform3D
{
    public TranslateTransform3D() { }
    public TranslateTransform3D(double offsetX, double offsetY, double offsetZ) { OffsetX = offsetX; OffsetY = offsetY; OffsetZ = offsetZ; }
    public TranslateTransform3D(Vector3D offset) { OffsetX = offset.X; OffsetY = offset.Y; OffsetZ = offset.Z; }

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetZ { get; set; }

    public override Matrix3D Value
    {
        get
        {
            var m = Matrix3D.Identity;
            m.OffsetX = OffsetX; m.OffsetY = OffsetY; m.OffsetZ = OffsetZ;
            return m;
        }
    }
}

/// <summary>
/// Scales an object in the three-dimensional x-y-z plane.
/// </summary>
public sealed class ScaleTransform3D : Transform3D
{
    public ScaleTransform3D() { ScaleX = 1; ScaleY = 1; ScaleZ = 1; }
    public ScaleTransform3D(double scaleX, double scaleY, double scaleZ) { ScaleX = scaleX; ScaleY = scaleY; ScaleZ = scaleZ; }
    public ScaleTransform3D(double scaleX, double scaleY, double scaleZ, double centerX, double centerY, double centerZ)
    {
        ScaleX = scaleX; ScaleY = scaleY; ScaleZ = scaleZ;
        CenterX = centerX; CenterY = centerY; CenterZ = centerZ;
    }

    public double ScaleX { get; set; }
    public double ScaleY { get; set; }
    public double ScaleZ { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }

    public override Matrix3D Value
    {
        get
        {
            var m = Matrix3D.Identity;
            m.M11 = ScaleX; m.M22 = ScaleY; m.M33 = ScaleZ;
            if (CenterX != 0 || CenterY != 0 || CenterZ != 0)
            {
                m.OffsetX = CenterX - ScaleX * CenterX;
                m.OffsetY = CenterY - ScaleY * CenterY;
                m.OffsetZ = CenterZ - ScaleZ * CenterZ;
            }
            return m;
        }
    }
}

/// <summary>
/// Specifies a rotation transformation in 3-D space.
/// </summary>
public sealed class RotateTransform3D : Transform3D
{
    public RotateTransform3D() { Rotation = Rotation3D.Identity; }
    public RotateTransform3D(Rotation3D rotation) { Rotation = rotation; }
    public RotateTransform3D(Rotation3D rotation, double centerX, double centerY, double centerZ)
    {
        Rotation = rotation; CenterX = centerX; CenterY = centerY; CenterZ = centerZ;
    }
    public RotateTransform3D(Rotation3D rotation, Point3D center)
    {
        Rotation = rotation; CenterX = center.X; CenterY = center.Y; CenterZ = center.Z;
    }

    public Rotation3D Rotation { get; set; }
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double CenterZ { get; set; }

    public override Matrix3D Value
    {
        get
        {
            var m = Rotation.Value;
            if (CenterX != 0 || CenterY != 0 || CenterZ != 0)
            {
                var pre = new TranslateTransform3D(-CenterX, -CenterY, -CenterZ);
                var post = new TranslateTransform3D(CenterX, CenterY, CenterZ);
                m = pre.Value * m * post.Value;
            }
            return m;
        }
    }
}

/// <summary>
/// Transforms a 3-D model using the specified Matrix3D.
/// </summary>
public sealed class MatrixTransform3D : Transform3D
{
    public MatrixTransform3D() { Matrix = Matrix3D.Identity; }
    public MatrixTransform3D(Matrix3D matrix) { Matrix = matrix; }

    public Matrix3D Matrix { get; set; }
    public override Matrix3D Value => Matrix;
}

/// <summary>
/// Represents a collection of Transform3D objects that combine into a single Transform3D.
/// </summary>
public sealed class Transform3DGroup : Transform3D
{
    public Transform3DGroup() { Children = new(); }

    public List<Transform3D> Children { get; }

    public override Matrix3D Value
    {
        get
        {
            var result = Matrix3D.Identity;
            foreach (var child in Children)
                result = result * child.Value;
            return result;
        }
    }
}

/// <summary>
/// Specifies a rotation used in 3-D space.
/// </summary>
public abstract class Rotation3D
{
    public static Rotation3D Identity { get; } = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
    public abstract Matrix3D Value { get; }
}

/// <summary>
/// Represents a 3-D rotation of a specified angle about a specified axis.
/// </summary>
public sealed class AxisAngleRotation3D : Rotation3D
{
    public AxisAngleRotation3D() { Axis = new(0, 1, 0); }
    public AxisAngleRotation3D(Vector3D axis, double angle) { Axis = axis; Angle = angle; }

    public Vector3D Axis { get; set; }
    public double Angle { get; set; }

    public override Matrix3D Value
    {
        get
        {
            var q = new Quaternion(Axis, Angle);
            double xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
            double xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
            double wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
            return new Matrix3D(
                1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0,
                2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0,
                2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0,
                0, 0, 0, 1);
        }
    }
}

/// <summary>
/// Represents a 3-D rotation specified as a quaternion.
/// </summary>
public sealed class QuaternionRotation3D : Rotation3D
{
    public QuaternionRotation3D() { Quaternion = Quaternion.Identity; }
    public QuaternionRotation3D(Quaternion quaternion) { Quaternion = quaternion; }

    public Quaternion Quaternion { get; set; }

    public override Matrix3D Value
    {
        get
        {
            var q = Quaternion;
            double xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
            double xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
            double wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
            return new Matrix3D(
                1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0,
                2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0,
                2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0,
                0, 0, 0, 1);
        }
    }
}
