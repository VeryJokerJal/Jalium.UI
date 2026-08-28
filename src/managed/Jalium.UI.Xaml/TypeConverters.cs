using System.Globalization;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Shapes;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using AnimationDuration = Jalium.UI.Duration;
using MediaPointCollection = Jalium.UI.Media.PointCollection;
using MediaDoubleCollection = Jalium.UI.Media.DoubleCollection;

namespace Jalium.UI.Markup;

/// <summary>
/// Base class for type converters.
/// </summary>
public abstract class TypeConverter
{
    /// <summary>
    /// Returns whether this converter can convert from the specified type.
    /// </summary>
    public virtual bool CanConvertFrom(Type sourceType) => sourceType == typeof(string);

    /// <summary>
    /// Converts the given value to the type of this converter.
    /// </summary>
    public abstract object? ConvertFrom(object? value);
}

/// <summary>
/// Converts strings to Thickness values.
/// </summary>
internal sealed class ThicknessConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();

        var parts = str.Split(',', ' ').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        return parts.Length switch
        {
            1 => new Thickness(double.Parse(parts[0], CultureInfo.InvariantCulture)),
            2 => new Thickness(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture)),
            4 => new Thickness(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"Invalid Thickness format: {str}")
        };
    }
}

/// <summary>
/// Converts a path mini-language string (e.g. <c>"M 0,0 L 10,10 Z"</c>) to a
/// <see cref="Geometry"/>, mirroring WPF's GeometryConverter. Lets any Geometry-typed
/// property (Path.Geometry, PathIcon.Data, UIElement.Clip, …) accept the string form in
/// JALXAML. Invalid input yields <see langword="null"/> (the property keeps its default)
/// rather than throwing.
/// </summary>
public sealed class GeometryTypeConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is Geometry g) return g;
        if (value is not string str) return null;
        try { return Geometry.Parse(str); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// Converts strings / <see cref="Uri"/> to <see cref="ImageSource"/>, so XAML can write
/// <c>&lt;Image Source="Assets/Brand/logo.png"/&gt;</c> as a literal attribute.
///
/// <para>Without this entry <see cref="TypeConverterRegistry.ConvertValue"/> found no converter
/// for <see cref="ImageSource"/> and returned <see langword="null"/> — the element kept its
/// explicit Width/Height, took up layout space and drew nothing, with no exception and no log.
/// That is indistinguishable from a fully transparent image.</para>
///
/// <para><see cref="Jalium.UI.Media.ImageSourceConverter"/> already covers the same conversion but
/// derives from <see cref="System.ComponentModel.TypeConverter"/>, which is a different hierarchy
/// from the <see cref="Jalium.UI.Markup.TypeConverter"/> this registry stores; it therefore cannot
/// be registered here. Both delegate to <see cref="ImageSourceLoader.FromUri"/>, so the XAML and
/// the <c>TypeDescriptor</c> paths stay in agreement.</para>
/// </summary>
public sealed class ImageSourceTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(Type sourceType) =>
        sourceType == typeof(string) ||
        sourceType == typeof(Uri) ||
        typeof(ImageSource).IsAssignableFrom(sourceType);

    public override object? ConvertFrom(object? value)
    {
        if (value is ImageSource source) return source;

        Uri? uri;
        switch (value)
        {
            case Uri u:
                uri = u;
                break;
            case string s when !string.IsNullOrWhiteSpace(s):
                if (!Uri.TryCreate(s.Trim(), UriKind.RelativeOrAbsolute, out uri)) return null;
                break;
            default:
                return null;
        }

        // SvgImage keeps its own vector pipeline; ImageSourceLoader would hand it to the raster
        // decoder. Probe the path the same way ImageSourceConverter does.
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        return SvgImage.IsSvgFile(path) ? new SvgImage(uri) : ImageSourceLoader.FromUri(uri);
    }
}

/// <summary>
/// Converts strings to CornerRadius values.
/// </summary>
internal sealed class CornerRadiusConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();

        var parts = str.Split(',', ' ').Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        return parts.Length switch
        {
            1 => new CornerRadius(double.Parse(parts[0], CultureInfo.InvariantCulture)),
            4 => new CornerRadius(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                double.Parse(parts[3], CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"Invalid CornerRadius format: {str}")
        };
    }
}

