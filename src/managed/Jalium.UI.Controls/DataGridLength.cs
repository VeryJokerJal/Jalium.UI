using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a DataGrid length that supports absolute, automatic, content-based,
/// and proportional sizing.
/// </summary>
[TypeConverter(typeof(DataGridLengthConverter))]
public struct DataGridLength : IEquatable<DataGridLength>
{
    private const double AutoValue = 1d;

    private static readonly DataGridLength s_auto =
        new(AutoValue, DataGridLengthUnitType.Auto, 0d, 0d);
    private static readonly DataGridLength s_sizeToCells =
        new(AutoValue, DataGridLengthUnitType.SizeToCells, 0d, 0d);
    private static readonly DataGridLength s_sizeToHeader =
        new(AutoValue, DataGridLengthUnitType.SizeToHeader, 0d, 0d);

    private readonly double _unitValue;
    private readonly DataGridLengthUnitType _unitType;
    private readonly double _desiredValue;
    private readonly double _displayValue;

    /// <summary>
    /// Initializes an absolute length in device-independent pixels.
    /// </summary>
    public DataGridLength(double pixels)
        : this(pixels, DataGridLengthUnitType.Pixel)
    {
    }

    /// <summary>
    /// Initializes a length with the specified value and unit type.
    /// </summary>
    public DataGridLength(double value, DataGridLengthUnitType type)
        : this(
            value,
            type,
            type == DataGridLengthUnitType.Pixel ? value : double.NaN,
            type == DataGridLengthUnitType.Pixel ? value : double.NaN)
    {
    }

    /// <summary>
    /// Initializes a length with its requested, desired, and displayed values.
    /// </summary>
    public DataGridLength(
        double value,
        DataGridLengthUnitType type,
        double desiredValue,
        double displayValue)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Value cannot be NaN or infinity.", nameof(value));
        }

        if (type is not DataGridLengthUnitType.Auto
            and not DataGridLengthUnitType.Pixel
            and not DataGridLengthUnitType.Star
            and not DataGridLengthUnitType.SizeToCells
            and not DataGridLengthUnitType.SizeToHeader)
        {
            throw new ArgumentException("The DataGridLength unit type is invalid.", nameof(type));
        }

        if (double.IsInfinity(desiredValue))
        {
            throw new ArgumentException("Value cannot be infinity.", nameof(desiredValue));
        }

        if (double.IsInfinity(displayValue))
        {
            throw new ArgumentException("Value cannot be infinity.", nameof(displayValue));
        }

        _unitValue = type == DataGridLengthUnitType.Auto ? AutoValue : value;
        _unitType = type;
        _desiredValue = desiredValue;
        _displayValue = displayValue;
    }

    /// <summary>
    /// Gets a value representing automatic sizing.
    /// </summary>
    public static DataGridLength Auto => s_auto;

    /// <summary>
    /// Gets a value representing sizing to cell content.
    /// </summary>
    public static DataGridLength SizeToCells => s_sizeToCells;

    /// <summary>
    /// Gets a value representing sizing to header content.
    /// </summary>
    public static DataGridLength SizeToHeader => s_sizeToHeader;

    /// <summary>
    /// Gets whether this length is absolute.
    /// </summary>
    public bool IsAbsolute => _unitType == DataGridLengthUnitType.Pixel;

    /// <summary>
    /// Gets whether this length uses automatic sizing.
    /// </summary>
    public bool IsAuto => _unitType == DataGridLengthUnitType.Auto;

    /// <summary>
    /// Gets whether this length is proportional.
    /// </summary>
    public bool IsStar => _unitType == DataGridLengthUnitType.Star;

    /// <summary>
    /// Gets whether this length sizes to cell content.
    /// </summary>
    public bool IsSizeToCells => _unitType == DataGridLengthUnitType.SizeToCells;

    /// <summary>
    /// Gets whether this length sizes to header content.
    /// </summary>
    public bool IsSizeToHeader => _unitType == DataGridLengthUnitType.SizeToHeader;

    /// <summary>
    /// Gets the requested unit value.
    /// </summary>
    public double Value => _unitType == DataGridLengthUnitType.Auto ? AutoValue : _unitValue;

    /// <summary>
    /// Gets the unit type.
    /// </summary>
    public DataGridLengthUnitType UnitType => _unitType;

    /// <summary>
    /// Gets the length desired by the measured content.
    /// </summary>
    public double DesiredValue => _desiredValue;

    /// <summary>
    /// Gets the length currently displayed.
    /// </summary>
    public double DisplayValue => _displayValue;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DataGridLength other && this == other;

    /// <inheritdoc />
    public bool Equals(DataGridLength other) => this == other;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return (int)_unitValue + (int)_unitType + (int)_desiredValue + (int)_displayValue;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return DataGridLengthConverter.ConvertToString(this, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Compares two lengths for value equality.
    /// </summary>
    public static bool operator ==(DataGridLength left, DataGridLength right)
    {
        return left.UnitType == right.UnitType
            && left.Value == right.Value
            && AreEqualIncludingNaN(left.DesiredValue, right.DesiredValue)
            && AreEqualIncludingNaN(left.DisplayValue, right.DisplayValue);
    }

    /// <summary>
    /// Compares two lengths for value inequality.
    /// </summary>
    public static bool operator !=(DataGridLength left, DataGridLength right) => !(left == right);

    /// <summary>
    /// Converts a pixel value to an absolute DataGrid length.
    /// </summary>
    public static implicit operator DataGridLength(double value) => new(value);

    private static bool AreEqualIncludingNaN(double left, double right)
    {
        return left == right || (double.IsNaN(left) && double.IsNaN(right));
    }
}
