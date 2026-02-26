namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Provides services and properties that are common to visual 3-D objects.
/// </summary>
public abstract class Visual3D
{
    private readonly List<Visual3D> _children = new();

    /// <summary>
    /// Gets or sets the Transform3D for this visual.
    /// </summary>
    public Transform3D? Transform { get; set; }

    /// <summary>
    /// Gets the child visuals.
    /// </summary>
    protected internal IList<Visual3D> InternalChildren => _children;

    /// <summary>
    /// Gets the number of child visuals.
    /// </summary>
    public int Visual3DChildrenCount => _children.Count;

    /// <summary>
    /// Gets the child visual at the specified index.
    /// </summary>
    public Visual3D GetVisual3DChild(int index) => _children[index];
}

/// <summary>
/// Provides a Visual3D that renders 3-D content based on a Model3D.
/// </summary>
public sealed class ModelVisual3D : Visual3D
{
    /// <summary>
    /// Gets or sets the Model3D rendered by this visual.
    /// </summary>
    public Model3D? Content { get; set; }

    /// <summary>
    /// Gets the child Visual3D objects.
    /// </summary>
    public IList<Visual3D> Children => InternalChildren;
}

/// <summary>
/// Provides infrastructure for rendering 3-D content within the 2-D layout bounds of a WPF element.
/// </summary>
public sealed class Viewport3DVisual
{
    private readonly List<Visual3D> _children = new();

    /// <summary>
    /// Gets or sets the Camera used to project the 3-D contents to the 2-D surface.
    /// </summary>
    public Camera? Camera { get; set; }

    /// <summary>
    /// Gets or sets the clipping region of this Viewport3DVisual.
    /// </summary>
    public Rect Viewport { get; set; }

    /// <summary>
    /// Gets the collection of Visual3D children.
    /// </summary>
    public IList<Visual3D> Children => _children;
}
