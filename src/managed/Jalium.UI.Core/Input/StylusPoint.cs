using System.Collections;
using System.Collections.Specialized;

namespace Jalium.UI.Input;

/// <summary>
/// Represents a single sampling point from a stylus or mouse input device.
/// </summary>
public struct StylusPoint : IEquatable<StylusPoint>
{
    /// <summary>
    /// The default pressure factor value.
    /// </summary>
    public const float DefaultPressure = 0.5f;

    private double _x;
    private double _y;
    private float _pressureFactor;

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPoint"/> struct.
    /// </summary>
    public StylusPoint(double x, double y) : this(x, y, DefaultPressure) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPoint"/> struct.
    /// </summary>
    public StylusPoint(double x, double y, float pressureFactor)
    {
        _x = x;
        _y = y;
        _pressureFactor = Math.Clamp(pressureFactor, 0f, 1f);
    }

    /// <summary>Gets or sets the x-coordinate of this point.</summary>
    public double X
    {
        readonly get => _x;
        set => _x = value;
    }

    /// <summary>Gets or sets the y-coordinate of this point.</summary>
    public double Y
    {
        readonly get => _y;
        set => _y = value;
    }

    /// <summary>
    /// Gets or sets the pressure factor of this point.
    /// The pressure factor ranges from 0.0 to 1.0, where 0.5 is the default.
    /// </summary>
    public float PressureFactor
    {
        readonly get => _pressureFactor;
        set => _pressureFactor = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>Converts this <see cref="StylusPoint"/> to a <see cref="Point"/>.</summary>
    public readonly Point ToPoint() => new(_x, _y);

    /// <summary>Implicitly converts a <see cref="StylusPoint"/> to a <see cref="Point"/>.</summary>
    public static implicit operator Point(StylusPoint sp) => sp.ToPoint();

    /// <summary>Creates a <see cref="StylusPoint"/> from a <see cref="Point"/> with default pressure.</summary>
    public static StylusPoint FromPoint(Point point) => new(point.X, point.Y);

    /// <inheritdoc/>
    public readonly bool Equals(StylusPoint other)
    {
        return _x.Equals(other._x) &&
               _y.Equals(other._y) &&
               _pressureFactor.Equals(other._pressureFactor);
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
    {
        return obj is StylusPoint other && Equals(other);
    }

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        return HashCode.Combine(_x, _y, _pressureFactor);
    }

    /// <summary>Determines whether two <see cref="StylusPoint"/> instances are equal.</summary>
    public static bool operator ==(StylusPoint left, StylusPoint right) => left.Equals(right);

    /// <summary>Determines whether two <see cref="StylusPoint"/> instances are not equal.</summary>
    public static bool operator !=(StylusPoint left, StylusPoint right) => !left.Equals(right);

    /// <inheritdoc/>
    public override readonly string ToString() => $"{_x},{_y},{_pressureFactor}";
}

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
    public StylusPointCollection(int capacity)
    {
        _points = new List<StylusPoint>(capacity);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class with the specified points.
    /// </summary>
    public StylusPointCollection(IEnumerable<StylusPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = new List<StylusPoint>(points);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StylusPointCollection"/> class from a collection of <see cref="Point"/> values.
    /// </summary>
    public StylusPointCollection(IEnumerable<Point> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = new List<StylusPoint>(points.Select(StylusPoint.FromPoint));
    }

    /// <summary>Occurs when the collection changes.</summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>Occurs when any property of any point in the collection changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets or sets the point at the specified index.</summary>
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

    /// <summary>Gets the number of points in the collection.</summary>
    public int Count => _points.Count;

    /// <summary>Gets a value indicating whether the collection is read-only.</summary>
    public bool IsReadOnly => false;

    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_points).SyncRoot;

    /// <summary>Adds a point to the collection.</summary>
    public void Add(StylusPoint item)
    {
        _points.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _points.Count - 1));
    }

    /// <summary>Adds a <see cref="Point"/> to the collection with default pressure.</summary>
    public void Add(Point point) => Add(StylusPoint.FromPoint(point));

    int IList.Add(object? value)
    {
        if (value is StylusPoint point)
        {
            Add(point);
            return _points.Count - 1;
        }
        return -1;
    }

    /// <summary>Adds a range of points to the collection.</summary>
    public void AddRange(IEnumerable<StylusPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var startIndex = _points.Count;
        var items = points.ToList();
        _points.AddRange(items);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, startIndex));
    }

    /// <summary>Removes all points from the collection.</summary>
    public void Clear()
    {
        _points.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>Determines whether the collection contains a specific point.</summary>
    public bool Contains(StylusPoint item) => _points.Contains(item);
    bool IList.Contains(object? value) => value is StylusPoint point && _points.Contains(point);

    /// <summary>Copies the points to an array.</summary>
    public void CopyTo(StylusPoint[] array, int arrayIndex) => _points.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_points).CopyTo(array, index);

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    public IEnumerator<StylusPoint> GetEnumerator() => _points.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _points.GetEnumerator();

    /// <summary>Returns the index of the specified point.</summary>
    public int IndexOf(StylusPoint item) => _points.IndexOf(item);
    int IList.IndexOf(object? value) => value is StylusPoint point ? _points.IndexOf(point) : -1;

    /// <summary>Inserts a point at the specified index.</summary>
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

    /// <summary>Removes the specified point from the collection.</summary>
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

    /// <summary>Removes the point at the specified index.</summary>
    public void RemoveAt(int index)
    {
        var item = _points[index];
        _points.RemoveAt(index);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>Creates a copy of this collection.</summary>
    public StylusPointCollection Clone() => new(_points);

    /// <summary>Gets the bounding rectangle of all points in the collection.</summary>
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
    /// Transforms all points in the collection by the specified 2D affine transformation.
    /// The transformation is expressed as six matrix components (row-major):
    /// <c>[m11, m12, m21, m22, offsetX, offsetY]</c>.
    /// </summary>
    public void Transform(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            var newX = point.X * m11 + point.Y * m21 + offsetX;
            var newY = point.X * m12 + point.Y * m22 + offsetY;
            _points[i] = new StylusPoint(newX, newY, point.PressureFactor);
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Raises collection reset notification, typically after an external transformation mutates the points.
    /// </summary>
    public void NotifyReset()
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
