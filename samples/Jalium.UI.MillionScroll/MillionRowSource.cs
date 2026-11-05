using System.Collections;
using System.Globalization;

namespace Jalium.UI.MillionScroll;

public readonly record struct MillionRow(int Index)
{
    public int RowNumber => Index + 1;

    public string IndexText => RowNumber.ToString("N0", CultureInfo.InvariantCulture);

    public string Key => FormattableString.Invariant($"ROW-{Index % 10_000:0000}-{Index:X6}");

    public string Group => FormattableString.Invariant($"Shard {Index % 64:00}");

    public string ValueText => ((Index * 2_654_435_761L) & 0xffff).ToString("X4", CultureInfo.InvariantCulture);

    public string Payload => string.Format(
        CultureInfo.InvariantCulture,
        "segment={0:00} bucket={1:000} sample={2:000000}",
        (Index / 1000) % 80,
        (Index * 17) % 997,
        (Index * 37) % 1_000_000);
}

public sealed class MillionRowSource : IList
{
    public MillionRowSource(int count)
    {
        Count = Math.Clamp(count, 1, 10_000_000);
    }

    public int Count { get; }

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new MillionRow(index);
        }
        set => throw new NotSupportedException();
    }

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(object? value)
    {
        return IndexOf(value) >= 0;
    }

    public int IndexOf(object? value)
    {
        return value is MillionRow row && row.Index >= 0 && row.Index < Count
            ? row.Index
            : -1;
    }

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < Count; i++)
        {
            array.SetValue(new MillionRow(i), index + i);
        }
    }

    public IEnumerator GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return new MillionRow(i);
        }
    }
}
