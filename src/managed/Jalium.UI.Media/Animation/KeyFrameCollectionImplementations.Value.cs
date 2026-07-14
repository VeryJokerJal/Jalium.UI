using System.Collections;

namespace Jalium.UI.Media.Animation;

public partial class MatrixKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<MatrixKeyFrame> _storage;
    public MatrixKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(MatrixKeyFrame? oldValue, MatrixKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public MatrixKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (MatrixKeyFrame)value!; }
    public int Add(MatrixKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((MatrixKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(MatrixKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((MatrixKeyFrame)value!);
    public int IndexOf(MatrixKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((MatrixKeyFrame)value!);
    public void Insert(int index, MatrixKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (MatrixKeyFrame)value!);
    public void Remove(MatrixKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((MatrixKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(MatrixKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class RectKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<RectKeyFrame> _storage;
    public RectKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(RectKeyFrame? oldValue, RectKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public RectKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (RectKeyFrame)value!; }
    public int Add(RectKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((RectKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(RectKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((RectKeyFrame)value!);
    public int IndexOf(RectKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((RectKeyFrame)value!);
    public void Insert(int index, RectKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (RectKeyFrame)value!);
    public void Remove(RectKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((RectKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(RectKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class SizeKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<SizeKeyFrame> _storage;
    public SizeKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(SizeKeyFrame? oldValue, SizeKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public SizeKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (SizeKeyFrame)value!; }
    public int Add(SizeKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((SizeKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(SizeKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((SizeKeyFrame)value!);
    public int IndexOf(SizeKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((SizeKeyFrame)value!);
    public void Insert(int index, SizeKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (SizeKeyFrame)value!);
    public void Remove(SizeKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((SizeKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(SizeKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class VectorKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<VectorKeyFrame> _storage;
    public VectorKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(VectorKeyFrame? oldValue, VectorKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public VectorKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (VectorKeyFrame)value!; }
    public int Add(VectorKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((VectorKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(VectorKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((VectorKeyFrame)value!);
    public int IndexOf(VectorKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((VectorKeyFrame)value!);
    public void Insert(int index, VectorKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (VectorKeyFrame)value!);
    public void Remove(VectorKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((VectorKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(VectorKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