/// <summary>
/// Converts strings to Brush values.
/// </summary>
// Parser implementation; the public WPF-compatible converter is
// Jalium.UI.Media.BrushConverter.
internal sealed class BrushConverter : TypeConverter
{
    private static readonly Dictionary<string, Color> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Transparent"] = Color.FromArgb(0, 255, 255, 255),
        ["Black"] = Color.FromRgb(0, 0, 0),
        ["White"] = Color.FromRgb(255, 255, 255),
        ["Red"] = Color.FromRgb(255, 0, 0),
        ["Green"] = Color.FromRgb(0, 128, 0),
        ["Blue"] = Color.FromRgb(0, 0, 255),
        ["Yellow"] = Color.FromRgb(255, 255, 0),
        ["Orange"] = Color.FromRgb(255, 165, 0),
        ["Purple"] = Color.FromRgb(128, 0, 128),
        ["Pink"] = Color.FromRgb(255, 192, 203),
        ["Gray"] = Color.FromRgb(128, 128, 128),
        ["LightGray"] = Color.FromRgb(211, 211, 211),
        ["DarkGray"] = Color.FromRgb(169, 169, 169),
        ["Cyan"] = Color.FromRgb(0, 255, 255),
        ["Magenta"] = Color.FromRgb(255, 0, 255),
        ["Brown"] = Color.FromRgb(165, 42, 42),
        ["Navy"] = Color.FromRgb(0, 0, 128),
        ["Teal"] = Color.FromRgb(0, 128, 128),
        ["Olive"] = Color.FromRgb(128, 128, 0),
        ["Maroon"] = Color.FromRgb(128, 0, 0),
        ["Silver"] = Color.FromRgb(192, 192, 192),
        ["Lime"] = Color.FromRgb(0, 255, 0),
        ["Aqua"] = Color.FromRgb(0, 255, 255),
        ["Fuchsia"] = Color.FromRgb(255, 0, 255),
    };

    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();

        // Try named color
        if (_namedColors.TryGetValue(str, out var namedColor))
        {
            return new SolidColorBrush(namedColor);
        }

        // Try hex color
        if (str.StartsWith('#'))
        {
            var color = ParseHexColor(str);
            return new SolidColorBrush(color);
        }

        // Fall back to the full standard named-color set (every name defined on Colors)
        // so bare names beyond the common fast-path list still resolve instead of throwing.
        if (NamedColorTable.TryGet(str, out var standardColor))
        {
            return new SolidColorBrush(standardColor);
        }

        throw new FormatException($"Invalid brush format: {str}");
    }

    private static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');

        return hex.Length switch
        {
            3 => Color.FromRgb(
                (byte)(Convert.ToByte(hex.Substring(0, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(1, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(2, 1), 16) * 17)),
            4 => Color.FromArgb(
                (byte)(Convert.ToByte(hex.Substring(0, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(1, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(2, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(3, 1), 16) * 17)),
            6 => Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16)),
            8 => Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16)),
            _ => throw new FormatException($"Invalid hex color format: #{hex}")
        };
    }
}

