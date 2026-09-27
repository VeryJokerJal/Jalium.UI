using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

/// <summary>Matches the controls' own placeholder display conditions.</summary>
internal static class CssPlaceholderState
{
    private static readonly string[] s_textProperties = ["PlaceholderText", "Text"];
    private static readonly string[] s_passwordProperties = ["PlaceholderText", "Password"];
    private static readonly string[] s_timeProperties = ["PlaceholderText", "SelectedTime"];
    private static readonly string[] s_comboProperties = ["PlaceholderText", "SelectedItem", "Text", "IsEditable"];
    private static readonly string[] s_treeProperties = ["PlaceholderText", "SelectedItems", "SearchText"];

    internal static string[] ObservedProperties(DependencyObject target) => target switch
    {
        TextBox or AutoCompleteBox or NumberBox or DatePicker => s_textProperties,
        PasswordBox => s_passwordProperties,
        TimePicker => s_timeProperties,
        ComboBox => s_comboProperties,
        TreeSelector => s_treeProperties,
        _ => Array.Empty<string>(),
    };

    internal static bool IsShown(DependencyObject target) => target switch
    {
        TextBox box => IsEmptyWithPlaceholder(box.Text, box.PlaceholderText),
        PasswordBox box => IsEmptyWithPlaceholder(box.Password, box.PlaceholderText),
        AutoCompleteBox box => IsEmptyWithPlaceholder(box.Text, box.PlaceholderText),
        NumberBox box => IsEmptyWithPlaceholder(box.Text, box.PlaceholderText),
        DatePicker picker => IsEmptyWithPlaceholder(picker.Text, picker.PlaceholderText ?? "Select a date"),
        TimePicker picker => !picker.SelectedTime.HasValue &&
            !string.IsNullOrEmpty(picker.PlaceholderText ?? "Select a time"),
        ComboBox box => box.SelectedItem is null &&
            (!box.IsEditable || string.IsNullOrEmpty(box.Text)) &&
            !string.IsNullOrEmpty(box.PlaceholderText),
        TreeSelector selector => selector.SelectedItems.Count == 0 &&
            string.IsNullOrEmpty(selector.SearchText) &&
            !string.IsNullOrEmpty(selector.PlaceholderText),
        _ => false,
    };

    private static bool IsEmptyWithPlaceholder(string? text, string? placeholder)
        => string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(placeholder);
}
