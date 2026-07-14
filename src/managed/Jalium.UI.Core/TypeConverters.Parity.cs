using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Converts WPF length literals to and from device-independent pixels.
/// </summary>
public class LengthConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return Type.GetTypeCode(sourceType) is TypeCode.String
            or TypeCode.Decimal
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Int16
            or TypeCode.Int32
            or TypeCode.Int64
            or TypeCode.UInt16
            or TypeCode.UInt32
            or TypeCode.UInt64;
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor);
    }

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is null)
        {
            throw GetConvertFromException(value);
        }

        culture ??= CultureInfo.CurrentCulture;

        return value is string text
            ? ParseLength(text, culture)
            : Convert.ToDouble(value, culture);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        culture ??= CultureInfo.CurrentCulture;

        if (value is double length)
        {
            if (destinationType == typeof(string))
            {
                return double.IsNaN(length)
                    ? "Auto"
                    : length.ToString(culture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                var constructor = typeof(double).GetConstructor([typeof(double)])!;
                return new InstanceDescriptor(constructor, new object?[] { length });
            }
        }

        throw GetConvertToException(value, destinationType);
    }

    internal static double ParseLength(string text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(culture);

        ReadOnlySpan<char> value = text.AsSpan().Trim();
        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return double.NaN;
        }

        double factor = 1d;
        if (TryStripUnit(ref value, "px"))
        {
            factor = 1d;
        }
        else if (TryStripUnit(ref value, "in"))
        {
            factor = 96d;
        }
        else if (TryStripUnit(ref value, "cm"))
        {
            factor = 96d / 2.54d;
        }
        else if (TryStripUnit(ref value, "pt"))
        {
            factor = 96d / 72d;
        }

        value = value.Trim();
        return value.IsEmpty ? 0d : double.Parse(value, culture) * factor;
    }

    private static bool TryStripUnit(ref ReadOnlySpan<char> value, string unit)
    {
        if (!value.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = value[..^unit.Length];
        return true;
    }
}

/// <summary>
/// Converts values used by FontSize dependency properties.
/// </summary>
public class FontSizeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string)
            || sourceType == typeof(int)
            || sourceType == typeof(float)
            || sourceType == typeof(double);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);
    }

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is null)
        {
            throw GetConvertFromException(value);
        }

        culture ??= CultureInfo.CurrentCulture;

        return value switch
        {
            string text => LengthConverter.ParseLength(text, culture),
            int integer => (double)integer,
            float single => (double)single,
            double number => number,
            _ => null!,
        };
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        culture ??= CultureInfo.CurrentCulture;

        if (value is double size)
        {
            if (destinationType == typeof(string)) return size.ToString(culture);
            if (destinationType == typeof(int)) return (int)size;
            if (destinationType == typeof(float)) return (float)size;
            if (destinationType == typeof(double)) return size;
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}

/// <summary>
/// Prevents Window.DialogResult from being assigned by a markup converter.
/// </summary>
public class DialogResultConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => false;

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => false;

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        throw new InvalidOperationException("DialogResult cannot be set through markup conversion.");
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        throw new InvalidOperationException("DialogResult cannot be serialized through markup conversion.");
    }
}

