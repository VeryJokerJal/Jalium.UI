using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Implements base support for the INameScope interface, with a dictionary store of name-object mappings.
/// </summary>
public sealed class NameScope : INameScope, IDictionary<string, object>
{
    private readonly Dictionary<string, object> _nameMap = new();

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty NameScopeProperty =
        DependencyProperty.RegisterAttached("NameScope", typeof(INameScope), typeof(NameScope), new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static INameScope? GetNameScope(DependencyObject dependencyObject)
    {
        return (INameScope?)dependencyObject.GetValue(NameScopeProperty);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetNameScope(DependencyObject dependencyObject, INameScope? value)
    {
        dependencyObject.SetValue(NameScopeProperty, value);
    }

    public void RegisterName(string name, object scopedElement)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        if (scopedElement == null) throw new ArgumentNullException(nameof(scopedElement));
        if (_nameMap.ContainsKey(name))
            throw new ArgumentException($"Name '{name}' is already registered in this scope.");
        _nameMap[name] = scopedElement;
    }

    public void UnregisterName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        if (!_nameMap.Remove(name))
            throw new ArgumentException($"Name '{name}' was not found.");
    }

    public object? FindName(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        _nameMap.TryGetValue(name, out var value);
        return value;
    }

    public int Count => _nameMap.Count;

    public bool IsReadOnly => false;

    public ICollection<string> Keys => _nameMap.Keys;

    public ICollection<object> Values => _nameMap.Values;

    public object this[string key]
    {
        get => _nameMap[key];
        set
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            _nameMap[key] = value;
        }
    }

    public void Add(string key, object value) => RegisterName(key, value);

    public void Add(KeyValuePair<string, object> item) => Add(item.Key, item.Value);

    public void Clear() => _nameMap.Clear();

    public bool Contains(KeyValuePair<string, object> item)
        => ((ICollection<KeyValuePair<string, object>>)_nameMap).Contains(item);

    public bool ContainsKey(string key) => _nameMap.ContainsKey(key);

    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<string, object>>)_nameMap).CopyTo(array, arrayIndex);

    public bool Remove(string key) => _nameMap.Remove(key);

    public bool Remove(KeyValuePair<string, object> item)
        => ((ICollection<KeyValuePair<string, object>>)_nameMap).Remove(item);

    public bool TryGetValue(string key, out object value)
        => _nameMap.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _nameMap.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Defines a contract for how names of elements should be accessed within a particular name scope.
/// </summary>
public interface INameScope
{
    void RegisterName(string name, object scopedElement);
    void UnregisterName(string name);
    object? FindName(string name);
}
