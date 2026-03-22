using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Ink;

/// <summary>
/// Represents a collection of <see cref="StylusPoint"/> values that can be individually accessed by index.
/// </summary>
public sealed class StylusPointCollection : IList<StylusPoint>, IList, INotifyCollectionChanged
{
    private readonly List<StylusPoint> _points;

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class.
    /// </summary>
    public StylusPointCollection()
    {
        _points = new List<StylusPoint>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class with the specified capacity.
    /// </summary>
    /// <param name="capacity">The number of <see cref="StylusPoint"/> values that the collection can initially store.</param>
    public StylusPointCollection(int capacity)
    {
        _points = new List<StylusPoint>(capacity);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class with the specified points.
    /// </summary>
    /// <param name="points">The points to add to the collection.</param>
    public StylusPointCollection(IEnumerable<StylusPoint> points)
    {
        _points = new List<StylusPoint>(points);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class from a collection of <see cref="Point"/> values.
    /// </summary>
    /// <param name="points">The points to add to the collection with default pressure.</param>
    public StylusPointCollection(IEnumerable<Point> points)
    {
        _points = new List<StylusPoint>(points.Select(StylusPoint.FromPoint));
    }

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Occurs when any property of any point in the collection changes.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets or sets the point at the specified index.
    /// </summary>
    public StylusPoint this[int index]
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
            if (value is StylusPoint point)
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
    public void Add(StylusPoint item)
    {
        _points.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _points.Count - 1));
    }

    /// <summary>
    /// Adds a <see cref="Point"/> to the collection with default pressure.
    /// </summary>
    public void Add(Point point)
    {
        Add(StylusPoint.FromPoint(point));
    }

    int IList.Add(object? value)
    {
        if (value is StylusPoint point)
        {
            Add(point);
            return _points.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Adds a range of points to the collection.
    /// </summary>
    public void AddRange(IEnumerable<StylusPoint> points)
    {
        var startIndex = _points.Count;
        var items = points.ToList();
        _points.AddRange(items);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, startIndex));
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
    public bool Contains(StylusPoint item) => _points.Contains(item);
    bool IList.Contains(object? value) => value is StylusPoint point && _points.Contains(point);

    /// <summary>
    /// Copies the points to an array.
    /// </summary>
    public void CopyTo(StylusPoint[] array, int arrayIndex) => _points.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_points).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<StylusPoint> GetEnumerator() => _points.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _points.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified point.
    /// </summary>
    public int IndexOf(StylusPoint item) => _points.IndexOf(item);
    int IList.IndexOf(object? value) => value is StylusPoint point ? _points.IndexOf(point) : -1;

    /// <summary>
    /// Inserts a point at the specified index.
    /// </summary>
    public void Insert(int index, StylusPoint item)
    {
        _points.Insert(index, item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    void IList.Insert(int index, object? value)
    {
        if (value is StylusPoint point)
            Insert(index, point);
    }

    /// <summary>
    /// Removes the specified point from the collection.
    /// </summary>
    public bool Remove(StylusPoint item)
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
        if (value is StylusPoint point)
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
    /// Creates a copy of this collection.
    /// </summary>
    /// <returns>A new <see cref="StylusPointCollection"/> containing the same points.</returns>
    public StylusPointCollection Clone()
    {
        return new StylusPointCollection(_points);
    }

    /// <summary>
    /// Gets the bounding rectangle of all points in the collection.
    /// </summary>
    /// <returns>A <see cref="Rect"/> that bounds all points.</returns>
    public Rect GetBounds()
    {
        if (_points.Count == 0)
            return Rect.Empty;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in _points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Transforms all points in the collection by the specified matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    public void Transform(Matrix matrix)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            var transformed = matrix.Transform(point.ToPoint());
            _points[i] = new StylusPoint(transformed.X, transformed.Y, point.PressureFactor);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Raises the <see cref="CollectionChanged"/> event.
    /// </summary>
    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
