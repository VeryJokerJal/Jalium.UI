using System.Globalization;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents the lengths of elements within the DataGrid control.
/// </summary>
public readonly struct DataGridLength : IEquatable<DataGridLength>
{
    /// <summary>
    /// Gets a DataGridLength structure that represents the standard automatic sizing mode.
    /// </summary>
    public static DataGridLength Auto => new(1.0, DataGridLengthUnitType.Auto);

    /// <summary>
    /// Gets a DataGridLength structure that represents the cell-based automatic sizing mode.
    /// </summary>
    public static DataGridLength SizeToCells => new(1.0, DataGridLengthUnitType.SizeToCells);

    /// <summary>
    /// Gets a DataGridLength structure that represents the header-based automatic sizing mode.
    /// </summary>
    public static DataGridLength SizeToHeader => new(1.0, DataGridLengthUnitType.SizeToHeader);

    /// <summary>
    /// Initializes a new instance of the DataGridLength struct with a pixel value.
    /// </summary>
    public DataGridLength(double pixels)
    {
        Value = pixels;
        UnitType = DataGridLengthUnitType.Pixel;
        DesiredValue = pixels;
        DisplayValue = pixels;
    }

    /// <summary>
    /// Initializes a new instance of the DataGridLength struct.
    /// </summary>
    public DataGridLength(double value, DataGridLengthUnitType type)
    {
        Value = value;
        UnitType = type;
        DesiredValue = double.NaN;
        DisplayValue = double.NaN;
    }

    /// <summary>
    /// Initializes a new instance of the DataGridLength struct with desired and display values.
    /// </summary>
    public DataGridLength(double value, DataGridLengthUnitType type, double desiredValue, double displayValue)
    {
        Value = value;
        UnitType = type;
        DesiredValue = desiredValue;
        DisplayValue = displayValue;
    }

    /// <summary>
    /// Gets the absolute value of the DataGridLength in pixels.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Gets the type that is used to determine how the size of the element is calculated.
    /// </summary>
    public DataGridLengthUnitType UnitType { get; }

    /// <summary>
    /// Gets the calculated pixel value needed for the element.
    /// </summary>
    public double DesiredValue { get; }

    /// <summary>
    /// Gets the pixel value allocated for the size of the element.
    /// </summary>
    public double DisplayValue { get; }

    /// <summary>
    /// Gets a value that indicates whether this instance sizes elements based on a fixed pixel value.
    /// </summary>
    public bool IsAbsolute => UnitType == DataGridLengthUnitType.Pixel;

    /// <summary>
    /// Gets a value that indicates whether this instance automatically sizes elements based on both the content and the header.
    /// </summary>
    public bool IsAuto => UnitType == DataGridLengthUnitType.Auto;

    /// <summary>
    /// Gets a value that indicates whether this instance automatically sizes elements based on a weighted proportion of available space.
    /// </summary>
    public bool IsStar => UnitType == DataGridLengthUnitType.Star;

    /// <summary>
    /// Gets a value that indicates whether this instance sizes based on cell content.
    /// </summary>
    public bool IsSizeToCells => UnitType == DataGridLengthUnitType.SizeToCells;

    /// <summary>
    /// Gets a value that indicates whether this instance sizes based on header content.
    /// </summary>
    public bool IsSizeToHeader => UnitType == DataGridLengthUnitType.SizeToHeader;

    public bool Equals(DataGridLength other) =>
        Value == other.Value && UnitType == other.UnitType;

    public override bool Equals(object? obj) =>
        obj is DataGridLength other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Value, UnitType);

    public static bool operator ==(DataGridLength left, DataGridLength right) => left.Equals(right);
    public static bool operator !=(DataGridLength left, DataGridLength right) => !left.Equals(right);

    public override string ToString()
    {
        return UnitType switch
        {
            DataGridLengthUnitType.Auto => "Auto",
            DataGridLengthUnitType.Pixel => Value.ToString(CultureInfo.InvariantCulture),
            DataGridLengthUnitType.Star => Value == 1.0 ? "*" : $"{Value.ToString(CultureInfo.InvariantCulture)}*",
            DataGridLengthUnitType.SizeToCells => "SizeToCells",
            DataGridLengthUnitType.SizeToHeader => "SizeToHeader",
            _ => Value.ToString(CultureInfo.InvariantCulture)
        };
    }
}

/// <summary>
/// Describes the sizing mode of the DataGridLength object.
/// </summary>
public enum DataGridLengthUnitType
{
    /// <summary>
    /// Sizes based on both the content of cells and column headers.
    /// </summary>
    Auto,

    /// <summary>
    /// A fixed pixel width.
    /// </summary>
    Pixel,

    /// <summary>
    /// A weighted proportion of available space.
    /// </summary>
    Star,

    /// <summary>
    /// Sizes based on the content of the cells.
    /// </summary>
    SizeToCells,

    /// <summary>
    /// Sizes based on the content of the column header.
    /// </summary>
    SizeToHeader
}

/// <summary>
/// Converts instances of various types to and from instances of DataGridLength.
/// </summary>
public static class DataGridLengthConverter
{
    /// <summary>
    /// Converts a string to a DataGridLength.
    /// </summary>
    public static DataGridLength ConvertFrom(string value)
    {
        var str = value.Trim();
        if (str.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return DataGridLength.Auto;
        if (str.Equals("SizeToCells", StringComparison.OrdinalIgnoreCase))
            return DataGridLength.SizeToCells;
        if (str.Equals("SizeToHeader", StringComparison.OrdinalIgnoreCase))
            return DataGridLength.SizeToHeader;
        if (str.EndsWith('*'))
        {
            var numStr = str[..^1];
            double val = string.IsNullOrEmpty(numStr) ? 1.0 : double.Parse(numStr, CultureInfo.InvariantCulture);
            return new DataGridLength(val, DataGridLengthUnitType.Star);
        }
        return new DataGridLength(double.Parse(str, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Converts a double to a DataGridLength.
    /// </summary>
    public static DataGridLength ConvertFrom(double value) => new DataGridLength(value);
}
