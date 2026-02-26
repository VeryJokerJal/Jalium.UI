namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Enables 2D content to be placed on a 3D object.
/// </summary>
public sealed class Viewport2DVisual3D : Visual3D
{
    /// <summary>
    /// Gets or sets the 3D geometry for this element.
    /// </summary>
    public Geometry3D? Geometry { get; set; }

    /// <summary>
    /// Gets or sets the Material to apply to the front of the 3D object.
    /// </summary>
    public Material? Material { get; set; }

    /// <summary>
    /// Gets or sets the 2D visual to place on the 3D object.
    /// </summary>
    public object? Visual { get; set; }

    /// <summary>
    /// Identifies the IsVisualHostMaterial attached property.
    /// </summary>
    public static bool GetIsVisualHostMaterial(Material material) => false;
    public static void SetIsVisualHostMaterial(Material material, bool value) { }
}

/// <summary>
/// Represents a 3D element that can contain child 3D elements and respond to input.
/// </summary>
public sealed class ContainerUIElement3D : UIElement3D
{
    private readonly List<Visual3D> _children = new();

    /// <summary>
    /// Gets the children of this element.
    /// </summary>
    public IList<Visual3D> Children => _children;
}

/// <summary>
/// Represents a 3D element that renders a Model3D and can respond to input.
/// </summary>
public sealed class ModelUIElement3D : UIElement3D
{
    /// <summary>
    /// Gets or sets the Model3D to render.
    /// </summary>
    public Model3D? Model { get; set; }
}

/// <summary>
/// Base class for 3D elements that can receive input events and focus.
/// </summary>
public abstract class UIElement3D : Visual3D
{
    /// <summary>
    /// Gets or sets a value indicating whether this element is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this element is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this element is focusable.
    /// </summary>
    public bool Focusable { get; set; }

    /// <summary>
    /// Gets a value indicating whether this element has keyboard focus.
    /// </summary>
    public bool IsKeyboardFocused { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the mouse is over this element.
    /// </summary>
    public bool IsMouseOver { get; internal set; }

    /// <summary>
    /// Invalidates the model of this element, causing a re-render.
    /// </summary>
    protected void InvalidateModel()
    {
    }
}

/// <summary>
/// Base class for hit test parameters in 3D space.
/// </summary>
public abstract class HitTestParameters3D
{
}

/// <summary>
/// Specifies hit test parameters for a ray in 3D space.
/// </summary>
public sealed class RayHitTestParameters : HitTestParameters3D
{
    /// <summary>
    /// Initializes a new instance with the specified origin and direction.
    /// </summary>
    public RayHitTestParameters(Point3D origin, Vector3D direction)
    {
        Origin = origin;
        Direction = direction;
    }

    /// <summary>
    /// Gets the origin of the ray.
    /// </summary>
    public Point3D Origin { get; }

    /// <summary>
    /// Gets the direction of the ray.
    /// </summary>
    public Vector3D Direction { get; }
}

/// <summary>
/// Represents the result of a ray hit test in 3D.
/// </summary>
public sealed class RayMeshGeometry3DHitTestResult
{
    /// <summary>
    /// Gets the Visual3D that was hit.
    /// </summary>
    public Visual3D? VisualHit { get; init; }

    /// <summary>
    /// Gets the Model3D that was hit.
    /// </summary>
    public Model3D? ModelHit { get; init; }

    /// <summary>
    /// Gets the MeshGeometry3D that was hit.
    /// </summary>
    public MeshGeometry3D? MeshHit { get; init; }

    /// <summary>
    /// Gets the point where the ray intersected the mesh.
    /// </summary>
    public Point3D PointHit { get; init; }

    /// <summary>
    /// Gets the distance along the ray from the origin to the hit point.
    /// </summary>
    public double DistanceToRayOrigin { get; init; }

    /// <summary>
    /// Gets the index of the first vertex of the triangle that was hit.
    /// </summary>
    public int VertexIndex1 { get; init; }

    /// <summary>
    /// Gets the index of the second vertex of the triangle that was hit.
    /// </summary>
    public int VertexIndex2 { get; init; }

    /// <summary>
    /// Gets the index of the third vertex of the triangle that was hit.
    /// </summary>
    public int VertexIndex3 { get; init; }

    /// <summary>
    /// Gets the weight of the first vertex in the barycentric coordinate.
    /// </summary>
    public double VertexWeight1 { get; init; }

    /// <summary>
    /// Gets the weight of the second vertex in the barycentric coordinate.
    /// </summary>
    public double VertexWeight2 { get; init; }

    /// <summary>
    /// Gets the weight of the third vertex in the barycentric coordinate.
    /// </summary>
    public double VertexWeight3 { get; init; }
}

/// <summary>
/// A general 3D to 3D transform.
/// </summary>
public abstract class GeneralTransform3D
{
    /// <summary>
    /// Attempts to transform the specified 3D point.
    /// </summary>
    public abstract bool TryTransform(Point3D inPoint, out Point3D result);

    /// <summary>
    /// Transforms the specified 3D point.
    /// </summary>
    public Point3D Transform(Point3D point)
    {
        if (!TryTransform(point, out var result))
            throw new InvalidOperationException("Transform failed.");
        return result;
    }

    /// <summary>
    /// Gets the inverse of this transform.
    /// </summary>
    public abstract GeneralTransform3D? Inverse { get; }
}

/// <summary>
/// Provides a transformation from 3D to 2D space.
/// </summary>
public sealed class GeneralTransform3DTo2D
{
    private readonly Matrix3D _matrix;

    internal GeneralTransform3DTo2D(Matrix3D matrix)
    {
        _matrix = matrix;
    }

    /// <summary>
    /// Attempts to transform the specified 3D point to a 2D point.
    /// </summary>
    public bool TryTransform(Point3D inPoint, out Point result)
    {
        var transformed = _matrix.Transform(inPoint);
        result = new Point(transformed.X, transformed.Y);
        return true;
    }

    /// <summary>
    /// Transforms the specified 3D point to a 2D point.
    /// </summary>
    public Point Transform(Point3D point)
    {
        TryTransform(point, out var result);
        return result;
    }
}
