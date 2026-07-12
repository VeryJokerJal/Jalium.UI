using System.Collections;
using Jalium.UI.Controls;

namespace Jalium.UI;

/// <summary>
/// Represents a collection of application windows.
/// </summary>
public sealed class WindowCollection : ICollection, IEnumerable
{
    private static readonly IReadOnlyList<Window> EmptyWindows = Array.Empty<Window>();

    private readonly Func<IReadOnlyList<Window>> _itemsProvider;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes an empty window collection.
    /// </summary>
    public WindowCollection()
        : this(static () => EmptyWindows)
    {
    }

    /// <summary>
    /// Initializes a live collection backed by the supplied window provider.
    /// </summary>
    internal WindowCollection(Func<IReadOnlyList<Window>> itemsProvider)
    {
        ArgumentNullException.ThrowIfNull(itemsProvider);
        _itemsProvider = itemsProvider;
    }

    /// <summary>
    /// Gets the number of windows in the collection.
    /// </summary>
    public int Count => _itemsProvider().Count;

    /// <summary>
    /// Gets the window at the specified index.
    /// </summary>
    public Window this[int index] => _itemsProvider()[index];

    /// <inheritdoc />
    public bool IsSynchronized => false;

    /// <inheritdoc />
    public object SyncRoot => _syncRoot;

    /// <summary>
    /// Copies the windows to a one-dimensional <see cref="Window"/> array.
    /// </summary>
    public void CopyTo(Window[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        Snapshot().CopyTo(array, index);
    }

    /// <inheritdoc />
    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        Snapshot().CopyTo(array, index);
    }

    /// <inheritdoc />
    public IEnumerator GetEnumerator() => Snapshot().GetEnumerator();

    private Window[] Snapshot()
    {
        var items = _itemsProvider();
        if (items.Count == 0)
        {
            return Array.Empty<Window>();
        }

        var snapshot = new Window[items.Count];
        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i] = items[i];
        }

        return snapshot;
    }
}
