namespace Jalium.UI.Styling;

/// <summary>Reads an element's native semantic open state without consulting visibility.</summary>
internal static class CssOpenState
{
    private static readonly string[] s_nativeNames =
        ["IsOpen", "IsDropDownOpen", "IsSubmenuOpen", "IsPaneOpen", "IsExpanded"];

    internal static string? ObservedProperty(CssNode element) => FindOpenProperty(element)?.Name;

    internal static bool IsOpen(CssNode element) =>
        FindOpenProperty(element) is { } property && element.GetValue(property) is true;

    private static DependencyProperty? FindOpenProperty(CssNode element)
    {
        foreach (var name in s_nativeNames)
        {
            var property = CssDependencyPropertyLookup.Find(element.GetType(), name);
            if (property?.PropertyType == typeof(bool)) return property;
        }

        return null;
    }
}
