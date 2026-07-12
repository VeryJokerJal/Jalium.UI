using System.Collections;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

public sealed class Transform3DCollection : Animatable, IList<Transform3D>, ICollection<Transform3D>, IEnumerable<Transform3D>, IList, ICollection, IEnumerable
{
    private readonly List<Transform3D> _items;
    private uint _version;

    public Transform3DCollection() => _items = [];
    public Transform3DCollection(int capacity) => _items = new List<Transform3D>(capacity);

    public Transform3DCollection(IEnumerable<Transform3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = [];
        foreach (Transform3D item in collection)
            Add(item);
    }

    public Transform3D this[int index]
    {
        get { ReadPreamble(); return _items[index]; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePreamble();
            Transform3D oldValue = _items[index];
            OnFreezablePropertyChanged(oldValue, value);
            _items[index] = value;
            _version++;
            WritePostscript();
        }
    }

    public int Count { get { ReadPreamble(); return _items.Count; } }
    bool ICollection<Transform3D>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(Transform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WritePreamble();
        OnFreezablePropertyChanged(null, value);
        _items.Add(value);
        _version++;
        WritePostscript();
    }

    public void Clear()
    {
        WritePreamble();
        foreach (Transform3D value in _items)
            OnFreezablePropertyChanged(value, null);
        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(Transform3D value) { ReadPreamble(); return _items.Contains(value); }
    public int IndexOf(Transform3D value) { ReadPreamble(); return _items.IndexOf(value); }

    public void Insert(int index, Transform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WritePreamble();
        OnFreezablePropertyChanged(null, value);
        _items.Insert(index, value);
        _version++;
        WritePostscript();
    }

    public bool Remove(Transform3D value)
    {
        WritePreamble();
        int index = _items.IndexOf(value);
        if (index < 0)
            return false;
        Transform3D removed = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(removed, null);
        _version++;
        WritePostscript();
        return true;
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        Transform3D removed = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(removed, null);
        _version++;
        WritePostscript();
    }

    public void CopyTo(Transform3D[] array, int index) { ReadPreamble(); _items.CopyTo(array, index); }
    public Enumerator GetEnumerator() { ReadPreamble(); return new Enumerator(this); }
    IEnumerator<Transform3D> IEnumerable<Transform3D>.GetEnumerator() => GetEnumerator();
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

    bool IList.Contains(object? value) => value is Transform3D transform && Contains(transform);
    int IList.IndexOf(object? value) => value is Transform3D transform ? IndexOf(transform) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is Transform3D transform) Remove(transform); }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    public new Transform3DCollection Clone() => (Transform3DCollection)base.Clone();
    public new Transform3DCollection CloneCurrentValue() => (Transform3DCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Transform3DCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((Transform3DCollection)source, CloneMode.BaseValue);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((Transform3DCollection)source, CloneMode.CurrentValue);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((Transform3DCollection)source, CloneMode.FrozenBaseValue);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((Transform3DCollection)source, CloneMode.FrozenCurrentValue);
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
            return false;
        foreach (Transform3D item in _items)
        {
            if (isChecking)
            {
                if (!item.CanFreeze)
                    return false;
            }
            else if (!item.IsFrozen)
            {
                item.Freeze();
            }
        }
        return true;
    }

    private void CopyFrom(Transform3DCollection source, CloneMode mode)
    {
        foreach (Transform3D item in source._items)
        {
            Transform3D clone = mode switch
            {
                CloneMode.BaseValue => item.Clone(),
                CloneMode.CurrentValue => item.CloneCurrentValue(),
                CloneMode.FrozenBaseValue => (Transform3D)item.GetAsFrozen(),
                _ => (Transform3D)item.GetCurrentValueAsFrozen(),
            };
            OnFreezablePropertyChanged(null, clone);
            _items.Add(clone);
        }
        _version++;
    }

    private static Transform3D Cast(object? value) => value as Transform3D
        ?? throw new ArgumentException($"Value must be of type {typeof(Transform3D)}.", nameof(value));

    public struct Enumerator : IEnumerator<Transform3D>, IEnumerator
    {
        private readonly Transform3DCollection _collection;
        private readonly uint _version;
        private int _index;
        private Transform3D? _current;

        internal Enumerator(Transform3DCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = null;
        }

        public readonly Transform3D Current => _index >= 0 && _index < _collection._items.Count
            ? _current!
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
            _current = null;
            return false;
        }

        void IEnumerator.Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = null;
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was created.");
        }
    }
}

public sealed class GeneralTransform3DCollection : Animatable, IList<GeneralTransform3D>, ICollection<GeneralTransform3D>, IEnumerable<GeneralTransform3D>, IList, ICollection, IEnumerable
{
    private readonly List<GeneralTransform3D> _items;
    private uint _version;

    public GeneralTransform3DCollection() => _items = [];
    public GeneralTransform3DCollection(int capacity) => _items = new List<GeneralTransform3D>(capacity);

    public GeneralTransform3DCollection(IEnumerable<GeneralTransform3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = [];
        foreach (GeneralTransform3D item in collection)
            Add(item);
    }

