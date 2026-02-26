namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Provides functionality for 3-D models.
/// </summary>
public abstract class Model3D
{
    /// <summary>
    /// Gets the bounding box of this model.
    /// </summary>
    public abstract Rect3D Bounds { get; }

    /// <summary>
    /// Gets or sets the Transform3D set on the model.
    /// </summary>
    public Transform3D? Transform { get; set; }
}

/// <summary>
/// Represents a 3-D model constructed using a Geometry3D and a Material.
/// </summary>
public sealed class GeometryModel3D : Model3D
{
    public GeometryModel3D() { }
    public GeometryModel3D(Geometry3D geometry, Material material) { Geometry = geometry; Material = material; }

    /// <summary>
    /// Gets or sets the Geometry3D that describes the shape of this model.
    /// </summary>
    public Geometry3D? Geometry { get; set; }

    /// <summary>
    /// Gets or sets the Material used to render the front of this model.
    /// </summary>
    public Material? Material { get; set; }

    /// <summary>
    /// Gets or sets the Material used to render the back of this model.
    /// </summary>
    public Material? BackMaterial { get; set; }

    /// <inheritdoc />
    public override Rect3D Bounds => Geometry?.Bounds ?? Rect3D.Empty;
}

/// <summary>
/// Enables using a collection of Model3D objects as a single Model3D.
/// </summary>
public sealed class Model3DGroup : Model3D
{
    public Model3DGroup() { Children = new(); }

    /// <summary>
    /// Gets the child Model3D objects in this group.
    /// </summary>
    public List<Model3D> Children { get; }

    /// <inheritdoc />
    public override Rect3D Bounds
    {
        get
        {
            if (Children.Count == 0) return Rect3D.Empty;
            var result = Children[0].Bounds;
            for (int i = 1; i < Children.Count; i++)
                result.Union(Children[i].Bounds);
            return result;
        }
    }
}
