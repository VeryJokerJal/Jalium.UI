using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="GridLength"/> values to and from strings and numeric values.
/// </summary>
public class GridLengthConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? typeDescriptorContext, Type sourceType)
    {
        return Type.GetTypeCode(sourceType) is
            TypeCode.String or
            TypeCode.Decimal or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Int16 or
            TypeCode.Int32 or
            TypeCode.Int64 or
            TypeCode.UInt16 or
            TypeCode.UInt32 or
            TypeCode.UInt64;
    }

    public override bool CanConvertTo(ITypeDescriptorContext? typeDescriptorContext, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor);

    public override object ConvertFrom(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        cultureInfo ??= CultureInfo.CurrentCulture;

        if (source is string text)
        {
            return Parse(text, cultureInfo);
        }

        double numericValue = Convert.ToDouble(source, cultureInfo);
        return double.IsNaN(numericValue)
            ? GridLength.Auto
            : new GridLength(numericValue, GridUnitType.Pixel);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is not GridLength gridLength)
        {
            throw GetConvertToException(value, destinationType);
        }

        if (destinationType == typeof(string))
        {
            return ConvertToString(gridLength, cultureInfo);
        }

        if (destinationType == typeof(InstanceDescriptor))
        {
            ConstructorInfo constructor = typeof(GridLength).GetConstructor(
                [typeof(double), typeof(GridUnitType)])!;
            return new InstanceDescriptor(
                constructor,
                new object[] { gridLength.Value, gridLength.GridUnitType });
        }

        throw GetConvertToException(value, destinationType);
    }

    internal static string ConvertToString(GridLength value, CultureInfo? culture)
    {
        culture ??= CultureInfo.InvariantCulture;
        return value.GridUnitType switch
        {
            GridUnitType.Auto => "Auto",
            GridUnitType.Star when value.Value == 1.0 => "*",
            GridUnitType.Star => $"{Convert.ToString(value.Value, culture)}*",
            _ => Convert.ToString(value.Value, culture)!,
        };
    }

    internal static GridLength Parse(string source, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(source);
        string text = source.Trim();

        if (text.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (text.EndsWith('*'))
        {
            string valueText = text[..^1].Trim();
            double starValue = valueText.Length == 0 ? 1.0 : double.Parse(valueText, culture);
            return new GridLength(starValue, GridUnitType.Star);
        }

        (string number, double factor) = GetPixelValue(text);
        return new GridLength(double.Parse(number, culture) * factor, GridUnitType.Pixel);
    }

    private static (string Number, double Factor) GetPixelValue(string text)
    {
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            return (text[..^2].Trim(), 1.0);
        }

        if (text.EndsWith("in", StringComparison.OrdinalIgnoreCase))
        {
            return (text[..^2].Trim(), 96.0);
        }

        if (text.EndsWith("cm", StringComparison.OrdinalIgnoreCase))
        {
            return (text[..^2].Trim(), 96.0 / 2.54);
        }

        if (text.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            return (text[..^2].Trim(), 96.0 / 72.0);
        }

        return (text, 1.0);
    }
}
