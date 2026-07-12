using System.Collections;
using System.Globalization;
using System.Text;

namespace Jalium.UI.Media;

/// <summary>
/// Represents an ordered collection of <see cref="double"/> values (e.g. for a stroke dash array).
/// </summary>
/// <remarks>
/// Mirrors WPF's <c>System.Windows.Media.DoubleCollection</c> (which derives from Animatable):
/// a <see cref="Freezable"/> — so it can be frozen, cloned and change-notified — implementing
/// <see cref="IList{T}"/> and the non-generic <see cref="IList"/>, with a version-checked struct
/// enumerator that throws if the collection is mutated during enumeration, plus
/// <see cref="Parse"/>/<see cref="ToString()"/>. (Jalium keeps <c>Animatable</c> in the Media
/// assembly, which Core cannot reference, so Core's value collections derive from
/// <see cref="Freezable"/> directly — the freeze/clone/change contract is identical.)
/// </remarks>
[System.ComponentModel.TypeConverter(typeof(DoubleCollectionConverter))]
public sealed class DoubleCollection : Freezable, IFormattable, IList, IList<double>
{
    private readonly List<double> _collection;
    private uint _version;

    /// <summary>Initializes a new empty <see cref="DoubleCollection"/>.</summary>
    public DoubleCollection()
    {
        _collection = new List<double>();
    }

    /// <summary>Initializes a new <see cref="DoubleCollection"/> with the given capacity.</summary>
    public DoubleCollection(int capacity)
    {
        _collection = new List<double>(capacity);
    }

    /// <summary>Initializes a new <see cref="DoubleCollection"/> populated from <paramref name="collection"/>.</summary>
    public DoubleCollection(IEnumerable<double> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _collection = collection is ICollection<double> c ? new List<double>(c.Count) : new List<double>();
        foreach (var item in collection)
            _collection.Add(item);
    }

    #region Freezable

    /// <summary>Creates a modifiable deep clone of this collection.</summary>
    public new DoubleCollection Clone() => (DoubleCollection)base.Clone();

    /// <summary>Creates a modifiable clone of this collection using current values.</summary>
    public new DoubleCollection CloneCurrentValue() => (DoubleCollection)base.CloneCurrentValue();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new DoubleCollection();

    /// <inheritdoc />
    protected override void CloneCore(Freezable source)
    {
        var src = (DoubleCollection)source;
        base.CloneCore(source);
        CopyFrom(src);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable source)
    {
        var src = (DoubleCollection)source;
        base.CloneCurrentValueCore(source);
        CopyFrom(src);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable source)
    {
        var src = (DoubleCollection)source;
        base.GetAsFrozenCore(source);
        CopyFrom(src);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        var src = (DoubleCollection)source;
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom(src);
    }

    private void CopyFrom(DoubleCollection src)
    {
        _collection.Clear();
        _collection.AddRange(src._collection);
        ++_version;
    }

    #endregion

    #region IList<double>

    /// <summary>Gets or sets the element at the specified index.</summary>
    public double this[int index]
    {
        get => _collection[index];
        set
        {
            WritePreamble();
            _collection[index] = value;
            ++_version;
            WritePostscript();
        }
    }

    /// <summary>Gets the number of elements in the collection.</summary>
    public int Count => _collection.Count;

    bool ICollection<double>.IsReadOnly => IsFrozen;

    /// <summary>Adds a value to the end of the collection.</summary>
    public void Add(double value)
    {
        WritePreamble();
        _collection.Add(value);
        ++_version;
        WritePostscript();
    }

    /// <summary>Removes all elements.</summary>
    public void Clear()
    {
        WritePreamble();
        _collection.Clear();
        ++_version;
        WritePostscript();
    }

    /// <summary>Determines whether the collection contains the value.</summary>
    public bool Contains(double value) => _collection.Contains(value);

    /// <summary>Copies the collection to an array.</summary>
    public void CopyTo(double[] array, int index) => _collection.CopyTo(array, index);

    /// <summary>Returns the index of the value, or -1.</summary>
    public int IndexOf(double value) => _collection.IndexOf(value);

    /// <summary>Inserts a value at the specified index.</summary>
    public void Insert(int index, double value)
    {
        WritePreamble();
        _collection.Insert(index, value);
        ++_version;
        WritePostscript();
    }

    /// <summary>Removes the first occurrence of the value.</summary>
    public bool Remove(double value)
    {
        WritePreamble();
        bool removed = _collection.Remove(value);
        if (removed)
        {
            ++_version;
            WritePostscript();
        }
        return removed;
    }

    /// <summary>Removes the element at the specified index.</summary>
    public void RemoveAt(int index)
    {
        WritePreamble();
        _collection.RemoveAt(index);
        ++_version;
        WritePostscript();
    }

    /// <summary>Returns a version-checked struct enumerator over the collection.</summary>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<double> IEnumerable<double>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion

    #region IList (non-generic)

    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    object? IList.this[int index]
    {
        get => _collection[index];
        set => this[index] = Cast(value);
    }
    int IList.Add(object? value)
    {
        Add(Cast(value));
        return _collection.Count - 1;
    }
    bool IList.Contains(object? value) => value is double d && Contains(d);
    int IList.IndexOf(object? value) => value is double d ? IndexOf(d) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is double d) Remove(d); }
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_collection).SyncRoot;
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_collection).CopyTo(array, index);

    private static double Cast(object? value)
    {
        if (value is double d) return d;
        throw new ArgumentException($"Value must be of type {typeof(double)}.", nameof(value));
    }

    #endregion

    #region Parse / ToString

    /// <summary>Parses a string of whitespace/comma-separated doubles into a <see cref="DoubleCollection"/>.</summary>
    public static DoubleCollection Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new DoubleCollection();
        foreach (var part in source.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            result._collection.Add(double.Parse(part, CultureInfo.InvariantCulture));
        return result;
    }

    /// <inheritdoc />
    public override string ToString() => ConvertToString(null, null);

    /// <summary>Formats the collection using the given format provider.</summary>
    public string ToString(IFormatProvider? provider) => ConvertToString(null, provider);

    string IFormattable.ToString(string? format, IFormatProvider? provider) => ConvertToString(format, provider);

    private string ConvertToString(string? format, IFormatProvider? provider)
    {
        if (_collection.Count == 0)
            return string.Empty;

        provider ??= CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        for (int i = 0; i < _collection.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(_collection[i].ToString(format, provider));
        }
        return sb.ToString();
    }

    #endregion

    /// <summary>
    /// Enumerates a <see cref="DoubleCollection"/>; throws if the collection is modified during enumeration.
    /// </summary>
    public struct Enumerator : IEnumerator, IEnumerator<double>
    {
        private readonly DoubleCollection _list;
        private readonly uint _version;
        private int _index;
        private double _current;

        internal Enumerator(DoubleCollection list)
        {
            _list = list;
            _version = list._version;
            _index = -1;
            _current = default;
        }

        /// <inheritdoc />
        public void Dispose() { }

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_version != _list._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");

            if (_index > -2 && _index < _list._collection.Count - 1)
            {
                _current = _list._collection[++_index];
                return true;
            }

            _index = -2;
            _current = default;
            return false;
        }

        /// <inheritdoc />
        public readonly double Current
        {
            get
            {
                if (_index > -1)
                    return _current;
                throw new InvalidOperationException(_index == -1
                    ? "Enumeration has not started. Call MoveNext."
                    : "Enumeration already finished.");
            }
        }

        readonly object IEnumerator.Current => Current;

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was instantiated.");
            _index = -1;
        }
    }
}
