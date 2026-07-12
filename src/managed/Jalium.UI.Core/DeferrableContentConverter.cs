using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI;

/// <summary>Converts binary deferred resource content into an owned payload.</summary>
public class DeferrableContentConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return typeof(Stream).IsAssignableFrom(sourceType)
            || sourceType == typeof(byte[])
            || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value)
    {
        if (value is null)
        {
            return base.ConvertFrom(context, culture, value!);
        }

        ArgumentNullException.ThrowIfNull(context);
        if (context.Instance is not null && context.Instance is not ResourceDictionary)
        {
            throw new InvalidOperationException(
                "Deferred content can only be assigned to a ResourceDictionary.");
        }

        byte[] payload = value switch
        {
            byte[] bytes => bytes.ToArray(),
            Stream stream => ReadRemainingBytes(stream),
            _ => throw new InvalidOperationException(
                "Deferred content must be supplied as a Stream or byte array."),
        };

        return new DeferrableContent(payload);
    }

    private static byte[] ReadRemainingBytes(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
