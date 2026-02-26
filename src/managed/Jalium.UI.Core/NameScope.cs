using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Implements base support for the INameScope interface, with a dictionary store of name-object mappings.
/// </summary>
public sealed class NameScope : INameScope, IEnumerable<KeyValuePair<string, object>>
{
    private readonly Dictionary<string, object> _nameMap = new();

    public static readonly DependencyProperty NameScopeProperty =
        DependencyProperty.RegisterAttached("NameScope", typeof(INameScope), typeof(NameScope), new PropertyMetadata(null));

    public static INameScope? GetNameScope(DependencyObject dependencyObject)
    {
        return (INameScope?)dependencyObject.GetValue(NameScopeProperty);
    }

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
