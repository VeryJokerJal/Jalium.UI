namespace Jalium.UI.Controls.Primitives;

public abstract partial class Selector
{
    private ItemOccurrence? _selectionOccurrence;
    private bool _isReconcilingOccurrence;
    private bool _pendingOccurrenceReconciliation;

    internal void OnItemOccurrencesChanged(ItemOccurrenceChange change)
    {
        var before = _selectionOccurrence;
        _selectionOccurrence = change.Map(before);
        if (_isReconcilingOccurrence) return;
        if (_isUpdatingSelection)
        {
            _pendingOccurrenceReconciliation = true;
            return;
        }
        if (before != null || _selectionOccurrence != null)
            CommitSingleOccurrence(_selectionOccurrence, previous: before);
    }

    private ItemOccurrence? ResolveSingleOccurrence(object? item)
    {
        if (item is null) return null;
        if (_selectionOccurrence is { IsAlive: true } current && ReferenceEquals(current.Item, item)) return current;
        return Items.FindOccurrence(item);
    }

    private void CommitSingleOccurrence(ItemOccurrence? requested, bool preserveSelectedValue = false,
        ItemOccurrence? previous = null)
    {
        var before = previous ?? _selectionOccurrence;
        _selectionOccurrence = requested;
        _isReconcilingOccurrence = true;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                var row = _selectionOccurrence is { IsAlive: true } live ? live : null;
                _selectionOccurrence = row;
                ulong version = Items.OccurrenceVersion;
                UpdateSelectionPropertiesFromBatch(row?.Index ?? -1, row?.Item,
                    () => version == Items.OccurrenceVersion, preserveSelectedValue);
                if (version == Items.OccurrenceVersion) break;
                if (attempt >= 31) throw new InvalidOperationException("The items keep changing during selection.");
            }
        }
        catch
        {
            // A callback can remove the selected row and throw. Retain the first
            // error while leaving the surviving selection coherent and usable.
            var row = _selectionOccurrence is { IsAlive: true } live ? live : null;
            _selectionOccurrence = row;
            try { UpdateSelectionPropertiesFromBatch(row?.Index ?? -1, row?.Item, preserveSelectedValue: preserveSelectedValue); }
            catch { }
            throw;
        }
        finally
        {
            _isReconcilingOccurrence = false;
            _pendingOccurrenceReconciliation = false;
        }
        try
        {
            if (!ReferenceEquals(before, _selectionOccurrence))
                RaiseSelectionChanged(before?.Item, _selectionOccurrence?.Item);
        }
        finally
        {
            // SetCurrentValue retains expressions; publish only the final current
            // selection through each binding's existing mode and update trigger.
            GetBindingExpression(SelectedItemProperty)?.OnTargetValueChanged();
            GetBindingExpression(SelectedIndexProperty)?.OnTargetValueChanged();
            GetBindingExpression(SelectedValueProperty)?.OnTargetValueChanged();
        }
    }
}