/// <summary>
/// Converts strings to Color values.
/// </summary>
// Parser implementation; the public WPF-compatible converter is
// Jalium.UI.Media.ColorConverter.
internal sealed class ColorConverter : TypeConverter
{
    private static readonly Dictionary<string, Color> _namedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Transparent"] = Color.FromArgb(0, 255, 255, 255),
        ["Black"] = Color.FromRgb(0, 0, 0),
        ["White"] = Color.FromRgb(255, 255, 255),
        ["Red"] = Color.FromRgb(255, 0, 0),
        ["Green"] = Color.FromRgb(0, 128, 0),
        ["Blue"] = Color.FromRgb(0, 0, 255),
        ["Yellow"] = Color.FromRgb(255, 255, 0),
        ["Orange"] = Color.FromRgb(255, 165, 0),
        ["Purple"] = Color.FromRgb(128, 0, 128),
        ["Pink"] = Color.FromRgb(255, 192, 203),
        ["Gray"] = Color.FromRgb(128, 128, 128),
        ["LightGray"] = Color.FromRgb(211, 211, 211),
        ["DarkGray"] = Color.FromRgb(169, 169, 169),
        ["Cyan"] = Color.FromRgb(0, 255, 255),
        ["Magenta"] = Color.FromRgb(255, 0, 255),
        ["Brown"] = Color.FromRgb(165, 42, 42),
        ["Navy"] = Color.FromRgb(0, 0, 128),
        ["Teal"] = Color.FromRgb(0, 128, 128),
        ["Olive"] = Color.FromRgb(128, 128, 0),
        ["Maroon"] = Color.FromRgb(128, 0, 0),
        ["Silver"] = Color.FromRgb(192, 192, 192),
        ["Lime"] = Color.FromRgb(0, 255, 0),
        ["Aqua"] = Color.FromRgb(0, 255, 255),
        ["Fuchsia"] = Color.FromRgb(255, 0, 255),
    };

    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();

        // Try named color
        if (_namedColors.TryGetValue(str, out var namedColor))
        {
            return namedColor;
        }

        // Try hex color
        if (str.StartsWith('#'))
        {
            return ParseHexColor(str);
        }

        // Fall back to the full standard named-color set (every name defined on Colors)
        // so bare names beyond the common fast-path list still resolve instead of throwing.
        if (NamedColorTable.TryGet(str, out var standardColor))
        {
            return standardColor;
        }

        throw new FormatException($"Invalid color format: {str}");
    }

    private static Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');

        return hex.Length switch
        {
            3 => Color.FromRgb(
                (byte)(Convert.ToByte(hex.Substring(0, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(1, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(2, 1), 16) * 17)),
            4 => Color.FromArgb(
                (byte)(Convert.ToByte(hex.Substring(0, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(1, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(2, 1), 16) * 17),
                (byte)(Convert.ToByte(hex.Substring(3, 1), 16) * 17)),
            6 => Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16)),
            8 => Color.FromArgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                Convert.ToByte(hex.Substring(6, 2), 16)),
            _ => throw new FormatException($"Invalid hex color format: #{hex}")
        };
    }
}

/// <summary>
/// Converts strings to GridLength values.
/// </summary>
internal sealed class GridLengthConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();

        if (str.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        if (str.Equals("*", StringComparison.Ordinal))
        {
            return GridLength.Star;
        }

        if (str.EndsWith('*'))
        {
            var factor = double.Parse(str.TrimEnd('*'), CultureInfo.InvariantCulture);
            return new GridLength(factor, GridUnitType.Star);
        }

        return new GridLength(double.Parse(str, CultureInfo.InvariantCulture), GridUnitType.Pixel);
    }
}

/// <summary>
/// Converts strings to <see cref="RowDefinitionCollection"/> values.
/// </summary>
public sealed class RowDefinitionCollectionConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        return value switch
        {
            RowDefinitionCollection collection => collection,
            string str => GridDefinitionParser.ParseRowDefinitions(str),
            _ => null
        };
    }
}

/// <summary>
/// Converts strings to <see cref="ColumnDefinitionCollection"/> values.
/// </summary>
public sealed class ColumnDefinitionCollectionConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        return value switch
        {
            ColumnDefinitionCollection collection => collection,
            string str => GridDefinitionParser.ParseColumnDefinitions(str),
            _ => null
        };
    }
}

/// <summary>
/// Converts strings to HorizontalAlignment values.
/// </summary>
public sealed class HorizontalAlignmentConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return Enum.Parse<HorizontalAlignment>(str, ignoreCase: true);
    }
}

/// <summary>
/// Converts strings to VerticalAlignment values.
/// </summary>
public sealed class VerticalAlignmentConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return Enum.Parse<VerticalAlignment>(str, ignoreCase: true);
    }
}

/// <summary>
/// Converts strings to Orientation values.
/// </summary>
public sealed class OrientationConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return Enum.Parse<Orientation>(str, ignoreCase: true);
    }
}

/// <summary>
/// Converts string type names to Type objects.
/// AOT-compatible: uses XamlTypeRegistry for type lookup.
/// </summary>
public sealed class TypeTypeConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string typeName) return null;

        // Use the static type registry for AOT compatibility
        return XamlTypeRegistry.GetType(typeName);
    }
}

