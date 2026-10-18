namespace Jalium.UI.Controls.Primitives;

public abstract partial class Selector
{
    private ItemOccurrence? _selectionOccurrence;
    private bool _isReconcilingOccurrence;
    private bool _pendingOccurrenceReconciliation;
    private ulong _singleSelectionRevision;
    private bool _preserveSingleSelectedValue;
    private SingleSelectionWrite? _singleSelectionWrite;

    private sealed class SingleSelectionWrite(DependencyProperty property, object? value)
    {
        internal readonly DependencyProperty Property = property;
        internal readonly object? Value = value;
        internal bool Pending = true;
    }

    private void WriteSingleSelectionProperty(DependencyProperty property, object? value)
    {
        if (!_isReconcilingOccurrence || this is ListBox) { SetCurrentValue(property, value); return; }
        var previous = _singleSelectionWrite;
        _singleSelectionWrite = new(property, value);
        try { SetCurrentValue(property, value); }
        finally { _singleSelectionWrite = previous; }
    }

    private void RecordSingleSelectionRequest(DependencyProperty property, object? value)
    {
        if (!_isReconcilingOccurrence || this is ListBox) return;
        if (_singleSelectionWrite is { Pending: true } write && write.Property == property &&
            (property == SelectedIndexProperty ? Equals(write.Value, value) : ReferenceEquals(write.Value, value)))
        {
            write.Pending = false;
            return;
        }
        ulong revision = ++_singleSelectionRevision;
        ItemOccurrence? row;
        if (property == SelectedIndexProperty) row = Items.GetOccurrenceAt(value is int index ? index : -1);
        else if (property == SelectedItemProperty) row = ResolveSingleOccurrence(value);
        else row = Items.GetOccurrenceAt(FindItemBySelectedValue(value).index);
        if (revision != _singleSelectionRevision) return;
        _selectionOccurrence = row;
        _preserveSingleSelectedValue = property == SelectedValueProperty;
    }

    private void ReconcileSingleSelection()
    {
        for (int attempt = 0; ; attempt++)
        {
            var row = _selectionOccurrence is { IsAlive: true } live ? live : null;
            _selectionOccurrence = row;
            ulong itemsVersion = Items.OccurrenceVersion, revision = _singleSelectionRevision;
            bool Current() => itemsVersion == Items.OccurrenceVersion && revision == _singleSelectionRevision;
            UpdateSelectionPropertiesFromBatch(row?.Index ?? -1, row?.Item, Current, _preserveSingleSelectedValue);
            if (Current()) return;
            if (attempt >= 31) throw new InvalidOperationException("The selection keeps changing during selection.");
        }
    }

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
        _preserveSingleSelectedValue = preserveSelectedValue;
        try { ReconcileSingleSelection(); }
        catch
        {
            // A callback can choose a newer row and throw. Reconcile that row,
            // while retaining the first exception if recovery also calls user code.
            try { ReconcileSingleSelection(); }
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
