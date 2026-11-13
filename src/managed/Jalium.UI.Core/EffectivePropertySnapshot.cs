using System.Collections;

namespace Jalium.UI;

/// <summary>
/// Stable property keys captured before clone/freeze callbacks run. Small
/// Freezable graphs usually have at most four keys; keep those references in
/// the snapshot itself so each traversal does not allocate another array.
/// </summary>
internal readonly struct EffectivePropertySnapshot : IEnumerable<DependencyProperty>
{
    private readonly DependencyProperty? _first;
    private readonly DependencyProperty? _second;
    private readonly DependencyProperty? _third;
    private readonly DependencyProperty? _fourth;
    private readonly DependencyProperty[]? _array;

    internal int Length { get; }

    internal EffectivePropertySnapshot(DependencyProperty? first, DependencyProperty? second,
        DependencyProperty? third, DependencyProperty? fourth, int length)
    {
        _first = first;
        _second = second;
        _third = third;
        _fourth = fourth;
        _array = null;
        Length = length;
    }

    internal EffectivePropertySnapshot(DependencyProperty[] array, int length)
    {
        _first = _second = _third = _fourth = null;
        _array = array;
        Length = length;
    }

    private DependencyProperty this[int index] => _array is not null ? _array[index] : index switch
    {
        0 => _first!,
        1 => _second!,
        2 => _third!,
        3 => _fourth!,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<DependencyProperty> IEnumerable<DependencyProperty>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<DependencyProperty>
    {
        private readonly EffectivePropertySnapshot _snapshot;
        private int _index;

        internal Enumerator(EffectivePropertySnapshot snapshot)
        {
            _snapshot = snapshot;
            _index = -1;
        }

        public DependencyProperty Current => _snapshot[_index];
        object IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _snapshot.Length;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
