using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI.Media;

/// <summary>
/// Converts instances of other types to and from an ImageSource instance.
/// </summary>
public sealed class ImageSourceConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || sourceType == typeof(Uri) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string path)
        {
            if (SvgImage.IsSvgFile(path))
                return new SvgImage(new Uri(path, UriKind.RelativeOrAbsolute));
            return ImageSourceLoader.FromUri(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        if (value is Uri uri)
        {
            var uriStr = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
            if (SvgImage.IsSvgFile(uriStr))
                return new SvgImage(uri);
            return ImageSourceLoader.FromUri(uri);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string))
        {
            return value switch
            {
                SvgImage svg when svg.UriSource is not null => svg.UriSource.OriginalString,
                BitmapImage bitmap when bitmap.UriSource is not null => bitmap.UriSource.OriginalString,
                ImageSource image => image.ToString(culture),
                _ => base.ConvertTo(context, culture, value, destinationType),
            };
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts instances of other types to and from a FontFamily.
/// </summary>
public sealed class FontFamilyConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string familyName)
        {
            return new FontFamily(familyName);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is FontFamily fontFamily)
        {
            return fontFamily.Source;
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts instances of other types to and from a Brush.
/// </summary>
public sealed class BrushConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string colorString)
        {
            var color = ColorConverter.ConvertFromString(colorString);
            if (color != null)
                return new SolidColorBrush((Color)color);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Brush brush
            ? brush is SolidColorBrush solid ? solid.Color.ToString(culture) : brush.ToString(culture)
            : base.ConvertTo(context, culture, value, destinationType);
}

/// <summary>
/// Converts instances of other types to and from a Color.
/// </summary>
public sealed class ColorConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string colorString)
        {
            return ConvertFromString(colorString);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Color color
            ? color.ToString(culture)
            : base.ConvertTo(context, culture, value, destinationType);

    /// <summary>
    /// Converts a string representation of a color to a Color.
    /// </summary>
    public new static object? ConvertFromString(string colorString)
    {
        if (string.IsNullOrEmpty(colorString))
            return null;

        colorString = colorString.Trim();

        if (colorString.StartsWith('#'))
        {
            var hex = colorString.AsSpan(1);
            if (hex.Length == 6)
            {
                var r = byte.Parse(hex.Slice(0, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber);
                return Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                var a = byte.Parse(hex.Slice(0, 2), NumberStyles.HexNumber);
                var r = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber);
                var g = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber);
                var b = byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber);
                return Color.FromArgb(a, r, g, b);
            }
            return null;
        }

        // Try named colors (full lookup from Colors class)
        if (s_namedColors.TryGetValue(colorString.ToLowerInvariant(), out var namedColor))
            return namedColor;

        return null;
    }

    private static readonly Dictionary<string, Color> s_namedColors = BuildNamedColorMap();

    private static Dictionary<string, Color> BuildNamedColorMap()
    {
        var map = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in typeof(Colors).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (prop.PropertyType == typeof(Color))
            {
                map[prop.Name.ToLowerInvariant()] = (Color)prop.GetValue(null)!;
            }
        }
        // Add common aliases
        map["grey"] = map["gray"];
        return map;
    }
}

/// <summary>
/// Converts instances of other types to and from a Geometry.
/// </summary>
public sealed class GeometryConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string pathData)
        {
            return Geometry.Parse(pathData);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is Geometry geometry
            ? geometry.ToString()
            : base.ConvertTo(context, culture, value, destinationType);
}

/// <summary>Converts point collections to and from their compact XAML representation.</summary>
public sealed class PointCollectionConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text ? PointCollection.Parse(text) : base.ConvertFrom(context, culture, value)!;

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is PointCollection points
            ? points.ToString(culture ?? CultureInfo.CurrentCulture)
            : base.ConvertTo(context, culture, value, destinationType);
}