/// <summary>
/// Converts strings to Uri values.
/// </summary>
public sealed class UriValueConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is Uri uri)
        {
            return uri;
        }

        if (value is not string str)
        {
            return null;
        }

        str = str.Trim();
        if (string.IsNullOrEmpty(str))
        {
            return null;
        }

        if (Uri.TryCreate(str, UriKind.RelativeOrAbsolute, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Invalid Uri format: {str}");
    }
}

/// <summary>
/// Converts strings to Duration values.
/// </summary>
public sealed class DurationValueConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str)
            return null;

        str = str.Trim();
        if (str.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
            return AnimationDuration.Automatic;
        if (str.Equals("Forever", StringComparison.OrdinalIgnoreCase))
            return AnimationDuration.Forever;

        return new AnimationDuration(TimeSpan.Parse(str, CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Converts strings to transition property collections.
/// </summary>
public sealed class TransitionPropertyCollectionConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        return value switch
        {
            TransitionPropertyCollection collection => collection,
            string str => TransitionPropertyCollection.Parse(str),
            IEnumerable<string> names => new TransitionPropertyCollection(names),
            _ => null
        };
    }
}

/// <summary>
/// Converts strings to IconElement values.
/// Supports Symbol names (for example "Save" or "Symbol.Save") and raw glyph strings.
/// </summary>
public sealed class IconElementConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        str = str.Trim();
        if (string.IsNullOrEmpty(str)) return null;

        var symbolName = str.StartsWith("Symbol.", StringComparison.OrdinalIgnoreCase)
            ? str.Substring("Symbol.".Length)
            : str;

        if (Enum.TryParse<Symbol>(symbolName, ignoreCase: true, out var symbol))
        {
            return new SymbolIcon(symbol);
        }

        return new FontIcon { Glyph = str };
    }
}

/// <summary>
/// Converts strings to PointCollection values.
/// Format: "x1,y1 x2,y2 x3,y3" (space-separated coordinate pairs).
/// </summary>
internal sealed class PointCollectionConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return MediaPointCollection.Parse(str);
    }
}

/// <summary>
/// Converts strings such as "10, 30, 60" or "5 3" to <see cref="MediaDoubleCollection"/>.
/// The type already carries a <c>[TypeConverter]</c> attribute, but
/// <see cref="TypeConverterRegistry.ConvertValue"/> only consults its own table, so without
/// this entry every DoubleCollection attribute in markup (Slider.Ticks, TickBar.Ticks,
/// Shape.StrokeDashArray) converted to null and was dropped without a diagnostic.
/// </summary>
internal sealed class DoubleCollectionValueConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return MediaDoubleCollection.Parse(str);
    }
}

/// <summary>
/// Converts strings to <see cref="Point"/> values. Accepts the standard XAML
/// "x,y" format (whitespace and comma both serve as separators) and the
/// space-separated "x y" form. Without this converter, properties like
/// <see cref="LinearGradientBrush.StartPoint"/> can't be set from jalxaml
/// (parser falls through to the unspecialised path which doesn't know how
/// to materialise a Point), and the brush silently ends up with default
/// endpoints — the gradient looks correct only by accident.
/// </summary>
internal sealed class PointConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        return ParsePoint(str);
    }

    internal static Point ParsePoint(string str)
    {
        var parts = str.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException($"Point requires exactly two components (got '{str}').");
        var x = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var y = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return new Point(x, y);
    }
}

/// <summary>
/// Converts strings to <see cref="Vector"/> values. Same "x,y" or "x y"
/// grammar as <see cref="PointConverter"/>.
/// </summary>
internal sealed class VectorConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is not string str) return null;
        var parts = str.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new FormatException($"Vector requires exactly two components (got '{str}').");
        var x = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var y = double.Parse(parts[1], CultureInfo.InvariantCulture);
        return new Vector(x, y);
    }
}

/// <summary>
/// Converts strings to <see cref="Size"/> values. Format: "width,height"
/// or "width height".
/// </summary>
internal sealed class SizeConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        return value is string source ? Size.Parse(source) : null;
    }
}