    public GeneralTransform3D this[int index]
    {
        get { ReadPreamble(); return _items[index]; }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePreamble();
            GeneralTransform3D oldValue = _items[index];
            OnFreezablePropertyChanged(oldValue, value);
            _items[index] = value;
            _version++;
            WritePostscript();
        }
    }

    public int Count { get { ReadPreamble(); return _items.Count; } }
    bool ICollection<GeneralTransform3D>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(GeneralTransform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WritePreamble();
        OnFreezablePropertyChanged(null, value);
        _items.Add(value);
        _version++;
        WritePostscript();
    }

    public void Clear()
    {
        WritePreamble();
        foreach (GeneralTransform3D value in _items)
            OnFreezablePropertyChanged(value, null);
        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(GeneralTransform3D value) { ReadPreamble(); return _items.Contains(value); }
    public int IndexOf(GeneralTransform3D value) { ReadPreamble(); return _items.IndexOf(value); }

    public void Insert(int index, GeneralTransform3D value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WritePreamble();
        OnFreezablePropertyChanged(null, value);
        _items.Insert(index, value);
        _version++;
        WritePostscript();
    }

    public bool Remove(GeneralTransform3D value)
    {
        WritePreamble();
        int index = _items.IndexOf(value);
        if (index < 0)
            return false;
        GeneralTransform3D removed = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(removed, null);
        _version++;
        WritePostscript();
        return true;
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        GeneralTransform3D removed = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(removed, null);
        _version++;
        WritePostscript();
    }

    public void CopyTo(GeneralTransform3D[] array, int index) { ReadPreamble(); _items.CopyTo(array, index); }
    public Enumerator GetEnumerator() { ReadPreamble(); return new Enumerator(this); }
    IEnumerator<GeneralTransform3D> IEnumerable<GeneralTransform3D>.GetEnumerator() => GetEnumerator();
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

    bool IList.Contains(object? value) => value is GeneralTransform3D transform && Contains(transform);
    int IList.IndexOf(object? value) => value is GeneralTransform3D transform ? IndexOf(transform) : -1;
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    void IList.Remove(object? value) { if (value is GeneralTransform3D transform) Remove(transform); }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ArgumentNullException.ThrowIfNull(array);
        ((ICollection)_items).CopyTo(array, index);
    }

    public new GeneralTransform3DCollection Clone() => (GeneralTransform3DCollection)base.Clone();
    public new GeneralTransform3DCollection CloneCurrentValue() => (GeneralTransform3DCollection)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeneralTransform3DCollection();

    protected override void CloneCore(Freezable source)
    {
        base.CloneCore(source);
        CopyFrom((GeneralTransform3DCollection)source, CloneMode.BaseValue);
    }

    protected override void CloneCurrentValueCore(Freezable source)
    {
        base.CloneCurrentValueCore(source);
        CopyFrom((GeneralTransform3DCollection)source, CloneMode.CurrentValue);
    }

    protected override void GetAsFrozenCore(Freezable source)
    {
        base.GetAsFrozenCore(source);
        CopyFrom((GeneralTransform3DCollection)source, CloneMode.FrozenBaseValue);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable source)
    {
        base.GetCurrentValueAsFrozenCore(source);
        CopyFrom((GeneralTransform3DCollection)source, CloneMode.FrozenCurrentValue);
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
            return false;
        foreach (GeneralTransform3D item in _items)
        {
            if (isChecking)
            {
                if (!item.CanFreeze)
                    return false;
            }
            else if (!item.IsFrozen)
            {
                item.Freeze();
            }
        }
        return true;
    }

    private void CopyFrom(GeneralTransform3DCollection source, CloneMode mode)
    {
        foreach (GeneralTransform3D item in source._items)
        {
            GeneralTransform3D clone = mode switch
            {
                CloneMode.BaseValue => item.Clone(),
                CloneMode.CurrentValue => item.CloneCurrentValue(),
                CloneMode.FrozenBaseValue => (GeneralTransform3D)item.GetAsFrozen(),
                _ => (GeneralTransform3D)item.GetCurrentValueAsFrozen(),
            };
            OnFreezablePropertyChanged(null, clone);
            _items.Add(clone);
        }
        _version++;
    }

    private static GeneralTransform3D Cast(object? value) => value as GeneralTransform3D
        ?? throw new ArgumentException($"Value must be of type {typeof(GeneralTransform3D)}.", nameof(value));

    public struct Enumerator : IEnumerator<GeneralTransform3D>, IEnumerator
    {
        private readonly GeneralTransform3DCollection _collection;
        private readonly uint _version;
        private int _index;
        private GeneralTransform3D? _current;

        internal Enumerator(GeneralTransform3DCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
            _current = null;
        }

        public readonly GeneralTransform3D Current => _index >= 0 && _index < _collection._items.Count
            ? _current!
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
            _current = null;
            return false;
        }

        void IEnumerator.Reset()
        {
            VerifyVersion();
            _index = -1;
            _current = null;
        }

        private readonly void VerifyVersion()
        {
            if (_version != _collection._version)
                throw new InvalidOperationException("Collection was modified after the enumerator was created.");
        }
    }
}

internal enum CloneMode
{
    BaseValue,
    CurrentValue,
    FrozenBaseValue,
    FrozenCurrentValue,
}
