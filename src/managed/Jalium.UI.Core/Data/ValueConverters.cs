using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Converts a boolean value to a Visibility value.
/// </summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether to invert the conversion.
    /// </summary>
    public bool IsInverted { get; set; }

    /// <summary>
    /// Gets or sets the visibility to use when the value is false (or true if inverted).
    /// </summary>
    public Visibility FalseVisibility { get; set; } = Visibility.Collapsed;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var boolValue = value is bool b && b;
        if (IsInverted) boolValue = !boolValue;
        return boolValue ? Visibility.Visible : FalseVisibility;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isVisible = value is Visibility v && v == Visibility.Visible;
        return IsInverted ? !isVisible : isVisible;
    }
}

/// <summary>
/// Inverts a boolean value.
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : value;
    }
}

/// <summary>
/// Converts null to a boolean value.
/// </summary>
public sealed class NullToBooleanConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the value to return when the input is null.
    /// </summary>
    public bool NullValue { get; set; } = false;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? NullValue : !NullValue;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

/// <summary>
/// Converts null to a Visibility value.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the visibility to use when the value is null.
    /// </summary>
    public Visibility NullVisibility { get; set; } = Visibility.Collapsed;

    /// <summary>
    /// Gets or sets the visibility to use when the value is not null.
    /// </summary>
    public Visibility NotNullVisibility { get; set; } = Visibility.Visible;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value == null ? NullVisibility : NotNullVisibility;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}

/// <summary>
/// Converts a string to uppercase or lowercase.
/// </summary>
public sealed class StringCaseConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets a value indicating whether to convert to uppercase.
    /// </summary>
    public bool ToUpper { get; set; } = true;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            return ToUpper ? s.ToUpper(culture) : s.ToLower(culture);
        }
        return value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

/// <summary>
/// Compares two values for equality.
/// </summary>
public sealed class EqualityConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the value to compare against.
    /// </summary>
    public object? CompareValue { get; set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var compareWith = parameter ?? CompareValue;
        return Equals(value, compareWith);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return parameter ?? CompareValue;
        }
        return null;
    }
}

/// <summary>
/// Multiplies a numeric value by a factor.
/// </summary>
public sealed class MultiplyConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the multiplication factor.
    /// </summary>
    public double Factor { get; set; } = 1.0;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var factor = parameter is double d ? d : Factor;

        if (value is double dValue)
            return dValue * factor;
        if (value is int iValue)
            return iValue * factor;
        if (value is float fValue)
            return fValue * factor;

        return value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var factor = parameter is double d ? d : Factor;
        if (factor == 0) return 0;

        if (value is double dValue)
            return dValue / factor;
        if (value is int iValue)
            return iValue / factor;
        if (value is float fValue)
            return fValue / factor;

        return value;
    }
}

/// <summary>
/// Adds a constant to a numeric value.
/// </summary>
public sealed class AddConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the value to add.
    /// </summary>
    public double Addend { get; set; }

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var addend = parameter is double d ? d : Addend;

        if (value is double dValue)
            return dValue + addend;
        if (value is int iValue)
            return iValue + addend;
        if (value is float fValue)
            return fValue + addend;

        return value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var addend = parameter is double d ? d : Addend;

        if (value is double dValue)
            return dValue - addend;
        if (value is int iValue)
            return iValue - addend;
        if (value is float fValue)
            return fValue - addend;

        return value;
    }
}

/// <summary>
/// Converts an enum value to a boolean indicating whether it equals a specified value.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.Equals(parameter);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            return parameter;
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// Formats a date/time value.
/// </summary>
public sealed class DateTimeFormatConverter : IValueConverter
{
    /// <summary>
    /// Gets or sets the format string.
    /// </summary>
    public string Format { get; set; } = "g";

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? Format;

        if (value is DateTime dt)
            return dt.ToString(format, culture);
        if (value is DateTimeOffset dto)
            return dto.ToString(format, culture);
        if (value is DateOnly d)
            return d.ToString(format, culture);
        if (value is TimeOnly t)
            return t.ToString(format, culture);

        return value?.ToString();
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (targetType == typeof(DateTime) && DateTime.TryParse(s, culture, out var dt))
                return dt;
            if (targetType == typeof(DateTimeOffset) && DateTimeOffset.TryParse(s, culture, out var dto))
                return dto;
            if (targetType == typeof(DateOnly) && DateOnly.TryParse(s, culture, out var d))
                return d;
            if (targetType == typeof(TimeOnly) && TimeOnly.TryParse(s, culture, out var t))
                return t;
        }
        return null;
    }
}

