using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="Int32Rect"/> values to and from their XAML string representation.
/// </summary>
public sealed class Int32RectConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    /// <inheritdoc />
    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string source)
        {
            return Int32Rect.Parse(source);
        }

        return base.ConvertFrom(context, culture, value)!;
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(string) && value is Int32Rect rect)
        {
            return rect.ToString(culture);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
