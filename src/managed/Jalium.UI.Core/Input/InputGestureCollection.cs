using System.Collections;
using System.Collections.ObjectModel;

namespace Jalium.UI.Input;

/// <summary>
/// Represents an ordered collection of InputGesture objects.
/// </summary>
public sealed class InputGestureCollection : IList<InputGesture>, IList
{
    private readonly List<InputGesture> _gestures;
    private readonly bool _isReadOnly;

    /// <summary>
    /// Initializes a new instance of the InputGestureCollection class.
    /// </summary>
    public InputGestureCollection()
    {
        _gestures = new List<InputGesture>();
        _isReadOnly = false;
    }

    /// <summary>
    /// Initializes a new instance of the InputGestureCollection class with the specified gestures.
    /// </summary>
    /// <param name="gestures">The gestures to add to the collection.</param>
    public InputGestureCollection(IEnumerable<InputGesture> gestures)
    {
        _gestures = new List<InputGesture>(gestures);
        _isReadOnly = false;
    }

    /// <summary>
    /// Initializes a new instance of the InputGestureCollection class with the specified gestures and read-only state.
    /// </summary>
    internal InputGestureCollection(IEnumerable<InputGesture> gestures, bool isReadOnly)
    {
        _gestures = new List<InputGesture>(gestures);
        _isReadOnly = isReadOnly;
    }

    /// <summary>
    /// Gets or sets the gesture at the specified index.
    /// </summary>
    public InputGesture this[int index]
    {
        get => _gestures[index];
        set
        {
            CheckReadOnly();
            _gestures[index] = value;
        }
    }

    object? IList.this[int index]
    {
        get => _gestures[index];
        set
        {
            CheckReadOnly();
            if (value is InputGesture gesture)
                _gestures[index] = gesture;
        }
    }

    /// <summary>
    /// Gets the number of gestures in the collection.
    /// </summary>
    public int Count => _gestures.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => _isReadOnly;

    bool IList.IsFixedSize => _isReadOnly;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_gestures).SyncRoot;

    /// <summary>
    /// Adds a gesture to the collection.
    /// </summary>
    public void Add(InputGesture item)
    {
        CheckReadOnly();
        _gestures.Add(item);
    }

    int IList.Add(object? value)
    {
        CheckReadOnly();
        if (value is InputGesture gesture)
        {
            _gestures.Add(gesture);
            return _gestures.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all gestures from the collection.
    /// </summary>
    public void Clear()
    {
        CheckReadOnly();
        _gestures.Clear();
    }

    /// <summary>
    /// Determines whether the collection contains a specific gesture.
    /// </summary>
    public bool Contains(InputGesture item) => _gestures.Contains(item);
    bool IList.Contains(object? value) => value is InputGesture gesture && _gestures.Contains(gesture);

    /// <summary>
    /// Copies the gestures to an array.
    /// </summary>
    public void CopyTo(InputGesture[] array, int arrayIndex) => _gestures.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_gestures).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<InputGesture> GetEnumerator() => _gestures.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _gestures.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified gesture.
    /// </summary>
    public int IndexOf(InputGesture item) => _gestures.IndexOf(item);
    int IList.IndexOf(object? value) => value is InputGesture gesture ? _gestures.IndexOf(gesture) : -1;

    /// <summary>
    /// Inserts a gesture at the specified index.
    /// </summary>
    public void Insert(int index, InputGesture item)
    {
        CheckReadOnly();
        _gestures.Insert(index, item);
    }

    void IList.Insert(int index, object? value)
    {
        CheckReadOnly();
        if (value is InputGesture gesture)
            _gestures.Insert(index, gesture);
    }

    /// <summary>
    /// Removes the specified gesture from the collection.
    /// </summary>
    public bool Remove(InputGesture item)
    {
        CheckReadOnly();
        return _gestures.Remove(item);
    }

    void IList.Remove(object? value)
    {
        CheckReadOnly();
        if (value is InputGesture gesture)
            _gestures.Remove(gesture);
    }

    /// <summary>
    /// Removes the gesture at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        CheckReadOnly();
        _gestures.RemoveAt(index);
    }

    /// <summary>
    /// Makes this collection read-only.
    /// </summary>
    internal InputGestureCollection AsReadOnly()
    {
        return new InputGestureCollection(_gestures, true);
    }

    private void CheckReadOnly()
    {
        if (_isReadOnly)
            throw new NotSupportedException("Collection is read-only.");
    }
}
