using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="Point"/> values to and from their XAML string representation.
/// </summary>
public sealed class PointConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) =>
        value is string source ? Point.Parse(source) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is Point point)
        {
            return point.ToString(culture);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts <see cref="Vector"/> values to and from their XAML string representation.
/// </summary>
public sealed class VectorConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) =>
        value is string source ? Vector.Parse(source) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is Vector vector)
        {
            return vector.ToString(culture);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts <see cref="Size"/> values to and from their XAML string representation.
/// </summary>
public sealed class SizeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value) =>
        value is string source ? Size.Parse(source) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is Size size)
        {
            return size.ToString(culture);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
