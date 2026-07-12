using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>Abstract base class for materials.</summary>
public abstract class Material : Animatable, IFormattable
{
    public new Material Clone() => (Material)base.Clone();

    public new Material CloneCurrentValue() => (Material)base.CloneCurrentValue();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider) => GetType().FullName ?? GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ToString(formatProvider);
}

/// <summary>Applies a brush as a diffuse material to a 3-D model.</summary>
public sealed class DiffuseMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(DiffuseMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty AmbientColorProperty =
        DependencyProperty.Register(
            nameof(AmbientColor),
            typeof(Color),
            typeof(DiffuseMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(DiffuseMaterial),
            new PropertyMetadata(null));

    public DiffuseMaterial()
    {
    }

    public DiffuseMaterial(Brush brush)
    {
        Brush = brush;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Color AmbientColor
    {
        get => (Color)(GetValue(AmbientColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(AmbientColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public new DiffuseMaterial Clone() => (DiffuseMaterial)base.Clone();

    public new DiffuseMaterial CloneCurrentValue() => (DiffuseMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new DiffuseMaterial();
}

/// <summary>Applies a specular highlight to a 3-D model.</summary>
public sealed class SpecularMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(SpecularMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(SpecularMaterial),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SpecularPowerProperty =
        DependencyProperty.Register(
            nameof(SpecularPower),
            typeof(double),
            typeof(SpecularMaterial),
            new PropertyMetadata(40d));

    public SpecularMaterial()
    {
    }

    public SpecularMaterial(Brush brush, double specularPower)
    {
        Brush = brush;
        SpecularPower = specularPower;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public double SpecularPower
    {
        get => (double)(GetValue(SpecularPowerProperty) ?? 40d);
        set => SetValue(SpecularPowerProperty, value);
    }

    public new SpecularMaterial Clone() => (SpecularMaterial)base.Clone();

    public new SpecularMaterial CloneCurrentValue() => (SpecularMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new SpecularMaterial();
}

/// <summary>Applies a brush as if it were emitting light.</summary>
public sealed class EmissiveMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(EmissiveMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(EmissiveMaterial),
            new PropertyMetadata(null));

    public EmissiveMaterial()
    {
    }

    public EmissiveMaterial(Brush brush)
    {
        Brush = brush;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public new EmissiveMaterial Clone() => (EmissiveMaterial)base.Clone();

    public new EmissiveMaterial CloneCurrentValue() => (EmissiveMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new EmissiveMaterial();
}

/// <summary>Represents an ordered collection of materials.</summary>
public sealed class MaterialCollection : FreezableCollection<Material>
{
    public MaterialCollection()
    {
    }

    public MaterialCollection(int capacity)
        : base(capacity)
    {
    }

    public MaterialCollection(IEnumerable<Material> collection)
        : base(collection)
    {
    }

    public new MaterialCollection Clone() => (MaterialCollection)base.Clone();

    public new MaterialCollection CloneCurrentValue() => (MaterialCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MaterialCollection();
}

/// <summary>Represents materials that are applied together.</summary>
public sealed class MaterialGroup : Material
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(MaterialCollection),
            typeof(MaterialGroup),
            new PropertyMetadata(null));

    public MaterialGroup()
    {
        Children = new MaterialCollection();
    }

    public MaterialCollection Children
    {
        get => (MaterialCollection?)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public new MaterialGroup Clone() => (MaterialGroup)base.Clone();

    public new MaterialGroup CloneCurrentValue() => (MaterialGroup)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MaterialGroup();
}
