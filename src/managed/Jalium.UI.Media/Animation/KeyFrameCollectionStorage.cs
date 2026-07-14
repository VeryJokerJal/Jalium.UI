using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Internal list/ownership implementation shared by the WPF-shaped key-frame
/// collections. Public collection types remain direct <see cref="Freezable"/>
/// subclasses and expose only the WPF non-generic <see cref="IList"/> contract.
/// </summary>
internal sealed class KeyFrameCollectionStorage<TFrame>
    where TFrame : Freezable, IKeyFrame
{
    private readonly List<TFrame> _items = new();
    private readonly Action _readPreamble;
    private readonly Action _writePreamble;
    private readonly Action _writePostscript;
    private readonly Action<TFrame?, TFrame?> _changeChild;
    private readonly Func<bool> _isFrozen;
    private readonly Func<bool> _hasDispatcher;

    internal KeyFrameCollectionStorage(
        Action readPreamble,
        Action writePreamble,
        Action writePostscript,
        Action<TFrame?, TFrame?> changeChild,
        Func<bool> isFrozen,
        Func<bool> hasDispatcher)
    {
        _readPreamble = readPreamble;
        _writePreamble = writePreamble;
        _writePostscript = writePostscript;
        _changeChild = changeChild;
        _isFrozen = isFrozen;
        _hasDispatcher = hasDispatcher;
    }

    internal int Count
    {
        get
        {
            _readPreamble();
            return _items.Count;
        }
    }

    internal bool IsFixedSize
    {
        get
        {
            _readPreamble();
            return _isFrozen();
        }
    }

    internal bool IsReadOnly
    {
        get
        {
            _readPreamble();
            return _isFrozen();
        }
    }

    internal bool IsSynchronized
    {
        get
        {
            _readPreamble();
            return _isFrozen() || _hasDispatcher();
        }
    }

    internal object SyncRoot
    {
        get
        {
            _readPreamble();
            return ((ICollection)_items).SyncRoot;
        }
    }

    internal TFrame GetItem(int index)
    {
        _readPreamble();
        return _items[index];
    }

    internal void SetItem(int index, TFrame keyFrame)
    {
        _writePreamble();
        ArgumentNullException.ThrowIfNull(keyFrame);
        TFrame oldValue = _items[index];
        if (ReferenceEquals(oldValue, keyFrame))
        {
            return;
        }

        _changeChild(oldValue, keyFrame);
        _items[index] = keyFrame;
        _writePostscript();
    }

    internal int Add(TFrame keyFrame)
    {
        _writePreamble();
        ArgumentNullException.ThrowIfNull(keyFrame);
        int index = _items.Count;
        _changeChild(null, keyFrame);
        _items.Add(keyFrame);
        _writePostscript();
        return index;
    }

    internal void Clear()
    {
        _writePreamble();
        if (_items.Count == 0)
        {
            return;
        }

        foreach (TFrame item in _items)
        {
            _changeChild(item, null);
        }

        _items.Clear();
        _writePostscript();
    }

    internal bool Contains(TFrame keyFrame)
    {
        _readPreamble();
        return _items.Contains(keyFrame);
    }

    internal int IndexOf(TFrame keyFrame)
    {
        _readPreamble();
        return _items.IndexOf(keyFrame);
    }

    internal void Insert(int index, TFrame keyFrame)
    {
        _writePreamble();
        ArgumentNullException.ThrowIfNull(keyFrame);
        _changeChild(null, keyFrame);
        _items.Insert(index, keyFrame);
        _writePostscript();
    }

    internal void Remove(TFrame keyFrame)
    {
        _writePreamble();
        if (keyFrame is null)
        {
            return;
        }

        int index = _items.IndexOf(keyFrame);
        if (index < 0)
        {
            return;
        }

        _changeChild(keyFrame, null);
        _items.RemoveAt(index);
        _writePostscript();
    }

    internal void RemoveAt(int index)
    {
        _writePreamble();
        TFrame item = _items[index];
        _changeChild(item, null);
        _items.RemoveAt(index);
        _writePostscript();
    }

    internal void CopyTo(TFrame[] array, int index)
    {
        _readPreamble();
        ArgumentNullException.ThrowIfNull(array);
        _items.CopyTo(array, index);
    }

    internal void CopyTo(Array array, int index)
    {
        _readPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    internal IEnumerator GetEnumerator()
    {
        _readPreamble();
        return _items.GetEnumerator();
    }

    internal bool Freeze(bool isChecking)
    {
        foreach (TFrame item in _items)
        {
            if (isChecking)
            {
                if (!item.CanFreeze)
                {
                    return false;
                }
            }
            else
            {
                item.Freeze();
            }
        }

        return true;
    }

    internal void CopyFrom(KeyFrameCollectionStorage<TFrame> source, KeyFrameCollectionCloneMode mode)
    {
        foreach (TFrame oldItem in _items)
        {
            _changeChild(oldItem, null);
        }

        _items.Clear();
        foreach (TFrame sourceItem in source._items)
        {
            TFrame copy = mode switch
            {
                KeyFrameCollectionCloneMode.BaseValue => (TFrame)sourceItem.Clone(),
                KeyFrameCollectionCloneMode.CurrentValue => (TFrame)sourceItem.CloneCurrentValue(),
                KeyFrameCollectionCloneMode.AsFrozen => (TFrame)sourceItem.GetAsFrozen(),
                KeyFrameCollectionCloneMode.CurrentValueAsFrozen => (TFrame)sourceItem.GetCurrentValueAsFrozen(),
                _ => throw new InvalidOperationException(),
            };

            _changeChild(null, copy);
            _items.Add(copy);
        }
    }
}
