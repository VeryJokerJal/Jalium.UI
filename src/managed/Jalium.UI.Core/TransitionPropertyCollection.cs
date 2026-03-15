using System.Collections.Specialized;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Represents the set of dependency property names that participate in automatic transitions.
/// </summary>
[CollectionBuilder(typeof(TransitionPropertyCollectionBuilder), nameof(TransitionPropertyCollectionBuilder.Create))]
public sealed class TransitionPropertyCollection : IList<string>, IReadOnlyList<string>, INotifyCollectionChanged
{
    /// <summary>
    /// The keyword that enables transitions for every supported property.
    /// </summary>
    public const string AllKeyword = "All";

    /// <summary>
    /// The keyword that disables automatic transitions.
    /// </summary>
    public const string NoneKeyword = "None";

    private enum SelectionMode
    {
        Explicit,
        All,
        None
    }

    private readonly List<string> _items = new();
    private SelectionMode _mode = SelectionMode.None;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransitionPropertyCollection"/> class.
    /// </summary>
    public TransitionPropertyCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransitionPropertyCollection"/> class
    /// with the specified property names.
    /// </summary>
    /// <param name="propertyNames">The property names to include.</param>
    public TransitionPropertyCollection(IEnumerable<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);
        Replace(propertyNames);
    }

    /// <summary>
    /// Occurs when the collection contents change.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Gets a value indicating whether the collection targets all properties.
    /// </summary>
    public bool IsAll => _mode == SelectionMode.All;

    /// <summary>
    /// Gets a value indicating whether the collection disables transitions.
    /// </summary>
    public bool IsNone => _mode == SelectionMode.None;

    /// <inheritdoc />
    public int Count => IsAll || IsNone ? 0 : _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public string this[int index]
    {
        get => _items[index];
        set
        {
            var normalized = NormalizeName(value);
            if (normalized == null)
                throw new ArgumentException("Transition property names cannot be null or whitespace.", nameof(value));

            EnsureExplicitMode();
            _items[index] = normalized;
            RaiseCollectionChanged();
        }
    }

    /// <summary>
    /// Creates a collection that enables transitions for every supported property.
    /// </summary>
    public static TransitionPropertyCollection All()
    {
        var collection = new TransitionPropertyCollection();
        collection._mode = SelectionMode.All;
        return collection;
    }

    /// <summary>
    /// Creates a collection that disables automatic transitions.
    /// </summary>
    public static TransitionPropertyCollection None()
    {
        return new TransitionPropertyCollection();
    }

    /// <summary>
    /// Parses a string representation into a transition property collection.
    /// </summary>
    /// <param name="source">The string to parse.</param>
    /// <returns>A new collection instance.</returns>
    public static TransitionPropertyCollection Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return None();

        var trimmed = source.Trim();
        if (string.Equals(trimmed, AllKeyword, StringComparison.OrdinalIgnoreCase))
            return All();

        if (string.Equals(trimmed, NoneKeyword, StringComparison.OrdinalIgnoreCase))
            return None();

        var collection = new TransitionPropertyCollection();
        foreach (var part in trimmed.Split(','))
        {
            collection.Add(part);
        }

        return collection;
    }

    /// <summary>
    /// Replaces the current contents with the provided property names.
    /// </summary>
    /// <param name="propertyNames">The property names to apply.</param>
    public void Replace(IEnumerable<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        _items.Clear();
        _mode = SelectionMode.None;

        foreach (var propertyName in propertyNames)
        {
            var normalized = NormalizeName(propertyName);
            if (normalized == null)
                continue;

            if (string.Equals(normalized, AllKeyword, StringComparison.OrdinalIgnoreCase))
            {
                _items.Clear();
                _mode = SelectionMode.All;
                continue;
            }

            if (string.Equals(normalized, NoneKeyword, StringComparison.OrdinalIgnoreCase))
            {
                _items.Clear();
                _mode = SelectionMode.None;
                continue;
            }

            if (_mode != SelectionMode.Explicit)
            {
                _items.Clear();
                _mode = SelectionMode.Explicit;
            }

            if (IndexOfName(normalized) < 0)
            {
                _items.Add(normalized);
            }
        }

        if (_mode == SelectionMode.Explicit && _items.Count == 0)
        {
            _mode = SelectionMode.None;
        }

        RaiseCollectionChanged();
    }

    /// <summary>
    /// Returns whether the collection matches a dependency property name.
    /// </summary>
    /// <param name="propertyName">The property name to test.</param>
    /// <returns><see langword="true"/> if the property should transition; otherwise, <see langword="false"/>.</returns>
    public bool Matches(string propertyName)
    {
        var normalized = NormalizeName(propertyName);
        if (normalized == null)
            return false;

        if (IsNone)
            return false;

        if (IsAll)
            return true;

        return IndexOfName(normalized) >= 0;
    }

    /// <inheritdoc />
    public void Add(string item)
    {
        var normalized = NormalizeName(item);
        if (normalized == null)
            return;

        if (string.Equals(normalized, AllKeyword, StringComparison.OrdinalIgnoreCase))
        {
            if (IsAll)
                return;

            _items.Clear();
            _mode = SelectionMode.All;
            RaiseCollectionChanged();
            return;
        }

        if (string.Equals(normalized, NoneKeyword, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        EnsureExplicitMode();

        if (IndexOfName(normalized) >= 0)
            return;

        _items.Add(normalized);
        RaiseCollectionChanged();
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (IsNone && _items.Count == 0)
            return;

        _items.Clear();
        _mode = SelectionMode.None;
        RaiseCollectionChanged();
    }

    /// <inheritdoc />
    public bool Contains(string item)
    {
        var normalized = NormalizeName(item);
        if (normalized == null)
            return false;

        if (IsAll)
            return true;

        if (IsNone)
            return string.Equals(normalized, NoneKeyword, StringComparison.OrdinalIgnoreCase);

        return IndexOfName(normalized) >= 0;
    }

    /// <inheritdoc />
    public void CopyTo(string[] array, int arrayIndex)
    {
        _items.CopyTo(array, arrayIndex);
    }

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    /// <inheritdoc />
    public int IndexOf(string item)
    {
        var normalized = NormalizeName(item);
        return normalized == null ? -1 : IndexOfName(normalized);
    }

    /// <inheritdoc />
    public void Insert(int index, string item)
    {
        var normalized = NormalizeName(item);
        if (normalized == null)
            return;

        if (string.Equals(normalized, AllKeyword, StringComparison.OrdinalIgnoreCase))
        {
            _items.Clear();
            _mode = SelectionMode.All;
            RaiseCollectionChanged();
            return;
        }

        if (string.Equals(normalized, NoneKeyword, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return;
        }

        EnsureExplicitMode();

        if (IndexOfName(normalized) >= 0)
            return;

        _items.Insert(index, normalized);
        RaiseCollectionChanged();
    }

    /// <inheritdoc />
    public bool Remove(string item)
    {
        var normalized = NormalizeName(item);
        if (normalized == null)
            return false;

        if (IsAll && string.Equals(normalized, AllKeyword, StringComparison.OrdinalIgnoreCase))
        {
            Clear();
            return true;
        }

        if (IsNone && string.Equals(normalized, NoneKeyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = IndexOfName(normalized);
        if (index < 0)
            return false;

        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            _mode = SelectionMode.None;
        }

        RaiseCollectionChanged();
        return true;
    }

    /// <inheritdoc />
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        if (_items.Count == 0)
        {
            _mode = SelectionMode.None;
        }

        RaiseCollectionChanged();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsAll)
            return AllKeyword;

        return IsNone || _items.Count == 0
            ? NoneKeyword
            : string.Join(", ", _items);
    }

    /// <summary>
    /// Converts a raw dependency property value into a transition property collection.
    /// </summary>
    /// <param name="value">The raw dependency property value.</param>
    /// <returns>A transition property collection.</returns>
    internal static TransitionPropertyCollection FromRawValue(object? value)
    {
        return value switch
        {
            TransitionPropertyCollection collection => collection,
            string text => Parse(text),
            IEnumerable<string> names => new TransitionPropertyCollection(names),
            _ => None()
        };
    }

    /// <summary>
    /// Produces a stable cache key for the supplied raw value.
    /// </summary>
    /// <param name="value">The raw dependency property value.</param>
    /// <returns>A cache key suitable for lookup invalidation.</returns>
    internal static string GetCacheKey(object? value)
    {
        return value switch
        {
            TransitionPropertyCollection collection => collection.ToString(),
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            _ => NoneKeyword
        };
    }

    /// <summary>
    /// Normalizes a property name token.
    /// </summary>
    /// <param name="value">The raw property name.</param>
    /// <returns>The normalized name, or <see langword="null"/> when no usable token exists.</returns>
    internal static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    /// <summary>
    /// Supports assigning raw string literals directly to the collection-typed property.
    /// </summary>
    public static implicit operator TransitionPropertyCollection(string value)
    {
        return Parse(value);
    }

    /// <summary>
    /// Supports assigning standard string arrays directly to the collection-typed property.
    /// </summary>
    public static implicit operator TransitionPropertyCollection(string[] values)
    {
        return new TransitionPropertyCollection(values);
    }

    /// <summary>
    /// Supports existing string-based comparisons and diagnostics.
    /// </summary>
    public static implicit operator string(TransitionPropertyCollection value)
    {
        return value?.ToString() ?? NoneKeyword;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void EnsureExplicitMode()
    {
        if (_mode == SelectionMode.Explicit)
            return;

        _items.Clear();
        _mode = SelectionMode.Explicit;
    }

    private int IndexOfName(string normalizedName)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i], normalizedName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseCollectionChanged()
    {
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// Enables C# collection expressions for <see cref="TransitionPropertyCollection"/>.
/// </summary>
public static class TransitionPropertyCollectionBuilder
{
    /// <summary>
    /// Creates a transition property collection from a span of property names.
    /// </summary>
    /// <param name="values">The property names to include.</param>
    /// <returns>A transition property collection.</returns>
    public static TransitionPropertyCollection Create(ReadOnlySpan<string> values)
    {
        return new TransitionPropertyCollection(values.ToArray());
    }
}
