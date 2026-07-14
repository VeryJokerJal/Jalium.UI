using System.Collections;

namespace Jalium.UI.Media.Animation;

public partial class ByteKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<ByteKeyFrame> _storage;
    public ByteKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(ByteKeyFrame? oldValue, ByteKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public ByteKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (ByteKeyFrame)value!; }
    public int Add(ByteKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((ByteKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(ByteKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((ByteKeyFrame)value!);
    public int IndexOf(ByteKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((ByteKeyFrame)value!);
    public void Insert(int index, ByteKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (ByteKeyFrame)value!);
    public void Remove(ByteKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((ByteKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(ByteKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class DecimalKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<DecimalKeyFrame> _storage;
    public DecimalKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(DecimalKeyFrame? oldValue, DecimalKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public DecimalKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (DecimalKeyFrame)value!; }
    public int Add(DecimalKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((DecimalKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(DecimalKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((DecimalKeyFrame)value!);
    public int IndexOf(DecimalKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((DecimalKeyFrame)value!);
    public void Insert(int index, DecimalKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (DecimalKeyFrame)value!);
    public void Remove(DecimalKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((DecimalKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(DecimalKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Int16KeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Int16KeyFrame> _storage;
    public Int16KeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Int16KeyFrame? oldValue, Int16KeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Int16KeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Int16KeyFrame)value!; }
    public int Add(Int16KeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Int16KeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Int16KeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Int16KeyFrame)value!);
    public int IndexOf(Int16KeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Int16KeyFrame)value!);
    public void Insert(int index, Int16KeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Int16KeyFrame)value!);
    public void Remove(Int16KeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Int16KeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Int16KeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Int32KeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Int32KeyFrame> _storage;
    public Int32KeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Int32KeyFrame? oldValue, Int32KeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Int32KeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Int32KeyFrame)value!; }
    public int Add(Int32KeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Int32KeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Int32KeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Int32KeyFrame)value!);
    public int IndexOf(Int32KeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Int32KeyFrame)value!);
    public void Insert(int index, Int32KeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Int32KeyFrame)value!);
    public void Remove(Int32KeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Int32KeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Int32KeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Int64KeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Int64KeyFrame> _storage;
    public Int64KeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Int64KeyFrame? oldValue, Int64KeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Int64KeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Int64KeyFrame)value!; }
    public int Add(Int64KeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Int64KeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Int64KeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Int64KeyFrame)value!);
    public int IndexOf(Int64KeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Int64KeyFrame)value!);
    public void Insert(int index, Int64KeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Int64KeyFrame)value!);
    public void Remove(Int64KeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Int64KeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Int64KeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class SingleKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<SingleKeyFrame> _storage;
    public SingleKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(SingleKeyFrame? oldValue, SingleKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public SingleKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (SingleKeyFrame)value!; }
    public int Add(SingleKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((SingleKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(SingleKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((SingleKeyFrame)value!);
    public int IndexOf(SingleKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((SingleKeyFrame)value!);
    public void Insert(int index, SingleKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (SingleKeyFrame)value!);
    public void Remove(SingleKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((SingleKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(SingleKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
