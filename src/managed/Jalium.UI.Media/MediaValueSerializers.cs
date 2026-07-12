using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Converters
{
    internal static class PrimitiveSerializerSupport
    {
        internal static bool CanConvert<T>(object value) => value is T;

        internal static object Parse<T>(string value, Func<string, T> parser)
        {
            ArgumentNullException.ThrowIfNull(value);
            return parser(value)!;
        }

        internal static string Format<T>(object value, Func<T, IFormatProvider?, string> formatter) =>
            value is T typed
                ? formatter(typed, CultureInfo.InvariantCulture)
                : throw new ArgumentException($"Value must be a {typeof(T).Name}.", nameof(value));
    }

    public class Int32RectValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.CanConvert<Int32Rect>(value);
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Parse(value, Int32Rect.Parse);
        public override string ConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Format<Int32Rect>(value, static (item, provider) => item.ToString(provider));
    }

    public class PointValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.CanConvert<Point>(value);
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Parse(value, Point.Parse);
        public override string ConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Format<Point>(value, static (item, provider) => item.ToString(provider));
    }

    public class RectValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.CanConvert<Rect>(value);
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Parse(value, Rect.Parse);
        public override string ConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Format<Rect>(value, static (item, provider) => item.ToString(provider));
    }

    public class SizeValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.CanConvert<Size>(value);
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Parse(value, Size.Parse);
        public override string ConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Format<Size>(value, static (item, provider) => item.ToString(provider));
    }

    public class VectorValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.CanConvert<Vector>(value);
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Parse(value, Vector.Parse);
        public override string ConvertToString(object value, IValueSerializerContext? context) => PrimitiveSerializerSupport.Format<Vector>(value, static (item, provider) => item.ToString(provider));
    }
}

namespace Jalium.UI.Media
{
    /// <summary>Represents a freezable collection of two-dimensional vectors.</summary>
    [TypeConverter(typeof(VectorCollectionConverter))]
    public sealed class VectorCollection : AnimatableCollection<Vector>, IFormattable
    {
        public VectorCollection()
        {
        }

        public VectorCollection(int capacity)
            : base(capacity)
        {
        }

        public VectorCollection(IEnumerable<Vector> collection)
            : base(collection)
        {
        }

        public static VectorCollection Parse(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            var result = new VectorCollection();
            foreach (string token in source.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                result.Add(Vector.Parse(token));
            }
            return result;
        }

        public new VectorCollection Clone() => (VectorCollection)base.Clone();
        public new VectorCollection CloneCurrentValue() => (VectorCollection)base.CloneCurrentValue();
        protected override Freezable CreateInstanceCore() => new VectorCollection();

        public override string ToString() => ToString(CultureInfo.CurrentCulture);
        public string ToString(IFormatProvider? provider) => string.Join(" ", this.Select(vector => vector.ToString(provider)));
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => string.Join(" ", this.Select(vector => ((IFormattable)vector).ToString(format, formatProvider)));
    }

    public sealed class VectorCollectionConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? VectorCollection.Parse(text) : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is VectorCollection collection
                ? collection.ToString(culture)
                : base.ConvertTo(context, culture, value, destinationType);
    }
}

