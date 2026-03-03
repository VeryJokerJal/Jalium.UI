using System.Collections;

namespace Jalium.UI.Controls.Virtualization;

/// <summary>
/// Provides count/index-based access to an <see cref="ItemsControl"/>'s logical items.
/// </summary>
internal sealed class LogicalItemAccessor
{
    private readonly ItemsControl _owner;

    public LogicalItemAccessor(ItemsControl owner)
    {
        _owner = owner;
    }

    public int Count
    {
        get
        {
            var source = _owner.ItemsSource ?? (IEnumerable)_owner.Items;
            if (source is ICollection collection)
            {
                return collection.Count;
            }

            var count = 0;
            foreach (var _ in source)
            {
                count++;
            }

            return count;
        }
    }

    public object? GetItemAt(int index)
    {
        if (index < 0)
        {
            return null;
        }

        var source = _owner.ItemsSource ?? (IEnumerable)_owner.Items;
        if (source is IList list)
        {
            return index < list.Count ? list[index] : null;
        }

        var i = 0;
        foreach (var item in source)
        {
            if (i == index)
            {
                return item;
            }

            i++;
        }

        return null;
    }

    public int IndexOf(object? item)
    {
        if (item == null)
        {
            return -1;
        }

        var source = _owner.ItemsSource ?? (IEnumerable)_owner.Items;
        if (source is IList list)
        {
            return list.IndexOf(item);
        }

        var i = 0;
        foreach (var current in source)
        {
            if (Equals(current, item))
            {
                return i;
            }

            i++;
        }

        return -1;
    }
}

