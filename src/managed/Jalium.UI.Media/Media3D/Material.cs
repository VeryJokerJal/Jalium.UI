namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Abstract base class for materials.
/// </summary>
public abstract class Material
{
}

/// <summary>
/// Applies a Brush as a diffuse material to a 3-D model.
/// </summary>
public sealed class DiffuseMaterial : Material
{
    public DiffuseMaterial() { }
    public DiffuseMaterial(Brush brush) { Brush = brush; }

    /// <summary>
    /// Gets or sets the Brush applied as a diffuse map.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the ambient color of the material.
    /// </summary>
    public Color AmbientColor { get; set; } = Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Gets or sets the color of the material.
    /// </summary>
    public Color Color { get; set; } = Color.FromRgb(255, 255, 255);
}

/// <summary>
/// Applies a specular highlight to a 3-D model.
/// </summary>
public sealed class SpecularMaterial : Material
{
    public SpecularMaterial() { }
    public SpecularMaterial(Brush brush, double specularPower) { Brush = brush; SpecularPower = specularPower; }

    /// <summary>
    /// Gets or sets the Brush applied as a specular map.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the specular power (shininess).
    /// </summary>
    public double SpecularPower { get; set; } = 40.0;

    /// <summary>
    /// Gets or sets the specular color.
    /// </summary>
    public Color Color { get; set; } = Color.FromRgb(255, 255, 255);
}

/// <summary>
/// Applies a Brush as if it were emitting light.
/// </summary>
public sealed class EmissiveMaterial : Material
{
    public EmissiveMaterial() { }
    public EmissiveMaterial(Brush brush) { Brush = brush; }

    /// <summary>
    /// Gets or sets the Brush applied as an emissive map.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the emissive color.
    /// </summary>
    public Color Color { get; set; } = Color.FromRgb(255, 255, 255);
}

/// <summary>
/// Represents a collection of Material objects that are treated as a single material.
/// </summary>
public sealed class MaterialGroup : Material
{
    public MaterialGroup() { Children = new(); }

    /// <summary>
    /// Gets the child materials.
    /// </summary>
    public List<Material> Children { get; }
}
