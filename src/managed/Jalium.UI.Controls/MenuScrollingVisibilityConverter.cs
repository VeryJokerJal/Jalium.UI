using System.Globalization;
using Jalium.UI;

namespace Jalium.UI.Controls;

/// <summary>
/// Data binding converter to handle the visibility of repeat buttons in scrolling menus.
/// </summary>
/// <remarks>
/// This converter expects four values:
/// <list type="number">
///   <item><description><see cref="Visibility"/> - The computed vertical scroll bar visibility.</description></item>
///   <item><description><see cref="double"/> - The vertical offset.</description></item>
///   <item><description><see cref="double"/> - The extent height.</description></item>
///   <item><description><see cref="double"/> - The viewport height.</description></item>
/// </list>
/// The converter parameter should be a <see cref="double"/> (0.0 for the top button, 100.0 for the bottom button).
/// </remarks>
public sealed class MenuScrollingVisibilityConverter : IMultiValueConverter
{
    /// <summary>
    /// Converts scroll viewer values to a <see cref="Visibility"/> value indicating whether a scroll button should be shown.
    /// </summary>
    /// <param name="values">
    /// An array of four values: ComputedVerticalScrollBarVisibility, VerticalOffset, ExtentHeight, and ViewportHeight.
    /// </param>
    /// <param name="targetType">The type of the binding target property (ignored).</param>
    /// <param name="parameter">A <see cref="double"/> or string representing the target percentage (0.0 or 100.0).</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>
    /// <see cref="Visibility.Visible"/> if the scroll button should be shown;
    /// <see cref="Visibility.Collapsed"/> if it should be hidden;
    /// or <see cref="DependencyProperty.UnsetValue"/> if the input is invalid.
    /// </returns>
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        //
        // Parameter Validation
        //
        if (parameter == null ||
            values == null ||
            values.Length != 4 ||
            values[0] is not Visibility ||
            values[1] is not double ||
            values[2] is not double ||
            values[3] is not double)
        {
            return DependencyProperty.UnsetValue;
        }

        if (parameter is not double && parameter is not string)
        {
            return DependencyProperty.UnsetValue;
        }

        //
        // Conversion
        //

        // If the scroll bar should be visible, then so should our buttons
        Visibility computedVerticalScrollBarVisibility = (Visibility)values[0]!;
        if (computedVerticalScrollBarVisibility == Visibility.Visible)
        {
            double target;

            if (parameter is string paramString)
            {
                target = double.Parse(paramString, NumberFormatInfo.InvariantInfo);
            }
            else
            {
                target = (double)parameter;
            }

            double verticalOffset = (double)values[1]!;
            double extentHeight = (double)values[2]!;
            double viewportHeight = (double)values[3]!;

            if (extentHeight != viewportHeight) // Avoid divide by 0
            {
                // Calculate the percent so that we can see if we are near the edge of the range
                double percent = Math.Min(100.0, Math.Max(0.0, (verticalOffset * 100.0 / (extentHeight - viewportHeight))));

                if (AreClose(percent, target))
                {
                    // We are at the end of the range, so no need for this button to be shown
                    return Visibility.Collapsed;
                }
            }

            return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    /// <summary>
    /// Not supported. This converter only supports one-way binding.
    /// </summary>
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return [DependencyProperty.UnsetValue];
    }

    /// <summary>
    /// Determines whether two double values are close enough to be considered equal.
    /// </summary>
    private static bool AreClose(double value1, double value2)
    {
        // Using a tolerance of 1e-6 for floating-point comparison
        if (value1 == value2)
            return true;

        double delta = value1 - value2;
        return (delta < 1.0e-6) && (delta > -1.0e-6);
    }
}
