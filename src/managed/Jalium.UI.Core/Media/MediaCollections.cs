using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media;

/// <summary>
/// Shared WPF-style storage for the mutable Animatable media collections.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal abstract class AnimatableCollection<T> : Animatable, IList<T>, IList
{
    private readonly List<T> _items;

    protected AnimatableCollection() => _items = new List<T>();
    protected AnimatableCollection(int capacity) => _items = new List<T>(capacity);

    protected AnimatableCollection(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = collection is ICollection<T> source ? new List<T>(source.Count) : new List<T>();
        foreach (T item in collection)
            Add(item);
    }

    public T this[int index]
    {
        get { ReadPreamble(); return _items[index]; }
        set
        {
            EnsureItem(value);
            WritePreamble();
            T old = _items[index];
            Detach(old);
            _items[index] = value;
            Attach(value);
            WritePostscript();
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public int Count { get { ReadPreamble(); return _items.Count; } }
    bool ICollection<T>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(T item)
    {
        EnsureItem(item);
        WritePreamble();
        Attach(item);
        _items.Add(item);
        WritePostscript();
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (T item in items)
            Add(item);
    }

    public void Clear()
    {
        WritePreamble();
        foreach (T item in _items)
            Detach(item);
        _items.Clear();
        WritePostscript();
    }

    public bool Contains(T item) { ReadPreamble(); return _items.Contains(item); }
    bool IList.Contains(object? value) => value is T item && Contains(item);
    public void CopyTo(T[] array, int arrayIndex) { ReadPreamble(); _items.CopyTo(array, arrayIndex); }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(array);
        for (int i = 0; i < _items.Count; i++)
            array.SetValue(_items[i], index + i);
    }

    public List<T>.Enumerator GetEnumerator() { ReadPreamble(); return _items.GetEnumerator(); }
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(T item) { ReadPreamble(); return _items.IndexOf(item); }
    int IList.IndexOf(object? value) => value is T item ? IndexOf(item) : -1;

    public void Insert(int index, T item)
    {
        EnsureItem(item);
        WritePreamble();
        Attach(item);
        _items.Insert(index, item);
        WritePostscript();
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    public bool Remove(T item)
    {
        WritePreamble();
        int index = _items.IndexOf(item);
        if (index < 0)
            return false;
        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        WritePostscript();
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value is T item)
            Remove(item);
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        T removed = _items[index];
        _items.RemoveAt(index);
        Detach(removed);
        WritePostscript();
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
            return false;

        foreach (T item in _items)
        {
            if (item is not Freezable freezable)
                continue;
            if (isChecking)
            {
                if (!freezable.CanFreeze)
                    return false;
            }
            else
            {
                freezable.Freeze();
            }
        }
        return true;
    }

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((AnimatableCollection<T>)source, CloneMode.Clone);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((AnimatableCollection<T>)source, CloneMode.CloneCurrentValue);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((AnimatableCollection<T>)source, CloneMode.GetAsFrozen);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((AnimatableCollection<T>)source, CloneMode.GetCurrentValueAsFrozen);
    }

    private void CopyFrom(AnimatableCollection<T> source, CloneMode mode)
    {
        foreach (T item in source._items)
        {
            T clone = item;
            if (item is Freezable freezable)
            {
                clone = (T)(object)(mode switch
                {
                    CloneMode.Clone => freezable.Clone(),
                    CloneMode.CloneCurrentValue => freezable.CloneCurrentValue(),
                    CloneMode.GetAsFrozen => freezable.GetAsFrozen(),
                    _ => freezable.GetCurrentValueAsFrozen(),
                });
            }
            Attach(clone);
            _items.Add(clone);
        }
    }

    private void Attach(T item)
    {
        if (item is DependencyObject dependencyObject)
            OnFreezablePropertyChanged(null, dependencyObject);
    }

    private void Detach(T item)
    {
        if (item is DependencyObject dependencyObject)
            OnFreezablePropertyChanged(dependencyObject, null);
    }

    private static void EnsureItem(T item)
    {
        if (item is null)
            throw new ArgumentException("The collection cannot contain null items.", nameof(item));
    }

    private static T Cast(object? value)
    {
        if (value is T item)
            return item;
        throw new ArgumentException($"Value must be a {typeof(T).FullName}.", nameof(value));
    }

    private enum CloneMode
    {
        Clone,
        CloneCurrentValue,
        GetAsFrozen,
        GetCurrentValueAsFrozen,
    }
}

/// <summary>Represents an animatable collection of geometries.</summary>
public sealed class GeometryCollection : Animatable, IList<Geometry>, IList
{
    private readonly AnimatableListStorage<Geometry> _items;

