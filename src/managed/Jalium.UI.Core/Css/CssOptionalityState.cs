using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;

namespace Jalium.UI.Styling;

/// <summary>Maps native form metadata and required value bindings onto XAML inputs.</summary>
internal static class CssOptionalityState
{
    /// <summary>Null means this element has no input optionality semantics.</summary>
    internal static bool? GetRequired(CssNode element) => GetRequired(element.Target);

    internal static bool? GetRequired(DependencyObject target)
    {
        if (!IsInputControl(target)) return null;
        if (AutomationProperties.GetIsRequiredForForm(target)) return true;
        foreach (var expression in target.GetBindingExpressionsInternal())
            if (IsValueBinding(target, expression.TargetProperty) &&
                HasRequiredRule(expression)) return true;
        return false;
    }

    internal static void NotifyBindingChange(DependencyObject target, bool? previous)
    {
        if (previous == GetRequired(target) || CssNode.Existing(target) is not { } node) return;
        CssSelectorDependencies.NativeValueChanged(target, "IsRequiredForForm");
        CssEvaluationScheduler.InvalidateSubtree(node);
    }

    private static bool HasRequiredRule(BindingExpressionBase expression) => expression switch
    {
        PriorityBindingExpression priority => priority.ActiveBindingExpression is { } active &&
            HasRequiredRule(active),
        MultiBindingExpression multi => multi.ParentMultiBinding.ValidationRules.Any(
            static rule => rule is RequiredValidationRule) ||
            multi.BindingExpressions.Any(HasRequiredRule),
        { ParentBindingBase: Binding single } => single.ValidationRules.Any(
            static rule => rule is RequiredValidationRule),
        _ => false,
    };

    internal static bool IsValueBinding(DependencyObject target, DependencyProperty property) => target switch
    {
        AutoCompleteBox => ReferenceEquals(property, AutoCompleteBox.TextProperty) ||
            ReferenceEquals(property, AutoCompleteBox.SelectedItemProperty),
        NumberBox => ReferenceEquals(property, NumberBox.ValueProperty),
        TextBox => ReferenceEquals(property, TextBox.TextProperty),
        ComboBox => ReferenceEquals(property, Selector.SelectedItemProperty) ||
            ReferenceEquals(property, Selector.SelectedValueProperty) ||
            ReferenceEquals(property, Selector.SelectedIndexProperty) ||
            ReferenceEquals(property, ComboBox.TextProperty),
        DatePicker => ReferenceEquals(property, DatePicker.SelectedDateProperty) ||
            ReferenceEquals(property, DatePicker.TextProperty),
        TimePicker => ReferenceEquals(property, TimePicker.SelectedTimeProperty),
        ColorPicker => ReferenceEquals(property, ColorPicker.ColorProperty),
        ToggleButton => ReferenceEquals(property, ToggleButton.IsCheckedProperty),
        TreeSelector => ReferenceEquals(property, TreeSelector.SelectedItemProperty),
        Selector => ReferenceEquals(property, Selector.SelectedItemProperty) ||
            ReferenceEquals(property, Selector.SelectedValueProperty) ||
            ReferenceEquals(property, Selector.SelectedIndexProperty),
        Slider => ReferenceEquals(property, RangeBase.ValueProperty),
        RangeSlider => ReferenceEquals(property, RangeSlider.RangeStartProperty) ||
            ReferenceEquals(property, RangeSlider.RangeEndProperty),
        DataGridCell => ReferenceEquals(property, ContentControl.ContentProperty),
        EditControl => ReferenceEquals(property, EditControl.TextProperty),
        HexEditor => ReferenceEquals(property, HexEditor.DataProperty),
        JsonTreeViewer => ReferenceEquals(property, JsonTreeViewer.JsonTextProperty),
        _ => false,
    };

    internal static bool IsInputControl(DependencyObject target) => target switch
    {
        TextBoxBase or PasswordBox or ComboBox or DatePicker or TimePicker or ColorPicker
            or ToggleButton or Selector or TreeSelector or Slider or RangeSlider
            or DataGridCell or EditControl or HexEditor or JsonTreeViewer => true,
        _ => false,
    };
}
