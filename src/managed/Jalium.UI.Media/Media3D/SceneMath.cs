namespace Jalium.UI.Media.Media3D;

internal static class SceneMath
{
    internal static Rect3D TransformBounds(Rect3D bounds, Matrix3D matrix)
    {
        if (bounds.IsEmpty)
        {
            return Rect3D.Empty;
        }

        double x0 = bounds.X;
        double y0 = bounds.Y;
        double z0 = bounds.Z;
        double x1 = x0 + bounds.SizeX;
        double y1 = y0 + bounds.SizeY;
        double z1 = z0 + bounds.SizeZ;
        Span<Point3D> corners = stackalloc Point3D[8]
        {
            new(x0, y0, z0),
            new(x1, y0, z0),
            new(x0, y1, z0),
            new(x1, y1, z0),
            new(x0, y0, z1),
            new(x1, y0, z1),
            new(x0, y1, z1),
            new(x1, y1, z1),
        };

        Point3D first = matrix.Transform(corners[0]);
        double minX = first.X;
        double minY = first.Y;
        double minZ = first.Z;
        double maxX = first.X;
        double maxY = first.Y;
        double maxZ = first.Z;
        for (int index = 1; index < corners.Length; index++)
        {
            Point3D point = matrix.Transform(corners[index]);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        return new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
    }

    internal static Rect ProjectBounds(Rect3D bounds, Matrix3D projection)
    {
        if (bounds.IsEmpty)
        {
            return Rect.Empty;
        }

        double x0 = bounds.X;
        double y0 = bounds.Y;
        double z0 = bounds.Z;
        double x1 = x0 + bounds.SizeX;
        double y1 = y0 + bounds.SizeY;
        double z1 = z0 + bounds.SizeZ;
        Span<Point3D> corners = stackalloc Point3D[8]
        {
            new(x0, y0, z0),
            new(x1, y0, z0),
            new(x0, y1, z0),
            new(x1, y1, z0),
            new(x0, y0, z1),
            new(x1, y0, z1),
            new(x0, y1, z1),
            new(x1, y1, z1),
        };

        Point3D first = projection.Transform(corners[0]);
        double minX = first.X;
        double minY = first.Y;
        double maxX = first.X;
        double maxY = first.Y;
        for (int index = 1; index < corners.Length; index++)
        {
            Point3D point = projection.Transform(corners[index]);
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
