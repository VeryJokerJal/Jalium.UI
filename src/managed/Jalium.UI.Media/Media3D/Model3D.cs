using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>Provides functionality shared by 3-D models.</summary>
public abstract class Model3D : Animatable, IFormattable
{
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(
            nameof(Transform),
            typeof(Transform3D),
            typeof(Model3D),
            new PropertyMetadata(Transform3D.Identity));

    /// <summary>Gets the model's axis-aligned bounds in its parent coordinate space.</summary>
    public abstract Rect3D Bounds { get; }

    /// <summary>Gets or sets the transform applied to the model.</summary>
    public Transform3D? Transform
    {
        get => (Transform3D?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    public new Model3D Clone() => (Model3D)base.Clone();

    public new Model3D CloneCurrentValue() => (Model3D)base.CloneCurrentValue();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider) => GetType().FullName ?? GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? provider) =>
        ToString(provider);

    internal Rect3D TransformBounds(Rect3D bounds) =>
        SceneMath.TransformBounds(bounds, Transform?.Value ?? Matrix3D.Identity);
}

/// <summary>Represents a 3-D model constructed from geometry and material.</summary>
public sealed class GeometryModel3D : Model3D
{
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(
            nameof(Geometry),
            typeof(Geometry3D),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaterialProperty =
        DependencyProperty.Register(
            nameof(Material),
            typeof(Material),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty BackMaterialProperty =
        DependencyProperty.Register(
            nameof(BackMaterial),
            typeof(Material),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public GeometryModel3D()
    {
    }

    public GeometryModel3D(Geometry3D geometry, Material material)
    {
        Geometry = geometry;
        Material = material;
    }

    public Geometry3D? Geometry
    {
        get => (Geometry3D?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public Material? Material
    {
        get => (Material?)GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    public Material? BackMaterial
    {
        get => (Material?)GetValue(BackMaterialProperty);
        set => SetValue(BackMaterialProperty, value);
    }

    public override Rect3D Bounds =>
        TransformBounds(Geometry?.Bounds ?? Rect3D.Empty);

    public new GeometryModel3D Clone() => (GeometryModel3D)base.Clone();

    public new GeometryModel3D CloneCurrentValue() => (GeometryModel3D)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new GeometryModel3D();
}

/// <summary>Represents an ordered collection of 3-D models.</summary>
public sealed class Model3DCollection : FreezableCollection<Model3D>
{
    public Model3DCollection()
    {
    }

    public Model3DCollection(int capacity)
        : base(capacity)
    {
    }

    public Model3DCollection(IEnumerable<Model3D> collection)
        : base(collection)
    {
    }

    public new Model3DCollection Clone() => (Model3DCollection)base.Clone();

    public new Model3DCollection CloneCurrentValue() => (Model3DCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Model3DCollection();
}

/// <summary>Uses a collection of models as one model.</summary>
public sealed class Model3DGroup : Model3D
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(Model3DCollection),
            typeof(Model3DGroup),
            new PropertyMetadata(null));

    public Model3DGroup()
    {
        Children = new Model3DCollection();
    }

    public Model3DCollection Children
    {
        get => (Model3DCollection?)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public override Rect3D Bounds
    {
        get
        {
            Model3DCollection? children = (Model3DCollection?)GetValue(ChildrenProperty);
            if (children is null || children.Count == 0)
            {
                return Rect3D.Empty;
            }

            Rect3D result = Rect3D.Empty;
            foreach (Model3D child in children)
            {
                result.Union(child.Bounds);
            }

            return TransformBounds(result);
        }
    }

    public new Model3DGroup Clone() => (Model3DGroup)base.Clone();

    public new Model3DGroup CloneCurrentValue() => (Model3DGroup)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Model3DGroup();
}
