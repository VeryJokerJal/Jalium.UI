using System.Collections;

namespace Jalium.UI.Media;

/// <summary>
/// Represents an ordered collection of Visual objects.
/// </summary>
public sealed class VisualCollection : ICollection, IEnumerable
{
    private readonly Visual _owner;
    private readonly List<Visual?> _items = new();
    private uint _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualCollection"/> class.
    /// </summary>
    /// <param name="parent">The Visual that owns this collection.</param>
    public VisualCollection(Visual parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _owner = parent;
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>Gets or sets the number of elements the collection can hold without resizing.</summary>
    public int Capacity
    {
        get => _items.Capacity;
        set => _items.Capacity = value;
    }

    /// <summary>Gets whether access to the collection is synchronized.</summary>
    public bool IsSynchronized => false;

    /// <summary>Gets an object that can be used to synchronize access to the collection.</summary>
    public object SyncRoot => ((ICollection)_items).SyncRoot;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public Visual this[int index]
    {
        get => _items[index]!;
        set
        {
            var old = _items[index];
            if (!ReferenceEquals(old, value))
            {
                _items[index] = value;
                try
                {
                    if (old is not null)
                    {
                        _owner.InternalRemoveVisualChild(old);
                    }

                    if (value is not null)
                    {
                        EnsureCanAttach(value);
                        _owner.InternalAddVisualChild(value);
                    }
                }
                catch
                {
                    _items[index] = old;
                    if (old is not null && old.InternalVisualParent is null)
                    {
                        _owner.InternalAddVisualChild(old);
                    }

                    throw;
                }

                _version++;
            }
        }
    }

    /// <summary>
    /// Adds the specified Visual to the collection.
    /// </summary>
    public int Add(Visual visual)
    {
        if (visual is not null)
        {
            EnsureCanAttach(visual);
        }

        int index = _items.Count;
        _items.Add(visual);
        try
        {
            if (visual is not null)
            {
                _owner.InternalAddVisualChild(visual);
            }
        }
        catch
        {
            _items.RemoveAt(index);
            throw;
        }

        _version++;
        return index;
    }

    /// <summary>
    /// Inserts the Visual at the specified index.
    /// </summary>
    public void Insert(int index, Visual visual)
    {
        if ((uint)index > (uint)_items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (visual is not null)
        {
            EnsureCanAttach(visual);
        }

        _items.Insert(index, visual);
        try
        {
            if (visual is not null)
            {
                _owner.InternalAddVisualChild(visual);
            }
        }
        catch
        {
            _items.RemoveAt(index);
            throw;
        }

        _version++;
    }

    /// <summary>
    /// Removes the first occurrence of the specified Visual.
    /// </summary>
    public void Remove(Visual visual)
    {
        int index = _items.IndexOf(visual);
        if (index >= 0)
        {
            RemoveAt(index);
        }
    }

    /// <summary>
    /// Removes the Visual at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        if (item is not null)
        {
            _owner.InternalRemoveVisualChild(item);
        }

        _version++;
    }

    /// <summary>
    /// Removes a range of Visual objects from the collection.
    /// </summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0 || count < 0 || index + count > _items.Count)
            throw new ArgumentOutOfRangeException();

        for (int i = count - 1; i >= 0; i--)
        {
            RemoveAt(index + i);
        }
    }

    /// <summary>
    /// Removes all Visual objects from the collection.
    /// </summary>
    public void Clear()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            RemoveAt(i);
        }
    }

    /// <summary>
    /// Determines whether the collection contains the specified Visual.
    /// </summary>
    public bool Contains(Visual visual) => _items.Contains(visual);

    /// <summary>
    /// Returns the index of the specified Visual.
    /// </summary>
    public int IndexOf(Visual visual) => _items.IndexOf(visual);

    /// <summary>
    /// Copies the elements of the collection to an array.
    /// </summary>
    public void CopyTo(Visual[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>Copies the collection to a compatible one-dimensional array.</summary>
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <inheritdoc />
    public Enumerator GetEnumerator() => new(this);

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static void EnsureCanAttach(Visual visual)
    {
        if (visual.InternalVisualParent is not null)
        {
            throw new ArgumentException(
                "Specified Visual is already a child of another Visual or the root of a CompositionTarget.");
        }
    }

    /// <summary>Enumerates the visuals while detecting concurrent collection changes.</summary>
    public struct Enumerator : IEnumerator
    {
        private readonly VisualCollection _collection;
        private readonly uint _version;
        private int _index;
        private Visual? _current;

        internal Enumerator(VisualCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = null;
        }

        public readonly Visual Current
        {
            get
            {
                if (_index < 0 || _index >= _collection._items.Count)
                {
                    throw new InvalidOperationException("The enumerator is not positioned on an element.");
                }

                return _current!;
            }
        }

        readonly object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            VerifyVersion();
            if (++_index < _collection._items.Count)
            {
                _current = _collection._items[_index];
                return true;
            }

            _index = _collection._items.Count;
            _current = null;
            return false;
        }

        public void Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = null;
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
            {
                throw new InvalidOperationException("Collection was modified after the enumerator was created.");
            }
        }
    }
}
