using Jalium.UI.Controls;
using Jalium.UI.Controls.Ribbon;

namespace Jalium.UI.Styling;

/// <summary>The mutually exclusive input-value states exposed by native XAML controls.</summary>
internal enum CssChoiceValue : byte
{
    NotApplicable,
    Checked,
    Unchecked,
    Indeterminate,
}

internal static class CssChoiceState
{
    private static readonly string[] s_checked = ["IsChecked"];
    private static readonly string[] s_selected = ["IsSelected"];
    private static readonly string[] s_checkable = ["IsCheckable", "IsChecked"];
    private static readonly string[] s_task = ["MarkerKind", "IsChecked"];
    private static readonly string[] s_progress = ["IsIndeterminate"];

    internal static string[] ObservedProperties(CssNode element)
    {
        var target = element.Target;
        if (target is ProgressBar) return s_progress;
        if (target is MenuItem or RibbonSplitButton) return s_checkable;
        if (target is MarkdownListItemPresenter) return s_task;
        if (FindBooleanProperty(element, "IsChecked") is not null) return s_checked;
        return FindBooleanProperty(element, "IsSelected") is not null ? s_selected : [];
    }

    internal static CssChoiceValue GetState(CssNode element)
    {
        var target = element.Target;
        if (target is ProgressBar progress)
            return progress.IsIndeterminate ? CssChoiceValue.Indeterminate : CssChoiceValue.NotApplicable;
        if (target is MenuItem menu && !menu.IsCheckable)
            return CssChoiceValue.NotApplicable;
        if (target is RibbonSplitButton split && !split.IsCheckable)
            return CssChoiceValue.NotApplicable;
        if (target is MarkdownListItemPresenter task && task.MarkerKind != MarkdownListMarkerKind.Task)
            return CssChoiceValue.NotApplicable;

        if (FindBooleanProperty(element, "IsChecked") is { } checkedProperty)
        {
            return element.GetValue(checkedProperty) switch
            {
                true => CssChoiceValue.Checked,
                false => CssChoiceValue.Unchecked,
                _ => CssChoiceValue.Indeterminate,
            };
        }

        // MenuItem.IsSelected reports hover highlighting, not a selected option.
        if (target is MenuItem) return CssChoiceValue.NotApplicable;
        if (FindBooleanProperty(element, "IsSelected") is { } selectedProperty)
            return element.GetValue(selectedProperty) is true ? CssChoiceValue.Checked : CssChoiceValue.Unchecked;

        return CssChoiceValue.NotApplicable;
    }

    private static DependencyProperty? FindBooleanProperty(CssNode element, string name)
    {
        var property = CssDependencyPropertyLookup.Find(element.GetType(), name);
        return property?.PropertyType == typeof(bool) || property?.PropertyType == typeof(bool?) ? property : null;
    }
}
