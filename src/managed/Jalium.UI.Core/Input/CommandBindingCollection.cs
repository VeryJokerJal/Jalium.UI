using System.Collections;
using System.Windows.Input;

namespace Jalium.UI.Input;

/// <summary>
/// Represents a collection of CommandBinding objects.
/// </summary>
public sealed class CommandBindingCollection : IList<CommandBinding>, IList
{
    private readonly List<CommandBinding> _bindings = new();

    /// <summary>
    /// Initializes a new instance of the CommandBindingCollection class.
    /// </summary>
    public CommandBindingCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the CommandBindingCollection class with the specified bindings.
    /// </summary>
    /// <param name="bindings">The bindings to add to the collection.</param>
    public CommandBindingCollection(IEnumerable<CommandBinding> bindings)
    {
        _bindings.AddRange(bindings);
    }

    /// <summary>
    /// Gets or sets the binding at the specified index.
    /// </summary>
    public CommandBinding this[int index]
    {
        get => _bindings[index];
        set => _bindings[index] = value;
    }

    object? IList.this[int index]
    {
        get => _bindings[index];
        set
        {
            if (value is CommandBinding binding)
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
    public void Add(CommandBinding item) => _bindings.Add(item);

    int IList.Add(object? value)
    {
        if (value is CommandBinding binding)
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
    public bool Contains(CommandBinding item) => _bindings.Contains(item);
    bool IList.Contains(object? value) => value is CommandBinding binding && _bindings.Contains(binding);

    /// <summary>
    /// Copies the bindings to an array.
    /// </summary>
    public void CopyTo(CommandBinding[] array, int arrayIndex) => _bindings.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_bindings).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<CommandBinding> GetEnumerator() => _bindings.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _bindings.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified binding.
    /// </summary>
    public int IndexOf(CommandBinding item) => _bindings.IndexOf(item);
    int IList.IndexOf(object? value) => value is CommandBinding binding ? _bindings.IndexOf(binding) : -1;

    /// <summary>
    /// Inserts a binding at the specified index.
    /// </summary>
    public void Insert(int index, CommandBinding item) => _bindings.Insert(index, item);
    void IList.Insert(int index, object? value)
    {
        if (value is CommandBinding binding)
            _bindings.Insert(index, binding);
    }

    /// <summary>
    /// Removes the specified binding from the collection.
    /// </summary>
    public bool Remove(CommandBinding item) => _bindings.Remove(item);
    void IList.Remove(object? value)
    {
        if (value is CommandBinding binding)
            _bindings.Remove(binding);
    }

    /// <summary>
    /// Removes the binding at the specified index.
    /// </summary>
    public void RemoveAt(int index) => _bindings.RemoveAt(index);

    /// <summary>
    /// Finds the first binding for the specified command.
    /// </summary>
    /// <param name="command">The command to find.</param>
    /// <returns>The command binding, or null if not found.</returns>
    public CommandBinding? FindBinding(ICommand command)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Command == command)
                return binding;
        }
        return null;
    }
}
