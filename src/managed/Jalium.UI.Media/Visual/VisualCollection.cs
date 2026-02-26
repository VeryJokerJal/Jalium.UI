using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Represents an ordered collection of Visual objects.
/// </summary>
public sealed class VisualCollection : IList<Visual>, IReadOnlyList<Visual>
{
    private readonly Visual _owner;
    private readonly List<Visual> _items = new();

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

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public Visual this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var old = _items[index];
            if (!ReferenceEquals(old, value))
            {
                _owner.InternalRemoveVisualChild(old);
                _items[index] = value;
                _owner.InternalAddVisualChild(value);
            }
        }
    }

    /// <summary>
    /// Adds the specified Visual to the collection.
    /// </summary>
    public void Add(Visual item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Add(item);
        _owner.InternalAddVisualChild(item);
    }

    /// <summary>
    /// Inserts the Visual at the specified index.
    /// </summary>
    public void Insert(int index, Visual item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Insert(index, item);
        _owner.InternalAddVisualChild(item);
    }

    /// <summary>
    /// Removes the first occurrence of the specified Visual.
    /// </summary>
    public bool Remove(Visual item)
    {
        if (_items.Remove(item))
        {
            _owner.InternalRemoveVisualChild(item);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes the Visual at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _owner.InternalRemoveVisualChild(item);
    }

    /// <summary>
    /// Removes a range of Visual objects from the collection.
    /// </summary>
    public void RemoveRange(int index, int count)
    {
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
            _owner.InternalRemoveVisualChild(_items[i]);
        }
        _items.Clear();
    }

    /// <summary>
    /// Determines whether the collection contains the specified Visual.
    /// </summary>
    public bool Contains(Visual item) => _items.Contains(item);

    /// <summary>
    /// Returns the index of the specified Visual.
    /// </summary>
    public int IndexOf(Visual item) => _items.IndexOf(item);

    /// <summary>
    /// Copies the elements of the collection to an array.
    /// </summary>
    public void CopyTo(Visual[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<Visual> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
