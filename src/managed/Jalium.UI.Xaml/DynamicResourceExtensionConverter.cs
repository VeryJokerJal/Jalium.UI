using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Markup;

namespace Jalium.UI;

/// <summary>
/// Converts a dynamic-resource markup extension into a constructor descriptor.
/// </summary>
public class DynamicResourceExtensionConverter : System.ComponentModel.TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(InstanceDescriptor))
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value is not DynamicResourceExtension dynamicResource)
            {
                throw new ArgumentException(
                    "The value must be a DynamicResourceExtension.",
                    nameof(value));
            }

            var constructor = typeof(DynamicResourceExtension).GetConstructor([typeof(object)])!;
            return new InstanceDescriptor(
                constructor,
                new object?[] { dynamicResource.ResourceKey });
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
