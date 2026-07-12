using System.Collections;
using System.Collections.ObjectModel;

namespace Jalium.UI;

/// <summary>
/// Stores property and event setters and becomes immutable with its owning style or trigger.
/// </summary>
public sealed class SetterBaseCollection : Collection<SetterBase>
{
    private bool _isSealed;

    /// <summary>
    /// Gets whether this collection is immutable.
    /// </summary>
    public bool IsSealed => _isSealed;

    internal void Seal()
    {
        if (_isSealed)
        {
            return;
        }

        _isSealed = true;
        foreach (var setter in Items)
        {
            setter.Seal();
        }
    }

    protected override void ClearItems()
    {
        CheckSealed();
        base.ClearItems();
    }

    protected override void InsertItem(int index, SetterBase item)
    {
        CheckSealed();
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        CheckSealed();
        base.RemoveItem(index);
    }

    protected override void SetItem(int index, SetterBase item)
    {
        CheckSealed();
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
    }

    private void CheckSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed SetterBaseCollection cannot be changed.");
        }
    }
}

/// <summary>
/// Compatibility view for Jalium's former Style.EventSetters collection. It remains backed by
/// Style.Setters so both APIs share ordering and sealing behavior.
/// </summary>
internal sealed class SetterBaseCollectionView<TSetter> : IList<TSetter>
    where TSetter : SetterBase
{
    private readonly SetterBaseCollection _source;

    internal SetterBaseCollectionView(SetterBaseCollection source)
    {
        _source = source;
    }

    public TSetter this[int index]
    {
        get => GetItem(index, out _);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _source[GetSourceIndex(index)] = value;
        }
    }

    public int Count => _source.Count(static item => item is TSetter);

    public bool IsReadOnly => _source.IsSealed;

    public void Add(TSetter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _source.Add(item);
    }

    public void Clear()
    {
        for (var i = _source.Count - 1; i >= 0; i--)
        {
            if (_source[i] is TSetter)
            {
                _source.RemoveAt(i);
            }
        }
    }

    public bool Contains(TSetter item) => IndexOf(item) >= 0;

    public void CopyTo(TSetter[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var item in this)
        {
            array[arrayIndex++] = item;
        }
    }

    public IEnumerator<TSetter> GetEnumerator()
        => _source.OfType<TSetter>().GetEnumerator();

    public int IndexOf(TSetter item)
    {
        var index = 0;
        foreach (var candidate in _source)
        {
            if (candidate is not TSetter typed)
            {
                continue;
            }

            if (EqualityComparer<TSetter>.Default.Equals(typed, item))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public void Insert(int index, TSetter item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (index == Count)
        {
            _source.Add(item);
            return;
        }

        _source.Insert(GetSourceIndex(index), item);
    }

    public bool Remove(TSetter item)
    {
        var index = IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    public void RemoveAt(int index) => _source.RemoveAt(GetSourceIndex(index));

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private TSetter GetItem(int index, out int sourceIndex)
    {
        sourceIndex = GetSourceIndex(index);
        return (TSetter)_source[sourceIndex];
    }

    private int GetSourceIndex(int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var typedIndex = 0;
        for (var i = 0; i < _source.Count; i++)
        {
            if (_source[i] is not TSetter)
            {
                continue;
            }

            if (typedIndex == index)
            {
                return i;
            }

            typedIndex++;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }
}