/// <summary>
/// Converts instances of other types to and from a PixelFormat.
/// </summary>
public sealed class PixelFormatConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
    }

    public override object ConvertFrom(ITypeDescriptorContext? td, CultureInfo? ci, object o)
    {
        if (o is string text)
        {
            string name = text.Trim();
            var property = typeof(PixelFormats).GetProperties(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate =>
                    candidate.PropertyType == typeof(PixelFormat) &&
                    string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property is not null)
            {
                return (PixelFormat)property.GetValue(null)!;
            }

            throw new FormatException($"'{text}' is not a recognized pixel format.");
        }

        return base.ConvertFrom(td, ci, o)!;
    }

    public new object ConvertFromString(string value) =>
        ConvertFrom(null, CultureInfo.CurrentCulture, value);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is PixelFormat format)
        {
            return format.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts instances of other types to and from a RequestCachePolicy.
/// </summary>
public sealed class RequestCachePolicyConverter : TypeConverter
{
}

/// <summary>
/// Provides a context for value serialization operations.
/// </summary>
public interface IValueSerializerContext : IServiceProvider
{
    /// <summary>
    /// Gets the value serializer for the specified type.
    /// </summary>
    ValueSerializer? GetValueSerializerFor(Type type);
}

/// <summary>
/// Abstract base class for converting instances of a type to and from a string representation.
/// ValueSerializer differs from <see cref="TypeConverter"/> in that it is specifically designed
/// for XAML serialization scenarios.
/// </summary>
public abstract class ValueSerializer
{
    /// <summary>
    /// Determines whether the specified value can be converted to a string.
    /// </summary>
    /// <param name="value">The value to evaluate for conversion.</param>
    /// <param name="context">Context information used for conversion.</param>
    /// <returns><c>true</c> if the value can be converted to a string; otherwise, <c>false</c>.</returns>
    public virtual bool CanConvertToString(object value, IValueSerializerContext? context) => false;

    /// <summary>
    /// Determines whether the specified string can be converted to an instance of the type.
    /// </summary>
    /// <param name="value">The string to evaluate for conversion.</param>
    /// <param name="context">Context information used for conversion.</param>
    /// <returns><c>true</c> if the string can be converted; otherwise, <c>false</c>.</returns>
    public virtual bool CanConvertFromString(string value, IValueSerializerContext? context) => false;

    /// <summary>
    /// Converts the specified value to a string.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="context">Context information used for conversion.</param>
    /// <returns>A string representation of the value.</returns>
    public virtual string ConvertToString(object value, IValueSerializerContext? context)
    {
        throw new NotSupportedException($"Conversion to string is not supported for {value?.GetType().Name ?? "null"}.");
    }

    /// <summary>
    /// Converts the specified string to an instance of the type.
    /// </summary>
    /// <param name="value">The string to convert.</param>
    /// <param name="context">Context information used for conversion.</param>
    /// <returns>An object instance created from the string.</returns>
    public virtual object ConvertFromString(string value, IValueSerializerContext? context)
    {
        throw new NotSupportedException($"Conversion from string is not supported.");
    }

    private static readonly ImageSourceValueSerializer s_imageSourceSerializer = new();
    private static readonly FontFamilyValueSerializer s_fontFamilySerializer = new();
    private static readonly Converters.BrushValueSerializer s_brushSerializer = new();
    private static readonly Converters.GeometryValueSerializer s_geometrySerializer = new();
    private static readonly Converters.TransformValueSerializer s_transformSerializer = new();
    private static readonly Converters.CacheModeValueSerializer s_cacheModeSerializer = new();
    private static readonly Converters.DoubleCollectionValueSerializer s_doubleCollectionSerializer = new();
    private static readonly Converters.MatrixValueSerializer s_matrixSerializer = new();
    private static readonly Converters.PathFigureCollectionValueSerializer s_pathFigureCollectionSerializer = new();
    private static readonly Converters.PointCollectionValueSerializer s_pointCollectionSerializer = new();
    private static readonly Converters.VectorCollectionValueSerializer s_vectorCollectionSerializer = new();
    private static readonly global::Jalium.UI.Converters.Int32RectValueSerializer s_int32RectSerializer = new();
    private static readonly global::Jalium.UI.Converters.PointValueSerializer s_pointSerializer = new();
    private static readonly global::Jalium.UI.Converters.RectValueSerializer s_rectSerializer = new();
    private static readonly global::Jalium.UI.Converters.SizeValueSerializer s_sizeSerializer = new();
    private static readonly global::Jalium.UI.Converters.VectorValueSerializer s_vectorSerializer = new();
    private static readonly ValueSerializer s_matrix3DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Matrix3DValueSerializer());
    private static readonly ValueSerializer s_point3DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Point3DValueSerializer());
    private static readonly ValueSerializer s_point4DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Point4DValueSerializer());
    private static readonly ValueSerializer s_quaternionSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.QuaternionValueSerializer());
    private static readonly ValueSerializer s_rect3DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Rect3DValueSerializer());
    private static readonly ValueSerializer s_size3DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Size3DValueSerializer());
    private static readonly ValueSerializer s_vector3DSerializer =
        new MarkupValueSerializerAdapter(new Media3D.Converters.Vector3DValueSerializer());

    /// <summary>
    /// Returns the built-in <see cref="ValueSerializer"/> that can round-trip the supplied type
    /// through a string, or <see langword="null"/> when no reliable string serializer is available.
    /// </summary>
    /// <param name="type">The type to obtain a serializer for.</param>
    public static ValueSerializer? GetSerializerFor(Type? type)
    {
        if (type == null)
        {
            return null;
        }

        if (typeof(ImageSource).IsAssignableFrom(type))
        {
            return s_imageSourceSerializer;
        }
        if (typeof(FontFamily).IsAssignableFrom(type))
        {
            return s_fontFamilySerializer;
        }
        if (typeof(Brush).IsAssignableFrom(type))
        {
            return s_brushSerializer;
        }
        if (typeof(Geometry).IsAssignableFrom(type))
        {
            return s_geometrySerializer;
        }
        if (typeof(Transform).IsAssignableFrom(type))
        {
            return s_transformSerializer;
        }
        if (typeof(CacheMode).IsAssignableFrom(type))
        {
            return s_cacheModeSerializer;
        }
        if (type == typeof(DoubleCollection))
        {
            return s_doubleCollectionSerializer;
        }
        if (type == typeof(Matrix))
        {
            return s_matrixSerializer;
        }
        if (type == typeof(PathFigureCollection))
        {
            return s_pathFigureCollectionSerializer;
        }
        if (type == typeof(PointCollection))
        {
            return s_pointCollectionSerializer;
        }
        if (type == typeof(VectorCollection))
        {
            return s_vectorCollectionSerializer;
        }
        if (type == typeof(Int32Rect))
        {
            return s_int32RectSerializer;
        }
        if (type == typeof(Point))
        {
            return s_pointSerializer;
        }
        if (type == typeof(Rect))
        {
            return s_rectSerializer;
        }
        if (type == typeof(Size))
        {
            return s_sizeSerializer;
        }
        if (type == typeof(Vector))
        {
            return s_vectorSerializer;
        }
        if (type == typeof(Media3D.Matrix3D))
        {
            return s_matrix3DSerializer;
        }
        if (type == typeof(Media3D.Point3D))
        {
            return s_point3DSerializer;
        }
        if (type == typeof(Media3D.Point4D))
        {
            return s_point4DSerializer;
        }
        if (type == typeof(Media3D.Quaternion))
        {
            return s_quaternionSerializer;
        }
        if (type == typeof(Media3D.Rect3D))
        {
            return s_rect3DSerializer;
        }
        if (type == typeof(Media3D.Size3D))
        {
            return s_size3DSerializer;
        }
        if (type == typeof(Media3D.Vector3D))
        {
            return s_vector3DSerializer;
        }

        return null;
    }

    private sealed class MarkupValueSerializerAdapter(Jalium.UI.Markup.ValueSerializer serializer)
        : ValueSerializer
    {
        public override bool CanConvertToString(object value, IValueSerializerContext? context) =>
            serializer.CanConvertToString(value, null);

        public override bool CanConvertFromString(string value, IValueSerializerContext? context) =>
            serializer.CanConvertFromString(value, null);

        public override string ConvertToString(object value, IValueSerializerContext? context) =>
            serializer.ConvertToString(value, null);

        public override object ConvertFromString(string value, IValueSerializerContext? context) =>
            serializer.ConvertFromString(value, null);
    }
}

