using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Jalium.UI;

/// <summary>Converts FigureLength values to and from WPF markup syntax.</summary>
public class FigureLengthConverter : TypeConverter
{
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
            or TypeCode.UInt64;
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        culture ??= CultureInfo.CurrentCulture;

        return value is string text
            ? Parse(text, culture)
            : new FigureLength(Convert.ToDouble(value, culture));
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is FigureLength length)
        {
            if (destinationType == typeof(string))
            {
                return Format(length, culture ?? CultureInfo.CurrentCulture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                var constructor = typeof(FigureLength).GetConstructor(
                    new[] { typeof(double), typeof(FigureUnitType) })!;
                return new InstanceDescriptor(
                    constructor,
                    new object[] { length.Value, length.FigureUnitType });
            }
        }

        throw GetConvertToException(value, destinationType);
    }

    internal static string Format(FigureLength length, CultureInfo culture)
    {
        return length.FigureUnitType switch
        {
            FigureUnitType.Auto => "Auto",
            FigureUnitType.Pixel => Convert.ToString(length.Value, culture)!,
            _ => $"{Convert.ToString(length.Value, culture)} {length.FigureUnitType}",
        };
    }

    internal static FigureLength Parse(string text, CultureInfo culture)
    {
        string token = text.Trim();
        if (token.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return new FigureLength(1d, FigureUnitType.Auto);
        }

        foreach (FigureUnitType unit in new[]
                 {
                     FigureUnitType.Column,
                     FigureUnitType.Content,
                     FigureUnitType.Page,
                     FigureUnitType.Pixel,
                 })
        {
            string unitName = unit.ToString();
            if (!token.EndsWith(unitName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string valueText = token[..^unitName.Length].Trim();
            double value = valueText.Length == 0
                ? 1d
                : double.Parse(valueText, NumberStyles.Float, culture);
            return new FigureLength(value, unit);
        }

        return new FigureLength(double.Parse(token, NumberStyles.Float, culture));
    }
}
