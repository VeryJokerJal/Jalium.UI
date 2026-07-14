using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Media3D
{
    /// <summary>
    /// Holds the shared implementation used by the WPF-compatible Media3D converters
    /// without introducing a public converter base type.
    /// </summary>
    internal sealed class Media3DConverterSupport<T>
        where T : struct
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, IFormatProvider?, string> _format;

        internal Media3DConverterSupport(
            Func<string, T> parse,
            Func<T, IFormatProvider?, string> format)
        {
            _parse = parse;
            _format = format;
        }

        internal static bool CanConvertFrom(Type sourceType) => sourceType == typeof(string);

        internal static bool CanConvertTo(Type? destinationType) => destinationType == typeof(string);

        internal bool TryConvertFrom(object value, out object? result)
        {
            if (value is string text)
            {
                result = _parse(text);
                return true;
            }

            result = null;
            return false;
        }

        internal bool TryConvertTo(
            object? value,
            Type destinationType,
            CultureInfo? culture,
            out object? result)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            if (destinationType == typeof(string) && value is T typedValue)
            {
                result = _format(typedValue, culture ?? CultureInfo.CurrentCulture);
                return true;
            }

            result = null;
            return false;
        }
    }

    public sealed class Matrix3DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Matrix3D> s_support =
            new(Matrix3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Matrix3D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Matrix3D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class Point3DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Point3D> s_support =
            new(Point3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Point3D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Point3D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class Point4DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Point4D> s_support =
            new(Point4D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Point4D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Point4D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class QuaternionConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Quaternion> s_support =
            new(Quaternion.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Quaternion>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Quaternion>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class Rect3DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Rect3D> s_support =
            new(Rect3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Rect3D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Rect3D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class Size3DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Size3D> s_support =
            new(Size3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Size3D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Size3D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }

    public sealed class Vector3DConverter : TypeConverter
    {
        private static readonly Media3DConverterSupport<Vector3D> s_support =
            new(Vector3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            Media3DConverterSupport<Vector3D>.CanConvertFrom(sourceType) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            Media3DConverterSupport<Vector3D>.CanConvertTo(destinationType) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            s_support.TryConvertFrom(value, out object? result)
                ? result!
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            s_support.TryConvertTo(value, destinationType, culture, out object? result)
                ? result
                : base.ConvertTo(context, culture, value, destinationType);
    }
}

namespace Jalium.UI.Media.Media3D.Converters
{
    /// <summary>
    /// Holds the shared implementation used by the WPF-compatible Media3D value serializers
    /// without introducing a public serializer base type.
    /// </summary>
    internal sealed class Media3DValueSerializerSupport<T>
        where T : struct
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, IFormatProvider?, string> _format;

        internal Media3DValueSerializerSupport(
            Func<string, T> parse,
            Func<T, IFormatProvider?, string> format)
        {
            _parse = parse;
            _format = format;
        }

        internal static bool CanConvertFromString(string value) => value is not null;

        internal static bool CanConvertToString(object value) => value is T;

        internal object ConvertFromString(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return _parse(value);
        }

        internal string ConvertToString(object value) =>
            value is T typedValue
                ? _format(typedValue, CultureInfo.InvariantCulture)
                : throw new ArgumentException($"Value must be a {typeof(T).Name}.", nameof(value));
    }

    public class Matrix3DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Matrix3D> s_support =
            new(Matrix3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Matrix3D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Matrix3D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class Point3DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Point3D> s_support =
            new(Point3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Point3D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Point3D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class Point4DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Point4D> s_support =
            new(Point4D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Point4D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Point4D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class QuaternionValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Quaternion> s_support =
            new(Quaternion.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Quaternion>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Quaternion>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class Rect3DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Rect3D> s_support =
            new(Rect3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Rect3D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Rect3D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class Size3DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Size3D> s_support =
            new(Size3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Size3D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Size3D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }

    public class Vector3DValueSerializer : ValueSerializer
    {
        private static readonly Media3DValueSerializerSupport<Vector3D> s_support =
            new(Vector3D.Parse, static (value, provider) => value.ToString(provider));

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Vector3D>.CanConvertFromString(value);

        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            Media3DValueSerializerSupport<Vector3D>.CanConvertToString(value);

        public override object ConvertFromString(string value, IValueSerializerContext? context) => s_support.ConvertFromString(value);

        public override string ConvertToString(object value, IValueSerializerContext? context) => s_support.ConvertToString(value);
    }
}
