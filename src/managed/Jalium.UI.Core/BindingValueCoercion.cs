using System.Globalization;

namespace Jalium.UI;

internal static class BindingValueCoercion
{
    public static object? Coerce(object? value, Type targetType, CultureInfo culture)
    {
        if (value == null ||
            ReferenceEquals(value, DependencyProperty.UnsetValue) ||
            ReferenceEquals(value, Binding.DoNothing))
        {
            return value;
        }

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType == typeof(object) || underlyingType.IsInstanceOfType(value))
            return value;

        if (underlyingType == typeof(string))
            return ConvertToString(value, culture);

        if (value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue) && Nullable.GetUnderlyingType(targetType) != null)
                return null;

            if (underlyingType.IsEnum && Enum.TryParse(underlyingType, stringValue, ignoreCase: true, out var enumValue))
                return enumValue;

            try
            {
                return System.Convert.ChangeType(stringValue, underlyingType, culture);
            }
            catch
            {
                return value;
            }
        }

        try
        {
            return System.Convert.ChangeType(value, underlyingType, culture);
        }
        catch
        {
            return value;
        }
    }

    private static string ConvertToString(object value, CultureInfo culture)
    {
        if (value is string stringValue)
            return stringValue;

        if (value is IFormattable formattable)
            return formattable.ToString(null, culture) ?? string.Empty;

        return value.ToString() ?? string.Empty;
    }
}