namespace Jalium.UI.Media.Converters
{
    public abstract class BaseIListConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text
                ? ConvertFromCore(context, culture ?? CultureInfo.CurrentCulture, text)
                : base.ConvertFrom(context, culture, value)!;

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);
            return destinationType == typeof(string) && value is not null
                ? ConvertToCore(context, culture ?? CultureInfo.CurrentCulture, value)
                : base.ConvertTo(context, culture, value, destinationType);
        }

        protected abstract object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source);
        protected abstract string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value);

        protected static string[] Tokens(string source) => source.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public sealed class BoolIListConverter : BaseIListConverter
    {
        protected override object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source) =>
            Tokens(source).Select(static token => int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture) != 0).ToList();

        protected override string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value) =>
            value is IEnumerable<bool> items
                ? string.Join(" ", items.Select(static item => item ? "1" : "0"))
                : throw new ArgumentException("Value must be an enumerable of Boolean values.", nameof(value));
    }

    public sealed class CharIListConverter : BaseIListConverter
    {
        protected override object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source) => source.ToCharArray();

        protected override string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value) =>
            value is IEnumerable<char> items
                ? new string(items.ToArray())
                : throw new ArgumentException("Value must be an enumerable of Char values.", nameof(value));
    }

    public sealed class DoubleIListConverter : BaseIListConverter
    {
        protected override object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source) =>
            Tokens(source).Select(token => double.Parse(token, NumberStyles.Float, culture)).ToList();

        protected override string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value) =>
            value is IEnumerable<double> items
                ? string.Join(" ", items.Select(item => item.ToString(culture)))
                : throw new ArgumentException("Value must be an enumerable of Double values.", nameof(value));
    }

    public sealed class PointIListConverter : BaseIListConverter
    {
        protected override object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source) =>
            Tokens(source).Select(Point.Parse).ToList();

        protected override string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value) =>
            value is IEnumerable<Point> items
                ? string.Join(" ", items.Select(item => item.ToString(culture)))
                : throw new ArgumentException("Value must be an enumerable of Point values.", nameof(value));
    }

    public sealed class UShortIListConverter : BaseIListConverter
    {
        protected override object ConvertFromCore(ITypeDescriptorContext? context, CultureInfo culture, string source) =>
            Tokens(source).Select(token => ushort.Parse(token, NumberStyles.Integer, culture)).ToList();

        protected override string ConvertToCore(ITypeDescriptorContext? context, CultureInfo culture, object value) =>
            value is IEnumerable<ushort> items
                ? string.Join(" ", items.Select(item => item.ToString(culture)))
                : throw new ArgumentException("Value must be an enumerable of UInt16 values.", nameof(value));
    }

    internal static class MediaSerializerSupport
    {
        internal static string Format<T>(object value, Func<T, IFormatProvider?, string> formatter) =>
            value is T typed
                ? formatter(typed, CultureInfo.InvariantCulture)
                : throw new ArgumentException($"Value must be a {typeof(T).Name}.", nameof(value));
    }

    public class CacheModeValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => string.Equals(value, nameof(BitmapCache), StringComparison.Ordinal);
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is BitmapCache;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => CanConvertFromString(value, context) ? new BitmapCache() : throw new FormatException($"'{value}' is not a valid cache mode.");
        public override string ConvertToString(object value, IValueSerializerContext? context) => value is BitmapCache ? nameof(BitmapCache) : throw new ArgumentException("Value must be a BitmapCache.", nameof(value));
    }

    public class DoubleCollectionValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is DoubleCollection;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => DoubleCollection.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => MediaSerializerSupport.Format<DoubleCollection>(value, static (item, provider) => item.ToString(provider));
    }

    public class MatrixValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is Matrix;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => Matrix.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => MediaSerializerSupport.Format<Matrix>(value, static (item, provider) => item.ToString(provider));
    }

    public class PathFigureCollectionValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is PathFigureCollection;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PathFigureCollection.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => MediaSerializerSupport.Format<PathFigureCollection>(value, static (item, provider) => item.ToString(provider));
    }

    public class PointCollectionValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is PointCollection;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => PointCollection.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => MediaSerializerSupport.Format<PointCollection>(value, static (item, provider) => item.ToString(provider));
    }

    public class VectorCollectionValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is VectorCollection;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => VectorCollection.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => MediaSerializerSupport.Format<VectorCollection>(value, static (item, provider) => item.ToString(provider));
    }

    public class BrushValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is SolidColorBrush;
        public override object ConvertFromString(string value, IValueSerializerContext? context)
        {
            object? color = ColorConverter.ConvertFromString(value);
            return color is Color typed ? new SolidColorBrush(typed) : throw new FormatException($"'{value}' is not a valid brush.");
        }
        public override string ConvertToString(object value, IValueSerializerContext? context) => value is SolidColorBrush brush ? brush.Color.ToString() : throw new ArgumentException("Value must be a SolidColorBrush.", nameof(value));
    }

    public class GeometryValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is Geometry;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => Geometry.Parse(value);
        public override string ConvertToString(object value, IValueSerializerContext? context) => value is Geometry geometry ? geometry.ToString() ?? string.Empty : throw new ArgumentException("Value must be a Geometry.", nameof(value));
    }

    public class TransformValueSerializer : ValueSerializer
    {
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is MatrixTransform;
        public override object ConvertFromString(string value, IValueSerializerContext? context) => new MatrixTransform(Matrix.Parse(value));
        public override string ConvertToString(object value, IValueSerializerContext? context) => value is MatrixTransform transform ? transform.Value.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException("Value must be a MatrixTransform.", nameof(value));
    }
}
