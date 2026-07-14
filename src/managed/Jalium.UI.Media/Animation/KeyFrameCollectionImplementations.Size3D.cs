using System.Collections;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

public sealed partial class Size3DKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Size3DKeyFrame> _storage;
    public Size3DKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Size3DKeyFrame? oldValue, Size3DKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Size3DKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Size3DKeyFrame)value!; }
    public int Add(Size3DKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Size3DKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Size3DKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Size3DKeyFrame)value!);
    public int IndexOf(Size3DKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Size3DKeyFrame)value!);
    public void Insert(int index, Size3DKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Size3DKeyFrame)value!);
    public void Remove(Size3DKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Size3DKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Size3DKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
