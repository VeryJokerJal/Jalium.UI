namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Specifies what portion of the 3-D scene is rendered by the Viewport3D element.
/// </summary>
public abstract class Camera
{
    /// <summary>
    /// Gets or sets the Transform3D applied to the camera.
    /// </summary>
    public Transform3D? Transform { get; set; }

    /// <summary>
    /// Gets the view matrix for this camera.
    /// </summary>
    public abstract Matrix3D GetViewMatrix();

    /// <summary>
    /// Gets the projection matrix for this camera.
    /// </summary>
    public abstract Matrix3D GetProjectionMatrix(double aspectRatio);
}

/// <summary>
/// Represents a projection camera that uses a perspective projection.
/// </summary>
public sealed class PerspectiveCamera : Camera
{
    public PerspectiveCamera() { LookDirection = new(0, 0, -1); UpDirection = new(0, 1, 0); FieldOfView = 45.0; NearPlaneDistance = 0.125; FarPlaneDistance = double.PositiveInfinity; }
    public PerspectiveCamera(Point3D position, Vector3D lookDirection, Vector3D upDirection, double fieldOfView)
    {
        Position = position; LookDirection = lookDirection; UpDirection = upDirection; FieldOfView = fieldOfView;
        NearPlaneDistance = 0.125; FarPlaneDistance = double.PositiveInfinity;
    }

    public Point3D Position { get; set; }
    public Vector3D LookDirection { get; set; }
    public Vector3D UpDirection { get; set; }
    public double FieldOfView { get; set; }
    public double NearPlaneDistance { get; set; }
    public double FarPlaneDistance { get; set; }

    public override Matrix3D GetViewMatrix() => CreateLookAtMatrix(Position, LookDirection, UpDirection);
    public override Matrix3D GetProjectionMatrix(double aspectRatio) => CreatePerspectiveMatrix(FieldOfView, aspectRatio, NearPlaneDistance, Math.Min(FarPlaneDistance, 1e6));

    internal static Matrix3D CreateLookAtMatrix(Point3D eye, Vector3D look, Vector3D up)
    {
        var zAxis = look; zAxis.Normalize(); zAxis.Negate();
        var xAxis = Vector3D.CrossProduct(up, zAxis); xAxis.Normalize();
        var yAxis = Vector3D.CrossProduct(zAxis, xAxis);
        return new Matrix3D(
            xAxis.X, yAxis.X, zAxis.X, 0,
            xAxis.Y, yAxis.Y, zAxis.Y, 0,
            xAxis.Z, yAxis.Z, zAxis.Z, 0,
            -Vector3D.DotProduct(xAxis, new Vector3D(eye.X, eye.Y, eye.Z)),
            -Vector3D.DotProduct(yAxis, new Vector3D(eye.X, eye.Y, eye.Z)),
            -Vector3D.DotProduct(zAxis, new Vector3D(eye.X, eye.Y, eye.Z)), 1);
    }

    internal static Matrix3D CreatePerspectiveMatrix(double fovDegrees, double aspect, double near, double far)
    {
        double fovRad = fovDegrees * Math.PI / 180.0;
        double yScale = 1.0 / Math.Tan(fovRad / 2.0);
        double xScale = yScale / aspect;
        return new Matrix3D(
            xScale, 0, 0, 0,
            0, yScale, 0, 0,
            0, 0, far / (near - far), -1,
            0, 0, near * far / (near - far), 0);
    }
}

/// <summary>
/// Represents a projection camera that uses an orthographic projection.
/// </summary>
public sealed class OrthographicCamera : Camera
{
    public OrthographicCamera() { LookDirection = new(0, 0, -1); UpDirection = new(0, 1, 0); Width = 2.0; NearPlaneDistance = 0.125; FarPlaneDistance = double.PositiveInfinity; }
    public OrthographicCamera(Point3D position, Vector3D lookDirection, Vector3D upDirection, double width)
    {
        Position = position; LookDirection = lookDirection; UpDirection = upDirection; Width = width;
        NearPlaneDistance = 0.125; FarPlaneDistance = double.PositiveInfinity;
    }

    public Point3D Position { get; set; }
    public Vector3D LookDirection { get; set; }
    public Vector3D UpDirection { get; set; }
    public double Width { get; set; }
    public double NearPlaneDistance { get; set; }
    public double FarPlaneDistance { get; set; }

    public override Matrix3D GetViewMatrix() => PerspectiveCamera.CreateLookAtMatrix(Position, LookDirection, UpDirection);
    public override Matrix3D GetProjectionMatrix(double aspectRatio)
    {
        double height = Width / aspectRatio;
        double far = Math.Min(FarPlaneDistance, 1e6);
        return new Matrix3D(
            2.0 / Width, 0, 0, 0,
            0, 2.0 / height, 0, 0,
            0, 0, 1.0 / (NearPlaneDistance - far), 0,
            0, 0, NearPlaneDistance / (NearPlaneDistance - far), 1);
    }
}

/// <summary>
/// Specifies a view and projection transformation using a Matrix3D.
/// </summary>
public sealed class MatrixCamera : Camera
{
    public MatrixCamera() { ViewMatrix = Matrix3D.Identity; ProjectionMatrix = Matrix3D.Identity; }
    public MatrixCamera(Matrix3D viewMatrix, Matrix3D projectionMatrix) { ViewMatrix = viewMatrix; ProjectionMatrix = projectionMatrix; }

    public Matrix3D ViewMatrix { get; set; }
    public Matrix3D ProjectionMatrix { get; set; }

    public override Matrix3D GetViewMatrix() => ViewMatrix;
    public override Matrix3D GetProjectionMatrix(double aspectRatio) => ProjectionMatrix;
}
