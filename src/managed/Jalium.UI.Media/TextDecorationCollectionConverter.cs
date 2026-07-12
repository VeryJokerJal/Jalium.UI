using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>
/// Converts predefined text-decoration names into a <see cref="TextDecorationCollection"/>.
/// </summary>
public sealed class TextDecorationCollectionConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(InstanceDescriptor);
    }

    /// <inheritdoc />
    public override object ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object input)
    {
        if (input is null)
        {
            throw GetConvertFromException(input);
        }

        if (input is not string text)
        {
            throw new ArgumentException("The value must be a string.", nameof(input));
        }

        return ConvertFromString(text)!;
    }

    /// <summary>
    /// Converts a comma-delimited list of predefined text-decoration names.
    /// </summary>
    /// <remarks>
    /// The conversion is case-insensitive. An empty string or <c>None</c> produces an
    /// empty collection; duplicate and unrecognized names are rejected.
    /// </remarks>
    public static new TextDecorationCollection? ConvertFromString(string? text)
    {
        if (text is null)
        {
            return null;
        }

        ReadOnlySpan<char> source = text.AsSpan().Trim();
        if (source.IsEmpty || source.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return new TextDecorationCollection();
        }

        var result = new TextDecorationCollection();
        Decorations matched = Decorations.None;

        foreach (Range segment in source.Split(','))
        {
            ReadOnlySpan<char> name = source[segment].Trim();
            if (name.Equals("Overline", StringComparison.OrdinalIgnoreCase)
                && !matched.HasFlag(Decorations.Overline))
            {
                result.Add(TextDecorations.OverLine[0]);
                matched |= Decorations.Overline;
            }
            else if (name.Equals("Baseline", StringComparison.OrdinalIgnoreCase)
                && !matched.HasFlag(Decorations.Baseline))
            {
                result.Add(TextDecorations.Baseline[0]);
                matched |= Decorations.Baseline;
            }
            else if (name.Equals("Underline", StringComparison.OrdinalIgnoreCase)
                && !matched.HasFlag(Decorations.Underline))
            {
                result.Add(TextDecorations.Underline[0]);
                matched |= Decorations.Underline;
            }
            else if (name.Equals("Strikethrough", StringComparison.OrdinalIgnoreCase)
                && !matched.HasFlag(Decorations.Strikethrough))
            {
                result.Add(TextDecorations.Strikethrough[0]);
                matched |= Decorations.Strikethrough;
            }
            else
            {
                throw new ArgumentException(
                    $"The requested TextDecorationCollection string is not valid: '{text}'.");
            }
        }

        return result;
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (destinationType == typeof(InstanceDescriptor)
            && value is IEnumerable<TextDecoration>)
        {
            ConstructorInfo constructor = typeof(TextDecorationCollection).GetConstructor(
                [typeof(IEnumerable<TextDecoration>)])!;

            return new InstanceDescriptor(constructor, new object?[] { value });
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    [Flags]
    private enum Decorations : byte
    {
        None = 0,
        Overline = 1 << 0,
        Baseline = 1 << 1,
        Underline = 1 << 2,
        Strikethrough = 1 << 3,
    }
}