/// <summary>
/// Converts the standard row-major 4x5 color-matrix text form into a
/// <see cref="ColorMatrix"/>. Values may be separated by whitespace, commas,
/// or semicolons.
/// </summary>
internal sealed class ColorMatrixConverter : TypeConverter
{
    public override object? ConvertFrom(object? value)
    {
        if (value is ColorMatrix matrix)
            return matrix;
        if (value is not string source)
            return null;

        source = source.Trim();
        if (string.Equals(source, "Identity", StringComparison.OrdinalIgnoreCase))
            return ColorMatrix.Identity;

        var parts = source.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 20)
        {
            throw new FormatException(
                $"A ColorMatrix requires exactly 20 row-major values; received {parts.Length}.");
        }

        Span<float> values = stackalloc float[20];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = float.Parse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return new ColorMatrix
        {
            M11 = values[0], M12 = values[1], M13 = values[2], M14 = values[3], M15 = values[4],
            M21 = values[5], M22 = values[6], M23 = values[7], M24 = values[8], M25 = values[9],
            M31 = values[10], M32 = values[11], M33 = values[12], M34 = values[13], M35 = values[14],
            M41 = values[15], M42 = values[16], M43 = values[17], M44 = values[18], M45 = values[19],
        };
    }
}

/// <summary>
/// Registry of type converters.
/// </summary>
/// <summary>
/// Converts font-weight strings ("Bold", "SemiBold", "700", …) for runtime-parsed JALXAML.
///
/// <para>FontWeight/FontStyle/FontStretch are structs, so <see cref="TypeConverterRegistry.ConvertValue"/>'s
/// generic enum branch never matches them; without these entries the registry returned
/// <see langword="null"/> and the attribute was silently dropped.
/// The parsing itself delegates to the WPF-parity <see cref="Jalium.UI.FontWeightConverter"/>
/// (a System.ComponentModel converter — a different hierarchy this registry cannot store
/// directly), keeping both paths in agreement. Invalid input yields null (property keeps its
/// default) matching the other converters here. Compiled markup rendered the weight while the
/// same markup parsed at runtime (designer preview, hot reload) silently stayed Normal.</para>
/// </summary>
internal sealed class FontWeightTypeConverter : TypeConverter
{
    private static readonly Jalium.UI.FontWeightConverter Parity = new();

    public override object? ConvertFrom(object? value)
    {
        if (value is FontWeight weight) return weight;
        if (value is not string str || string.IsNullOrWhiteSpace(str)) return null;
        try { return Parity.ConvertFrom(null, CultureInfo.InvariantCulture, str); }
        catch (FormatException) { return null; }
    }
}

/// <summary>Converts font-style strings ("Italic", "Oblique", "Normal") — see <see cref="FontWeightTypeConverter"/>.</summary>
internal sealed class FontStyleTypeConverter : TypeConverter
{
    private static readonly Jalium.UI.FontStyleConverter Parity = new();

    public override object? ConvertFrom(object? value)
    {
        if (value is FontStyle style) return style;
        if (value is not string str || string.IsNullOrWhiteSpace(str)) return null;
        try { return Parity.ConvertFrom(null, CultureInfo.InvariantCulture, str); }
        catch (FormatException) { return null; }
    }
}

/// <summary>Converts font-stretch strings ("Condensed", "Expanded", …) — see <see cref="FontWeightTypeConverter"/>.</summary>
internal sealed class FontStretchTypeConverter : TypeConverter
{
    private static readonly Jalium.UI.FontStretchConverter Parity = new();

    public override object? ConvertFrom(object? value)
    {
        if (value is FontStretch stretch) return stretch;
        if (value is not string str || string.IsNullOrWhiteSpace(str)) return null;
        try { return Parity.ConvertFrom(null, CultureInfo.InvariantCulture, str); }
        catch (FormatException) { return null; }
    }
}

