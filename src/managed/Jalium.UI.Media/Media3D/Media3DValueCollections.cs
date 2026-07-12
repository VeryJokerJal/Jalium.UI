using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media.Media3D;

[TypeConverter(typeof(Point3DCollectionConverter))]
public sealed class Point3DCollection : Freezable, IList<Point3D>, ICollection<Point3D>, IEnumerable<Point3D>, IList, ICollection, IEnumerable, IFormattable
{
    private readonly List<Point3D> _items;
    private uint _version;

    public Point3DCollection() => _items = [];

    public Point3DCollection(int capacity) => _items = new List<Point3D>(capacity);

    public Point3DCollection(IEnumerable<Point3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new List<Point3D>(collection);
    }

    public Point3D this[int index]
    {
        get { ReadPreamble(); return _items[index]; }
        set
        {
            WritePreamble();
            _items[index] = value;
            _version++;
            WritePostscript();
        }
    }

    public int Count { get { ReadPreamble(); return _items.Count; } }
    bool ICollection<Point3D>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(Point3D value)
    {
        WritePreamble();
        _items.Add(value);
        _version++;
        WritePostscript();
    }

    public void Clear()
    {
        WritePreamble();
        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(Point3D value) { ReadPreamble(); return _items.Contains(value); }
    public int IndexOf(Point3D value) { ReadPreamble(); return _items.IndexOf(value); }

    public void Insert(int index, Point3D value)
    {
        WritePreamble();
        _items.Insert(index, value);
        _version++;
        WritePostscript();
    }

    public bool Remove(Point3D value)
    {
        WritePreamble();
        bool removed = _items.Remove(value);
        if (removed)
        {
            _version++;
            WritePostscript();
        }
        return removed;
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
    }

    public void CopyTo(Point3D[] array, int index) { ReadPreamble(); _items.CopyTo(array, index); }

    public Enumerator GetEnumerator() { ReadPreamble(); return new Enumerator(this); }
    IEnumerator<Point3D> IEnumerable<Point3D>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is Point3D point && Contains(point);
    int IList.IndexOf(object? value) => value is Point3D point ? IndexOf(point) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is Point3D point) Remove(point); }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    public new Point3DCollection Clone() => (Point3DCollection)base.Clone();
    public new Point3DCollection CloneCurrentValue() => (Point3DCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Point3DCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((Point3DCollection)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((Point3DCollection)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((Point3DCollection)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((Point3DCollection)source);
    }

    private void CopyFrom(Point3DCollection source)
    {
        _items.Clear();
        _items.AddRange(source._items);
        _version++;
    }

    public static Point3DCollection Parse(string source)
    {
        double[] numbers = Media3DCollectionParser.ParseDoubles(source);
        if (numbers.Length % 3 != 0)
            throw new FormatException("A Point3DCollection requires groups of three coordinates.");

        var result = new Point3DCollection(numbers.Length / 3);
        for (int index = 0; index < numbers.Length; index += 3)
            result._items.Add(new Point3D(numbers[index], numbers[index + 1], numbers[index + 2]));
        return result;
    }

    public override string ToString() => ConvertToString(null, null);
    public string ToString(IFormatProvider? provider) => ConvertToString(null, provider);
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ConvertToString(format, provider);

    private string ConvertToString(string? format, IFormatProvider? provider)
    {
        ReadPreamble();
        return string.Join(' ', _items.Select(point =>
            ((IFormattable)point).ToString(format, provider)));
    }

    private static Point3D Cast(object? value) => value is Point3D point
        ? point
        : throw new ArgumentException($"Value must be of type {typeof(Point3D)}.", nameof(value));

    public struct Enumerator : IEnumerator<Point3D>, IEnumerator
    {
        private readonly Point3DCollection _collection;
        private readonly uint _version;
        private int _index;
        private Point3D _current;

        internal Enumerator(Point3DCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = default;
        }

        public readonly Point3D Current => _index >= 0 && _index < _collection._items.Count
            ? _current
            : throw new InvalidOperationException("The enumerator is not positioned on an element.");
        readonly object IEnumerator.Current => Current;
        public void Dispose() { }

        public bool MoveNext()
        {
            VerifyVersion();
            if (++_index < _collection._items.Count)
            {
                _current = _collection._items[_index];
                return true;
            }
            _index = _collection._items.Count;
            _current = default;
            return false;
        }

        void IEnumerator.Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = default;
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was created.");
        }
    }
}

[TypeConverter(typeof(Vector3DCollectionConverter))]
public sealed class Vector3DCollection : Freezable, IList<Vector3D>, ICollection<Vector3D>, IEnumerable<Vector3D>, IList, ICollection, IEnumerable, IFormattable
{
    private readonly List<Vector3D> _items;
    private uint _version;

    public Vector3DCollection() => _items = [];
    public Vector3DCollection(int capacity) => _items = new List<Vector3D>(capacity);

    public Vector3DCollection(IEnumerable<Vector3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new List<Vector3D>(collection);
    }

    public Vector3D this[int index]
    {
        get { ReadPreamble(); return _items[index]; }
        set
        {
            WritePreamble();
            _items[index] = value;
            _version++;
            WritePostscript();
        }
    }

    public int Count { get { ReadPreamble(); return _items.Count; } }
    bool ICollection<Vector3D>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(Vector3D value)
    {
        WritePreamble();
        _items.Add(value);
        _version++;
        WritePostscript();
    }

    public void Clear()
    {
        WritePreamble();
        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(Vector3D value) { ReadPreamble(); return _items.Contains(value); }
    public int IndexOf(Vector3D value) { ReadPreamble(); return _items.IndexOf(value); }

    public void Insert(int index, Vector3D value)
    {
        WritePreamble();
        _items.Insert(index, value);
        _version++;
        WritePostscript();
    }

    public bool Remove(Vector3D value)
    {
        WritePreamble();
        bool removed = _items.Remove(value);
        if (removed)
        {
            _version++;
            WritePostscript();
        }
        return removed;
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
    }

    public void CopyTo(Vector3D[] array, int index) { ReadPreamble(); _items.CopyTo(array, index); }
    public Enumerator GetEnumerator() { ReadPreamble(); return new Enumerator(this); }
    IEnumerator<Vector3D> IEnumerable<Vector3D>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is Vector3D vector && Contains(vector);
    int IList.IndexOf(object? value) => value is Vector3D vector ? IndexOf(vector) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is Vector3D vector) Remove(vector); }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    public new Vector3DCollection Clone() => (Vector3DCollection)base.Clone();
    public new Vector3DCollection CloneCurrentValue() => (Vector3DCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Vector3DCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((Vector3DCollection)source);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((Vector3DCollection)source);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((Vector3DCollection)source);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((Vector3DCollection)source);
    }

    private void CopyFrom(Vector3DCollection source)
    {
        _items.Clear();
        _items.AddRange(source._items);
        _version++;
    }

    public static Vector3DCollection Parse(string source)
    {
        double[] numbers = Media3DCollectionParser.ParseDoubles(source);
        if (numbers.Length % 3 != 0)
            throw new FormatException("A Vector3DCollection requires groups of three coordinates.");

        var result = new Vector3DCollection(numbers.Length / 3);
        for (int index = 0; index < numbers.Length; index += 3)
            result._items.Add(new Vector3D(numbers[index], numbers[index + 1], numbers[index + 2]));
        return result;
    }

    public override string ToString() => ConvertToString(null, null);
    public string ToString(IFormatProvider? provider) => ConvertToString(null, provider);
    string IFormattable.ToString(string? format, IFormatProvider? provider) => ConvertToString(format, provider);

    private string ConvertToString(string? format, IFormatProvider? provider)
    {
        ReadPreamble();
        return string.Join(' ', _items.Select(vector =>
            ((IFormattable)vector).ToString(format, provider)));
    }

    private static Vector3D Cast(object? value) => value is Vector3D vector
        ? vector
        : throw new ArgumentException($"Value must be of type {typeof(Vector3D)}.", nameof(value));

    public struct Enumerator : IEnumerator<Vector3D>, IEnumerator
    {
        private readonly Vector3DCollection _collection;
        private readonly uint _version;
        private int _index;
        private Vector3D _current;

        internal Enumerator(Vector3DCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = default;
        }

        public readonly Vector3D Current => _index >= 0 && _index < _collection._items.Count
            ? _current
            : throw new InvalidOperationException("The enumerator is not positioned on an element.");
        readonly object IEnumerator.Current => Current;
        public void Dispose() { }

        public bool MoveNext()
        {
            VerifyVersion();
            if (++_index < _collection._items.Count)
            {
                _current = _collection._items[_index];
                return true;
            }
            _index = _collection._items.Count;
            _current = default;
            return false;
        }

        void IEnumerator.Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = default;
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was created.");
        }
    }
}

internal static class Media3DCollectionParser
{
    public static double[] ParseDoubles(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        string[] tokens = source.Split(
            [',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new double[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
            result[index] = double.Parse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture);
        return result;
    }
}
