using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>Base class for 3-D geometric shapes.</summary>
public abstract class Geometry3D : Animatable
{
    public abstract Rect3D Bounds { get; }

    public new Geometry3D Clone() => (Geometry3D)base.Clone();

    public new Geometry3D CloneCurrentValue() => (Geometry3D)base.CloneCurrentValue();
}

/// <summary>Defines a set of indexed triangles.</summary>
public sealed class MeshGeometry3D : Geometry3D
{
    public static readonly DependencyProperty PositionsProperty =
        DependencyProperty.Register(
            nameof(Positions),
            typeof(Point3DCollection),
            typeof(MeshGeometry3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty NormalsProperty =
        DependencyProperty.Register(
            nameof(Normals),
            typeof(Vector3DCollection),
            typeof(MeshGeometry3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TextureCoordinatesProperty =
        DependencyProperty.Register(
            nameof(TextureCoordinates),
            typeof(PointCollection),
            typeof(MeshGeometry3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty TriangleIndicesProperty =
        DependencyProperty.Register(
            nameof(TriangleIndices),
            typeof(Int32Collection),
            typeof(MeshGeometry3D),
            new PropertyMetadata(null));

    public MeshGeometry3D()
    {
        Positions = new Point3DCollection();
        Normals = new Vector3DCollection();
        TextureCoordinates = new PointCollection();
        TriangleIndices = new Int32Collection();
    }

    public Point3DCollection Positions
    {
        get => (Point3DCollection?)GetValue(PositionsProperty)!;
        set => SetValue(PositionsProperty, value);
    }

    public Vector3DCollection Normals
    {
        get => (Vector3DCollection?)GetValue(NormalsProperty)!;
        set => SetValue(NormalsProperty, value);
    }

    public PointCollection TextureCoordinates
    {
        get => (PointCollection?)GetValue(TextureCoordinatesProperty)!;
        set => SetValue(TextureCoordinatesProperty, value);
    }

    public Int32Collection TriangleIndices
    {
        get => (Int32Collection?)GetValue(TriangleIndicesProperty)!;
        set => SetValue(TriangleIndicesProperty, value);
    }

    public override Rect3D Bounds
    {
        get
        {
            Point3DCollection? positions = (Point3DCollection?)GetValue(PositionsProperty);
            if (positions is null || positions.Count == 0)
            {
                return Rect3D.Empty;
            }

            Point3D first = positions[0];
            double minX = first.X;
            double minY = first.Y;
            double minZ = first.Z;
            double maxX = first.X;
            double maxY = first.Y;
            double maxZ = first.Z;
            for (int index = 1; index < positions.Count; index++)
            {
                Point3D point = positions[index];
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            return new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }
    }

    public new MeshGeometry3D Clone() => (MeshGeometry3D)base.Clone();

    public new MeshGeometry3D CloneCurrentValue() => (MeshGeometry3D)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MeshGeometry3D();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
    }

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CloneMutableCollections((MeshGeometry3D)sourceFreezable, useCurrentValue: false);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CloneMutableCollections((MeshGeometry3D)sourceFreezable, useCurrentValue: true);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        ClonePointCollection((MeshGeometry3D)sourceFreezable);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        ClonePointCollection((MeshGeometry3D)sourceFreezable);
    }

    private void CloneMutableCollections(MeshGeometry3D source, bool useCurrentValue)
    {
        if (source.Positions is not null)
        {
            Positions = useCurrentValue
                ? (Point3DCollection)source.Positions.CloneCurrentValue()
                : (Point3DCollection)source.Positions.Clone();
        }

        if (source.Normals is not null)
        {
            Normals = useCurrentValue
                ? (Vector3DCollection)source.Normals.CloneCurrentValue()
                : (Vector3DCollection)source.Normals.Clone();
        }

        if (source.TriangleIndices is not null)
        {
            TriangleIndices = useCurrentValue
                ? (Int32Collection)source.TriangleIndices.CloneCurrentValue()
                : (Int32Collection)source.TriangleIndices.Clone();
        }

        ClonePointCollection(source);
    }

    private void ClonePointCollection(MeshGeometry3D source)
    {
        TextureCoordinates = source.TextureCoordinates is null
            ? null!
            : new PointCollection(source.TextureCoordinates);
    }
}
