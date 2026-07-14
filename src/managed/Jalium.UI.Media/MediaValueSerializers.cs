using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using IValueSerializerContext = Jalium.UI.Markup.IValueSerializerContext;
using ValueSerializer = Jalium.UI.Markup.ValueSerializer;

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
    public sealed class VectorCollection : Freezable, IList<Vector>, IList, IFormattable
    {
        private readonly AnimatableListStorage<Vector> _items;

        public VectorCollection() => _items = CreateStorage();

        public VectorCollection(int capacity) => _items = CreateStorage(capacity);

        public VectorCollection(IEnumerable<Vector> collection)
        {
            ArgumentNullException.ThrowIfNull(collection);
            _items = CreateStorage(collection is ICollection<Vector> source ? source.Count : 0);
            _items.AddRange(collection);
        }

        public Vector this[int index] { get => _items[index]; set => _items[index] = value; }
        object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Vector>.Cast(value); }
        public int Count => _items.Count;
        bool ICollection<Vector>.IsReadOnly => _items.IsReadOnly;
        bool IList.IsReadOnly => _items.IsReadOnly;
        bool IList.IsFixedSize => _items.IsReadOnly;
        bool ICollection.IsSynchronized => _items.IsSynchronized;
        object ICollection.SyncRoot => this;
        public void Add(Vector value) => _items.Add(value);
        int IList.Add(object? value) { Add(AnimatableListStorage<Vector>.Cast(value)); return Count - 1; }
        public void Clear() => _items.Clear();
        public bool Contains(Vector value) => _items.Contains(value);
        bool IList.Contains(object? value) => value is Vector vector && Contains(vector);
        public void CopyTo(Vector[] array, int index) => _items.CopyTo(array, index);
        void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
        public Enumerator GetEnumerator() => new(_items.GetEnumerator());
        IEnumerator<Vector> IEnumerable<Vector>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(Vector value) => _items.IndexOf(value);
        int IList.IndexOf(object? value) => value is Vector vector ? IndexOf(vector) : -1;
        public void Insert(int index, Vector value) => _items.Insert(index, value);
        void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Vector>.Cast(value));
        public bool Remove(Vector value) => _items.Remove(value);
        void IList.Remove(object? value) { if (value is Vector vector) Remove(vector); }
        public void RemoveAt(int index) => _items.RemoveAt(index);

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
        protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
        protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((VectorCollection)source)._items, AnimatableListCloneMode.Clone); }
        protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((VectorCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
        protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((VectorCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
        protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((VectorCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

        private AnimatableListStorage<Vector> CreateStorage(int capacity = 0) => new(
            () => ReadPreamble(),
            () => WritePreamble(),
            () => WritePostscript(),
            (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
            () => IsFrozen,
            capacity);

        public override string ToString() => ToString(CultureInfo.CurrentCulture);
        public string ToString(IFormatProvider? provider) => string.Join(" ", this.Select(vector => vector.ToString(provider)));
        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => string.Join(" ", this.Select(vector => ((IFormattable)vector).ToString(format, formatProvider)));

        public struct Enumerator : IEnumerator<Vector>
        {
            private List<Vector>.Enumerator _inner;
            internal Enumerator(List<Vector>.Enumerator inner) => _inner = inner;
            public Vector Current => _inner.Current;
            object IEnumerator.Current => Current;
            public bool MoveNext() => _inner.MoveNext();
            public void Reset() => ((IEnumerator)_inner).Reset();
            public void Dispose() => _inner.Dispose();
        }
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
        public override object ConvertFromString(string value, IValueSerializerContext? context)
        {
            ArgumentNullException.ThrowIfNull(value);
            return string.Equals(value.Trim(), "Identity", StringComparison.OrdinalIgnoreCase)
                ? new MatrixTransform(Matrix.Identity)
                : new MatrixTransform(Matrix.Parse(value));
        }
        public override string ConvertToString(object value, IValueSerializerContext? context) => value is MatrixTransform transform ? transform.Value.ToString(CultureInfo.InvariantCulture) : throw new ArgumentException("Value must be a MatrixTransform.", nameof(value));
    }
}
