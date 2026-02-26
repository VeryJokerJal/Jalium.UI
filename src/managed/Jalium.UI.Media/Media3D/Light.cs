namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Model3D object that represents lighting applied to a 3-D scene.
/// </summary>
public abstract class Light : Model3D
{
    /// <summary>
    /// Gets or sets the light's color.
    /// </summary>
    public Color Color { get; set; } = Color.FromRgb(255, 255, 255);

    /// <inheritdoc />
    public override Rect3D Bounds => Rect3D.Empty;
}

/// <summary>
/// Applies light uniformly in all directions.
/// </summary>
public sealed class AmbientLight : Light
{
    public AmbientLight() { }
    public AmbientLight(Color color) { Color = color; }
}

/// <summary>
/// Applies light uniformly along a direction.
/// </summary>
public sealed class DirectionalLight : Light
{
    public DirectionalLight() { Direction = new(0, 0, -1); }
    public DirectionalLight(Color color, Vector3D direction) { Color = color; Direction = direction; }

    /// <summary>
    /// Gets or sets the direction of the light.
    /// </summary>
    public Vector3D Direction { get; set; }
}

/// <summary>
/// Applies light from a point in all directions.
/// </summary>
public class PointLight : Light
{
    public PointLight() { }
    public PointLight(Color color, Point3D position) { Color = color; Position = position; }

    /// <summary>
    /// Gets or sets the position of the light.
    /// </summary>
    public Point3D Position { get; set; }

    /// <summary>
    /// Gets or sets the range of the light.
    /// </summary>
    public double Range { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets the constant attenuation factor.
    /// </summary>
    public double ConstantAttenuation { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the linear attenuation factor.
    /// </summary>
    public double LinearAttenuation { get; set; }

    /// <summary>
    /// Gets or sets the quadratic attenuation factor.
    /// </summary>
    public double QuadraticAttenuation { get; set; }
}

/// <summary>
/// Applies light in a cone shape from a point.
/// </summary>
public sealed class SpotLight : PointLight
{
    public SpotLight() { Direction = new(0, 0, -1); InnerConeAngle = 180; OuterConeAngle = 90; }
    public SpotLight(Color color, Point3D position, Vector3D direction, double outerConeAngle, double innerConeAngle)
    {
        Color = color; Position = position; Direction = direction;
        OuterConeAngle = outerConeAngle; InnerConeAngle = innerConeAngle;
    }

    /// <summary>
    /// Gets or sets the direction of the spot light.
    /// </summary>
    public Vector3D Direction { get; set; }

    /// <summary>
    /// Gets or sets the inner cone angle (in degrees).
    /// </summary>
    public double InnerConeAngle { get; set; }

    /// <summary>
    /// Gets or sets the outer cone angle (in degrees).
    /// </summary>
    public double OuterConeAngle { get; set; }
}
