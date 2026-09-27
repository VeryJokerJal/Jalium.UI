using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;

namespace Jalium.UI.Styling;

/// <summary>Evaluates native and bound numeric limits for input controls.</summary>
internal static class CssRangeState
{
    /// <summary>Null means the input has no applicable range limitation.</summary>
    internal static bool? GetInRange(CssNode element) => GetInRange(element.Target);

    internal static bool? GetInRange(DependencyObject target)
    {
        bool? state = target switch
        {
            NumberBox number => !IsOutside(number.Text, number.Minimum, number.Maximum),
            Slider slider => !IsOutside(slider.Value, slider.Minimum, slider.Maximum),
            RangeSlider slider => !IsOutside(slider.RangeStart, slider.Minimum, slider.Maximum) &&
                !IsOutside(slider.RangeEnd, slider.Minimum, slider.Maximum),
            DatePicker date when date.DisplayDateStart.HasValue || date.DisplayDateEnd.HasValue =>
                !IsOutside(date.SelectedDate, date.DisplayDateStart, date.DisplayDateEnd),
            _ => null,
        };

        if (!CssOptionalityState.IsInputControl(target)) return null;
        foreach (var expression in target.GetBindingExpressionsInternal())
        {
            if (!CssOptionalityState.IsValueBinding(target, expression.TargetProperty)) continue;
            state = Combine(state, GetBindingState(expression));
            if (state is false) break;
        }
        return state;
    }

    internal static IEnumerable<string> ObservedProperties(CssNode element)
    {
        yield return "Range";
        switch (element.Target)
        {
            case NumberBox:
                yield return nameof(NumberBox.Text);
                yield return nameof(NumberBox.Value);
                yield return nameof(NumberBox.Minimum);
                yield return nameof(NumberBox.Maximum);
                break;
            case Slider:
                yield return nameof(RangeBase.Value);
                yield return nameof(RangeBase.Minimum);
                yield return nameof(RangeBase.Maximum);
                break;
            case RangeSlider:
                yield return nameof(RangeSlider.RangeStart);
                yield return nameof(RangeSlider.RangeEnd);
                yield return nameof(RangeSlider.Minimum);
                yield return nameof(RangeSlider.Maximum);
                break;
            case DatePicker:
                yield return nameof(DatePicker.SelectedDate);
                yield return nameof(DatePicker.DisplayDateStart);
                yield return nameof(DatePicker.DisplayDateEnd);
                break;
        }

        foreach (var expression in element.Target.GetBindingExpressionsInternal())
        {
            if (!CssOptionalityState.IsValueBinding(element.Target, expression.TargetProperty)) continue;
            foreach (var name in BoundProperties(expression)) yield return name;
        }
    }

    internal static void NotifyBindingChange(DependencyObject target, bool? previous)
    {
        if (previous == GetInRange(target) || CssNode.Existing(target) is not { } node) return;
        CssSelectorDependencies.NativeValueChanged(target, "Range");
        CssEvaluationScheduler.InvalidateSubtree(node);
    }

    private static IEnumerable<string> BoundProperties(BindingExpressionBase expression)
    {
        yield return expression.TargetProperty.Name;
        if (expression is PriorityBindingExpression priority)
        {
            foreach (var child in priority.BindingExpressions)
                foreach (var name in BoundProperties(child)) yield return name;
        }
        else if (expression is MultiBindingExpression multi)
        {
            foreach (var child in multi.BindingExpressions)
                foreach (var name in BoundProperties(child)) yield return name;
        }
    }

    private static bool? GetBindingState(BindingExpressionBase expression) => expression switch
    {
        PriorityBindingExpression priority => priority.ActiveBindingExpression is { } active
            ? GetBindingState(active) : null,
        MultiBindingExpression multi => GetMultiBindingState(multi),
        { ParentBindingBase: Binding binding } => GetRulesState(
            binding.ValidationRules, expression.Target.GetValue(expression.TargetProperty)),
        _ => null,
    };

    private static bool? GetMultiBindingState(MultiBindingExpression multi)
    {
        bool? state = GetRulesState(multi.ParentMultiBinding.ValidationRules,
            multi.Target.GetValue(multi.TargetProperty));
        foreach (var child in multi.BindingExpressions)
        {
            state = Combine(state, GetBindingState(child));
            if (state is false) break;
        }
        return state;
    }

    private static bool? GetRulesState(IEnumerable<ValidationRule> rules, object? value)
    {
        bool? state = null;
        foreach (var rule in rules)
        {
            if (rule is not RangeValidationRule range ||
                !range.Minimum.HasValue && !range.Maximum.HasValue) continue;
            state = !IsOutside(value, range.Minimum, range.Maximum);
            if (state is false) break;
        }
        return state;
    }

    private static bool? Combine(bool? first, bool? second) =>
        first is false || second is false ? false :
        first is true || second is true ? true : null;

    private static bool IsOutside(object? value, double? minimum, double? maximum)
    {
        if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue)) return false;
        if (!TryGetNumber(value, out var number)) return false;
        return minimum.HasValue && number < minimum.Value ||
            maximum.HasValue && number > maximum.Value;
    }

    private static bool TryGetNumber(object value, out double number)
    {
        switch (value)
        {
            case double doubleValue: number = doubleValue; return true;
            case float floatValue: number = floatValue; return true;
            case decimal decimalValue: number = (double)decimalValue; return true;
            case sbyte signedByte: number = signedByte; return true;
            case byte unsignedByte: number = unsignedByte; return true;
            case short signedShort: number = signedShort; return true;
            case ushort unsignedShort: number = unsignedShort; return true;
            case int signedInt: number = signedInt; return true;
            case uint unsignedInt: number = unsignedInt; return true;
            case long signedLong: number = signedLong; return true;
            case ulong unsignedLong: number = unsignedLong; return true;
            case string text:
                return double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out number);
            default:
                number = 0;
                return false;
        }
    }

    private static bool IsOutside(DateTime? value, DateTime? minimum, DateTime? maximum) =>
        value.HasValue && (minimum.HasValue && value.Value.Date < minimum.Value.Date ||
            maximum.HasValue && value.Value.Date > maximum.Value.Date);
}
