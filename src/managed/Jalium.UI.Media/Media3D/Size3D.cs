using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a 3-D size structure.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(Size3DConverter))]
public partial struct Size3D : IEquatable<Size3D>, IFormattable
{
    private double _x;
    private double _y;
    private double _z;

    private Size3D(double x, double y, double z, bool _)
    {
        _x = x;
        _y = y;
        _z = z;
    }

    internal static Size3D CreateUnchecked(double x, double y, double z) => new(x, y, z, true);

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
            throw new InvalidOperationException("Cannot modify an empty Size3D.");
        }
    }

    public static bool Equals(Size3D size1, Size3D size2) => size1.Equals(size2);

    public static Size3D Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Empty", StringComparison.OrdinalIgnoreCase))
        {
            return Empty;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 3);
        return new Size3D(values[0], values[1], values[2]);
    }

    public override string ToString() =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, null, X, Y, Z);

    public string ToString(IFormatProvider? provider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(null, provider, X, Y, Z);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsEmpty ? "Empty" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z);

    public static explicit operator Vector3D(Size3D size) => new(size.X, size.Y, size.Z);

    public static explicit operator Point3D(Size3D size) => new(size.X, size.Y, size.Z);

    // --- from Point3D.cs ---
    public static Size3D Empty => new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, true);

    public Size3D(double x, double y, double z)
    {
        ValidateSize(x, nameof(x));
        ValidateSize(y, nameof(y));
        ValidateSize(z, nameof(z));
        _x = x;
        _y = y;
        _z = z;
    }

    public double X
    {
        get => _x;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _x = value; }
    }

    public double Y
    {
        get => _y;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _y = value; }
    }

    public double Z
    {
        get => _z;
        set { VerifyNotEmpty(); ValidateSize(value, nameof(value)); _z = value; }
    }

    public readonly bool IsEmpty => _x < 0.0;

    public bool Equals(Size3D other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Size3D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Size3D left, Size3D right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    public static bool operator !=(Size3D left, Size3D right) => !(left == right);
}