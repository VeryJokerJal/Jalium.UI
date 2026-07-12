using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

public abstract class Transform3D : GeneralTransform3D
{
    private static readonly Transform3D s_identity = CreateIdentity();

    public static Transform3D Identity => s_identity;
    public abstract Matrix3D Value { get; }
    public abstract bool IsAffine { get; }
    public bool IsIdentity => Value.IsIdentity;

    public override GeneralTransform3D? Inverse
    {
        get
        {
            Matrix3D matrix = Value;
            if (!matrix.HasInverse)
                return null;
            matrix.Invert();
            return new MatrixTransform3D(matrix);
        }
    }

    public new Point3D Transform(Point3D point) => Value.Transform(point);
    public Vector3D Transform(Vector3D vector) => Value.Transform(vector);
    public Point4D Transform(Point4D point) => Value.Transform(point);

    public void Transform(Point3D[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Matrix3D matrix = Value;
        matrix.Transform(points);
    }

    public void Transform(Vector3D[] vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        Matrix3D matrix = Value;
        matrix.Transform(vectors);
    }

    public void Transform(Point4D[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Matrix3D matrix = Value;
        matrix.Transform(points);
    }

    public override bool TryTransform(Point3D inPoint, out Point3D result)
    {
        result = Value.Transform(inPoint);
        return true;
    }

    public override Rect3D TransformBounds(Rect3D rect)
    {
        if (rect.IsEmpty)
            return Rect3D.Empty;

        Matrix3D matrix = Value;
        double x0 = rect.X;
        double y0 = rect.Y;
        double z0 = rect.Z;
        double x1 = x0 + rect.SizeX;
        double y1 = y0 + rect.SizeY;
        double z1 = z0 + rect.SizeZ;

        Rect3D result = Rect3D.Empty;
        result.Union(matrix.Transform(new Point3D(x0, y0, z0)));
        result.Union(matrix.Transform(new Point3D(x1, y0, z0)));
        result.Union(matrix.Transform(new Point3D(x0, y1, z0)));
        result.Union(matrix.Transform(new Point3D(x1, y1, z0)));
        result.Union(matrix.Transform(new Point3D(x0, y0, z1)));
        result.Union(matrix.Transform(new Point3D(x1, y0, z1)));
        result.Union(matrix.Transform(new Point3D(x0, y1, z1)));
        result.Union(matrix.Transform(new Point3D(x1, y1, z1)));
        return result;
    }

    public new Transform3D Clone() => (Transform3D)base.Clone();
    public new Transform3D CloneCurrentValue() => (Transform3D)base.CloneCurrentValue();

    private static Transform3D CreateIdentity()
    {
        var identity = new MatrixTransform3D(Matrix3D.Identity);
        identity.Freeze();
        return identity;
    }
}

public abstract class AffineTransform3D : Transform3D
{
    public override bool IsAffine => true;
    public new AffineTransform3D Clone() => (AffineTransform3D)base.Clone();
    public new AffineTransform3D CloneCurrentValue() => (AffineTransform3D)base.CloneCurrentValue();
}

public sealed class TranslateTransform3D : AffineTransform3D
{
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.Register(
        nameof(OffsetX), typeof(double), typeof(TranslateTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(
        nameof(OffsetY), typeof(double), typeof(TranslateTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty OffsetZProperty = DependencyProperty.Register(
        nameof(OffsetZ), typeof(double), typeof(TranslateTransform3D), new PropertyMetadata(0d));

    public TranslateTransform3D() { }

    public TranslateTransform3D(Vector3D offset)
        : this(offset.X, offset.Y, offset.Z) { }

    public TranslateTransform3D(double offsetX, double offsetY, double offsetZ)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
    }

    public double OffsetX { get => (double)GetValue(OffsetXProperty)!; set => SetValue(OffsetXProperty, value); }
    public double OffsetY { get => (double)GetValue(OffsetYProperty)!; set => SetValue(OffsetYProperty, value); }
    public double OffsetZ { get => (double)GetValue(OffsetZProperty)!; set => SetValue(OffsetZProperty, value); }

    public override Matrix3D Value
    {
        get
        {
            var matrix = Matrix3D.Identity;
            matrix.Translate(new Vector3D(OffsetX, OffsetY, OffsetZ));
            return matrix;
        }
    }

    public new TranslateTransform3D Clone() => (TranslateTransform3D)base.Clone();
    public new TranslateTransform3D CloneCurrentValue() => (TranslateTransform3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new TranslateTransform3D();
}

public sealed class ScaleTransform3D : AffineTransform3D
{
    public static readonly DependencyProperty ScaleXProperty = DependencyProperty.Register(
        nameof(ScaleX), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(1d));
    public static readonly DependencyProperty ScaleYProperty = DependencyProperty.Register(
        nameof(ScaleY), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(1d));
    public static readonly DependencyProperty ScaleZProperty = DependencyProperty.Register(
        nameof(ScaleZ), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(1d));
    public static readonly DependencyProperty CenterXProperty = DependencyProperty.Register(
        nameof(CenterX), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty CenterYProperty = DependencyProperty.Register(
        nameof(CenterY), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty CenterZProperty = DependencyProperty.Register(
        nameof(CenterZ), typeof(double), typeof(ScaleTransform3D), new PropertyMetadata(0d));

    public ScaleTransform3D() { }

    public ScaleTransform3D(Vector3D scale)
        : this(scale.X, scale.Y, scale.Z) { }

    public ScaleTransform3D(double scaleX, double scaleY, double scaleZ)
    {
        ScaleX = scaleX;
        ScaleY = scaleY;
        ScaleZ = scaleZ;
    }

    public ScaleTransform3D(Vector3D scale, Point3D center)
        : this(scale.X, scale.Y, scale.Z, center.X, center.Y, center.Z) { }

    public ScaleTransform3D(
        double scaleX,
        double scaleY,
        double scaleZ,
        double centerX,
        double centerY,
        double centerZ)
        : this(scaleX, scaleY, scaleZ)
    {
        CenterX = centerX;
        CenterY = centerY;
        CenterZ = centerZ;
    }

    public double ScaleX { get => (double)GetValue(ScaleXProperty)!; set => SetValue(ScaleXProperty, value); }
    public double ScaleY { get => (double)GetValue(ScaleYProperty)!; set => SetValue(ScaleYProperty, value); }
    public double ScaleZ { get => (double)GetValue(ScaleZProperty)!; set => SetValue(ScaleZProperty, value); }
    public double CenterX { get => (double)GetValue(CenterXProperty)!; set => SetValue(CenterXProperty, value); }
    public double CenterY { get => (double)GetValue(CenterYProperty)!; set => SetValue(CenterYProperty, value); }
    public double CenterZ { get => (double)GetValue(CenterZProperty)!; set => SetValue(CenterZProperty, value); }

    public override Matrix3D Value
    {
        get
        {
            var matrix = Matrix3D.Identity;
            matrix.ScaleAt(
                new Vector3D(ScaleX, ScaleY, ScaleZ),
                new Point3D(CenterX, CenterY, CenterZ));
            return matrix;
        }
    }

    public new ScaleTransform3D Clone() => (ScaleTransform3D)base.Clone();
    public new ScaleTransform3D CloneCurrentValue() => (ScaleTransform3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ScaleTransform3D();
}

public sealed class RotateTransform3D : AffineTransform3D
{
    public static readonly DependencyProperty CenterXProperty = DependencyProperty.Register(
        nameof(CenterX), typeof(double), typeof(RotateTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty CenterYProperty = DependencyProperty.Register(
        nameof(CenterY), typeof(double), typeof(RotateTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty CenterZProperty = DependencyProperty.Register(
        nameof(CenterZ), typeof(double), typeof(RotateTransform3D), new PropertyMetadata(0d));
    public static readonly DependencyProperty RotationProperty = DependencyProperty.Register(
        nameof(Rotation), typeof(Rotation3D), typeof(RotateTransform3D), new PropertyMetadata(Rotation3D.Identity));

    public RotateTransform3D() { }
    public RotateTransform3D(Rotation3D rotation) => Rotation = rotation;

    public RotateTransform3D(Rotation3D rotation, Point3D center)
        : this(rotation, center.X, center.Y, center.Z) { }

    public RotateTransform3D(Rotation3D rotation, double centerX, double centerY, double centerZ)
    {
        Rotation = rotation;
        CenterX = centerX;
        CenterY = centerY;
        CenterZ = centerZ;
    }

    public double CenterX { get => (double)GetValue(CenterXProperty)!; set => SetValue(CenterXProperty, value); }
    public double CenterY { get => (double)GetValue(CenterYProperty)!; set => SetValue(CenterYProperty, value); }
    public double CenterZ { get => (double)GetValue(CenterZProperty)!; set => SetValue(CenterZProperty, value); }
    public Rotation3D Rotation
    {
        get => GetValue(RotationProperty) as Rotation3D ?? Rotation3D.Identity;
        set => SetValue(RotationProperty, value);
    }

    public override Matrix3D Value
    {
        get
        {
            Matrix3D matrix = Rotation.Value;
            Point3D center = new(CenterX, CenterY, CenterZ);
            if (center != default)
            {
                matrix.OffsetX = center.X - (center.X * matrix.M11 + center.Y * matrix.M21 + center.Z * matrix.M31);
                matrix.OffsetY = center.Y - (center.X * matrix.M12 + center.Y * matrix.M22 + center.Z * matrix.M32);
                matrix.OffsetZ = center.Z - (center.X * matrix.M13 + center.Y * matrix.M23 + center.Z * matrix.M33);
            }
            return matrix;
        }
    }

    public new RotateTransform3D Clone() => (RotateTransform3D)base.Clone();
    public new RotateTransform3D CloneCurrentValue() => (RotateTransform3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new RotateTransform3D();
}

public sealed class MatrixTransform3D : Transform3D
{
    public static readonly DependencyProperty MatrixProperty = DependencyProperty.Register(
        nameof(Matrix), typeof(Matrix3D), typeof(MatrixTransform3D), new PropertyMetadata(Matrix3D.Identity));

    public MatrixTransform3D() { }
    public MatrixTransform3D(Matrix3D matrix) => Matrix = matrix;

    public Matrix3D Matrix { get => (Matrix3D)GetValue(MatrixProperty)!; set => SetValue(MatrixProperty, value); }
    public override Matrix3D Value => Matrix;
    public override bool IsAffine => Matrix.IsAffine;

    public new MatrixTransform3D Clone() => (MatrixTransform3D)base.Clone();
    public new MatrixTransform3D CloneCurrentValue() => (MatrixTransform3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new MatrixTransform3D();
}

public sealed class Transform3DGroup : Transform3D
{
    public static readonly DependencyProperty ChildrenProperty = DependencyProperty.Register(
        nameof(Children), typeof(Transform3DCollection), typeof(Transform3DGroup), new PropertyMetadata(null));

    public Transform3DGroup() => Children = new Transform3DCollection();

    public Transform3DCollection Children
    {
        get => (Transform3DCollection)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public override Matrix3D Value
    {
        get
        {
            Matrix3D result = Matrix3D.Identity;
            foreach (Transform3D child in Children)
                result.Append(child.Value);
            return result;
        }
    }

    public override bool IsAffine => Children.All(child => child.IsAffine);
    public new Transform3DGroup Clone() => (Transform3DGroup)base.Clone();
    public new Transform3DGroup CloneCurrentValue() => (Transform3DGroup)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Transform3DGroup();
}

public abstract class Rotation3D : Animatable, IFormattable
{
    private static readonly Rotation3D s_identity = CreateIdentity();

    public static Rotation3D Identity => s_identity;

    // Kept as a useful Jalium extension; RotateTransform3D consumes the same normalized matrix.
    public abstract Matrix3D Value { get; }

    public new Rotation3D Clone() => (Rotation3D)base.Clone();
    public new Rotation3D CloneCurrentValue() => (Rotation3D)base.CloneCurrentValue();
    public override string ToString() => GetType().FullName ?? GetType().Name;
    public string ToString(IFormatProvider? provider) => ToString();
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    private static Rotation3D CreateIdentity()
    {
        var identity = new QuaternionRotation3D(Quaternion.Identity);
        identity.Freeze();
        return identity;
    }
}

public sealed class AxisAngleRotation3D : Rotation3D
{
    public static readonly DependencyProperty AxisProperty = DependencyProperty.Register(
        nameof(Axis), typeof(Vector3D), typeof(AxisAngleRotation3D), new PropertyMetadata(new Vector3D(0d, 1d, 0d)));
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(AxisAngleRotation3D), new PropertyMetadata(0d));

    public AxisAngleRotation3D() { }

    public AxisAngleRotation3D(Vector3D axis, double angle)
    {
        Axis = axis;
        Angle = angle;
    }

    public Vector3D Axis { get => (Vector3D)GetValue(AxisProperty)!; set => SetValue(AxisProperty, value); }
    public double Angle { get => (double)GetValue(AngleProperty)!; set => SetValue(AngleProperty, value); }

    public override Matrix3D Value
    {
        get
        {
            if (Axis.LengthSquared == 0d)
                return Matrix3D.Identity;
            var matrix = Matrix3D.Identity;
            matrix.Rotate(new Quaternion(Axis, Angle));
            return matrix;
        }
    }

    public new AxisAngleRotation3D Clone() => (AxisAngleRotation3D)base.Clone();
    public new AxisAngleRotation3D CloneCurrentValue() => (AxisAngleRotation3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new AxisAngleRotation3D();
}

public sealed class QuaternionRotation3D : Rotation3D
{
    public static readonly DependencyProperty QuaternionProperty = DependencyProperty.Register(
        nameof(Quaternion), typeof(Quaternion), typeof(QuaternionRotation3D), new PropertyMetadata(Quaternion.Identity));

    public QuaternionRotation3D() { }
    public QuaternionRotation3D(Quaternion quaternion) => Quaternion = quaternion;

    public Quaternion Quaternion
    {
        get => (Quaternion)GetValue(QuaternionProperty)!;
        set => SetValue(QuaternionProperty, value);
    }

    public override Matrix3D Value
    {
        get
        {
            var matrix = Matrix3D.Identity;
            matrix.Rotate(Quaternion);
            return matrix;
        }
    }

    public new QuaternionRotation3D Clone() => (QuaternionRotation3D)base.Clone();
    public new QuaternionRotation3D CloneCurrentValue() => (QuaternionRotation3D)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new QuaternionRotation3D();
}
