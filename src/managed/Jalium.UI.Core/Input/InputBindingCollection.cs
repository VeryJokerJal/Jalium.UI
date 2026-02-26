using System.Collections;

namespace Jalium.UI.Input;

/// <summary>
/// Represents an ordered collection of InputBinding objects.
/// </summary>
public sealed class InputBindingCollection : IList<InputBinding>, IList
{
    private readonly List<InputBinding> _bindings = new();

    /// <summary>
    /// Initializes a new instance of the InputBindingCollection class.
    /// </summary>
    public InputBindingCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the InputBindingCollection class with the specified bindings.
    /// </summary>
    /// <param name="bindings">The bindings to add to the collection.</param>
    public InputBindingCollection(IEnumerable<InputBinding> bindings)
    {
        _bindings.AddRange(bindings);
    }

    /// <summary>
    /// Gets or sets the binding at the specified index.
    /// </summary>
    public InputBinding this[int index]
    {
        get => _bindings[index];
        set => _bindings[index] = value;
    }

    object? IList.this[int index]
    {
        get => _bindings[index];
        set
        {
            if (value is InputBinding binding)
                _bindings[index] = binding;
        }
    }

    /// <summary>
    /// Gets the number of bindings in the collection.
    /// </summary>
    public int Count => _bindings.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_bindings).SyncRoot;

    /// <summary>
    /// Adds a binding to the collection.
    /// </summary>
    public void Add(InputBinding item) => _bindings.Add(item);

    int IList.Add(object? value)
    {
        if (value is InputBinding binding)
        {
            _bindings.Add(binding);
            return _bindings.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all bindings from the collection.
    /// </summary>
    public void Clear() => _bindings.Clear();

    /// <summary>
    /// Determines whether the collection contains a specific binding.
    /// </summary>
    public bool Contains(InputBinding item) => _bindings.Contains(item);
    bool IList.Contains(object? value) => value is InputBinding binding && _bindings.Contains(binding);

    /// <summary>
    /// Copies the bindings to an array.
    /// </summary>
    public void CopyTo(InputBinding[] array, int arrayIndex) => _bindings.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_bindings).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<InputBinding> GetEnumerator() => _bindings.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _bindings.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified binding.
    /// </summary>
    public int IndexOf(InputBinding item) => _bindings.IndexOf(item);
    int IList.IndexOf(object? value) => value is InputBinding binding ? _bindings.IndexOf(binding) : -1;

    /// <summary>
    /// Inserts a binding at the specified index.
    /// </summary>
    public void Insert(int index, InputBinding item) => _bindings.Insert(index, item);
    void IList.Insert(int index, object? value)
    {
        if (value is InputBinding binding)
            _bindings.Insert(index, binding);
    }

    /// <summary>
    /// Removes the specified binding from the collection.
    /// </summary>
    public bool Remove(InputBinding item) => _bindings.Remove(item);
    void IList.Remove(object? value)
    {
        if (value is InputBinding binding)
            _bindings.Remove(binding);
    }

    /// <summary>
    /// Removes the binding at the specified index.
    /// </summary>
    public void RemoveAt(int index) => _bindings.RemoveAt(index);

    /// <summary>
    /// Finds an input binding that matches the specified input event.
    /// </summary>
    /// <param name="targetElement">The target element.</param>
    /// <param name="inputEventArgs">The input event data.</param>
    /// <returns>The matching binding, or null if not found.</returns>
    public InputBinding? FindMatch(object targetElement, InputEventArgs inputEventArgs)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Gesture?.Matches(targetElement, inputEventArgs) == true)
                return binding;
        }
        return null;
    }
}
