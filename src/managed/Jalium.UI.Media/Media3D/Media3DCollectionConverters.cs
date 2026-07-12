using System.ComponentModel;
using System.Globalization;
#if JALIUM_UI_CORE
using ContractValueSerializer = Jalium.UI.Markup.ValueSerializer;
using ContractIValueSerializerContext = Jalium.UI.Markup.IValueSerializerContext;
#else
using ContractValueSerializer = Jalium.UI.Media.ValueSerializer;
using ContractIValueSerializerContext = Jalium.UI.Media.IValueSerializerContext;
#endif

namespace Jalium.UI.Media.Media3D
{
    public sealed class Point3DCollectionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? Point3DCollection.Parse(text) : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            return destinationType == typeof(string) && value is Point3DCollection collection
                ? collection.ToString(culture)
                : base.ConvertTo(context, culture, value, destinationType);
        }
    }

    public sealed class Vector3DCollectionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? Vector3DCollection.Parse(text) : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            return destinationType == typeof(string) && value is Vector3DCollection collection
                ? collection.ToString(culture)
                : base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

namespace Jalium.UI.Media.Media3D.Converters
{
    public class Point3DCollectionValueSerializer : ContractValueSerializer
    {
        public override bool CanConvertFromString(string value, ContractIValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, ContractIValueSerializerContext? context) => value is Point3DCollection;
        public override object ConvertFromString(string value, ContractIValueSerializerContext? context) => Point3DCollection.Parse(value);
        public override string ConvertToString(object value, ContractIValueSerializerContext? context) =>
            value is Point3DCollection collection
                ? collection.ToString(CultureInfo.InvariantCulture)
                : throw new ArgumentException("Value must be a Point3DCollection.", nameof(value));
    }

    public class Vector3DCollectionValueSerializer : ContractValueSerializer
    {
        public override bool CanConvertFromString(string value, ContractIValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, ContractIValueSerializerContext? context) => value is Vector3DCollection;
        public override object ConvertFromString(string value, ContractIValueSerializerContext? context) => Vector3DCollection.Parse(value);
        public override string ConvertToString(object value, ContractIValueSerializerContext? context) =>
            value is Vector3DCollection collection
                ? collection.ToString(CultureInfo.InvariantCulture)
                : throw new ArgumentException("Value must be a Vector3DCollection.", nameof(value));
    }
}
