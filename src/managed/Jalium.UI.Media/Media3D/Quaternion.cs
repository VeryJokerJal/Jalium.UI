using System.Globalization;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents a 3-D quaternion for rotation.
/// </summary>
[System.ComponentModel.TypeConverter(typeof(QuaternionConverter))]
public partial struct Quaternion : IEquatable<Quaternion>, IFormattable
{
    private double _x;
    private double _y;
    private double _z;
    private double _w;
    private bool _isNotDistinguishedIdentity;

    private void EnsureNotDistinguishedIdentity()
    {
        if (_isNotDistinguishedIdentity)
        {
            return;
        }

        _x = 0.0;
        _y = 0.0;
        _z = 0.0;
        _w = 1.0;
        _isNotDistinguishedIdentity = true;
    }

    public static Quaternion Add(Quaternion left, Quaternion right) => left + right;

    public static Quaternion Subtract(Quaternion left, Quaternion right) => left - right;

    public static Quaternion Multiply(Quaternion left, Quaternion right) => left * right;

    public static bool Equals(Quaternion quaternion1, Quaternion quaternion2) => quaternion1.Equals(quaternion2);

    public static Quaternion Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.Equals(source.Trim(), "Identity", StringComparison.OrdinalIgnoreCase))
        {
            return Identity;
        }

        double[] values = Media3DValueFormatter.ParseNumbers(source, 4);
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    public override string ToString() =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(null, null, X, Y, Z, W);

    public string ToString(IFormatProvider? provider) =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(null, provider, X, Y, Z, W);

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        IsIdentity ? "Identity" : Media3DValueFormatter.Format(format, formatProvider, X, Y, Z, W);

    public static Quaternion operator +(Quaternion left, Quaternion right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    public static Quaternion operator -(Quaternion left, Quaternion right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    // --- from Point3D.cs ---
    public Quaternion(double x, double y, double z, double w)
    {
        _x = x;
        _y = y;
        _z = z;
        _w = w;
        _isNotDistinguishedIdentity = true;
    }

    public Quaternion(Vector3D axisOfRotation, double angleInDegrees)
    {
        double length = axisOfRotation.Length;
        if (length == 0.0)
        {
            throw new InvalidOperationException("The axis of rotation cannot be zero.");
        }

        double halfAngle = angleInDegrees * Math.PI / 360.0;
        double scale = Math.Sin(halfAngle) / length;
        _x = axisOfRotation.X * scale;
        _y = axisOfRotation.Y * scale;
        _z = axisOfRotation.Z * scale;
        _w = Math.Cos(halfAngle);
        _isNotDistinguishedIdentity = true;
    }

    public double X
    {
        get => _isNotDistinguishedIdentity ? _x : 0.0;
        set { EnsureNotDistinguishedIdentity(); _x = value; }
    }

    public double Y
    {
        get => _isNotDistinguishedIdentity ? _y : 0.0;
        set { EnsureNotDistinguishedIdentity(); _y = value; }
    }

    public double Z
    {
        get => _isNotDistinguishedIdentity ? _z : 0.0;
        set { EnsureNotDistinguishedIdentity(); _z = value; }
    }

    public double W
    {
        get => _isNotDistinguishedIdentity ? _w : 1.0;
        set { EnsureNotDistinguishedIdentity(); _w = value; }
    }

    public Vector3D Axis
    {
        get
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z);
            return length > 0.0 ? new Vector3D(X / length, Y / length, Z / length) : new Vector3D(0.0, 1.0, 0.0);
        }
    }

    public double Angle
    {
        get
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return length > 0.0
                ? 2.0 * Math.Acos(Math.Clamp(W / length, -1.0, 1.0)) * (180.0 / Math.PI)
                : 0.0;
        }
    }

    public bool IsNormalized { get { double n = X * X + Y * Y + Z * Z + W * W; return Math.Abs(n - 1.0) < 1e-10; } }
    public bool IsIdentity => X == 0 && Y == 0 && Z == 0 && W == 1;
    public static Quaternion Identity => default;

    public void Normalize()
    {
        double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        X /= n;
        Y /= n;
        Z /= n;
        W /= n;
    }

    public void Conjugate() { X = -X; Y = -Y; Z = -Z; }
    public void Invert() { Conjugate(); double n = X * X + Y * Y + Z * Z + W * W; X /= n; Y /= n; Z /= n; W /= n; }

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quaternion Slerp(Quaternion from, Quaternion to, double t) =>
        Slerp(from, to, t, useShortestPath: true);

    public static Quaternion Slerp(Quaternion from, Quaternion to, double t, bool useShortestPath)
    {
        double lengthFrom = Math.Sqrt(from.X * from.X + from.Y * from.Y + from.Z * from.Z + from.W * from.W);
        double lengthTo = Math.Sqrt(to.X * to.X + to.Y * to.Y + to.Z * to.Z + to.W * to.W);

        // Jalium's compact Quaternion representation has no distinguished-identity bit;
        // treat the all-zero default value as WPF treats its distinguished identity value.
        if (lengthFrom == 0.0)
        {
            from = Identity;
            lengthFrom = 1.0;
        }
        if (lengthTo == 0.0)
        {
            to = Identity;
            lengthTo = 1.0;
        }

        from = new(from.X / lengthFrom, from.Y / lengthFrom, from.Z / lengthFrom, from.W / lengthFrom);
        to = new(to.X / lengthTo, to.Y / lengthTo, to.Z / lengthTo, to.W / lengthTo);

        double cosOmega = from.X * to.X + from.Y * to.Y + from.Z * to.Z + from.W * to.W;
        if (useShortestPath && cosOmega < 0.0)
        {
            cosOmega = -cosOmega;
            to = new(-to.X, -to.Y, -to.Z, -to.W);
        }

        cosOmega = Math.Clamp(cosOmega, -1.0, 1.0);

        double scaleFrom;
        double scaleTo;
        if (cosOmega > 1.0 - 1e-6)
        {
            scaleFrom = 1.0 - t;
            scaleTo = t;
        }
        else if (cosOmega < 1e-10 - 1.0)
        {
            // Nearly antipodal quaternions have infinitely many great-circle paths.
            // Match WPF by selecting a stable perpendicular quaternion.
            to = new(-from.Y, from.X, -from.W, from.Z);
            double theta = t * Math.PI;
            scaleFrom = Math.Cos(theta);
            scaleTo = Math.Sin(theta);
        }
        else
        {
            double omega = Math.Acos(cosOmega);
            double sinOmega = Math.Sqrt(1.0 - cosOmega * cosOmega);
            scaleFrom = Math.Sin((1.0 - t) * omega) / sinOmega;
            scaleTo = Math.Sin(t * omega) / sinOmega;
        }

        double lengthOut = lengthFrom * Math.Pow(lengthTo / lengthFrom, t);
        scaleFrom *= lengthOut;
        scaleTo *= lengthOut;
        return new(
            scaleFrom * from.X + scaleTo * to.X,
            scaleFrom * from.Y + scaleTo * to.Y,
            scaleFrom * from.Z + scaleTo * to.Z,
            scaleFrom * from.W + scaleTo * to.W);
    }

    public bool Equals(Quaternion other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static bool operator ==(Quaternion left, Quaternion right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
    public static bool operator !=(Quaternion left, Quaternion right) => !(left == right);
}