/// <summary>
/// Converts FontWeight values from their names or OpenType numeric values.
/// </summary>
public sealed class FontWeightConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is null)
        {
            throw GetConvertFromException(value);
        }

        if (value is not string text)
        {
            throw new ArgumentException("The value must be a string.", nameof(value));
        }

        return Parse(text, culture ?? CultureInfo.CurrentCulture);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is FontWeight weight)
        {
            if (destinationType == typeof(string))
            {
                return weight.ToString(null, culture ?? CultureInfo.CurrentCulture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                return CreateDescriptor(weight);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    private static FontWeight Parse(string value, IFormatProvider provider)
    {
        string token = value.Trim();
        if (int.TryParse(token, NumberStyles.Integer, provider, out int numeric))
        {
            return FontWeight.FromOpenTypeWeight(numeric);
        }

        return token.ToUpperInvariant() switch
        {
            "THIN" => FontWeights.Thin,
            "EXTRALIGHT" => FontWeights.ExtraLight,
            "ULTRALIGHT" => FontWeights.UltraLight,
            "LIGHT" => FontWeights.Light,
            "NORMAL" => FontWeights.Normal,
            "REGULAR" => FontWeights.Regular,
            "MEDIUM" => FontWeights.Medium,
            "DEMIBOLD" => FontWeights.DemiBold,
            "SEMIBOLD" => FontWeights.SemiBold,
            "BOLD" => FontWeights.Bold,
            "EXTRABOLD" => FontWeights.ExtraBold,
            "ULTRABOLD" => FontWeights.UltraBold,
            "BLACK" => FontWeights.Black,
            "HEAVY" => FontWeights.Heavy,
            "EXTRABLACK" => FontWeights.ExtraBlack,
            "ULTRABLACK" => FontWeights.UltraBlack,
            _ => throw new FormatException($"'{value}' is not a valid font weight."),
        };
    }

    private static InstanceDescriptor CreateDescriptor(FontWeight value)
    {
        var method = typeof(FontWeight).GetMethod(
            nameof(FontWeight.FromOpenTypeWeight),
            new[] { typeof(int) })!;
        return new InstanceDescriptor(method, new object[] { value.ToOpenTypeWeight() });
    }
}

/// <summary>
/// Converts FontStyle values from their well-known names.
/// </summary>
public sealed class FontStyleConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is null)
        {
            throw GetConvertFromException(value);
        }

        if (value is not string text)
        {
            throw new ArgumentException("The value must be a string.", nameof(value));
        }

        return Parse(text);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is FontStyle style)
        {
            if (destinationType == typeof(string))
            {
                return style.ToString(null, culture ?? CultureInfo.CurrentCulture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                return CreateDescriptor(style);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    private static FontStyle Parse(string value)
    {
        return value.Trim().ToUpperInvariant() switch
        {
            "NORMAL" => FontStyles.Normal,
            "OBLIQUE" => FontStyles.Oblique,
            "ITALIC" => FontStyles.Italic,
            _ => throw new FormatException($"'{value}' is not a valid font style."),
        };
    }

    private static InstanceDescriptor CreateDescriptor(FontStyle value)
    {
        var constructor = typeof(FontStyle).GetConstructor([typeof(int)])!;
        return new InstanceDescriptor(constructor, new object?[] { value.ToOpenTypeStyle() });
    }
}

/// <summary>
/// Converts FontStretch values from their names or OpenType numeric values.
/// </summary>
public sealed class FontStretchConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is null)
        {
            throw GetConvertFromException(value);
        }

        if (value is not string text)
        {
            throw new ArgumentException("The value must be a string.", nameof(value));
        }

        return Parse(text, culture ?? CultureInfo.CurrentCulture);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (value is FontStretch stretch)
        {
            if (destinationType == typeof(string))
            {
                return stretch.ToString(null, culture ?? CultureInfo.CurrentCulture);
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                return CreateDescriptor(stretch);
            }
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    private static FontStretch Parse(string value, IFormatProvider provider)
    {
        string token = value.Trim();
        if (int.TryParse(token, NumberStyles.Integer, provider, out int numeric))
        {
            return FontStretch.FromOpenTypeStretch(numeric);
        }

        return token.ToUpperInvariant() switch
        {
            "ULTRACONDENSED" => FontStretches.UltraCondensed,
            "EXTRACONDENSED" => FontStretches.ExtraCondensed,
            "CONDENSED" => FontStretches.Condensed,
            "SEMICONDENSED" => FontStretches.SemiCondensed,
            "NORMAL" or "MEDIUM" => FontStretches.Normal,
            "SEMIEXPANDED" => FontStretches.SemiExpanded,
            "EXPANDED" => FontStretches.Expanded,
            "EXTRAEXPANDED" => FontStretches.ExtraExpanded,
            "ULTRAEXPANDED" => FontStretches.UltraExpanded,
            _ => throw new FormatException($"'{value}' is not a valid font stretch."),
        };
    }

    private static InstanceDescriptor CreateDescriptor(FontStretch value)
    {
        var method = typeof(FontStretch).GetMethod(
            nameof(FontStretch.FromOpenTypeStretch),
            new[] { typeof(int) })!;
        return new InstanceDescriptor(method, new object[] { value.ToOpenTypeStretch() });
    }
}
