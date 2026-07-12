using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Converts values to and from <see cref="Thickness"/> instances.
/// </summary>
public class ThicknessConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? typeDescriptorContext, Type sourceType)
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

    public override bool CanConvertTo(ITypeDescriptorContext? typeDescriptorContext, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor);

    public override object ConvertFrom(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object source)
    {
        if (source is null)
        {
            throw GetConvertFromException(source);
        }

        cultureInfo ??= CultureInfo.CurrentCulture;
        if (source is string text)
        {
            return Parse(text, cultureInfo);
        }

        double uniformLength = source is double length
            ? length
            : Convert.ToDouble(source, cultureInfo);
        return new Thickness(uniformLength);
    }

    public override object ConvertTo(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is not Thickness thickness)
        {
            throw new ArgumentException(
                $"Value must be a {typeof(Thickness).FullName}.",
                nameof(value));
        }

        cultureInfo ??= CultureInfo.CurrentCulture;
        if (destinationType == typeof(string))
        {
            string separator = LayoutConverterTokenizer.GetNumericListSeparator(cultureInfo).ToString();
            return string.Join(separator,
                FormatLength(thickness.Left, cultureInfo),
                FormatLength(thickness.Top, cultureInfo),
                FormatLength(thickness.Right, cultureInfo),
                FormatLength(thickness.Bottom, cultureInfo));
        }

        if (destinationType == typeof(InstanceDescriptor))
        {
            var constructor = typeof(Thickness).GetConstructor(new[]
            {
                typeof(double), typeof(double), typeof(double), typeof(double),
            })!;
            return new InstanceDescriptor(constructor, new object[]
            {
                thickness.Left, thickness.Top, thickness.Right, thickness.Bottom,
            });
        }

        throw new ArgumentException(
            $"Cannot convert {typeof(Thickness).FullName} to {destinationType.FullName}.",
            nameof(destinationType));
    }

    private static Thickness Parse(string source, CultureInfo culture)
    {
        List<string> tokens = LayoutConverterTokenizer.Tokenize(source, culture);
        Span<double> lengths = stackalloc double[4];
        for (int i = 0; i < tokens.Count && i < lengths.Length; i++)
        {
            lengths[i] = LengthConverter.ParseLength(tokens[i], culture);
        }

        return tokens.Count switch
        {
            1 => new Thickness(lengths[0]),
            2 => new Thickness(lengths[0], lengths[1], lengths[0], lengths[1]),
            4 => new Thickness(lengths[0], lengths[1], lengths[2], lengths[3]),
            _ => throw new FormatException($"'{source}' is not a valid Thickness value."),
        };
    }

    private static string FormatLength(double value, CultureInfo culture) =>
        double.IsNaN(value) ? "Auto" : value.ToString(culture);
}

/// <summary>
/// Converts values to and from <see cref="CornerRadius"/> instances.
/// </summary>
public class CornerRadiusConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? typeDescriptorContext, Type sourceType)
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

    public override bool CanConvertTo(ITypeDescriptorContext? typeDescriptorContext, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor);

    public override object ConvertFrom(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object source)
    {
        if (source is null)
        {
            throw GetConvertFromException(source);
        }

        cultureInfo ??= CultureInfo.CurrentCulture;
        if (source is string text)
        {
            return Parse(text, cultureInfo);
        }

        return new CornerRadius(Convert.ToDouble(source, cultureInfo));
    }

    public override object ConvertTo(
        ITypeDescriptorContext? typeDescriptorContext,
        CultureInfo? cultureInfo,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is not CornerRadius cornerRadius)
        {
            throw new ArgumentException(
                $"Value must be a {typeof(CornerRadius).FullName}.",
                nameof(value));
        }

        cultureInfo ??= CultureInfo.CurrentCulture;
        if (destinationType == typeof(string))
        {
            string separator = LayoutConverterTokenizer.GetNumericListSeparator(cultureInfo).ToString();
            return string.Join(separator,
                cornerRadius.TopLeft.ToString(cultureInfo),
                cornerRadius.TopRight.ToString(cultureInfo),
                cornerRadius.BottomRight.ToString(cultureInfo),
                cornerRadius.BottomLeft.ToString(cultureInfo));
        }

        if (destinationType == typeof(InstanceDescriptor))
        {
            var constructor = typeof(CornerRadius).GetConstructor(new[]
            {
                typeof(double), typeof(double), typeof(double), typeof(double),
            })!;
            return new InstanceDescriptor(constructor, new object[]
            {
                cornerRadius.TopLeft,
                cornerRadius.TopRight,
                cornerRadius.BottomRight,
                cornerRadius.BottomLeft,
            });
        }

        throw new ArgumentException(
            $"Cannot convert {typeof(CornerRadius).FullName} to {destinationType.FullName}.",
            nameof(destinationType));
    }

    private static CornerRadius Parse(string source, CultureInfo culture)
    {
        List<string> tokens = LayoutConverterTokenizer.Tokenize(source, culture);
        Span<double> radii = stackalloc double[4];
        for (int i = 0; i < tokens.Count && i < radii.Length; i++)
        {
            radii[i] = double.Parse(tokens[i], culture);
        }

        return tokens.Count switch
        {
            1 => new CornerRadius(radii[0]),
            4 => new CornerRadius(radii[0], radii[1], radii[2], radii[3]),
            _ => throw new FormatException($"'{source}' is not a valid CornerRadius value."),
        };
    }
}

internal static class LayoutConverterTokenizer
{
    internal static List<string> Tokenize(string source, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(culture);

        char separator = GetNumericListSeparator(culture);
        var tokens = new List<string>(4);
        int index = 0;
        SkipWhitespace(source, ref index);

        while (index < source.Length)
        {
            if (source[index] == separator)
            {
                throw new FormatException($"'{source}' contains an empty component.");
            }

            int start = index;
            while (index < source.Length
                && !char.IsWhiteSpace(source[index])
                && source[index] != separator)
            {
                index++;
            }

            tokens.Add(source[start..index]);
            bool consumedWhitespace = SkipWhitespace(source, ref index);

            if (index < source.Length && source[index] == separator)
            {
                index++;
                SkipWhitespace(source, ref index);
                if (index >= source.Length || source[index] == separator)
                {
                    throw new FormatException($"'{source}' contains an empty component.");
                }
            }
            else if (!consumedWhitespace && index < source.Length)
            {
                throw new FormatException($"'{source}' contains an invalid separator.");
            }
        }

        return tokens;
    }

    internal static char GetNumericListSeparator(CultureInfo culture) =>
        culture.NumberFormat.NumberDecimalSeparator.Contains(',', StringComparison.Ordinal) ? ';' : ',';

    private static bool SkipWhitespace(string source, ref int index)
    {
        int start = index;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index != start;
    }
}
