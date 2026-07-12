using System.Collections;

namespace Jalium.UI.Media.Media3D;

/// <summary>
/// Represents the ordered visual children owned by a 3-D visual container.
/// </summary>
public sealed class Visual3DCollection : IList, IList<Visual3D>
{
    private readonly DependencyObject _owner;
    private readonly List<Visual3D> _items = new();
    private int _version;

    internal Visual3DCollection(DependencyObject owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets the number of children in the collection.</summary>
    public int Count
    {
        get
        {
            VerifyAccess();
            return _items.Count;
        }
    }

    /// <summary>Gets or replaces the child at the specified index.</summary>
    public Visual3D this[int index]
    {
        get
        {
            VerifyAccess();
            return _items[index];
        }
        set
        {
            VerifyAccess();
            ValidateForAdd(value);

            var removed = _items[index];
            DisconnectChild(removed);
            _items[index] = value;
            _version++;
            ConnectChild(value);
        }
    }

    /// <summary>Adds a child to the end of the collection.</summary>
    public void Add(Visual3D value)
    {
        VerifyAccess();
        ValidateForAdd(value);
        _items.Add(value);
        _version++;
        ConnectChild(value);
    }

    /// <summary>Inserts a child at the specified index.</summary>
    public void Insert(int index, Visual3D value)
    {
        VerifyAccess();
        ValidateForAdd(value);
        _items.Insert(index, value);
        _version++;
        ConnectChild(value);
    }

    /// <summary>Removes the specified child.</summary>
    public bool Remove(Visual3D value)
    {
        VerifyAccess();
        if (value is null || !ReferenceEquals(value.Visual3DParent, _owner))
        {
            return false;
        }

        var index = _items.IndexOf(value);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        _version++;
        DisconnectChild(value);
        return true;
    }

    /// <summary>Removes the child at the specified index.</summary>
    public void RemoveAt(int index)
    {
        VerifyAccess();
        var removed = _items[index];
        _items.RemoveAt(index);
        _version++;
        DisconnectChild(removed);
    }

    /// <summary>Removes every child from the collection.</summary>
    public void Clear()
    {
        VerifyAccess();
        if (_items.Count == 0)
        {
            return;
        }

        var removed = _items.ToArray();
        _items.Clear();
        _version++;
        for (var index = removed.Length - 1; index >= 0; index--)
        {
            DisconnectChild(removed[index]);
        }
    }

    /// <summary>Determines whether the collection owns the specified child.</summary>
    public bool Contains(Visual3D value)
    {
        VerifyAccess();
        return value is not null && ReferenceEquals(value.Visual3DParent, _owner);
    }

    /// <summary>Returns the index of the specified child, or -1.</summary>
    public int IndexOf(Visual3D value)
    {
        VerifyAccess();
        return value is not null && ReferenceEquals(value.Visual3DParent, _owner)
            ? _items.IndexOf(value)
            : -1;
    }

    /// <summary>Copies the collection into a strongly typed array.</summary>
    public void CopyTo(Visual3D[] array, int arrayIndex)
    {
        VerifyAccess();
        _items.CopyTo(array, arrayIndex);
    }

    /// <summary>Returns a version-aware enumerator.</summary>
    public Enumerator GetEnumerator()
    {
        VerifyAccess();
        return new Enumerator(this);
    }

    IEnumerator<Visual3D> IEnumerable<Visual3D>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<Visual3D>.IsReadOnly => false;

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    bool ICollection.IsSynchronized => true;

    object ICollection.SyncRoot => _owner;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is Visual3D child && Contains(child);

    int IList.IndexOf(object? value) => value is Visual3D child ? IndexOf(child) : -1;

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    void IList.Remove(object? value)
    {
        if (value is Visual3D child)
        {
            Remove(child);
        }
    }

    void ICollection.CopyTo(Array array, int index)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank != 1)
        {
            throw new ArgumentException("The destination array must be one-dimensional.", nameof(array));
        }

        if (index < 0 || index > array.Length - Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        try
        {
            for (var itemIndex = 0; itemIndex < _items.Count; itemIndex++)
            {
                array.SetValue(_items[itemIndex], index + itemIndex);
            }
        }
        catch (InvalidCastException exception)
        {
            throw new ArgumentException("The destination array has an incompatible element type.", nameof(array), exception);
        }
    }

    private void ValidateForAdd(Visual3D? child)
    {
        if (child is null)
        {
            throw new ArgumentException("Visual3DCollection does not accept null children.", nameof(child));
        }

        if (child.Visual3DParent is not null)
        {
            throw new ArgumentException("The Visual3D already has a visual parent.", nameof(child));
        }

        for (DependencyObject? ancestor = _owner;
             ancestor is Visual3D visual;
             ancestor = visual.Visual3DParent)
        {
            if (ReferenceEquals(visual, child))
            {
                throw new ArgumentException("Adding this Visual3D would create a visual cycle.", nameof(child));
            }
        }
    }

    private void ConnectChild(Visual3D child)
    {
        if (_owner is Visual3D visualOwner)
        {
            visualOwner.AttachVisual3DChild(child);
        }
        else
        {
            child.SetVisual3DParent(_owner);
        }
    }

    private void DisconnectChild(Visual3D child)
    {
        if (_owner is Visual3D visualOwner)
        {
            visualOwner.DetachVisual3DChild(child);
        }
        else if (ReferenceEquals(child.Visual3DParent, _owner))
        {
            child.SetVisual3DParent(null);
        }
    }

    private static Visual3D Cast(object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as Visual3D
            ?? throw new ArgumentException(
                $"The value must derive from {nameof(Visual3D)}.",
                nameof(value));
    }

    private void VerifyAccess() => _owner.VerifyAccess();

    /// <summary>Enumerates a stable version of the collection.</summary>
    public struct Enumerator : IEnumerator<Visual3D>
    {
        private readonly Visual3DCollection _collection;
        private readonly int _version;
        private int _index;

        internal Enumerator(Visual3DCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
        }

        /// <inheritdoc />
        public Visual3D Current
        {
            get
            {
                VerifyState();
                if (_index < 0 || _index >= _collection._items.Count)
                {
                    throw new InvalidOperationException("The enumerator is not positioned on an element.");
                }

                return _collection._items[_index];
            }
        }

        object IEnumerator.Current => Current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            VerifyState();
            if (_index < _collection._items.Count)
            {
                _index++;
            }

            return _index < _collection._items.Count;
        }

        /// <inheritdoc />
        public void Reset()
        {
            VerifyState();
            _index = -1;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private void VerifyState()
        {
            _collection.VerifyAccess();
            if (_version != _collection._version)
            {
                throw new InvalidOperationException("The collection changed during enumeration.");
            }
        }
    }
}
