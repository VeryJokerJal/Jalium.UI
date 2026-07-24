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
        get => _owner.Items.Count;
    }

    public object? GetItemAt(int index)
    {
        if (index < 0)
        {
            return null;
        }

        var items = _owner.Items;
        return index < items.Count ? items.GetItemAt(index) : null;
    }

    public int IndexOf(object? item)
    {
        if (item == null)
        {
            return -1;
        }

        return _owner.Items.IndexOf(item);
    }
}

