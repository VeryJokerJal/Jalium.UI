using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Styling;

/// <summary>Defines the XAML controls whose content can be edited by a user.</summary>
internal static class CssMutabilityState
{
    private static readonly string[] s_readOnlyProperty = ["IsReadOnly"];
    private static readonly string[] s_editableProperty = ["IsEditable"];
    private static readonly string[] s_comboProperties = ["IsEditable", "IsReadOnly"];

    internal static string[] ObservedProperties(DependencyObject target) => target switch
    {
        ComboBox => s_comboProperties,
        JsonTreeViewer => s_editableProperty,
        TextBoxBase or PasswordBox or EditControl or HexEditor or Terminal or DataGrid or DataGridCell
            => s_readOnlyProperty,
        _ => Array.Empty<string>(),
    };

    internal static bool IsReadWrite(CssNode element)
    {
        if (!element.IsEnabled) return false;
        return element.Target switch
        {
            ComboBox box => box.IsEditable && !box.IsReadOnly,
            JsonTreeViewer viewer => viewer.IsEditable,
            TextBoxBase box => !box.IsReadOnly,
            PasswordBox box => !box.IsReadOnly,
            EditControl editor => !editor.IsReadOnly,
            HexEditor editor => !editor.IsReadOnly,
            Terminal terminal => !terminal.IsReadOnly,
            DataGrid grid => !grid.IsReadOnly,
            DataGridCell cell => !cell.IsReadOnly,
            _ => false,
        };
    }
}
