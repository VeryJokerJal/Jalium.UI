using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Shared implementation for WPF-shaped key-frame collections.  The concrete
/// collections intentionally expose the non-generic <see cref="IList"/>
/// contract used by WPF while retaining generic enumeration for existing
/// Jalium call sites.
/// </summary>
public abstract class KeyFrameCollectionBase<TFrame> : Freezable, IList, IEnumerable<TFrame>
    where TFrame : Freezable, IKeyFrame
{
    private readonly List<TFrame> _items = new();

    public int Count
    {
        get
        {
            ReadPreamble();
            return _items.Count;
        }
    }

    public bool IsFixedSize => IsFrozen;

    public bool IsReadOnly => IsFrozen;

    public bool IsSynchronized => IsFrozen || Dispatcher is not null;

    public object SyncRoot => ((ICollection)_items).SyncRoot;

    public TFrame this[int index]
    {
        get
        {
            ReadPreamble();
            return _items[index];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePreamble();

            TFrame oldValue = _items[index];
            if (ReferenceEquals(oldValue, value))
            {
                return;
            }

            OnFreezablePropertyChanged(oldValue, value);
            _items[index] = value;
            WritePostscript();
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public int Add(TFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        WritePreamble();

        int index = _items.Count;
        OnFreezablePropertyChanged(null, keyFrame);
        _items.Add(keyFrame);
        WritePostscript();
        return index;
    }

    int IList.Add(object? value) => Add(Cast(value));

    public void Clear()
    {
        WritePreamble();
        if (_items.Count == 0)
        {
            return;
        }

        foreach (TFrame item in _items)
        {
            OnFreezablePropertyChanged(item, null);
        }

        _items.Clear();
        WritePostscript();
    }

    public bool Contains(TFrame keyFrame)
    {
        ReadPreamble();
        return keyFrame is not null && _items.Contains(keyFrame);
    }

    bool IList.Contains(object? value) => value is TFrame frame && Contains(frame);

    public int IndexOf(TFrame keyFrame)
    {
        ReadPreamble();
        return keyFrame is null ? -1 : _items.IndexOf(keyFrame);
    }

    int IList.IndexOf(object? value) => value is TFrame frame ? IndexOf(frame) : -1;

    public void Insert(int index, TFrame keyFrame)
    {
        ArgumentNullException.ThrowIfNull(keyFrame);
        WritePreamble();

        OnFreezablePropertyChanged(null, keyFrame);
        _items.Insert(index, keyFrame);
        WritePostscript();
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    public void Remove(TFrame keyFrame)
    {
        if (keyFrame is null)
        {
            return;
        }

        WritePreamble();
        int index = _items.IndexOf(keyFrame);
        if (index < 0)
        {
            return;
        }

        OnFreezablePropertyChanged(keyFrame, null);
        _items.RemoveAt(index);
        WritePostscript();
    }

    void IList.Remove(object? value)
    {
        if (value is TFrame frame)
        {
            Remove(frame);
        }
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        TFrame item = _items[index];
        OnFreezablePropertyChanged(item, null);
        _items.RemoveAt(index);
        WritePostscript();
    }

    public void CopyTo(TFrame[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ReadPreamble();
        _items.CopyTo(array, index);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ReadPreamble();
        ((ICollection)_items).CopyTo(array, index);
    }

    public IEnumerator GetEnumerator()
    {
        ReadPreamble();
        return _items.GetEnumerator();
    }

    IEnumerator<TFrame> IEnumerable<TFrame>.GetEnumerator()
    {
        ReadPreamble();
        return _items.GetEnumerator();
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
        {
            return false;
        }

        foreach (TFrame item in _items)
        {
            if (!Freeze(item, isChecking))
            {
                return false;
            }
        }

        return true;
    }

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyFrom((KeyFrameCollectionBase<TFrame>)sourceFreezable, CloneMode.BaseValue);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyFrom((KeyFrameCollectionBase<TFrame>)sourceFreezable, CloneMode.CurrentValue);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyFrom((KeyFrameCollectionBase<TFrame>)sourceFreezable, CloneMode.AsFrozen);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyFrom((KeyFrameCollectionBase<TFrame>)sourceFreezable, CloneMode.CurrentValueAsFrozen);
    }

    private void CopyFrom(KeyFrameCollectionBase<TFrame> source, CloneMode mode)
    {
        foreach (TFrame oldItem in _items)
        {
            OnFreezablePropertyChanged(oldItem, null);
        }

        _items.Clear();
        foreach (TFrame sourceItem in source._items)
        {
            TFrame copy = mode switch
            {
                CloneMode.BaseValue => (TFrame)sourceItem.Clone(),
                CloneMode.CurrentValue => (TFrame)sourceItem.CloneCurrentValue(),
                CloneMode.AsFrozen => (TFrame)sourceItem.GetAsFrozen(),
                CloneMode.CurrentValueAsFrozen => (TFrame)sourceItem.GetCurrentValueAsFrozen(),
                _ => throw new InvalidOperationException(),
            };

            OnFreezablePropertyChanged(null, copy);
            _items.Add(copy);
        }
    }

    private static TFrame Cast(object? value)
    {
        if (value is TFrame frame)
        {
            return frame;
        }

        throw new ArgumentException($"Value must be a {typeof(TFrame).FullName}.", nameof(value));
    }

    private enum CloneMode
    {
        BaseValue,
        CurrentValue,
        AsFrozen,
        CurrentValueAsFrozen,
    }
}
