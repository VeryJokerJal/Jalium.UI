namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Classes derived from this abstract base class define 3-D geometric shapes.
/// </summary>
public abstract class Geometry3D
{
    /// <summary>
    /// Gets the bounding box of this geometry.
    /// </summary>
    public abstract Rect3D Bounds { get; }
}

/// <summary>
/// Triangle primitive for building 3-D shapes.
/// </summary>
public sealed class MeshGeometry3D : Geometry3D
{
    public MeshGeometry3D()
    {
        Positions = new();
        TriangleIndices = new();
        Normals = new();
        TextureCoordinates = new();
    }

    /// <summary>
    /// Gets or sets the collection of vertex positions.
    /// </summary>
    public Point3DCollection Positions { get; set; }

    /// <summary>
    /// Gets or sets the collection of triangle indices.
    /// </summary>
    public Int32Collection TriangleIndices { get; set; }

    /// <summary>
    /// Gets or sets the collection of normal vectors.
    /// </summary>
    public Vector3DCollection Normals { get; set; }

    /// <summary>
    /// Gets or sets the collection of texture coordinates.
    /// </summary>
    public PointCollection TextureCoordinates { get; set; }

    /// <inheritdoc />
    public override Rect3D Bounds
    {
        get
        {
            if (Positions.Count == 0) return Rect3D.Empty;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var p in Positions)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            return new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }
    }
}
