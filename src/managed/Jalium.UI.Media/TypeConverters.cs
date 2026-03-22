using System.ComponentModel;
using System.Globalization;

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
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        if (value is Uri uri)
        {
            return new BitmapImage(uri);
        }
        return base.ConvertFrom(context, culture, value);
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

        // Try named colors
        return colorString.ToLowerInvariant() switch
        {
            "transparent" => Color.FromArgb(0, 255, 255, 255),
            "black" => Color.FromRgb(0, 0, 0),
            "white" => Color.FromRgb(255, 255, 255),
            "red" => Color.FromRgb(255, 0, 0),
            "green" => Color.FromRgb(0, 128, 0),
            "blue" => Color.FromRgb(0, 0, 255),
            "yellow" => Color.FromRgb(255, 255, 0),
            "gray" or "grey" => Color.FromRgb(128, 128, 128),
            _ => null
        };
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
        if (value is BitmapImage bitmapImage)
            return bitmapImage.UriSource != null;

        return false;
    }

    /// <inheritdoc />
    public override bool CanConvertFromString(string value, IValueSerializerContext? context) => true;

    /// <inheritdoc />
    public override string ConvertToString(object value, IValueSerializerContext? context)
    {
        if (value is BitmapImage bitmapImage && bitmapImage.UriSource != null)
            return bitmapImage.UriSource.OriginalString;

        throw new NotSupportedException($"Cannot convert {value?.GetType().Name ?? "null"} to string.");
    }

    /// <inheritdoc />
    public override object ConvertFromString(string value, IValueSerializerContext? context)
    {
        return new BitmapImage(new Uri(value, UriKind.RelativeOrAbsolute));
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
