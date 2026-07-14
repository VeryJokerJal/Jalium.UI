using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents an axis-aligned bounding box in 3-D space.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Rect3DConverter))]
public partial struct Rect3D : IEquatable<Rect3D>, IFormattable
{
    private double _x;
    private double _y;
    private double _z;
    private double _sizeX;
    private double _sizeY;
    private double _sizeZ;

    private Rect3D(double x, double y, double z, double sizeX, double sizeY, double sizeZ, bool _)
    {
        _x = x;
        _y = y;
        _z = z;
        _sizeX = sizeX;
        _sizeY = sizeY;
        _sizeZ = sizeZ;
    }

    private static void ValidateSize(double value, string parameterName)
    {
        if (value < 0.0)
        {
            throw new ArgumentException("Size dimensions cannot be negative.", parameterName);
        }
    }

    private readonly void VerifyNotEmpty()
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException("Cannot modify an empty Rect3D.");
        }
    }

    public bool Contains(double x, double y, double z) => Contains(new Point3D(x, y, z));

    public bool Contains(Rect3D rect) =>
        !IsEmpty && !rect.IsEmpty &&
        X <= rect.X && Y <= rect.Y && Z <= rect.Z &&
        X + SizeX >= rect.X + rect.SizeX &&
        Y + SizeY >= rect.Y + rect.SizeY &&
        Z + SizeZ >= rect.Z + rect.SizeZ;

    public bool IntersectsWith(Rect3D rect) =>
        !IsEmpty && !rect.IsEmpty &&
        rect.X <= X + SizeX && rect.X + rect.SizeX >= X &&
        rect.Y <= Y + SizeY && rect.Y + rect.SizeY >= Y &&
        rect.Z <= Z + SizeZ && rect.Z + rect.SizeZ >= Z;

    public void Intersect(Rect3D rect) => this = Intersect(this, rect);

    public static Rect3D Intersect(Rect3D rect1, Rect3D rect2)
    {
        if (!rect1.IntersectsWith(rect2))
        {
            return Empty;
        }

        double x = Math.Max(rect1.X, rect2.X);
        double y = Math.Max(rect1.Y, rect2.Y);
        double z = Math.Max(rect1.Z, rect2.Z);
        double maxX = Math.Min(rect1.X + rect1.SizeX, rect2.X + rect2.SizeX);
        double maxY = Math.Min(rect1.Y + rect1.SizeY, rect2.Y + rect2.SizeY);
        double maxZ = Math.Min(rect1.Z + rect1.SizeZ, rect2.Z + rect2.SizeZ);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public static Rect3D Union(Rect3D rect1, Rect3D rect2)
    {
        if (rect1.IsEmpty)
        {
            return rect2;
        }

        if (rect2.IsEmpty)
        {
            return rect1;
        }

        double x = Math.Min(rect1.X, rect2.X);
        double y = Math.Min(rect1.Y, rect2.Y);
        double z = Math.Min(rect1.Z, rect2.Z);
        double maxX = Math.Max(rect1.X + rect1.SizeX, rect2.X + rect2.SizeX);
        double maxY = Math.Max(rect1.Y + rect1.SizeY, rect2.Y + rect2.SizeY);
        double maxZ = Math.Max(rect1.Z + rect1.SizeZ, rect2.Z + rect2.SizeZ);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public void Union(Point3D point) => this = Union(this, point);

    public static Rect3D Union(Rect3D rect, Point3D point)
    {
        if (rect.IsEmpty)
        {
            return new Rect3D(point, new Size3D(0.0, 0.0, 0.0));
        }

        double x = Math.Min(rect.X, point.X);
        double y = Math.Min(rect.Y, point.Y);
        double z = Math.Min(rect.Z, point.Z);
        double maxX = Math.Max(rect.X + rect.SizeX, point.X);
        double maxY = Math.Max(rect.Y + rect.SizeY, point.Y);
        double maxZ = Math.Max(rect.Z + rect.SizeZ, point.Z);
        return new Rect3D(x, y, z, maxX - x, maxY - y, maxZ - z);
    }

    public void Offset(Vector3D offsetVector) => Offset(offsetVector.X, offsetVector.Y, offsetVector.Z);

    public void Offset(double offsetX, double offsetY, double offsetZ)
    {
        VerifyNotEmpty();
        _x += offsetX;
        _y += offsetY;
        _z += offsetZ;
    }

    public static Rect3D Offset(Rect3D rect, Vector3D offsetVector)
    {
        rect.Offset(offsetVector);
        return rect;
    }

    public static Rect3D Offset(Rect3D rect, double offsetX, double offsetY, double offsetZ)
    {
        rect.Offset(offsetX, offsetY, offsetZ);
        return rect;
    }

    public static bool Equals(Rect3D rect1, Rect3D rect2) => rect1.Equals(rect2);

    public static Rect3D Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Empty", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 6);
        return new Rect3D(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    public override string ToString() =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, null, X, Y, Z, SizeX, SizeY, SizeZ);

    public string ToString(IFormatProvider? provider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, provider, X, Y, Z, SizeX, SizeY, SizeZ);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z, SizeX, SizeY, SizeZ);

    // --- from Point3D.cs ---
    public static Rect3D Empty => new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        true);

    public Rect3D(double x, double y, double z, double sizeX, double sizeY, double sizeZ)
    {
        ValidateSize(sizeX, nameof(sizeX));
        ValidateSize(sizeY, nameof(sizeY));
        ValidateSize(sizeZ, nameof(sizeZ));
        _x = x;
        _y = y;
        _z = z;
        _sizeX = sizeX;
        _sizeY = sizeY;
        _sizeZ = sizeZ;
    }

    public Rect3D(Point3D location, Size3D size)
        : this(location.X, location.Y, location.Z, size.X, size.Y, size.Z) { }

    public double X
    {
        get => _x;
        set { VerifyNotEmpty(); _x = value; }
    }

    public double Y
    {
        get => _y;
        set { VerifyNotEmpty(); _y = value; }
    }

    public double Z
    {
        get => _z;
        set { VerifyNotEmpty(); _z = value; }
    }

    public double SizeX
    {
        get => _sizeX;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeX = value; }
    }

    public double SizeY
    {
        get => _sizeY;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeY = value; }
    }

    public double SizeZ
    {
        get => _sizeZ;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _sizeZ = value; }
    }

    public Point3D Location
    {
        get => new(X, Y, Z);
        set { VerifyNotEmpty(); _x = value.X; _y = value.Y; _z = value.Z; }
    }

    public Size3D Size
    {
        get => IsEmpty ? Size3D.Empty : new Size3D(SizeX, SizeY, SizeZ);
        set
        {
            VerifyNotEmpty();
            if (value.IsEmpty)
            {
                throw new ArgumentException("Size cannot be empty.", nameof(value));
            }

            _sizeX = value.X;
            _sizeY = value.Y;
            _sizeZ = value.Z;
        }
    }

    public readonly bool IsEmpty => _sizeX < 0.0;

    public bool Contains(Point3D point) =>
        point.X >= X && point.X <= X + SizeX &&
        point.Y >= Y && point.Y <= Y + SizeY &&
        point.Z >= Z && point.Z <= Z + SizeZ;

    public void Union(Rect3D rect)
    {
        this = Union(this, rect);
    }

    public bool Equals(Rect3D other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) &&
        SizeX.Equals(other.SizeX) && SizeY.Equals(other.SizeY) && SizeZ.Equals(other.SizeZ);
    public override bool Equals(object? obj) => obj is Rect3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, SizeX, SizeY, SizeZ);
    public static bool operator ==(Rect3D left, Rect3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z &&
        left.SizeX == right.SizeX && left.SizeY == right.SizeY && left.SizeZ == right.SizeZ;
    public static bool operator !=(Rect3D left, Rect3D right) => !(left == right);
}