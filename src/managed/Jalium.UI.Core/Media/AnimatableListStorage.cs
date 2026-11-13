using System.Collections;

namespace Jalium.UI.Media;

/// <summary>
/// Composition-based storage shared by the WPF-shaped media collections whose direct base type
/// must remain <see cref="Animation.Animatable"/>.  Keeping storage out of their inheritance
/// chain preserves the exact public metadata while retaining the same Freezable ownership and
/// clone/freeze behaviour as <see cref="AnimatableCollection{T}"/>.
/// Stored inline in its owner: the immutable wrapper holds references to the owner and its
/// mutable list, avoiding a separate allocation for every small geometry collection.
/// </summary>
internal readonly struct AnimatableListStorage<T>
{
    private readonly List<T> _items;
    private readonly Freezable _owner;

    internal AnimatableListStorage(
        Freezable owner,
        int capacity = 0)
    {
        _owner = owner;
        _items = capacity == 0 ? new List<T>() : new List<T>(capacity);
    }

    internal T this[int index]
    {
        get
        {
            _owner.ReadCollectionPreamble();
            return _items[index];
        }
    }

    internal void SetItem(int index, T value)
    {
        EnsureItem(value);
        _owner.WriteCollectionPreamble();
        T old = _items[index];
        Detach(old);
        _items[index] = value;
        Attach(value);
        _owner.WriteCollectionPostscript();
    }

    internal int Count
    {
        get
        {
            _owner.ReadCollectionPreamble();
            return _items.Count;
        }
    }

    internal bool IsReadOnly => _owner.IsFrozen;

    internal bool IsSynchronized => _owner.IsFrozen;

    internal void Add(T item)
    {
        EnsureItem(item);
        _owner.WriteCollectionPreamble();
        Attach(item);
        _items.Add(item);
        _owner.WriteCollectionPostscript();
    }

    internal void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (T item in items)
        {
            Add(item);
        }
    }

    internal void Clear()
    {
        _owner.WriteCollectionPreamble();
        foreach (T item in _items)
        {
            Detach(item);
        }

        _items.Clear();
        _owner.WriteCollectionPostscript();
    }

    internal bool Contains(T item)
    {
        _owner.ReadCollectionPreamble();
        return _items.Contains(item);
    }

    internal void CopyTo(T[] array, int arrayIndex)
    {
        _owner.ReadCollectionPreamble();
        _items.CopyTo(array, arrayIndex);
    }

    internal void CopyTo(Array array, int index)
    {
        _owner.ReadCollectionPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    internal List<T>.Enumerator GetEnumerator()
    {
        _owner.ReadCollectionPreamble();
        return _items.GetEnumerator();
    }

    internal int IndexOf(T item)
    {
        _owner.ReadCollectionPreamble();
        return _items.IndexOf(item);
    }

    internal void Insert(int index, T item)
    {
        EnsureItem(item);
        _owner.WriteCollectionPreamble();
        Attach(item);
        _items.Insert(index, item);
        _owner.WriteCollectionPostscript();
    }

    internal bool Remove(T item)
    {
        _owner.WriteCollectionPreamble();
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        _owner.WriteCollectionPostscript();
        return true;
    }

    internal void RemoveAt(int index)
    {
        _owner.WriteCollectionPreamble();
        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        _owner.WriteCollectionPostscript();
    }

    internal bool Freeze(bool isChecking)
    {
        foreach (T item in _items)
        {
            if (item is not Freezable freezable)
            {
                continue;
            }

            if (!Freezable.Freeze(freezable, isChecking))
            {
                return false;
            }
        }

        return true;
    }

    internal void CopyFrom(AnimatableListStorage<T> source, AnimatableListCloneMode mode)
    {
        // Value items cannot own child Freezables. Copy their contiguous data
        // once, rather than growing several intermediate arrays point by point.
        if (typeof(T).IsValueType)
        {
            _items.AddRange(source._items);
            return;
        }

        _items.EnsureCapacity(checked(_items.Count + source._items.Count));
        foreach (T item in source._items)
        {
            T copy = item;
            if (item is Freezable freezable)
            {
                copy = (T)(object)(mode switch
                {
                    AnimatableListCloneMode.Clone => freezable.Clone(),
                    AnimatableListCloneMode.CloneCurrentValue => freezable.CloneCurrentValue(),
                    AnimatableListCloneMode.GetAsFrozen => freezable.GetAsFrozen(),
                    _ => freezable.GetCurrentValueAsFrozen(),
                });
            }

            Attach(copy);
            _items.Add(copy);
        }
    }

    internal static T Cast(object? value)
    {
        if (value is T item)
        {
            return item;
        }

        throw new ArgumentException($"Value must be a {typeof(T).FullName}.", nameof(value));
    }

    private void Attach(T item)
    {
        if (item is DependencyObject dependencyObject)
        {
            _owner.ChangeCollectionOwner(null, dependencyObject);
        }
    }

    private void Detach(T item)
    {
        if (item is DependencyObject dependencyObject)
        {
            _owner.ChangeCollectionOwner(dependencyObject, null);
        }
    }

    private static void EnsureItem(T item)
    {
        if (item is null)
        {
            throw new ArgumentException("The collection cannot contain null items.", nameof(item));
        }
    }
}

internal enum AnimatableListCloneMode
{
    Clone,
    CloneCurrentValue,
    GetAsFrozen,
    GetCurrentValueAsFrozen,
}
