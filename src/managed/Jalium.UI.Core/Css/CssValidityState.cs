using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Styling;

/// <summary>Maps native validation results to CSS validity for value-bearing XAML controls.</summary>
internal static class CssValidityState
{
    /// <summary>Null means this element has no data-validity semantics.</summary>
    internal static bool? GetValidity(CssNode element)
    {
        var target = element.Target;
        // Validation.MarkInvalid can opt any element into native validation, including
        // DataGrid containers and custom controls. ClearInvalid retains the collection.
        if (!IsValueControl(target) && Validation.GetErrors(target) is null && !Validation.GetHasError(target))
            return null;

        return !Validation.GetHasError(target);
    }

    private static bool IsValueControl(DependencyObject target) => target switch
    {
        TextBoxBase or PasswordBox or ComboBox or DatePicker or TimePicker or ColorPicker
            or ToggleButton or Selector or TreeSelector or Slider or RangeSlider
            or DataGrid or DataGridRow or DataGridCell or EditControl or HexEditor
            or JsonTreeViewer => true,
        _ => false,
    };
}
