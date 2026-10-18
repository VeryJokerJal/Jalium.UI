namespace Jalium.UI.Controls;

public partial class ListBox
{
    private readonly record struct RowSelection(ItemOccurrence Row, object Alias);
    private readonly List<RowSelection> _selectionRows = [];
    private List<RowSelection>? _pendingSelectionRows;
    private ItemOccurrence? _primaryOccurrence, _pendingPrimaryOccurrence;
    private ItemOccurrence? _anchorOccurrence, _focusedOccurrence, _pendingFocusOccurrence;

    internal bool IsOccurrenceSelected(int index) => Items.GetOccurrenceAt(index) is { IsAlive: true } row &&
        _selectionRows.Any(entry => ReferenceEquals(entry.Row, row));
    internal int[] GetSelectedOccurrenceIndices() => _selectionRows.Where(entry => entry.Row.IsAlive)
        .Select(entry => entry.Row.Index).Order().ToArray();

    private ItemOccurrence? ResolveSelectionOccurrence(object requested, ISet<ItemOccurrence>? used = null,
        ItemOccurrence? preferred = null)
    {
        for (int match = 0; match < 3; match++)
        {
            bool Matches(object? candidate) => candidate is { } item && GetSelectionValueFromLogicalItem(item) is { } value &&
                (match == 0 ? ReferenceEquals(item, requested) : match == 1 ? ReferenceEquals(value, requested) : Equals(item, requested) || Equals(value, requested));
            ItemOccurrence? first = null;
            if (preferred is { IsAlive: true } && Matches(preferred.Item) && used?.Contains(preferred) != true) return preferred;
            for (int i = 0; i < Items.Count; i++)
            {
                if (!Matches(Items.GetItemAt(i))) continue;
                var row = Items.GetOccurrenceAt(i)!;
                first ??= row;
                if (used?.Contains(row) != true) return row;
            }
            if (first != null) return first;
        }
        return null;
    }

    private RowSelection SelectionAt(int index, bool keepItem = false)
    {
        var row = Items.GetOccurrenceAt(index) ?? throw new ArgumentOutOfRangeException(nameof(index));
        return new(row, (keepItem ? row.Item : GetSelectionValueFromLogicalItem(row.Item))!);
    }

    internal override bool TryApplySelectionProperty(DependencyProperty property, object? value)
    {
        if (_isApplyingSelectedItems) return false;
        ItemOccurrence? row = property == SelectedIndexProperty ? Items.GetOccurrenceAt(value is int index ? index : -1)
            : value is null ? null : ResolveSelectionOccurrence(value, preferred: _primaryOccurrence);
        var selected = row?.Item is not null ? new[] { new RowSelection(row, property == SelectedIndexProperty || ReferenceEquals(row.Item, value)
            ? row.Item : GetSelectionValueFromLogicalItem(row.Item)!) } : [];
        return CommitOccurrenceSelection(selected, row, publishToSource: false);
    }

    internal void OnItemOccurrencesChanged(ItemOccurrenceChange change, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        SynchronizeExplicitItemOwnership(args);
        _anchorOccurrence = change.Map(_anchorOccurrence);
        _focusedOccurrence = change.Map(_focusedOccurrence);
        _pendingFocusOccurrence = change.Map(_pendingFocusOccurrence);
        var entries = _isApplyingSelectedItems ? _pendingSelectionRows! : _selectionRows;
        var mapped = new List<RowSelection>();
        foreach (var entry in entries)
        {
            if (change.Map(entry.Row) is not { } row || row.Item is null) continue;
            object alias = ReferenceEquals(row, entry.Row) ? entry.Alias :
                ReferenceEquals(entry.Alias, entry.Row.Item) ? row.Item : GetSelectionValueFromLogicalItem(row.Item)!;
            mapped.Add(new(row, alias));
        }
        var primary = change.Map(_isApplyingSelectedItems ? _pendingPrimaryOccurrence : _primaryOccurrence);
        if (_isApplyingSelectedItems) { _pendingSelectionRows = mapped; _pendingPrimaryOccurrence = primary; return; }
        if (mapped.Count != 0 || _selectionRows.Count != 0)
            CommitOccurrenceSelection(mapped, primary, publishToSource: true);
    }

