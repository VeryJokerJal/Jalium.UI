using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Markup;

/// <summary>
/// Reports that component resource keys deliberately have no general type-conversion path.
/// </summary>
public class ComponentResourceKeyConverter : ExpressionConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return false;
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        return false;
    }

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        throw GetConvertFromException(value);

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is not ComponentResourceKey)
        {
            throw new ArgumentException("The value must be a ComponentResourceKey.", nameof(value));
        }

        throw GetConvertToException(value, destinationType);
    }
}

/// <summary>
/// Reports that template keys deliberately have no general type-conversion path.
/// </summary>
public sealed class TemplateKeyConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return false;
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        return false;
    }

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        throw GetConvertFromException(value);

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is not TemplateKey)
        {
            throw new ArgumentException("The value must be a TemplateKey.", nameof(value));
        }

        throw GetConvertToException(value, destinationType);
    }
}
