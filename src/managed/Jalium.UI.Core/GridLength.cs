using System.ComponentModel;

namespace Jalium.UI;

/// <summary>
/// Specifies the unit represented by a <see cref="GridLength"/>.
/// </summary>
public enum GridUnitType
{
    Auto = 0,
    Pixel = 1,
    Star = 2,
}

/// <summary>
/// Represents a length that supports absolute, automatic, and weighted sizing.
/// </summary>
[TypeConverter(typeof(GridLengthConverter))]
public readonly struct GridLength : IEquatable<GridLength>
{
    private readonly double _unitValue;
    private readonly GridUnitType _unitType;

    public GridLength(double pixels)
        : this(pixels, GridUnitType.Pixel)
    {
    }

    public GridLength(double value, GridUnitType type)
    {
        if (double.IsNaN(value))
        {
            throw new ArgumentException("Value cannot be NaN.", nameof(value));
        }

        if (double.IsInfinity(value))
        {
            throw new ArgumentException("Value cannot be infinite.", nameof(value));
        }

        if (type is not GridUnitType.Auto and not GridUnitType.Pixel and not GridUnitType.Star)
        {
            throw new ArgumentException("The grid unit type is not valid.", nameof(type));
        }

        _unitValue = type == GridUnitType.Auto ? 0.0 : value;
        _unitType = type;
    }

    public static GridLength Auto { get; } = new(1.0, GridUnitType.Auto);

    /// <summary>
    /// Convenience value representing one star. This is an additive Jalium API.
    /// </summary>
    public static GridLength Star => new(1.0, GridUnitType.Star);

    public double Value => _unitType == GridUnitType.Auto ? 1.0 : _unitValue;

    public GridUnitType GridUnitType => _unitType;

    public bool IsAbsolute => _unitType == GridUnitType.Pixel;

    public bool IsAuto => _unitType == GridUnitType.Auto;

    public bool IsStar => _unitType == GridUnitType.Star;

    public static GridLength FromStar(double value) => new(value, GridUnitType.Star);

    public static GridLength FromPixels(double pixels) => new(pixels, GridUnitType.Pixel);

    public bool Equals(GridLength other) => this == other;

    public override bool Equals(object? obj) => obj is GridLength other && this == other;

    public override int GetHashCode() => (int)_unitValue + (int)_unitType;

    public override string ToString() => GridLengthConverter.ConvertToString(this, null);

    public static bool operator ==(GridLength left, GridLength right) =>
        left.GridUnitType == right.GridUnitType && left.Value == right.Value;

    public static bool operator !=(GridLength left, GridLength right) => !(left == right);
}
