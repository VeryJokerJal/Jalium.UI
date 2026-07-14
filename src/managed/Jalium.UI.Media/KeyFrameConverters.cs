using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI;

/// <summary>Converts <see cref="KeyTime"/> values to and from strings.</summary>
public sealed class KeyTimeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        text = text.Trim();
        if (text.Length == 0)
            throw new IndexOutOfRangeException();
        if (text.Equals(nameof(KeyTime.Uniform), StringComparison.OrdinalIgnoreCase))
        {
            return KeyTime.Uniform;
        }

        if (text.Equals(nameof(KeyTime.Paced), StringComparison.OrdinalIgnoreCase))
        {
            return KeyTime.Paced;
        }

        culture ??= CultureInfo.CurrentCulture;
        if (text.EndsWith('%'))
        {
            double percent = double.Parse(
                text[..^1].Trim(),
                NumberStyles.Float,
                culture);
            return KeyTime.FromPercent(percent / 100.0);
        }

        return KeyTime.FromTimeSpan(TimeSpan.Parse(text, culture));
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is KeyTime keyTime)
        {
            if (destinationType == typeof(string))
            {
                culture ??= CultureInfo.CurrentCulture;
                return keyTime.Type switch
                {
                    KeyTimeType.Uniform => nameof(KeyTime.Uniform),
                    KeyTimeType.Paced => nameof(KeyTime.Paced),
                    KeyTimeType.Percent => string.Concat(
                        (keyTime.Percent * 100.0).ToString(culture),
                        "%"),
                    _ => keyTime.TimeSpan.ToString(null, culture),
                };
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                return keyTime.Type switch
                {
                    KeyTimeType.Uniform => new InstanceDescriptor(
                        typeof(KeyTime).GetProperty(nameof(KeyTime.Uniform))!,
                        null),
                    KeyTimeType.Paced => new InstanceDescriptor(
                        typeof(KeyTime).GetProperty(nameof(KeyTime.Paced))!,
                        null),
                    KeyTimeType.Percent => new InstanceDescriptor(
                        typeof(KeyTime).GetMethod(nameof(KeyTime.FromPercent))!,
                        new object[] { keyTime.Percent }),
                    _ => new InstanceDescriptor(
                        typeof(KeyTime).GetMethod(nameof(KeyTime.FromTimeSpan))!,
                        new object[] { keyTime.TimeSpan }),
                };
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>Converts <see cref="KeySpline"/> values to and from strings.</summary>
public sealed class KeySplineConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string)
            || destinationType == typeof(InstanceDescriptor);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        culture ??= CultureInfo.CurrentCulture;
        string separator = culture.TextInfo.ListSeparator;
        string normalized = text.Trim();
        if (normalized.Length == 0)
            throw new InvalidOperationException("The input ended before a KeySpline value was found.");
        string[] tokens;

        if (separator == ",")
        {
            tokens = normalized.Split(
                [',', ' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else
        {
            tokens = normalized.Split(
                [separator, " ", "\t", "\r", "\n"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (tokens.Length != 4)
        {
            throw new FormatException(
                $"Cannot parse KeySpline from '{text}'. Expected four coordinates.");
        }

        return new KeySpline(
            double.Parse(tokens[0], NumberStyles.Float, culture),
            double.Parse(tokens[1], NumberStyles.Float, culture),
            double.Parse(tokens[2], NumberStyles.Float, culture),
            double.Parse(tokens[3], NumberStyles.Float, culture));
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is KeySpline keySpline)
        {
            if (destinationType == typeof(string))
            {
                culture ??= CultureInfo.CurrentCulture;
                string separator = culture.TextInfo.ListSeparator;
                return string.Join(
                    separator,
                    keySpline.ControlPoint1.X.ToString(culture),
                    keySpline.ControlPoint1.Y.ToString(culture),
                    keySpline.ControlPoint2.X.ToString(culture),
                    keySpline.ControlPoint2.Y.ToString(culture));
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                return new InstanceDescriptor(
                    typeof(KeySpline).GetConstructor(
                        [typeof(double), typeof(double), typeof(double), typeof(double)])!,
                    new object[]
                    {
                        keySpline.ControlPoint1.X,
                        keySpline.ControlPoint1.Y,
                        keySpline.ControlPoint2.X,
                        keySpline.ControlPoint2.Y,
                    });
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
