using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Specifies what portion of the 3-D scene is rendered by a viewport.
/// </summary>
public abstract class Camera : Animatable, IFormattable
{
    /// <summary>Identifies the <see cref="Transform"/> dependency property.</summary>
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(
            nameof(Transform),
            typeof(Transform3D),
            typeof(Camera),
            new PropertyMetadata(Transform3D.Identity));

    /// <summary>Gets or sets the transform applied to the camera.</summary>
    public Transform3D? Transform
    {
        get => (Transform3D?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    /// <summary>Gets the view matrix for this camera.</summary>
    public abstract Matrix3D GetViewMatrix();

    /// <summary>Gets the projection matrix for this camera.</summary>
    public abstract Matrix3D GetProjectionMatrix(double aspectRatio);

    /// <summary>Creates a modifiable clone.</summary>
    public new Camera Clone() => (Camera)base.Clone();

    /// <summary>Creates a modifiable clone using current values.</summary>
    public new Camera CloneCurrentValue() => (Camera)base.CloneCurrentValue();

    /// <inheritdoc />
    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    /// <summary>Returns a culture-aware string representation.</summary>
    public string ToString(IFormatProvider? provider) => GetType().FullName ?? GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ToString(formatProvider);

    internal Matrix3D GetCombinedViewMatrix()
    {
        Matrix3D transform = Transform?.Value ?? Matrix3D.Identity;
        if (transform.HasInverse)
        {
            transform.Invert();
            return transform * GetViewMatrix();
        }

        return GetViewMatrix();
    }
}

/// <summary>Base class for cameras defined by a position and viewing direction.</summary>
public abstract class ProjectionCamera : Camera
{
    public static readonly DependencyProperty NearPlaneDistanceProperty =
        DependencyProperty.Register(
            nameof(NearPlaneDistance),
            typeof(double),
            typeof(ProjectionCamera),
            new PropertyMetadata(0.125d));

    public static readonly DependencyProperty FarPlaneDistanceProperty =
        DependencyProperty.Register(
            nameof(FarPlaneDistance),
            typeof(double),
            typeof(ProjectionCamera),
            new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(
            nameof(Position),
            typeof(Point3D),
            typeof(ProjectionCamera),
            new PropertyMetadata(new Point3D()));

    public static readonly DependencyProperty LookDirectionProperty =
        DependencyProperty.Register(
            nameof(LookDirection),
            typeof(Vector3D),
            typeof(ProjectionCamera),
            new PropertyMetadata(new Vector3D(0d, 0d, -1d)));

    public static readonly DependencyProperty UpDirectionProperty =
        DependencyProperty.Register(
            nameof(UpDirection),
            typeof(Vector3D),
            typeof(ProjectionCamera),
            new PropertyMetadata(new Vector3D(0d, 1d, 0d)));

    public double NearPlaneDistance
    {
        get => (double)(GetValue(NearPlaneDistanceProperty) ?? 0.125d);
        set => SetValue(NearPlaneDistanceProperty, value);
    }

    public double FarPlaneDistance
    {
        get => (double)(GetValue(FarPlaneDistanceProperty) ?? double.PositiveInfinity);
        set => SetValue(FarPlaneDistanceProperty, value);
    }

    public Point3D Position
    {
        get => (Point3D)(GetValue(PositionProperty) ?? default(Point3D));
        set => SetValue(PositionProperty, value);
    }

    public Vector3D LookDirection
    {
        get => (Vector3D)(GetValue(LookDirectionProperty) ?? new Vector3D(0d, 0d, -1d));
        set => SetValue(LookDirectionProperty, value);
    }

    public Vector3D UpDirection
    {
        get => (Vector3D)(GetValue(UpDirectionProperty) ?? new Vector3D(0d, 1d, 0d));
        set => SetValue(UpDirectionProperty, value);
    }

    public override Matrix3D GetViewMatrix() =>
        CreateLookAtMatrix(Position, LookDirection, UpDirection);

    internal static Matrix3D CreateLookAtMatrix(Point3D eye, Vector3D look, Vector3D up)
    {
        Vector3D zAxis = look;
        if (zAxis.LengthSquared == 0d)
        {
            zAxis = new Vector3D(0d, 0d, -1d);
        }

        zAxis.Normalize();
        zAxis.Negate();

        Vector3D xAxis = Vector3D.CrossProduct(up, zAxis);
        if (xAxis.LengthSquared == 0d)
        {
            Vector3D fallback = Math.Abs(zAxis.Y) < 0.99d
                ? new Vector3D(0d, 1d, 0d)
                : new Vector3D(1d, 0d, 0d);
            xAxis = Vector3D.CrossProduct(fallback, zAxis);
        }

        xAxis.Normalize();
        Vector3D yAxis = Vector3D.CrossProduct(zAxis, xAxis);
        return new Matrix3D(
            xAxis.X, yAxis.X, zAxis.X, 0d,
            xAxis.Y, yAxis.Y, zAxis.Y, 0d,
            xAxis.Z, yAxis.Z, zAxis.Z, 0d,
            -Vector3D.DotProduct(xAxis, new Vector3D(eye.X, eye.Y, eye.Z)),
            -Vector3D.DotProduct(yAxis, new Vector3D(eye.X, eye.Y, eye.Z)),
            -Vector3D.DotProduct(zAxis, new Vector3D(eye.X, eye.Y, eye.Z)),
            1d);
    }
}

/// <summary>Represents a camera that uses perspective projection.</summary>
public sealed class PerspectiveCamera : ProjectionCamera
{
    public static readonly DependencyProperty FieldOfViewProperty =
        DependencyProperty.Register(
            nameof(FieldOfView),
            typeof(double),
            typeof(PerspectiveCamera),
            new PropertyMetadata(45d));

    public PerspectiveCamera()
    {
    }

    public PerspectiveCamera(
        Point3D position,
        Vector3D lookDirection,
        Vector3D upDirection,
        double fieldOfView)
    {
        Position = position;
        LookDirection = lookDirection;
        UpDirection = upDirection;
        FieldOfView = fieldOfView;
    }

    public double FieldOfView
    {
        get => (double)(GetValue(FieldOfViewProperty) ?? 45d);
        set => SetValue(FieldOfViewProperty, value);
    }

    public override Matrix3D GetProjectionMatrix(double aspectRatio)
    {
        if (!(aspectRatio > 0d) || double.IsNaN(aspectRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        double near = NearPlaneDistance;
        double far = FarPlaneDistance;
        double fovRadians = FieldOfView * Math.PI / 180d;
        double yScale = 1d / Math.Tan(fovRadians / 2d);
        double xScale = yScale / aspectRatio;
        double depthScale;
        double depthOffset;
        if (double.IsPositiveInfinity(far))
        {
            depthScale = -1d;
            depthOffset = -near;
        }
        else
        {
            depthScale = far / (near - far);
            depthOffset = near * far / (near - far);
        }

        return new Matrix3D(
            xScale, 0d, 0d, 0d,
            0d, yScale, 0d, 0d,
            0d, 0d, depthScale, -1d,
            0d, 0d, depthOffset, 0d);
    }

    public new PerspectiveCamera Clone() => (PerspectiveCamera)base.Clone();

    public new PerspectiveCamera CloneCurrentValue() => (PerspectiveCamera)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new PerspectiveCamera();
}

/// <summary>Represents a camera that uses orthographic projection.</summary>
public sealed class OrthographicCamera : ProjectionCamera
{
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(
            nameof(Width),
            typeof(double),
            typeof(OrthographicCamera),
            new PropertyMetadata(2d));

    public OrthographicCamera()
    {
    }

    public OrthographicCamera(
        Point3D position,
        Vector3D lookDirection,
        Vector3D upDirection,
        double width)
    {
        Position = position;
        LookDirection = lookDirection;
        UpDirection = upDirection;
        Width = width;
    }

    public double Width
    {
        get => (double)(GetValue(WidthProperty) ?? 2d);
        set => SetValue(WidthProperty, value);
    }

    public override Matrix3D GetProjectionMatrix(double aspectRatio)
    {
        if (!(aspectRatio > 0d) || double.IsNaN(aspectRatio))
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        double height = Width / aspectRatio;
        double near = NearPlaneDistance;
        double far = FarPlaneDistance;
        double depthScale;
        double depthOffset;
        if (double.IsPositiveInfinity(far))
        {
            // Orthographic projection cannot encode an infinite interval exactly.
            // Preserve a stable, practically unbounded depth range.
            far = near + 1e12d;
        }

        depthScale = 1d / (near - far);
        depthOffset = near / (near - far);
        return new Matrix3D(
            2d / Width, 0d, 0d, 0d,
            0d, 2d / height, 0d, 0d,
            0d, 0d, depthScale, 0d,
            0d, 0d, depthOffset, 1d);
    }

    public new OrthographicCamera Clone() => (OrthographicCamera)base.Clone();

    public new OrthographicCamera CloneCurrentValue() => (OrthographicCamera)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new OrthographicCamera();
}

/// <summary>Specifies view and projection transformations directly.</summary>
public sealed class MatrixCamera : Camera
{
    public static readonly DependencyProperty ViewMatrixProperty =
        DependencyProperty.Register(
            nameof(ViewMatrix),
            typeof(Matrix3D),
            typeof(MatrixCamera),
            new PropertyMetadata(Matrix3D.Identity));

    public static readonly DependencyProperty ProjectionMatrixProperty =
        DependencyProperty.Register(
            nameof(ProjectionMatrix),
            typeof(Matrix3D),
            typeof(MatrixCamera),
            new PropertyMetadata(Matrix3D.Identity));

    public MatrixCamera()
    {
    }

    public MatrixCamera(Matrix3D viewMatrix, Matrix3D projectionMatrix)
    {
        ViewMatrix = viewMatrix;
        ProjectionMatrix = projectionMatrix;
    }

    public Matrix3D ViewMatrix
    {
        get => (Matrix3D)(GetValue(ViewMatrixProperty) ?? Matrix3D.Identity);
        set => SetValue(ViewMatrixProperty, value);
    }

    public Matrix3D ProjectionMatrix
    {
        get => (Matrix3D)(GetValue(ProjectionMatrixProperty) ?? Matrix3D.Identity);
        set => SetValue(ProjectionMatrixProperty, value);
    }

    public override Matrix3D GetViewMatrix() => ViewMatrix;

    public override Matrix3D GetProjectionMatrix(double aspectRatio) => ProjectionMatrix;

    public new MatrixCamera Clone() => (MatrixCamera)base.Clone();

    public new MatrixCamera CloneCurrentValue() => (MatrixCamera)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MatrixCamera();
}
