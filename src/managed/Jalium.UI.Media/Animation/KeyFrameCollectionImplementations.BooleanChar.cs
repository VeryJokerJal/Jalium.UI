using System.Collections;

namespace Jalium.UI.Media.Animation;

public partial class BooleanKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<BooleanKeyFrame> _storage;
    public BooleanKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(BooleanKeyFrame? oldValue, BooleanKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public BooleanKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (BooleanKeyFrame)value!; }
    public int Add(BooleanKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((BooleanKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(BooleanKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((BooleanKeyFrame)value!);
    public int IndexOf(BooleanKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((BooleanKeyFrame)value!);
    public void Insert(int index, BooleanKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (BooleanKeyFrame)value!);
    public void Remove(BooleanKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((BooleanKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(BooleanKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class CharKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<CharKeyFrame> _storage;
    public CharKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(CharKeyFrame? oldValue, CharKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public CharKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (CharKeyFrame)value!; }
    public int Add(CharKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((CharKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(CharKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((CharKeyFrame)value!);
    public int IndexOf(CharKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((CharKeyFrame)value!);
    public void Insert(int index, CharKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (CharKeyFrame)value!);
    public void Remove(CharKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((CharKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(CharKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
