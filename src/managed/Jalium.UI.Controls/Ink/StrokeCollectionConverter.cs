using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Ink;

namespace Jalium.UI;

/// <summary>
/// Converts supported ink persistence representations to and from a
/// <see cref="StrokeCollection"/>.
/// </summary>
/// <remarks>String conversion uses the same lossless stream codec as <see cref="StrokeCollection.Save(Stream)"/>.</remarks>
public class StrokeCollectionConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);
    }

    /// <inheritdoc />
    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value)!;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return new StrokeCollection();
        }

        byte[] serializedInk = Convert.FromBase64String(text);
        using var stream = new MemoryStream(serializedInk, writable: false);
        return new StrokeCollection(stream);
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is StrokeCollection strokes)
        {
            if (destinationType == typeof(string))
            {
                using var stream = new MemoryStream();
                strokes.Save(stream);
                return Convert.ToBase64String(stream.ToArray());
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                ConstructorInfo constructor = typeof(StrokeCollection).GetConstructor(
                    [typeof(IEnumerable<Stroke>)])!;

                return new InstanceDescriptor(constructor, new object?[] { strokes.ToArray() });
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    /// <inheritdoc />
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context)
    {
        return false;
    }
}