/// <summary>
/// Converts <see cref="ImageSource"/> instances to and from string representations
/// for XAML serialization.
/// </summary>
public sealed class ImageSourceValueSerializer : ValueSerializer
{
    /// <inheritdoc />
    public override bool CanConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is SvgImage svgImage)
            return svgImage.UriSource != null;
        if (value is BitmapImage bitmapImage)
            return bitmapImage.UriSource != null;

        return false;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is SvgImage svgImage && svgImage.UriSource != null)
            return svgImage.UriSource.OriginalString;
        if (value is BitmapImage bitmapImage && bitmapImage.UriSource != null)
            return bitmapImage.UriSource.OriginalString;

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        if (SvgImage.IsSvgFile(value))
            return new SvgImage(new Uri(value, UriKind.RelativeOrAbsolute));
        return ImageSourceLoader.FromUri(new Uri(value, UriKind.RelativeOrAbsolute));
    }
}

/// <summary>
/// Converts <see cref="FontFamily"/> instances to and from string representations
/// for XAML serialization.
/// </summary>
public sealed class FontFamilyValueSerializer : ValueSerializer
{
    /// <inheritdoc />
    public override bool CanConvertToString(object value, IValueSerializerContext? context)
    {
        return value is FontFamily;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is FontFamily fontFamily)
            return fontFamily.Source;

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        return new FontFamily(value);
    }
}

