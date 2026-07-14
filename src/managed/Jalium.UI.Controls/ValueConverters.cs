using System.Collections;
using System.Globalization;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Controls;

/// <summary>
/// Converts an alternation index to the selected item in <see cref="Values"/>.
/// </summary>
[ContentProperty(nameof(Values))]
public class AlternationConverter : IValueConverter
{
    private readonly List<object> _values = new();

    /// <summary>
    /// Gets the list of values used by the converter.
    /// </summary>
    public IList Values => _values;

    /// <inheritdoc />
    public object Convert(object o, Type targetType, object parameter, CultureInfo culture)
    {
        if (_values.Count > 0 && o is int alternationIndex)
        {
            int index = alternationIndex % _values.Count;
            if (index < 0)
            {
                index += _values.Count;
            }

            return _values[index];
        }

        return DependencyProperty.UnsetValue;
    }

    /// <inheritdoc />
    public object ConvertBack(object o, Type targetType, object parameter, CultureInfo culture)
        => _values.IndexOf(o);

    object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Convert(value!, targetType, parameter!, culture);

    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConvertBack(value!, targetType, parameter!, culture);
}

/// <summary>
/// Converts between Boolean values and <see cref="Visibility"/> values.
/// </summary>
[Localizability(LocalizationCategory.NeverLocalize)]
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility visibility && visibility == Visibility.Visible;

    object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Convert(value!, targetType, parameter!, culture);

    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConvertBack(value!, targetType, parameter!, culture);
}
