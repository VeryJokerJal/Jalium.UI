using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public enum PropertyGridEditorKind { Text, Boolean, Enum, Color, Expandable, Custom }

internal static class PropertyEditorSelector
{
    internal static PropertyGridEditorKind GetKind(PropertyItem? item)
    {
        if (item == null) return PropertyGridEditorKind.Text;
        var type = Nullable.GetUnderlyingType(item.PropertyType) ?? item.PropertyType;
        if (type == typeof(bool)) return PropertyGridEditorKind.Boolean;
        if (type.IsEnum) return PropertyGridEditorKind.Enum;
        if (type == typeof(Color)) return PropertyGridEditorKind.Color;
        if (item.IsExpandable && Nullable.GetUnderlyingType(item.PropertyType) == null && item.Value != null) return PropertyGridEditorKind.Expandable;
        return PropertyGridEditorKind.Text;
    }
}
