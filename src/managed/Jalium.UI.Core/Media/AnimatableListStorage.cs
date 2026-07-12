using System.Collections;

namespace Jalium.UI.Media;

/// <summary>
/// Composition-based storage shared by the WPF-shaped media collections whose direct base type
/// must remain <see cref="Animation.Animatable"/>.  Keeping storage out of their inheritance
/// chain preserves the exact public metadata while retaining the same Freezable ownership and
/// clone/freeze behaviour as <see cref="AnimatableCollection{T}"/>.
/// </summary>
internal sealed class AnimatableListStorage<T>
{
    private readonly List<T> _items;
    private readonly Action _readPreamble;
    private readonly Action _writePreamble;
    private readonly Action _writePostscript;
    private readonly Action<DependencyObject?, DependencyObject?> _changeOwner;
    private readonly Func<bool> _isFrozen;

    internal AnimatableListStorage(
        Action readPreamble,
        Action writePreamble,
        Action writePostscript,
        Action<DependencyObject?, DependencyObject?> changeOwner,
        Func<bool> isFrozen,
        int capacity = 0)
    {
        _readPreamble = readPreamble;
        _writePreamble = writePreamble;
        _writePostscript = writePostscript;
        _changeOwner = changeOwner;
        _isFrozen = isFrozen;
        _items = capacity == 0 ? new List<T>() : new List<T>(capacity);
    }

    internal T this[int index]
    {
        get
        {
            _readPreamble();
            return _items[index];
        }
        set
        {
            EnsureItem(value);
            _writePreamble();
            T old = _items[index];
            Detach(old);
            _items[index] = value;
            Attach(value);
            _writePostscript();
        }
    }

    internal int Count
    {
        get
        {
            _readPreamble();
            return _items.Count;
        }
    }

    internal bool IsReadOnly => _isFrozen();

    internal bool IsSynchronized => _isFrozen();

    internal void Add(T item)
    {
        EnsureItem(item);
        _writePreamble();
        Attach(item);
        _items.Add(item);
        _writePostscript();
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
        _writePreamble();
        foreach (T item in _items)
        {
            Detach(item);
        }

        _items.Clear();
        _writePostscript();
    }

    internal bool Contains(T item)
    {
        _readPreamble();
        return _items.Contains(item);
    }

    internal void CopyTo(T[] array, int arrayIndex)
    {
        _readPreamble();
        _items.CopyTo(array, arrayIndex);
    }

    internal void CopyTo(Array array, int index)
    {
        _readPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    internal List<T>.Enumerator GetEnumerator()
    {
        _readPreamble();
        return _items.GetEnumerator();
    }

    internal int IndexOf(T item)
    {
        _readPreamble();
        return _items.IndexOf(item);
    }

    internal void Insert(int index, T item)
    {
        EnsureItem(item);
        _writePreamble();
        Attach(item);
        _items.Insert(index, item);
        _writePostscript();
    }

    internal bool Remove(T item)
    {
        _writePreamble();
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        _writePostscript();
        return true;
    }

    internal void RemoveAt(int index)
    {
        _writePreamble();
        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        _writePostscript();
    }

    internal bool Freeze(bool isChecking)
    {
        foreach (T item in _items)
        {
            if (item is not Freezable freezable)
            {
                continue;
            }

            if (isChecking)
            {
                if (!freezable.CanFreeze)
                {
                    return false;
                }
            }
            else if (!freezable.IsFrozen)
            {
                freezable.Freeze();
            }
        }

        return true;
    }

    internal void CopyFrom(AnimatableListStorage<T> source, AnimatableListCloneMode mode)
    {
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
            _changeOwner(null, dependencyObject);
        }
    }

    private void Detach(T item)
    {
        if (item is DependencyObject dependencyObject)
        {
            _changeOwner(dependencyObject, null);
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
