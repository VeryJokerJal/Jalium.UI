using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Base type for values that are evaluated by the dependency-property system.
/// </summary>
[TypeConverter(typeof(ExpressionConverter))]
public class Expression
{
    internal Expression()
    {
    }
}

/// <summary>
/// Prevents infrastructure expressions from being serialized through their
/// incidental <see cref="object.ToString"/> representation.
/// </summary>
public class ExpressionConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => false;

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => false;

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        throw GetConvertFromException(value);

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType) =>
        throw GetConvertToException(value, destinationType);
}