public static class TypeConverterRegistry
{
    private static readonly Dictionary<Type, TypeConverter> _converters = new()
    {
        [typeof(FontWeight)] = new FontWeightTypeConverter(),
        [typeof(FontStyle)] = new FontStyleTypeConverter(),
        [typeof(FontStretch)] = new FontStretchTypeConverter(),
        [typeof(Thickness)] = new ThicknessConverter(),
        [typeof(CornerRadius)] = new CornerRadiusConverter(),
        [typeof(Brush)] = new BrushConverter(),
        [typeof(SolidColorBrush)] = new BrushConverter(),
        [typeof(Color)] = new ColorConverter(),
        [typeof(GridLength)] = new GridLengthConverter(),
        [typeof(RowDefinitionCollection)] = new RowDefinitionCollectionConverter(),
        [typeof(ColumnDefinitionCollection)] = new ColumnDefinitionCollectionConverter(),
        [typeof(HorizontalAlignment)] = new HorizontalAlignmentConverter(),
        [typeof(VerticalAlignment)] = new VerticalAlignmentConverter(),
        [typeof(Orientation)] = new OrientationConverter(),
        [typeof(AnimationDuration)] = new DurationValueConverter(),
        [typeof(TransitionPropertyCollection)] = new TransitionPropertyCollectionConverter(),
        [typeof(Uri)] = new UriValueConverter(),
        [typeof(Type)] = new TypeTypeConverter(),
        [typeof(IconElement)] = new IconElementConverter(),
        [typeof(MediaPointCollection)] = new PointCollectionConverter(),
        [typeof(MediaDoubleCollection)] = new DoubleCollectionValueConverter(),
        [typeof(Point)] = new PointConverter(),
        [typeof(Vector)] = new VectorConverter(),
        [typeof(Size)] = new SizeConverter(),
        [typeof(Geometry)] = new GeometryTypeConverter(),
        [typeof(ColorMatrix)] = new ColorMatrixConverter(),
        // 基类一条即可：GetConverter 找不到精确匹配时会按 IsAssignableFrom 回退，
        // 所以 BitmapImage / SvgImage 这些派生类型的属性也一并覆盖。
        [typeof(ImageSource)] = new ImageSourceTypeConverter(),
    };

    /// <summary>
    /// Gets a type converter for the specified type.
    /// </summary>
    public static TypeConverter? GetConverter(Type type)
    {
        if (_converters.TryGetValue(type, out var converter))
        {
            return converter;
        }

        // Check for base types/interfaces
        foreach (var (converterType, converterInstance) in _converters)
        {
            if (converterType.IsAssignableFrom(type))
            {
                return converterInstance;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a type converter for the specified type.
    /// </summary>
    public static void Register(Type type, TypeConverter converter)
    {
        _converters[type] = converter;
    }

    /// <summary>
    /// Converts a string value to the target type.
    /// </summary>
    public static object? ConvertValue(string value, Type targetType)
    {
        if (targetType == typeof(string) || targetType == typeof(object))
            return value;

        if (targetType == typeof(FontFamily))
            return new FontFamily(value);

        if (targetType == typeof(double))
        {
            // Handle XAML special values
            if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
                return double.NaN;
            if (string.Equals(value, "NaN", StringComparison.OrdinalIgnoreCase))
                return double.NaN;
            if (string.Equals(value, "Infinity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "+Infinity", StringComparison.OrdinalIgnoreCase))
                return double.PositiveInfinity;
            if (string.Equals(value, "-Infinity", StringComparison.OrdinalIgnoreCase))
                return double.NegativeInfinity;
            if (double.TryParse(value, CultureInfo.InvariantCulture, out var d))
                return d;
            // Fall through to TypeConverter
        }

        if (targetType == typeof(float))
        {
            if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "NaN", StringComparison.OrdinalIgnoreCase))
                return float.NaN;
            if (float.TryParse(value, CultureInfo.InvariantCulture, out var f))
                return f;
        }

        if (targetType == typeof(int))
        {
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var i))
                return i;
        }

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var b))
                return b;
        }

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, value, ignoreCase: true, out var e))
                return e;
        }

        if (targetType == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts))
                return ts;
        }

        // Remaining primitive scalars (long/short/byte/uint/decimal/char …): without this bridge
        // any DP of such a type silently dropped its attribute in runtime-parsed markup, because
        // only double/float/int/bool have dedicated branches above and no converter is registered.
        if (targetType.IsPrimitive || targetType == typeof(decimal))
        {
            try
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // Fall through to the converter lookup.
            }
        }

        var converter = GetConverter(targetType);
        if (converter != null)
        {
            return converter.ConvertFrom(value);
        }

        // Try TypeConverter attribute (future enhancement)
        return null;
    }
}
