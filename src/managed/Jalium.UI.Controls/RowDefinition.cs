using System.Collections;
using Jalium.UI;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines row-specific properties that apply to Grid elements.
/// </summary>
public class RowDefinition : DefinitionBase
{
    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(GridLength), typeof(RowDefinition),
            new PropertyMetadata(new GridLength(1.0, GridUnitType.Star), DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register(nameof(MinHeight), typeof(double), typeof(RowDefinition),
            new PropertyMetadata(0.0, DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxHeightProperty =
        DependencyProperty.Register(nameof(MaxHeight), typeof(double), typeof(RowDefinition),
            new PropertyMetadata(double.PositiveInfinity, DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Gets or sets the height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridLength Height
    {
        get => (GridLength)GetValue(HeightProperty)!;
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinHeight
    {
        get => (double)GetValue(MinHeightProperty)!;
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxHeight
    {
        get => (double)GetValue(MaxHeightProperty)!;
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// Gets the actual height of the row after layout.
    /// </summary>
    public double ActualHeight { get; internal set; }

    /// <summary>
    /// Gets the offset of the row from the top of the grid.
    /// </summary>
    public double Offset { get; internal set; }
}

/// <summary>
/// Defines column-specific properties that apply to Grid elements.
/// </summary>
public class ColumnDefinition : DefinitionBase
{
    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(GridLength), typeof(ColumnDefinition),
            new PropertyMetadata(new GridLength(1.0, GridUnitType.Star), DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(ColumnDefinition),
            new PropertyMetadata(0.0, DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(ColumnDefinition),
            new PropertyMetadata(double.PositiveInfinity, DefinitionBase.OnLayoutPropertyChanged));

    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridLength Width
    {
        get => (GridLength)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// Gets the actual width of the column after layout.
    /// </summary>
    public double ActualWidth { get; internal set; }

    /// <summary>
    /// Gets the offset of the column from the left of the grid.
    /// </summary>
    public double Offset { get; internal set; }
}

/// <summary>
/// A collection of <see cref="RowDefinition"/> objects.
/// </summary>
public sealed class RowDefinitionCollection : IList<RowDefinition>, IList
{
    private readonly DefinitionCollectionStorage<RowDefinition> _storage;

    internal RowDefinitionCollection(Grid? owner = null)
    {
        _storage = new DefinitionCollectionStorage<RowDefinition>(this, owner);
    }

    internal Grid? Owner
    {
        get => _storage.Owner;
        set => _storage.Owner = value;
    }

    public int Count => _storage.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    bool IList.IsFixedSize => IsReadOnly;
    bool IList.IsReadOnly => IsReadOnly;
    bool ICollection.IsSynchronized => IsSynchronized;
    object ICollection.SyncRoot => SyncRoot;

    public RowDefinition this[int index]
    {
        get => _storage[index];
        set => _storage[index] = value;
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Require(value);
    }

    public void Add(RowDefinition value) => _storage.Add(value);
    public void Clear() => _storage.Clear();
    public bool Contains(RowDefinition value) => _storage.Contains(value);
    public void CopyTo(RowDefinition[] array, int index) => _storage.CopyTo(array, index);
    public int IndexOf(RowDefinition value) => _storage.IndexOf(value);
    public void Insert(int index, RowDefinition value) => _storage.Insert(index, value);
    public bool Remove(RowDefinition value) => _storage.Remove(value);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void RemoveRange(int index, int count) => _storage.RemoveRange(index, count);

    int IList.Add(object? value)
    {
        Add(Require(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is RowDefinition row && Contains(row);
    int IList.IndexOf(object? value) => value is RowDefinition row ? IndexOf(row) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Require(value));
    void IList.Remove(object? value) => Remove(Require(value));
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    IEnumerator<RowDefinition> IEnumerable<RowDefinition>.GetEnumerator() => _storage.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _storage.GetEnumerator();

    private static RowDefinition Require(object? value) =>
        value switch
        {
            null => throw new ArgumentNullException(nameof(value)),
            RowDefinition row => row,
            _ => throw new ArgumentException("The value must be a RowDefinition.", nameof(value)),
        };
}

/// <summary>
/// A collection of <see cref="ColumnDefinition"/> objects.
/// </summary>
public sealed class ColumnDefinitionCollection : IList<ColumnDefinition>, IList
{
    private readonly DefinitionCollectionStorage<ColumnDefinition> _storage;

    internal ColumnDefinitionCollection(Grid? owner = null)
    {
        _storage = new DefinitionCollectionStorage<ColumnDefinition>(this, owner);
    }

    internal Grid? Owner
    {
        get => _storage.Owner;
        set => _storage.Owner = value;
    }

    public int Count => _storage.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    bool IList.IsFixedSize => IsReadOnly;
    bool IList.IsReadOnly => IsReadOnly;
    bool ICollection.IsSynchronized => IsSynchronized;
    object ICollection.SyncRoot => SyncRoot;

    public ColumnDefinition this[int index]
    {
        get => _storage[index];
        set => _storage[index] = value;
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Require(value);
    }

    public void Add(ColumnDefinition value) => _storage.Add(value);
    public void Clear() => _storage.Clear();
    public bool Contains(ColumnDefinition value) => _storage.Contains(value);
    public void CopyTo(ColumnDefinition[] array, int index) => _storage.CopyTo(array, index);
    public int IndexOf(ColumnDefinition value) => _storage.IndexOf(value);
    public void Insert(int index, ColumnDefinition value) => _storage.Insert(index, value);
    public bool Remove(ColumnDefinition value) => _storage.Remove(value);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void RemoveRange(int index, int count) => _storage.RemoveRange(index, count);

    int IList.Add(object? value)
    {
        Add(Require(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is ColumnDefinition column && Contains(column);
    int IList.IndexOf(object? value) => value is ColumnDefinition column ? IndexOf(column) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Require(value));
    void IList.Remove(object? value) => Remove(Require(value));
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    IEnumerator<ColumnDefinition> IEnumerable<ColumnDefinition>.GetEnumerator() => _storage.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _storage.GetEnumerator();

    private static ColumnDefinition Require(object? value) =>
        value switch
        {
            null => throw new ArgumentNullException(nameof(value)),
            ColumnDefinition column => column,
            _ => throw new ArgumentException("The value must be a ColumnDefinition.", nameof(value)),
        };
}

internal sealed class DefinitionCollectionStorage<T> where T : DefinitionBase
{
    private readonly object _identity;
    private readonly List<T> _items = [];
    private Grid? _owner;

    internal DefinitionCollectionStorage(object identity, Grid? owner)
    {
        _identity = identity;
        _owner = owner;
    }

    internal Grid? Owner
    {
        get => _owner;
        set
        {
            if (ReferenceEquals(_owner, value))
                return;
            if (_owner is not null && value is not null)
                throw new ArgumentException("The collection already belongs to another Grid.", nameof(value));

            var oldOwner = _owner;
            _owner = value;
            foreach (var item in _items)
                item.OwnerGrid = value;

            oldOwner?.OnDefinitionChanged();
            value?.OnDefinitionChanged();
        }
    }

    internal int Count => _items.Count;

    internal T this[int index]
    {
        get => _items[index];
        set
        {
            ValidateForAddition(value);
            var previous = _items[index];
            Disconnect(previous);
            _items[index] = value;
            Connect(value);
            Modified();
        }
    }

    internal void Add(T value) => Insert(_items.Count, value);

    internal void Insert(int index, T value)
    {
        ValidateForAddition(value);
        _items.Insert(index, value);
        Connect(value);
        Modified();
    }

    internal void Clear()
    {
        foreach (var item in _items)
            Disconnect(item);
        _items.Clear();
        Modified();
    }

    internal bool Contains(T? value) => value is not null && ReferenceEquals(value.OwnerCollection, _identity);

    internal int IndexOf(T? value) => Contains(value) ? _items.IndexOf(value!) : -1;

    internal bool Remove(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = IndexOf(value);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    internal void RemoveAt(int index)
    {
        var value = _items[index];
        _items.RemoveAt(index);
        Disconnect(value);
        Modified();
    }

    internal void RemoveRange(int index, int count)
    {
        if (index < 0 || index >= _items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (_items.Count - index < count)
            throw new ArgumentException("The range exceeds the collection.", nameof(count));

        for (var itemIndex = index; itemIndex < index + count; itemIndex++)
            Disconnect(_items[itemIndex]);
        _items.RemoveRange(index, count);
        Modified();
    }

    internal void CopyTo(T[] array, int index) => _items.CopyTo(array, index);
    internal void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    internal IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    private void ValidateForAddition(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.OwnerCollection is not null)
            throw new ArgumentException("A definition can belong to only one collection.", nameof(value));
    }

    private void Connect(T value)
    {
        value.OwnerCollection = _identity;
        value.OwnerGrid = _owner;
    }

    private static void Disconnect(T value)
    {
        value.OwnerCollection = null;
        value.OwnerGrid = null;
    }

    private void Modified() => _owner?.OnDefinitionChanged();
}
