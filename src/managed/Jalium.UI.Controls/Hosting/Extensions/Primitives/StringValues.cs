using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.Extensions.Primitives;

/// <summary>
/// Carries zero, one, or many strings without allocating for the 0- and 1-string cases. Mirrors MS <c>StringValues</c>.
/// Common usage: HTTP header values, query parameters, parsed CLI args.
/// </summary>
public readonly struct StringValues : IList<string?>, IReadOnlyList<string?>, IEquatable<StringValues>, IEquatable<string?>, IEquatable<string?[]?>
{
    public static readonly StringValues Empty = new(Array.Empty<string?>());

    private readonly object? _values; // null | string | string?[]

    public StringValues(string? value) { _values = value; }
    public StringValues(string?[]? values) { _values = values; }

    public int Count => _values switch
    {
        null => 0,
        string => 1,
        string?[] arr => arr.Length,
        _ => 0,
    };

    public string? this[int index]
    {
        get
        {
            if (_values is string?[] arr) return arr[index];
            if (_values is string s && index == 0) return s;
            throw new IndexOutOfRangeException();
        }
    }
    string? IList<string?>.this[int index] { get => this[index]; set => throw new NotSupportedException(); }

    public bool IsReadOnly => true;

    public override string ToString()
    {
        return _values switch
        {
            null => string.Empty,
            string s => s,
            string?[] arr when arr.Length == 0 => string.Empty,
            string?[] arr when arr.Length == 1 => arr[0] ?? string.Empty,
            string?[] arr => string.Join(",", arr),
            _ => string.Empty,
        };
    }

    public string?[] ToArray() => _values switch
    {
        null => Array.Empty<string?>(),
        string s => new[] { (string?)s },
        string?[] arr => (string?[])arr.Clone(),
        _ => Array.Empty<string?>(),
    };

    public bool Equals(StringValues other)
    {
        var aCount = Count; var bCount = other.Count;
        if (aCount != bCount) return false;
        for (int i = 0; i < aCount; i++) if (this[i] != other[i]) return false;
        return true;
    }
    public bool Equals([NotNullWhen(true)] string? other) => Count == 1 && this[0] == other;
    public bool Equals(string?[]? other) => other != null && Count == other.Length && SequenceEqualCore(other);
    private bool SequenceEqualCore(string?[] other) { for (int i = 0; i < other.Length; i++) if (this[i] != other[i]) return false; return true; }
    public override bool Equals(object? obj) => obj switch
    {
        null => Count == 0,
        StringValues sv => Equals(sv),
        string s => Equals(s),
        string?[] arr => Equals(arr),
        _ => false,
    };
    public override int GetHashCode()
    {
        var h = new HashCode();
        for (int i = 0; i < Count; i++) h.Add(this[i]);
        return h.ToHashCode();
    }

    public static bool IsNullOrEmpty(StringValues v)
    {
        if (v._values is null) return true;
        if (v._values is string s) return string.IsNullOrEmpty(s);
        if (v._values is string?[] arr)
        {
            if (arr.Length == 0) return true;
            if (arr.Length == 1) return string.IsNullOrEmpty(arr[0]);
        }
        return false;
    }

    public static StringValues Concat(in StringValues a, in StringValues b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var combined = new string?[a.Count + b.Count];
        for (int i = 0; i < a.Count; i++) combined[i] = a[i];
        for (int i = 0; i < b.Count; i++) combined[a.Count + i] = b[i];
        return new StringValues(combined);
    }

    public static bool operator ==(StringValues a, StringValues b) => a.Equals(b);
    public static bool operator !=(StringValues a, StringValues b) => !a.Equals(b);
    public static implicit operator StringValues(string? value) => new(value);
    public static implicit operator StringValues(string?[]? values) => new(values);
    public static implicit operator string?(StringValues v) => v.Count == 0 ? null : v.Count == 1 ? v[0] : v.ToString();
    public static implicit operator string?[]?(StringValues v) => v.ToArray();

    public int IndexOf(string? item)
    {
        for (int i = 0; i < Count; i++) if (this[i] == item) return i;
        return -1;
    }
    public bool Contains(string? item) => IndexOf(item) >= 0;
    public void CopyTo(string?[] array, int arrayIndex) { for (int i = 0; i < Count; i++) array[arrayIndex + i] = this[i]; }
    public IEnumerator<string?> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    void IList<string?>.Insert(int index, string? item) => throw new NotSupportedException();
    void IList<string?>.RemoveAt(int index) => throw new NotSupportedException();
    void ICollection<string?>.Add(string? item) => throw new NotSupportedException();
    void ICollection<string?>.Clear() => throw new NotSupportedException();
    bool ICollection<string?>.Remove(string? item) => throw new NotSupportedException();
}

/// <summary>
/// Iterate over substrings split by a delimiter without allocating each segment.
/// </summary>
public readonly struct StringTokenizer : IEnumerable<StringSegment>
{
    private readonly string? _value;
    private readonly char[] _separators;

    public StringTokenizer(string value, char[] separators)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(separators);
        _value = value;
        _separators = separators;
    }

    public StringTokenizer(StringSegment value, char[] separators)
    {
        ArgumentNullException.ThrowIfNull(separators);
        _value = value.ToString();
        _separators = separators;
    }

    public Enumerator GetEnumerator() => new(_value, _separators);
    IEnumerator<StringSegment> IEnumerable<StringSegment>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<StringSegment>
    {
        private readonly string? _value;
        private readonly char[] _separators;
        private int _index;
        public Enumerator(string? value, char[] separators) { _value = value; _separators = separators; _index = 0; Current = default; }
        public StringSegment Current { get; private set; }
        object IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_value == null || _index > _value.Length) return false;
            var start = _index;
            var sepIdx = _value.IndexOfAny(_separators, start);
            if (sepIdx < 0)
            {
                Current = new StringSegment(_value, start, _value.Length - start);
                _index = _value.Length + 1;
                return start <= _value.Length;
            }
            Current = new StringSegment(_value, start, sepIdx - start);
            _index = sepIdx + 1;
            return true;
        }
        public void Reset() => _index = 0;
        public void Dispose() { }
    }
}
