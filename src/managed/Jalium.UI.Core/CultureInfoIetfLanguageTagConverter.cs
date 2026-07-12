using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="CultureInfo"/> values using their IETF language tags.
/// </summary>
public class CultureInfoIetfLanguageTagConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor)
            || destinationType == typeof(string);
    }

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string cultureName)
        {
            return CultureInfo.GetCultureInfoByIetfLanguageTag(cultureName);
        }

        throw GetConvertFromException(value);
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is CultureInfo cultureValue)
        {
            if (destinationType == typeof(string))
            {
                return cultureValue.IetfLanguageTag;
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                MethodInfo getCultureInfo = typeof(CultureInfo).GetMethod(
                    nameof(CultureInfo.GetCultureInfo),
                    BindingFlags.Static | BindingFlags.InvokeMethod | BindingFlags.Public,
                    binder: null,
                    types: [typeof(string)],
                    modifiers: null)!;

                return new InstanceDescriptor(getCultureInfo, new object[] { cultureValue.Name });
            }
        }

        throw GetConvertToException(value, destinationType);
    }
}
