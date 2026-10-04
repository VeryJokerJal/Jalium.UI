using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Data;

/// <summary>Keeps CSS input states in sync when rules change on an attached binding.</summary>
internal sealed class ValidationRuleCollection : Collection<ValidationRule>
{
    private static readonly object s_marker = new();
    private readonly ConditionalWeakTable<DependencyObject, object> _targets = new();
    private bool _hasTargets;

    internal void TrackTarget(DependencyObject target)
    {
        if (!CssOptionalityState.IsInputControl(target)) return;

        _targets.GetValue(target, static _ => s_marker);
        _hasTargets = true;
        foreach (var rule in this)
            if (rule is RangeValidationRule range) range.TrackCssTarget(target);
    }

    protected override void InsertItem(int index, ValidationRule item) =>
        Change(() => base.InsertItem(index, item));

    protected override void RemoveItem(int index) =>
        Change(() => base.RemoveItem(index));

    protected override void SetItem(int index, ValidationRule item) =>
        Change(() => base.SetItem(index, item));

    protected override void ClearItems() => Change(base.ClearItems);

    private void Change(Action mutation)
    {
        if (!_hasTargets)
        {
            mutation();
            return;
        }

        var previous = new List<(DependencyObject Target, bool? Required, bool? InRange)>();
        foreach (var entry in _targets)
        {
            var target = entry.Key;
            previous.Add((target, CssOptionalityState.GetRequired(target), CssRangeState.GetInRange(target)));
        }

        try
        {
            mutation();
        }
        finally
        {
            foreach (var (target, required, inRange) in previous)
            {
                foreach (var rule in this)
                    if (rule is RangeValidationRule range) range.TrackCssTarget(target);
                CssOptionalityState.NotifyBindingChange(target, required);
                CssRangeState.NotifyBindingChange(target, inRange);
            }
        }
    }
}
