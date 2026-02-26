using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Jalium.UI.Data;

/// <summary>
/// Represents a group created by a CollectionView object, based on the GroupDescriptions.
/// </summary>
public abstract class CollectionViewGroup : INotifyPropertyChanged
{
    private readonly ObservableCollection<object> _items = new();
    private readonly ReadOnlyObservableCollection<object> _readOnlyItems;

    /// <summary>
    /// Initializes a new instance of the CollectionViewGroup class.
    /// </summary>
    /// <param name="name">The name of this group.</param>
    protected CollectionViewGroup(object name)
    {
        Name = name;
        _readOnlyItems = new ReadOnlyObservableCollection<object>(_items);
    }

    /// <summary>
    /// Gets the name of this group.
    /// </summary>
    public object Name { get; }

    /// <summary>
    /// Gets the immediate items contained in this group.
    /// </summary>
    public ReadOnlyObservableCollection<object> Items => _readOnlyItems;

    /// <summary>
    /// Gets the number of items in the subtree under this group.
    /// </summary>
    public int ItemCount { get; private set; }

    /// <summary>
    /// Gets a value that indicates whether this group has any subgroups.
    /// </summary>
    public abstract bool IsBottomLevel { get; }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the internal items collection for modification by derived classes.
    /// </summary>
    protected ObservableCollection<object> ProtectedItems => _items;

    /// <summary>
    /// Gets or sets the item count for use by derived classes.
    /// </summary>
    protected int ProtectedItemCount
    {
        get => ItemCount;
        set
        {
            ItemCount = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ItemCount)));
        }
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Represents an internal implementation of CollectionViewGroup for leaf groups.
/// </summary>
internal sealed class LeafCollectionViewGroup : CollectionViewGroup
{
    public LeafCollectionViewGroup(object name) : base(name)
    {
    }

    /// <inheritdoc />
    public override bool IsBottomLevel => true;

    /// <summary>
    /// Adds an item to this group.
    /// </summary>
    public void AddItem(object item)
    {
        ProtectedItems.Add(item);
        ProtectedItemCount = ProtectedItems.Count;
    }

    /// <summary>
    /// Removes an item from this group.
    /// </summary>
    public bool RemoveItem(object item)
    {
        bool removed = ProtectedItems.Remove(item);
        if (removed)
            ProtectedItemCount = ProtectedItems.Count;
        return removed;
    }

    /// <summary>
    /// Clears all items from this group.
    /// </summary>
    public void Clear()
    {
        ProtectedItems.Clear();
        ProtectedItemCount = 0;
    }
}
