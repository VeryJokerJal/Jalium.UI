namespace Jalium.UI.Media.Media3D;

/// <summary>Represents lighting applied to a 3-D scene.</summary>
public abstract class Light : Model3D
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(Light),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public override Rect3D Bounds => Rect3D.Empty;

    public new Light Clone() => (Light)base.Clone();

    public new Light CloneCurrentValue() => (Light)base.CloneCurrentValue();
}

/// <summary>Applies light uniformly in all directions.</summary>
public sealed class AmbientLight : Light
{
    public AmbientLight()
    {
    }

    public AmbientLight(Color color)
    {
        Color = color;
    }

    public new AmbientLight Clone() => (AmbientLight)base.Clone();

    public new AmbientLight CloneCurrentValue() => (AmbientLight)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new AmbientLight();
}

/// <summary>Applies light uniformly along a direction.</summary>
public sealed class DirectionalLight : Light
{
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(
            nameof(Direction),
            typeof(Vector3D),
            typeof(DirectionalLight),
            new PropertyMetadata(new Vector3D(0d, 0d, -1d)));

    public DirectionalLight()
    {
    }

    public DirectionalLight(Color color, Vector3D direction)
    {
        Color = color;
        Direction = direction;
    }

    public Vector3D Direction
    {
        get => (Vector3D)(GetValue(DirectionProperty) ?? new Vector3D(0d, 0d, -1d));
        set => SetValue(DirectionProperty, value);
    }

    public new DirectionalLight Clone() => (DirectionalLight)base.Clone();

    public new DirectionalLight CloneCurrentValue() => (DirectionalLight)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new DirectionalLight();
}

/// <summary>Base class for lights emitted from a position.</summary>
public abstract class PointLightBase : Light
{
    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(
            nameof(Position),
            typeof(Point3D),
            typeof(PointLightBase),
            new PropertyMetadata(new Point3D()));

    public static readonly DependencyProperty RangeProperty =
        DependencyProperty.Register(
            nameof(Range),
            typeof(double),
            typeof(PointLightBase),
            new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty ConstantAttenuationProperty =
        DependencyProperty.Register(
            nameof(ConstantAttenuation),
            typeof(double),
            typeof(PointLightBase),
            new PropertyMetadata(1d));

    public static readonly DependencyProperty LinearAttenuationProperty =
        DependencyProperty.Register(
            nameof(LinearAttenuation),
            typeof(double),
            typeof(PointLightBase),
            new PropertyMetadata(0d));

    public static readonly DependencyProperty QuadraticAttenuationProperty =
        DependencyProperty.Register(
            nameof(QuadraticAttenuation),
            typeof(double),
            typeof(PointLightBase),
            new PropertyMetadata(0d));

    public Point3D Position
    {
        get => (Point3D)(GetValue(PositionProperty) ?? default(Point3D));
        set => SetValue(PositionProperty, value);
    }

    public double Range
    {
        get => (double)(GetValue(RangeProperty) ?? double.PositiveInfinity);
        set => SetValue(RangeProperty, value);
    }

    public double ConstantAttenuation
    {
        get => (double)(GetValue(ConstantAttenuationProperty) ?? 1d);
        set => SetValue(ConstantAttenuationProperty, value);
    }

    public double LinearAttenuation
    {
        get => (double)(GetValue(LinearAttenuationProperty) ?? 0d);
        set => SetValue(LinearAttenuationProperty, value);
    }

    public double QuadraticAttenuation
    {
        get => (double)(GetValue(QuadraticAttenuationProperty) ?? 0d);
        set => SetValue(QuadraticAttenuationProperty, value);
    }
}

/// <summary>Applies light from a point in all directions.</summary>
public sealed class PointLight : PointLightBase
{
    public PointLight()
    {
    }

    public PointLight(Color color, Point3D position)
    {
        Color = color;
        Position = position;
    }

    public new PointLight Clone() => (PointLight)base.Clone();

    public new PointLight CloneCurrentValue() => (PointLight)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new PointLight();
}

/// <summary>Applies light in a cone from a point.</summary>
public sealed class SpotLight : PointLightBase
{
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.Register(
            nameof(Direction),
            typeof(Vector3D),
            typeof(SpotLight),
            new PropertyMetadata(new Vector3D(0d, 0d, -1d)));

    public static readonly DependencyProperty OuterConeAngleProperty =
        DependencyProperty.Register(
            nameof(OuterConeAngle),
            typeof(double),
            typeof(SpotLight),
            new PropertyMetadata(90d));

    public static readonly DependencyProperty InnerConeAngleProperty =
        DependencyProperty.Register(
            nameof(InnerConeAngle),
            typeof(double),
            typeof(SpotLight),
            new PropertyMetadata(180d));

    public SpotLight()
    {
    }

    public SpotLight(
        Color color,
        Point3D position,
        Vector3D direction,
        double outerConeAngle,
        double innerConeAngle)
    {
        Color = color;
        Position = position;
        Direction = direction;
        OuterConeAngle = outerConeAngle;
        InnerConeAngle = innerConeAngle;
    }

    public Vector3D Direction
    {
        get => (Vector3D)(GetValue(DirectionProperty) ?? new Vector3D(0d, 0d, -1d));
        set => SetValue(DirectionProperty, value);
    }

    public double OuterConeAngle
    {
        get => (double)(GetValue(OuterConeAngleProperty) ?? 90d);
        set => SetValue(OuterConeAngleProperty, value);
    }

    public double InnerConeAngle
    {
        get => (double)(GetValue(InnerConeAngleProperty) ?? 180d);
        set => SetValue(InnerConeAngleProperty, value);
    }

    public new SpotLight Clone() => (SpotLight)base.Clone();

    public new SpotLight CloneCurrentValue() => (SpotLight)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new SpotLight();
}
