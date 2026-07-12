namespace Jalium.UI.Media.Media3D;

/// <summary>Places a two-dimensional visual on a 3-D mesh.</summary>
public sealed class Viewport2DVisual3D : Visual3D
{
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(
            nameof(Geometry),
            typeof(Geometry3D),
            typeof(Viewport2DVisual3D),
            new PropertyMetadata(null, OnScenePropertyChanged));

    public static readonly DependencyProperty MaterialProperty =
        DependencyProperty.Register(
            nameof(Material),
            typeof(Material),
            typeof(Viewport2DVisual3D),
            new PropertyMetadata(null, OnScenePropertyChanged));

    public static readonly DependencyProperty VisualProperty =
        DependencyProperty.Register(
            nameof(Visual),
            typeof(Visual),
            typeof(Viewport2DVisual3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CacheModeProperty =
        DependencyProperty.Register(
            nameof(CacheMode),
            typeof(CacheMode),
            typeof(Viewport2DVisual3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsVisualHostMaterialProperty =
        DependencyProperty.RegisterAttached(
            "IsVisualHostMaterial",
            typeof(bool),
            typeof(Viewport2DVisual3D),
            new PropertyMetadata(false));

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

    public Visual? Visual
    {
        get => (Visual?)GetValue(VisualProperty);
        set => SetValue(VisualProperty, value);
    }

    public CacheMode? CacheMode
    {
        get => (CacheMode?)GetValue(CacheModeProperty);
        set => SetValue(CacheModeProperty, value);
    }

    public static bool GetIsVisualHostMaterial(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return (bool)(material.GetValue(IsVisualHostMaterialProperty) ?? false);
    }

    public static void SetIsVisualHostMaterial(Material material, bool value)
    {
        ArgumentNullException.ThrowIfNull(material);
        material.SetValue(IsVisualHostMaterialProperty, value);
    }

    private static void OnScenePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var visual = (Viewport2DVisual3D)dependencyObject;
        visual.Visual3DModel = visual.Geometry is null
            ? null
            : new GeometryModel3D(visual.Geometry, visual.Material!);
    }
}

/// <summary>Contains child 3-D elements and participates in routed input.</summary>
public sealed class ContainerUIElement3D : UIElement3D
{
    public Visual3DCollection Children => InternalChildren;

    protected override int Visual3DChildrenCount => Children.Count;

    protected override Visual3D GetVisual3DChild(int index) => Children[index];

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        base.OnCreateAutomationPeer();
}

/// <summary>Renders a Model3D and participates in routed input.</summary>
public sealed class ModelUIElement3D : UIElement3D
{
    public static readonly DependencyProperty ModelProperty =
        DependencyProperty.Register(
            nameof(Model),
            typeof(Model3D),
            typeof(ModelUIElement3D),
            new PropertyMetadata(null, OnModelChanged));

    public Model3D? Model
    {
        get => (Model3D?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        base.OnCreateAutomationPeer();

    private static void OnModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((ModelUIElement3D)dependencyObject).Visual3DModel = (Model3D?)e.NewValue;
    }
}

/// <summary>Base class for hit-test parameters in 3-D space.</summary>
public abstract class HitTestParameters3D
{
}

/// <summary>Specifies a ray in 3-D space.</summary>
public sealed class RayHitTestParameters : HitTestParameters3D
{
    public RayHitTestParameters(Point3D origin, Vector3D direction)
    {
        Origin = origin;
        Direction = direction;
    }

    public Point3D Origin { get; }

    public Vector3D Direction { get; }
}

/// <summary>Base result for ray-based 3-D hit testing.</summary>
public abstract class RayHitTestResult : HitTestResult
{
    internal RayHitTestResult(Viewport3DVisual host, Visual3D visualHit, Model3D modelHit)
        : base(host)
    {
        VisualHit = visualHit;
        ModelHit = modelHit;
    }

    public new Visual3D VisualHit { get; }

    public Model3D ModelHit { get; }

    public abstract Point3D PointHit { get; }

    public abstract double DistanceToRayOrigin { get; }
}

/// <summary>Describes a ray intersection with a MeshGeometry3D triangle.</summary>
public sealed class RayMeshGeometry3DHitTestResult : RayHitTestResult
{
    private readonly Point3D _pointHit;
    private readonly double _distanceToRayOrigin;

    internal RayMeshGeometry3DHitTestResult(
        Viewport3DVisual host,
        Visual3D visualHit,
        Model3D modelHit,
        MeshGeometry3D meshHit,
        Point3D pointHit,
        double distanceToRayOrigin,
        int vertexIndex1,
        int vertexIndex2,
        int vertexIndex3,
        Point barycentricCoordinate)
        : base(host, visualHit, modelHit)
    {
        MeshHit = meshHit;
        _pointHit = pointHit;
        _distanceToRayOrigin = distanceToRayOrigin;
        VertexIndex1 = vertexIndex1;
        VertexIndex2 = vertexIndex2;
        VertexIndex3 = vertexIndex3;
        VertexWeight2 = barycentricCoordinate.X;
        VertexWeight3 = barycentricCoordinate.Y;
        VertexWeight1 = 1d - VertexWeight2 - VertexWeight3;
    }

    public override Point3D PointHit => _pointHit;

    public override double DistanceToRayOrigin => _distanceToRayOrigin;

    public int VertexIndex1 { get; }

    public int VertexIndex2 { get; }

    public int VertexIndex3 { get; }

    public double VertexWeight1 { get; }

    public double VertexWeight2 { get; }

    public double VertexWeight3 { get; }

    public MeshGeometry3D MeshHit { get; }
}