/// <summary>
/// Converts <see cref="Brush"/> instances to and from string representations
/// for XAML serialization.
/// </summary>
public sealed class BrushValueSerializer : ValueSerializer
{
    /// <inheritdoc />
    public override bool CanConvertToString(object value, IValueSerializerContext? context)
    {
        return value is SolidColorBrush;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is SolidColorBrush solidBrush)
            return solidBrush.Color.ToString();

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        var color = ColorConverter.ConvertFromString(value);
        if (color is Color c)
            return new SolidColorBrush(c);

        throw new FormatException($"Invalid brush format: {value}");
    }
}

/// <summary>
/// Converts <see cref="Transform"/> instances to and from string representations
/// for XAML serialization.
/// </summary>
public sealed class TransformValueSerializer : ValueSerializer
{
    /// <inheritdoc />
    public override bool CanConvertToString(object value, IValueSerializerContext? context)
    {
        return value is MatrixTransform;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is MatrixTransform matrixTransform)
        {
            var m = matrixTransform.Value;
            return FormattableString.Invariant(
                $"{m.M11},{m.M12},{m.M21},{m.M22},{m.OffsetX},{m.OffsetY}");
        }

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        if (string.Equals(value, "Identity", StringComparison.OrdinalIgnoreCase))
            return Transform.Identity;

        var parts = value.Split(',');
        if (parts.Length == 6)
        {
            return new MatrixTransform(new Matrix(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture),
                double.Parse(parts[4], CultureInfo.InvariantCulture),
                double.Parse(parts[5], CultureInfo.InvariantCulture)));
        }

        throw new FormatException($"Invalid transform format: {value}");
    }
}

/// <summary>
/// Converts <see cref="Geometry"/> instances to and from path markup string representations
/// for XAML serialization.
/// </summary>
public sealed class GeometryValueSerializer : ValueSerializer
{
    /// <inheritdoc />
    public override bool CanConvertToString(object value, IValueSerializerContext? context)
    {
        return value is Geometry;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is Geometry geometry)
            return geometry.ToString()!;

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        return Geometry.Parse(value);
    }
}