    private bool CommitOccurrenceSelection(IEnumerable<RowSelection> requested, ItemOccurrence? primary, bool publishToSource)
    {
        if (_isApplyingSelectedItems) return false;
        var resolved = new List<RowSelection>();
        foreach (var entry in requested)
        {
            if (!entry.Row.IsAlive || !ReferenceEquals(Items.GetOccurrenceAt(entry.Row.Index), entry.Row)) return false;
            if (!resolved.Any(previous => ReferenceEquals(previous.Row, entry.Row))) resolved.Add(entry);
        }
        if (SelectionMode == SelectionMode.Single && resolved.Count > 1) return false;
        var before = _selectionRows.ToArray();
        _pendingSelectionRows = resolved; _pendingPrimaryOccurrence = primary; _isApplyingSelectedItems = true;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                _selectionRows.Clear(); _selectionRows.AddRange(_pendingSelectionRows.Where(entry => entry.Row.IsAlive));
                _selectedItems.Clear(); _selectedItems.AddRange(_selectionRows.Select(entry => entry.Alias));
                var active = _selectionRows.FirstOrDefault(entry => ReferenceEquals(entry.Row, _pendingPrimaryOccurrence));
                if (active.Row is null && _selectionRows.Count > 0) active = _selectionRows[0];
                _primaryOccurrence = active.Row;
                ulong version = Items.OccurrenceVersion;
                UpdateSelectionPropertiesFromBatch(active.Row?.Index ?? -1, active.Row is null ? null : active.Alias,
                    () => version == Items.OccurrenceVersion);
                if (version == Items.OccurrenceVersion) break;
                if (attempt >= 31) throw new InvalidOperationException("The items keep changing during selection.");
            }
        }
        catch
        {
            // A property callback can mutate the view and then throw. Keep the
            // surviving occurrence model usable and retain the original error.
            _selectionRows.Clear(); _selectionRows.AddRange(_pendingSelectionRows.Where(entry => entry.Row.IsAlive));
            _selectedItems.Clear(); _selectedItems.AddRange(_selectionRows.Select(entry => entry.Alias));
            var active = _selectionRows.FirstOrDefault(entry => ReferenceEquals(entry.Row, _pendingPrimaryOccurrence));
            if (active.Row is null && _selectionRows.Count != 0) active = _selectionRows[0];
            _primaryOccurrence = active.Row;
            try { UpdateSelectionPropertiesFromBatch(active.Row?.Index ?? -1, active.Row is null ? null : active.Alias); }
            catch { /* The first callback error remains authoritative. */ }
            throw;
        }
        finally { _isApplyingSelectedItems = false; _pendingSelectionRows = null; _pendingPrimaryOccurrence = null; }
        var after = _selectionRows.ToArray();
        var removed = before.Where(entry => !after.Any(current => ReferenceEquals(current.Row, entry.Row))).Select(entry => entry.Alias).ToArray();
        var added = after.Where(entry => !before.Any(previous => ReferenceEquals(previous.Row, entry.Row))).Select(entry => entry.Alias).ToArray();
        try
        {
            if (removed.Length != 0 || added.Length != 0)
                OnSelectionChanged(new SelectionChangedEventArgs(SelectionChangedEvent, removed, added));
        }
        finally
        {
            try { _selectedItemsView.PublishCommittedSelection(); }
            finally { if (publishToSource) PublishSelectionToSource(); }
        }
        return true;
    }

    private void ApplyIndexSelection(IEnumerable<int> indices, int preferred)
    {
        var entries = indices.Where(index => GetSelectionValueAtIndex(index) != null).Select(index => SelectionAt(index)).ToArray();
        CommitOccurrenceSelection(entries, Items.GetOccurrenceAt(preferred), publishToSource: true);
    }

    internal bool SetAutomationSelectedOccurrences(IEnumerable<ItemOccurrence> rows, Func<bool> current)
    {
        var entries = rows.Select(row => new RowSelection(row, GetSelectionValueFromLogicalItem(row.Item)!)).ToArray();
        return current() && CommitOccurrenceSelection(entries, entries.FirstOrDefault().Row, publishToSource: true);
    }

    internal void SetAutomationSelectionAt(int index, bool selected, bool exclusive)
    {
        var row = Items.GetOccurrenceAt(index);
        if (row is null || row.Item is null) throw new InvalidOperationException("The item is no longer in this ListBox.");
        var entries = exclusive ? new List<RowSelection>() : new List<RowSelection>(_selectionRows);
        entries.RemoveAll(entry => ReferenceEquals(entry.Row, row));
        if (selected) entries.Add(SelectionAt(index));
        entries = entries.Select(entry => new RowSelection(entry.Row, GetSelectionValueFromLogicalItem(entry.Row.Item)!)).ToList();
        if (!CommitOccurrenceSelection(entries, selected ? row : entries.LastOrDefault().Row, publishToSource: true))
            throw new InvalidOperationException("The list cannot apply the requested automation selection.");
    }

    internal void ScrollOccurrenceIntoView(int index)
    {
        if ((uint)index >= (uint)Items.Count) return;
        if (ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container) container.BringIntoView();
        else if (ItemsHost is VirtualizingPanel panel) panel.BringIndexIntoViewPublic(index);
    }
}
