using System.Collections;
using System.Collections.Specialized;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Represents a collection of Point values that can be individually accessed by index.
/// </summary>
public sealed class PointCollection : IList<Point>, IList, INotifyCollectionChanged
{
    private readonly List<Point> _points = new();

    /// <summary>
    /// Initializes a new instance of the PointCollection class.
    /// </summary>
    public PointCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the PointCollection class with the specified points.
    /// </summary>
    /// <param name="points">The points to add to the collection.</param>
    public PointCollection(IEnumerable<Point> points)
    {
        _points.AddRange(points);
    }

    /// <summary>
    /// Initializes a new instance of the PointCollection class with the specified capacity.
    /// </summary>
    /// <param name="capacity">The number of Point values that the collection can initially store.</param>
    public PointCollection(int capacity)
    {
        _points = new List<Point>(capacity);
    }

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Gets or sets the point at the specified index.
    /// </summary>
    public Point this[int index]
    {
        get => _points[index];
        set
        {
            var oldItem = _points[index];
            _points[index] = value;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    object? IList.this[int index]
    {
        get => _points[index];
        set
        {
            if (value is Point point)
                this[index] = point;
        }
    }

    /// <summary>
    /// Gets the number of points in the collection.
    /// </summary>
    public int Count => _points.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_points).SyncRoot;

    /// <summary>
    /// Adds a point to the collection.
    /// </summary>
    public void Add(Point item)
    {
        _points.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _points.Count - 1));
    }

    int IList.Add(object? value)
    {
        if (value is Point point)
        {
            Add(point);
            return _points.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all points from the collection.
    /// </summary>
    public void Clear()
    {
        _points.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific point.
    /// </summary>
    public bool Contains(Point item) => _points.Contains(item);
    bool IList.Contains(object? value) => value is Point point && _points.Contains(point);

    /// <summary>
    /// Copies the points to an array.
    /// </summary>
    public void CopyTo(Point[] array, int arrayIndex) => _points.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_points).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<Point> GetEnumerator() => _points.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _points.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified point.
    /// </summary>
    public int IndexOf(Point item) => _points.IndexOf(item);
    int IList.IndexOf(object? value) => value is Point point ? _points.IndexOf(point) : -1;

    /// <summary>
    /// Inserts a point at the specified index.
    /// </summary>
    public void Insert(int index, Point item)
    {
        _points.Insert(index, item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    void IList.Insert(int index, object? value)
    {
        if (value is Point point)
            Insert(index, point);
    }

    /// <summary>
    /// Removes the specified point from the collection.
    /// </summary>
    public bool Remove(Point item)
    {
        var index = _points.IndexOf(item);
        if (index >= 0)
        {
            _points.RemoveAt(index);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            return true;
        }
        return false;
    }

    void IList.Remove(object? value)
    {
        if (value is Point point)
            Remove(point);
    }

    /// <summary>
    /// Removes the point at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        var item = _points[index];
        _points.RemoveAt(index);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>
    /// Parses a string representation of a point collection.
    /// </summary>
    /// <param name="source">The string to parse.</param>
    /// <returns>A new PointCollection.</returns>
    public static PointCollection Parse(string source)
    {
        var collection = new PointCollection();
        if (string.IsNullOrWhiteSpace(source))
            return collection;

        var parts = source.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i += 2)
        {
            if (double.TryParse(parts[i], out var x) && double.TryParse(parts[i + 1], out var y))
            {
                collection._points.Add(new Point(x, y));
            }
        }

        return collection;
    }

    /// <summary>
    /// Returns a string representation of the collection.
    /// </summary>
    public override string ToString()
    {
        return string.Join(" ", _points.Select(p => $"{p.X},{p.Y}"));
    }

    /// <summary>
    /// Raises the CollectionChanged event.
    /// </summary>
    protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }
}
