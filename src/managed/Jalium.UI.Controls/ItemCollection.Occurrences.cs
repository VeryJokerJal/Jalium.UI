using System.Collections.Specialized;

namespace Jalium.UI.Controls;

// A view occurrence survives index shifts and container recycling. Equality of
// user data never identifies a row, and retired occurrences never become live.
internal sealed class ItemOccurrence(object? item, int index)
{
    internal object? Item { get; } = item;
    internal int Index { get; set; } = index;
    internal int PreviousIndex { get; set; } = index;
    internal bool IsAlive => Index >= 0;
}

internal sealed class ItemOccurrenceChange(Func<ItemOccurrence, ItemOccurrence?> map)
{
    internal ItemOccurrence? Map(ItemOccurrence? row) => row is null ? null : row.IsAlive ? row : map(row);
}

public sealed partial class ItemCollection
{
    // Sparse: selecting and querying an occurrence must not allocate every row.
    private Dictionary<int, ItemOccurrence> _occurrences = [];
    internal ulong OccurrenceVersion { get; private set; }

    internal ItemOccurrence? GetOccurrenceAt(int index)
    {
        if ((uint)index >= (uint)Count) return null;
        if (_occurrences.TryGetValue(index, out var row)) return row;
        ulong version = OccurrenceVersion;
        object? item = GetItemAt(index);
        if (version != OccurrenceVersion || (uint)index >= (uint)Count) return null;
        if (_occurrences.TryGetValue(index, out row)) return row;
        _occurrences[index] = row = new(item, index);
        return row;
    }

    internal ItemOccurrence? FindOccurrence(object item)
    {
        for (int i = 0; i < Count; i++) if (ReferenceEquals(GetItemAt(i), item)) return GetOccurrenceAt(i);
        for (int i = 0; i < Count; i++) if (Equals(GetItemAt(i), item)) return GetOccurrenceAt(i);
        return null;
    }

    private ItemOccurrenceChange AdvanceOccurrences(NotifyCollectionChangedEventArgs args)
    {
        OccurrenceVersion++;
        var old = _occurrences.Values.ToArray();
        var transfers = new Dictionary<ItemOccurrence, ItemOccurrence>();
        int removed = args.OldItems?.Count ?? 0, added = args.NewItems?.Count ?? 0;
        bool reset = args.Action == NotifyCollectionChangedAction.Reset;
        foreach (var row in old)
        {
            int index = row.Index; row.PreviousIndex = index;
            switch (args.Action)
            {
                case NotifyCollectionChangedAction.Add when args.NewStartingIndex >= 0:
                    if (index >= args.NewStartingIndex) index += added;
                    break;
                case NotifyCollectionChangedAction.Remove when args.OldStartingIndex >= 0:
                    index = index < args.OldStartingIndex ? index : index < args.OldStartingIndex + removed ? -1 : index - removed;
                    break;
                case NotifyCollectionChangedAction.Replace when args.OldStartingIndex >= 0 && args.NewStartingIndex == args.OldStartingIndex:
                    index = index < args.OldStartingIndex ? index : index < args.OldStartingIndex + removed ? -1 : index - removed + added;
                    break;
                case NotifyCollectionChangedAction.Move when args.OldStartingIndex >= 0 && args.NewStartingIndex >= 0:
                    if (index >= args.OldStartingIndex && index < args.OldStartingIndex + removed) index += args.NewStartingIndex - args.OldStartingIndex;
                    else
                    {
                        if (index >= args.OldStartingIndex + removed) index -= removed;
                        if (index >= args.NewStartingIndex) index += removed;
                    }
                    break;
                default: reset = true; break;
            }
            row.Index = index;
        }
        if (!reset && old.Any(row => row.IsAlive && ((uint)row.Index >= (uint)Count || !SameData(row.Item, GetItemAt(row.Index))))) reset = true;
        if (reset)
        {
            foreach (var row in old) row.Index = -1;
            _occurrences = [];
            var assigned = new HashSet<ItemOccurrence>();
            return new(row =>
            {
                if (transfers.TryGetValue(row, out var known)) return known;
                int index = row.PreviousIndex;
                ItemOccurrence? target = (uint)index < (uint)Count && SameData(row.Item, GetItemAt(index)) ? GetOccurrenceAt(index) : null;
                if (target is null || assigned.Contains(target))
                {
                    target = null;
                    for (int i = 0; i < Count; i++)
                        if (SameData(row.Item, GetItemAt(i)) && GetOccurrenceAt(i) is { } candidate && !assigned.Contains(candidate)) { target = candidate; break; }
                }
                if (target != null) { assigned.Add(target); transfers[row] = target; }
                return target;
            });
        }
        _occurrences = old.Where(row => row.IsAlive).ToDictionary(row => row.Index);
        if (args.Action == NotifyCollectionChangedAction.Replace)
            foreach (var row in old.Where(row => !row.IsAlive))
            {
                int offset = row.PreviousIndex - args.OldStartingIndex;
                if ((uint)offset < (uint)added && GetOccurrenceAt(args.NewStartingIndex + offset) is { } replacement) transfers[row] = replacement;
            }
        return new(row => transfers.GetValueOrDefault(row));
    }

    private static bool SameData(object? left, object? right) => ReferenceEquals(left, right) ||
        left is not null && left.GetType().IsValueType && Equals(left, right);
}
