using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI;

/// <summary>
/// Converts <see cref="Duration"/> values to and from their XAML string representation.
/// </summary>
public class DurationConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor);

    public override object? ConvertFrom(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object value)
    {
        if (value is string source)
        {
            string text = source.Trim();
            if (text.Equals("Automatic", StringComparison.Ordinal))
            {
                return Duration.Automatic;
            }

            if (text.Equals("Forever", StringComparison.Ordinal))
            {
                return Duration.Forever;
            }

            try
            {
                return new Duration(TimeSpan.Parse(text, culture));
            }
            catch (FormatException exception)
            {
                throw new FormatException($"{text} is not a valid value for {nameof(TimeSpan)}.", exception);
            }
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is not Duration duration
            || (destinationType != typeof(string) && destinationType != typeof(InstanceDescriptor)))
        {
            return base.ConvertTo(context, culture, value, destinationType);
        }

        if (destinationType == typeof(string))
        {
            return duration.ToStringInvariant();
        }

        if (duration.HasTimeSpan)
        {
            ConstructorInfo constructor = typeof(Duration).GetConstructor([typeof(TimeSpan)])!;
            return new InstanceDescriptor(constructor, new object[] { duration.TimeSpan });
        }

        PropertyInfo property = typeof(Duration).GetProperty(
            duration == Duration.Forever ? nameof(Duration.Forever) : nameof(Duration.Automatic))!;
        return new InstanceDescriptor(property, null);
    }
}
