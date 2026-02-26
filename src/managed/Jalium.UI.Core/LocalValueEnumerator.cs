using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Provides enumeration support for the local values of any dependency properties that exist on a DependencyObject.
/// </summary>
public struct LocalValueEnumerator : IEnumerator
{
    private readonly LocalValueEntry[] _entries;
    private int _index;

    internal LocalValueEnumerator(LocalValueEntry[] entries)
    {
        _entries = entries;
        _index = -1;
    }

    public LocalValueEntry Current => _entries[_index];
    object IEnumerator.Current => Current;

    public int Count => _entries.Length;

    public bool MoveNext()
    {
        _index++;
        return _index < _entries.Length;
    }

    public void Reset() => _index = -1;
}

/// <summary>
/// Represents an entry in the local value enumeration.
/// </summary>
public readonly struct LocalValueEntry
{
    public LocalValueEntry(DependencyProperty property, object? value)
    {
        Property = property;
        Value = value;
    }

    public DependencyProperty Property { get; }
    public object? Value { get; }

    public override bool Equals(object? obj) => obj is LocalValueEntry other && Property == other.Property;
    public override int GetHashCode() => Property.GetHashCode();
    public static bool operator ==(LocalValueEntry left, LocalValueEntry right) => left.Equals(right);
    public static bool operator !=(LocalValueEntry left, LocalValueEntry right) => !left.Equals(right);
}
