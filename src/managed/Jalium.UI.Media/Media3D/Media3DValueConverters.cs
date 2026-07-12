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
    public abstract class Media3DValueConverter<T> : TypeConverter
        where T : struct
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, IFormatProvider?, string> _format;

        protected Media3DValueConverter(
            Func<string, T> parse,
            Func<T, IFormatProvider?, string> format)
        {
            _parse = parse;
            _format = format;
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object value)
        {
            return value is string text
                ? _parse(text)
                : base.ConvertFrom(context, culture, value)!;
        }

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            if (destinationType == typeof(string) && value is T typedValue)
            {
                return _format(typedValue, culture ?? CultureInfo.CurrentCulture);
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }

    public sealed class Matrix3DConverter : Media3DValueConverter<Matrix3D>
    {
        public Matrix3DConverter()
            : base(Matrix3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class Point3DConverter : Media3DValueConverter<Point3D>
    {
        public Point3DConverter()
            : base(Point3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class Point4DConverter : Media3DValueConverter<Point4D>
    {
        public Point4DConverter()
            : base(Point4D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class QuaternionConverter : Media3DValueConverter<Quaternion>
    {
        public QuaternionConverter()
            : base(Quaternion.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class Rect3DConverter : Media3DValueConverter<Rect3D>
    {
        public Rect3DConverter()
            : base(Rect3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class Size3DConverter : Media3DValueConverter<Size3D>
    {
        public Size3DConverter()
            : base(Size3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public sealed class Vector3DConverter : Media3DValueConverter<Vector3D>
    {
        public Vector3DConverter()
            : base(Vector3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }
}

namespace Jalium.UI.Media.Media3D.Converters
{
    public abstract class Media3DValueSerializer<T> : ContractValueSerializer
        where T : struct
    {
        private readonly Func<string, T> _parse;
        private readonly Func<T, IFormatProvider?, string> _format;

        protected Media3DValueSerializer(
            Func<string, T> parse,
            Func<T, IFormatProvider?, string> format)
        {
            _parse = parse;
            _format = format;
        }

        public override bool CanConvertFromString(string value, ContractIValueSerializerContext? context) =>
            value is not null;

        public override bool CanConvertToString(object value, ContractIValueSerializerContext? context) =>
            value is T;

        public override object ConvertFromString(string value, ContractIValueSerializerContext? context)
        {
            ArgumentNullException.ThrowIfNull(value);
            return _parse(value);
        }

        public override string ConvertToString(object value, ContractIValueSerializerContext? context)
        {
            return value is T typedValue
                ? _format(typedValue, CultureInfo.InvariantCulture)
                : throw new ArgumentException($"Value must be a {typeof(T).Name}.", nameof(value));
        }
    }

    public class Matrix3DValueSerializer : Media3DValueSerializer<Matrix3D>
    {
        public Matrix3DValueSerializer()
            : base(Matrix3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class Point3DValueSerializer : Media3DValueSerializer<Point3D>
    {
        public Point3DValueSerializer()
            : base(Point3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class Point4DValueSerializer : Media3DValueSerializer<Point4D>
    {
        public Point4DValueSerializer()
            : base(Point4D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class QuaternionValueSerializer : Media3DValueSerializer<Quaternion>
    {
        public QuaternionValueSerializer()
            : base(Quaternion.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class Rect3DValueSerializer : Media3DValueSerializer<Rect3D>
    {
        public Rect3DValueSerializer()
            : base(Rect3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class Size3DValueSerializer : Media3DValueSerializer<Size3D>
    {
        public Size3DValueSerializer()
            : base(Size3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }

    public class Vector3DValueSerializer : Media3DValueSerializer<Vector3D>
    {
        public Vector3DValueSerializer()
            : base(Vector3D.Parse, static (value, provider) => value.ToString(provider))
        {
        }
    }
}
