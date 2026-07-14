using System.Collections;

namespace Jalium.UI.Media.Animation;

public partial class StringKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<StringKeyFrame> _storage;
    public StringKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(StringKeyFrame? oldValue, StringKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public StringKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (StringKeyFrame)value!; }
    public int Add(StringKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((StringKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(StringKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((StringKeyFrame)value!);
    public int IndexOf(StringKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((StringKeyFrame)value!);
    public void Insert(int index, StringKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (StringKeyFrame)value!);
    public void Remove(StringKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((StringKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(StringKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Point3DKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Point3DKeyFrame> _storage;
    public Point3DKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Point3DKeyFrame? oldValue, Point3DKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Point3DKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Point3DKeyFrame)value!; }
    public int Add(Point3DKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Point3DKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Point3DKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Point3DKeyFrame)value!);
    public int IndexOf(Point3DKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Point3DKeyFrame)value!);
    public void Insert(int index, Point3DKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Point3DKeyFrame)value!);
    public void Remove(Point3DKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Point3DKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Point3DKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Vector3DKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Vector3DKeyFrame> _storage;
    public Vector3DKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Vector3DKeyFrame? oldValue, Vector3DKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Vector3DKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Vector3DKeyFrame)value!; }
    public int Add(Vector3DKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Vector3DKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Vector3DKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Vector3DKeyFrame)value!);
    public int IndexOf(Vector3DKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Vector3DKeyFrame)value!);
    public void Insert(int index, Vector3DKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Vector3DKeyFrame)value!);
    public void Remove(Vector3DKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Vector3DKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Vector3DKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class QuaternionKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<QuaternionKeyFrame> _storage;
    public QuaternionKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(QuaternionKeyFrame? oldValue, QuaternionKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public QuaternionKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (QuaternionKeyFrame)value!; }
    public int Add(QuaternionKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((QuaternionKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(QuaternionKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((QuaternionKeyFrame)value!);
    public int IndexOf(QuaternionKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((QuaternionKeyFrame)value!);
    public void Insert(int index, QuaternionKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (QuaternionKeyFrame)value!);
    public void Remove(QuaternionKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((QuaternionKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(QuaternionKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class Rotation3DKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<Rotation3DKeyFrame> _storage;
    public Rotation3DKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(Rotation3DKeyFrame? oldValue, Rotation3DKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public Rotation3DKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (Rotation3DKeyFrame)value!; }
    public int Add(Rotation3DKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((Rotation3DKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(Rotation3DKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((Rotation3DKeyFrame)value!);
    public int IndexOf(Rotation3DKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((Rotation3DKeyFrame)value!);
    public void Insert(int index, Rotation3DKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (Rotation3DKeyFrame)value!);
    public void Remove(Rotation3DKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((Rotation3DKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(Rotation3DKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
