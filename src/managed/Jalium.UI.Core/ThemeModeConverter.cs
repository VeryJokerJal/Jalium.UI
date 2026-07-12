using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="ThemeMode"/> values to and from strings and design-time
/// instance descriptors.
/// </summary>
[Experimental("WPF0001")]
public class ThemeModeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(
        ITypeDescriptorContext? typeDescriptorContext,
        Type sourceType)
    {
        return Type.GetTypeCode(sourceType) == TypeCode.String;
    }

    /// <inheritdoc />
    public override bool CanConvertTo(
        ITypeDescriptorContext? typeDescriptorContext,
        Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor)
            || destinationType == typeof(string);
    }

    /// <inheritdoc />
    public override object ConvertFrom(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object source)
    {
        if (source is not null)
        {
            return new ThemeMode(source.ToString()!);
        }

        throw GetConvertFromException(source);
    }

    /// <inheritdoc />
    public override object ConvertTo(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is ThemeMode themeMode)
        {
            if (destinationType == typeof(string))
            {
                return themeMode.Value;
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                ConstructorInfo constructor = typeof(ThemeMode).GetConstructor([typeof(string)])!;
                return new InstanceDescriptor(constructor, new object[] { themeMode.Value });
            }
        }

        throw GetConvertToException(value, destinationType);
    }
}
