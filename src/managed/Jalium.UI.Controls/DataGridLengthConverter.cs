using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI.Controls;

/// <summary>
/// Converts <see cref="DataGridLength"/> values to and from markup strings and numbers.
/// </summary>
public class DataGridLengthConverter : TypeConverter
{
    private static readonly string[] s_unitStrings =
        ["auto", "px", "sizetocells", "sizetoheader", "*"];

    // This deliberately matches WPF's unit table split. Auto, px, and SizeToCells
    // are accepted only as standalone descriptive values; SizeToHeader and Star are
    // processed as suffixes.
    private const int DescriptiveUnitCount = 3;

    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return Type.GetTypeCode(sourceType) is TypeCode.String
            or TypeCode.Decimal
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Int16
            or TypeCode.Int32
            or TypeCode.Int64
            or TypeCode.UInt16
            or TypeCode.UInt32
            or TypeCode.UInt64
            or TypeCode.Byte;
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor);
    }

    /// <inheritdoc />
    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string source)
        {
            return ConvertFromString(source, culture);
        }

        if (value is not null)
        {
            double doubleValue = Convert.ToDouble(value, culture);
            DataGridLengthUnitType type;

            if (double.IsNaN(doubleValue))
            {
                doubleValue = 1d;
                type = DataGridLengthUnitType.Auto;
            }
            else
            {
                type = DataGridLengthUnitType.Pixel;
            }

            if (!double.IsInfinity(doubleValue))
            {
                return new DataGridLength(doubleValue, type);
            }
        }

        throw GetConvertFromException(value);
    }

    /// <inheritdoc />
    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is DataGridLength length)
        {
            if (destinationType == typeof(string))
            {
                return ConvertToString(length, culture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                ConstructorInfo constructor = typeof(DataGridLength).GetConstructor(
                    [typeof(double), typeof(DataGridLengthUnitType)])!;

                return new InstanceDescriptor(
                    constructor,
                    new object[] { length.Value, length.UnitType });
            }
        }

        throw GetConvertToException(value, destinationType);
    }

    internal static string ConvertToString(DataGridLength length, CultureInfo? culture)
    {
        return length.UnitType switch
        {
            DataGridLengthUnitType.Auto
                or DataGridLengthUnitType.SizeToCells
                or DataGridLengthUnitType.SizeToHeader => length.UnitType.ToString(),
            DataGridLengthUnitType.Star => IsOne(length.Value)
                ? "*"
                : $"{Convert.ToString(length.Value, culture)}*",
            _ => Convert.ToString(length.Value, culture)!,
        };
    }

    private static DataGridLength ConvertFromString(string source, CultureInfo? culture)
    {
        ReadOnlySpan<char> valueSpan = source.AsSpan().Trim();

        for (int index = 0; index < DescriptiveUnitCount; index++)
        {
            if (valueSpan.Equals(s_unitStrings[index], StringComparison.OrdinalIgnoreCase))
            {
                return new DataGridLength(1d, (DataGridLengthUnitType)index);
            }
        }

        double value = 0d;
        DataGridLengthUnitType unit = DataGridLengthUnitType.Pixel;
        int unitLength = 0;
        double unitFactor = 1d;

        for (int index = DescriptiveUnitCount; index < s_unitStrings.Length; index++)
        {
            string unitString = s_unitStrings[index];
            if (valueSpan.EndsWith(unitString, StringComparison.OrdinalIgnoreCase))
            {
                unitLength = unitString.Length;
                unit = (DataGridLengthUnitType)index;
                break;
            }
        }

        if (unitLength == 0)
        {
            if (TryGetPhysicalUnit(valueSpan, "in", 96d, out unitLength, out unitFactor)
                || TryGetPhysicalUnit(valueSpan, "cm", 96d / 2.54d, out unitLength, out unitFactor)
                || TryGetPhysicalUnit(valueSpan, "pt", 96d / 72d, out unitLength, out unitFactor))
            {
                unit = DataGridLengthUnitType.Pixel;
            }
        }

        if (valueSpan.Length == unitLength)
        {
            if (unit == DataGridLengthUnitType.Star)
            {
                value = 1d;
            }
        }
        else
        {
            ReadOnlySpan<char> numberSpan = valueSpan[..^unitLength];
            value = double.Parse(numberSpan, provider: culture) * unitFactor;
        }

        return new DataGridLength(value, unit);
    }

    private static bool TryGetPhysicalUnit(
        ReadOnlySpan<char> source,
        string name,
        double factor,
        out int unitLength,
        out double unitFactor)
    {
        if (source.EndsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            unitLength = name.Length;
            unitFactor = factor;
            return true;
        }

        unitLength = 0;
        unitFactor = 1d;
        return false;
    }

    private static bool IsOne(double value)
    {
        return Math.Abs(value - 1d) < 10d * 2.2204460492503131e-16;
    }
}