    public GeometryCollection() => _items = CreateStorage();
    public GeometryCollection(int capacity) => _items = CreateStorage(capacity);
    public GeometryCollection(IEnumerable<Geometry> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<Geometry> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public Geometry this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Geometry>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Geometry>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Geometry item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<Geometry>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<Geometry> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(Geometry item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is Geometry item && Contains(item);
    public void CopyTo(Geometry[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public List<Geometry>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<Geometry> IEnumerable<Geometry>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Geometry item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is Geometry item ? IndexOf(item) : -1;
    public void Insert(int index, Geometry item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Geometry>.Cast(value));
    public bool Remove(Geometry item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is Geometry item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new GeometryCollection Clone() => (GeometryCollection)base.Clone();
    public new GeometryCollection CloneCurrentValue() => (GeometryCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeometryCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((GeometryCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((GeometryCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((GeometryCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((GeometryCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    private AnimatableListStorage<Geometry> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}

/// <summary>Represents an animatable collection of path figures.</summary>
public sealed class PathFigureCollection : Animatable, IList<PathFigure>, IList, IFormattable
{
    private readonly AnimatableListStorage<PathFigure> _items;

    public PathFigureCollection() => _items = CreateStorage();
    public PathFigureCollection(int capacity) => _items = CreateStorage(capacity);
    public PathFigureCollection(IEnumerable<PathFigure> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<PathFigure> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public PathFigure this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<PathFigure>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<PathFigure>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(PathFigure item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<PathFigure>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<PathFigure> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(PathFigure item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is PathFigure item && Contains(item);
    public void CopyTo(PathFigure[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public List<PathFigure>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<PathFigure> IEnumerable<PathFigure>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(PathFigure item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is PathFigure item ? IndexOf(item) : -1;
    public void Insert(int index, PathFigure item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<PathFigure>.Cast(value));
    public bool Remove(PathFigure item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is PathFigure item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public static PathFigureCollection Parse(string source)
    {
        Geometry geometry = Geometry.Parse(source);
        PathGeometry path = geometry is PathGeometry existing
            ? existing
            : geometry.GetFlattenedPathGeometry();
        return new PathFigureCollection(path.Figures.Select(static figure => figure.Clone()));
    }

    public new PathFigureCollection Clone() => (PathFigureCollection)base.Clone();
    public new PathFigureCollection CloneCurrentValue() => (PathFigureCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PathFigureCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((PathFigureCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((PathFigureCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((PathFigureCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((PathFigureCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    public override string ToString() => ToString(CultureInfo.CurrentCulture);
    public string ToString(IFormatProvider? provider) => string.Join(" ", this.Select(figure => figure.ToString(provider)));
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    private AnimatableListStorage<PathFigure> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}

/// <summary>Represents an animatable collection of path segments.</summary>
public sealed class PathSegmentCollection : Animatable, IList<PathSegment>, IList
{
    private readonly AnimatableListStorage<PathSegment> _items;

    public PathSegmentCollection() => _items = CreateStorage();
    public PathSegmentCollection(int capacity) => _items = CreateStorage(capacity);
    public PathSegmentCollection(IEnumerable<PathSegment> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<PathSegment> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public PathSegment this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<PathSegment>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<PathSegment>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(PathSegment item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<PathSegment>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<PathSegment> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(PathSegment item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is PathSegment item && Contains(item);
    public void CopyTo(PathSegment[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public List<PathSegment>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<PathSegment> IEnumerable<PathSegment>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(PathSegment item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is PathSegment item ? IndexOf(item) : -1;
    public void Insert(int index, PathSegment item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<PathSegment>.Cast(value));
    public bool Remove(PathSegment item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is PathSegment item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new PathSegmentCollection Clone() => (PathSegmentCollection)base.Clone();
    public new PathSegmentCollection CloneCurrentValue() => (PathSegmentCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PathSegmentCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((PathSegmentCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((PathSegmentCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((PathSegmentCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((PathSegmentCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    private AnimatableListStorage<PathSegment> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}

/// <summary>Represents an animatable collection of two-dimensional points.</summary>
[TypeConverter("Jalium.UI.Media.PointCollectionConverter, Jalium.UI.Media")]
public sealed class PointCollection : Freezable, IList<Point>, IList, IFormattable
{
    private readonly AnimatableListStorage<Point> _items;

    public PointCollection() => _items = CreateStorage();
    public PointCollection(int capacity) => _items = CreateStorage(capacity);
    public PointCollection(IEnumerable<Point> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<Point> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public Point this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Point>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Point>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Point value) => _items.Add(value);
    int IList.Add(object? value) { Add(AnimatableListStorage<Point>.Cast(value)); return Count - 1; }
    public void Clear() => _items.Clear();
    public bool Contains(Point value) => _items.Contains(value);
    bool IList.Contains(object? value) => value is Point point && Contains(point);
    public void CopyTo(Point[] array, int index) => _items.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public Enumerator GetEnumerator() => new(_items.GetEnumerator());
    IEnumerator<Point> IEnumerable<Point>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Point value) => _items.IndexOf(value);
    int IList.IndexOf(object? value) => value is Point point ? IndexOf(point) : -1;
    public void Insert(int index, Point value) => _items.Insert(index, value);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Point>.Cast(value));
    public bool Remove(Point value) => _items.Remove(value);
    void IList.Remove(object? value) { if (value is Point point) Remove(point); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public static PointCollection Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] values = Regex.Split(source.Trim(), @"[\s,]+")
            .Where(static value => value.Length != 0)
            .ToArray();
        if ((values.Length & 1) != 0)
            throw new FormatException("A point collection requires coordinate pairs.");

        var result = new PointCollection(values.Length / 2);
        for (int i = 0; i < values.Length; i += 2)
            result.Add(new Point(
                double.Parse(values[i], CultureInfo.InvariantCulture),
                double.Parse(values[i + 1], CultureInfo.InvariantCulture)));
        return result;
    }

    public new PointCollection Clone() => (PointCollection)base.Clone();
    public new PointCollection CloneCurrentValue() => (PointCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PointCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((PointCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((PointCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((PointCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((PointCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    private AnimatableListStorage<Point> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);

    public override string ToString() => ToString(CultureInfo.CurrentCulture);
    public string ToString(IFormatProvider? provider) => string.Join(" ", this.Select(point =>
        string.Format(provider, "{0},{1}", point.X, point.Y)));
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ToString(provider);

    public struct Enumerator : IEnumerator<Point>
    {
        private List<Point>.Enumerator _inner;
        internal Enumerator(List<Point>.Enumerator inner) => _inner = inner;
        public Point Current => _inner.Current;
        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => ((IEnumerator)_inner).Reset();
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>Converts path figure collections to and from path markup.</summary>
public sealed class PathFigureCollectionConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor)
            || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string source ? PathFigureCollection.Parse(source) : base.ConvertFrom(context, culture, value);

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        if (value is PathFigureCollection figures)
        {
            if (destinationType == typeof(string))
                return figures.ToString(culture);
            if (destinationType == typeof(InstanceDescriptor))
            {
                MethodInfo method = typeof(PathFigureCollection).GetMethod(
                    nameof(PathFigureCollection.Parse),
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string) },
                    null)!;
                return new InstanceDescriptor(method, new object[] { figures.ToString(CultureInfo.InvariantCulture) });
            }
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Represents a collection of <see cref="GradientStop"/> objects.
/// </summary>
/// <remarks>
/// Raises <see cref="Changed"/> whenever the collection is structurally mutated (add/insert/
/// remove/replace/clear) <em>or</em> when any contained stop's <c>Color</c>/<c>Offset</c> changes.
/// This lets an owning <see cref="GradientBrush"/> invalidate its cached content hash automatically
/// — closing the previous gap where a raw <c>List&lt;GradientStop&gt;</c> could not notify the brush
/// that a stop had been added or recoloured. Stays a <see cref="Collection{T}"/> so existing
/// index/Count/Add/foreach usage is unchanged.
/// </remarks>
public sealed class GradientStopCollection : Animatable, IList<GradientStop>, IList, IFormattable
{
    private readonly List<GradientStop> _items;

    /// <summary>
    /// Initializes a new empty GradientStopCollection.
    /// </summary>
    public GradientStopCollection()
    {
        _items = new List<GradientStop>();
    }

    public GradientStopCollection(int capacity)
    {
        _items = new List<GradientStop>(capacity);
    }

    /// <summary>
    /// Initializes a new GradientStopCollection with the specified stops.
    /// </summary>
    public GradientStopCollection(IEnumerable<GradientStop> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = collection is ICollection<GradientStop> source ? new List<GradientStop>(source.Count) : new List<GradientStop>();
        foreach (var item in collection)
            Add(item);
    }

    public GradientStop this[int index]
    {
        get => _items[index];
        set
        {
            EnsureItem(value);
            WritePreamble();
            GradientStop old = _items[index];
            if (!ReferenceEquals(old, value))
            {
                Unsubscribe(old);
                OnFreezablePropertyChanged(old, value);
                _items[index] = value;
                Subscribe(value);
                WritePostscript();
                OnChanged();
            }
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public int Count => _items.Count;

    bool ICollection<GradientStop>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen || Dispatcher is not null;
    object ICollection.SyncRoot => this;

    public void Add(GradientStop item)
    {
        EnsureItem(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Add(item);
        Subscribe(item);
        WritePostscript();
        OnChanged();
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    public void Clear()
    {
        WritePreamble();
        foreach (GradientStop stop in _items)
        {
            Unsubscribe(stop);
            OnFreezablePropertyChanged(stop, null);
        }

        _items.Clear();
        WritePostscript();
        OnChanged();
    }

    public bool Contains(GradientStop item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is GradientStop stop && Contains(stop);

    public void CopyTo(GradientStop[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int itemIndex = 0; itemIndex < _items.Count; itemIndex++)
        {
            array.SetValue(_items[itemIndex], index + itemIndex);
        }
    }

    public List<GradientStop>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator<GradientStop> IEnumerable<GradientStop>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int IndexOf(GradientStop item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is GradientStop stop ? IndexOf(stop) : -1;

    public void Insert(int index, GradientStop item)
    {
        EnsureItem(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Insert(index, item);
        Subscribe(item);
        WritePostscript();
        OnChanged();
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    public bool Remove(GradientStop item)
    {
        int index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value is GradientStop stop)
        {
            Remove(stop);
        }
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        GradientStop item = _items[index];
        Unsubscribe(item);
        OnFreezablePropertyChanged(item, null);
        _items.RemoveAt(index);
        WritePostscript();
        OnChanged();
    }

    public new GradientStopCollection Clone() => (GradientStopCollection)base.Clone();

    public new GradientStopCollection CloneCurrentValue() => (GradientStopCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new GradientStopCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((GradientStopCollection)source, static stop => stop.Clone());
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((GradientStopCollection)source, static stop => stop.CloneCurrentValue());
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((GradientStopCollection)source, static stop => (GradientStop)stop.GetAsFrozen());
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((GradientStopCollection)source, static stop => (GradientStop)stop.GetCurrentValueAsFrozen());
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
        {
            return false;
        }

        foreach (GradientStop item in _items)
        {
            if (isChecking)
            {
                if (!item.CanFreeze)
                {
                    return false;
                }
            }
            else if (!item.IsFrozen)
            {
                item.Freeze();
            }
        }

        return true;
    }

    public static GradientStopCollection Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new GradientStopCollection();
        if (string.IsNullOrWhiteSpace(source))
        {
            return result;
        }

        const string pattern = @"(?<color>#[0-9A-Fa-f]{6,8}|[A-Za-z]+)\s*(?:,\s*|\s+)(?<offset>[+-]?(?:NaN|Infinity|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?))";
        MatchCollection matches = Regex.Matches(source, pattern, RegexOptions.CultureInvariant);
        string residue = Regex.Replace(source, pattern, string.Empty, RegexOptions.CultureInvariant).Trim(' ', '\t', '\r', '\n', ',');
        if (matches.Count == 0 || residue.Length != 0)
        {
            throw new FormatException($"Invalid gradient stop collection '{source}'.");
        }

        foreach (Match match in matches)
        {
            Color color = ParseColor(match.Groups["color"].Value);
            double offset = double.Parse(match.Groups["offset"].Value, CultureInfo.InvariantCulture);
            result.Add(new GradientStop(color, offset));
        }

        return result;
    }

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider)
        => string.Join(" ", _items.Select(stop => stop.ToString(provider)));

    string IFormattable.ToString(string? format, IFormatProvider? provider)
        => ToString(provider);

    private void Subscribe(GradientStop? stop)
    {
        if (stop != null)
            stop.PropertyChangedInternal += OnStopPropertyChanged;
    }

    private void Unsubscribe(GradientStop? stop)
    {
        if (stop != null)
            stop.PropertyChangedInternal -= OnStopPropertyChanged;
    }

    private void OnStopPropertyChanged(DependencyProperty property, object? oldValue, object? newValue) => OnChanged();

    private void CopyFrom(GradientStopCollection source, Func<GradientStop, GradientStop> clone)
    {
        foreach (GradientStop item in source._items)
        {
            GradientStop copy = clone(item);
            OnFreezablePropertyChanged(null, copy);
            _items.Add(copy);
            Subscribe(copy);
        }
    }

    private static void EnsureItem(GradientStop? item)
    {
        if (item is null)
        {
            throw new ArgumentException("The collection cannot contain null items.", nameof(item));
        }
    }

    private static GradientStop Cast(object? value)
        => value is GradientStop stop
            ? stop
            : throw new ArgumentException("The value must be a GradientStop.", nameof(value));

    private static Color ParseColor(string value)
    {
        if (value.StartsWith('#'))
        {
            string hex = value[1..];
            return hex.Length switch
            {
                6 => Color.FromRgb(
                    byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                8 => Color.FromArgb(
                    byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
                _ => throw new FormatException($"Invalid color '{value}'."),
            };
        }

        var property = typeof(Colors).GetProperty(value, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
        return property?.GetValue(null) is Color color
            ? color
            : throw new FormatException($"Unknown color '{value}'.");
    }
}

/// <summary>
/// Represents a collection of Transform objects.
/// </summary>
public sealed class TransformCollection : Animatable, IList<Transform>, IList
{
    private readonly AnimatableListStorage<Transform> _items;

    /// <summary>
    /// Initializes a new empty TransformCollection.
    /// </summary>
    public TransformCollection() => _items = CreateStorage();
    public TransformCollection(int capacity) => _items = CreateStorage(capacity);
    public TransformCollection(IEnumerable<Transform> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<Transform> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public Transform this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Transform>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Transform>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Transform item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<Transform>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<Transform> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(Transform item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is Transform item && Contains(item);
    public void CopyTo(Transform[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    IEnumerator<Transform> IEnumerable<Transform>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(Transform item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is Transform item ? IndexOf(item) : -1;
    public void Insert(int index, Transform item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Transform>.Cast(value));
    public bool Remove(Transform item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is Transform item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new TransformCollection Clone() => (TransformCollection)base.Clone();
    public new TransformCollection CloneCurrentValue() => (TransformCollection)base.CloneCurrentValue();
    public Enumerator GetEnumerator() => new(this);

    protected override Freezable CreateInstanceCore() => new TransformCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((TransformCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((TransformCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((TransformCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((TransformCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    public struct Enumerator : IEnumerator<Transform>
    {
        private IEnumerator<Transform>? _inner;

        internal Enumerator(TransformCollection collection)
        {
            _inner = ((IEnumerable<Transform>)collection).GetEnumerator();
        }

        public Transform Current =>
            _inner?.Current ?? throw new InvalidOperationException("The enumerator is not positioned on an item.");
        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner?.MoveNext() ?? false;
        public void Reset() => _inner?.Reset();
        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }
    }

    private AnimatableListStorage<Transform> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}

/// <summary>
/// Represents a set of guidelines used for rendering.
/// </summary>
public sealed class GuidelineSet : Animatable
{
    public static readonly DependencyProperty GuidelinesXProperty =
        DependencyProperty.Register(nameof(GuidelinesX), typeof(DoubleCollection), typeof(GuidelineSet),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GuidelinesYProperty =
        DependencyProperty.Register(nameof(GuidelinesY), typeof(DoubleCollection), typeof(GuidelineSet),
            new PropertyMetadata(null));

    public GuidelineSet()
    {
        SetCurrentValue(GuidelinesXProperty, new DoubleCollection());
        SetCurrentValue(GuidelinesYProperty, new DoubleCollection());
    }

    public GuidelineSet(double[] guidelinesX, double[] guidelinesY)
    {
        ArgumentNullException.ThrowIfNull(guidelinesX);
        ArgumentNullException.ThrowIfNull(guidelinesY);
        SetCurrentValue(GuidelinesXProperty, new DoubleCollection(guidelinesX));
        SetCurrentValue(GuidelinesYProperty, new DoubleCollection(guidelinesY));
    }

    /// <summary>Gets or sets a collection of X coordinate guidelines.</summary>
    public DoubleCollection GuidelinesX
    {
        get => (DoubleCollection?)GetValue(GuidelinesXProperty) ?? new DoubleCollection();
        set => SetValue(GuidelinesXProperty, value);
    }

    /// <summary>Gets or sets a collection of Y coordinate guidelines.</summary>
    public DoubleCollection GuidelinesY
    {
        get => (DoubleCollection?)GetValue(GuidelinesYProperty) ?? new DoubleCollection();
        set => SetValue(GuidelinesYProperty, value);
    }

    public new GuidelineSet Clone() => (GuidelineSet)base.Clone();

    public new GuidelineSet CloneCurrentValue() => (GuidelineSet)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new GuidelineSet();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        var source = (GuidelineSet)sourceFreezable;
        GuidelinesX = source.GuidelinesX.Clone();
        GuidelinesY = source.GuidelinesY.Clone();
    }
}
