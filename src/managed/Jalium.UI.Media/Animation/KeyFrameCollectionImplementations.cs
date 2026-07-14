using System.Collections;

namespace Jalium.UI.Media.Animation;

public partial class DoubleKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<DoubleKeyFrame> _storage;
    public DoubleKeyFrameCollection() => _storage = CreateStorage();
    private KeyFrameCollectionStorage<DoubleKeyFrame> CreateStorage() => new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(DoubleKeyFrame? oldValue, DoubleKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public DoubleKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (DoubleKeyFrame)value!; }
    public int Add(DoubleKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((DoubleKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(DoubleKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((DoubleKeyFrame)value!);
    public int IndexOf(DoubleKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((DoubleKeyFrame)value!);
    public void Insert(int index, DoubleKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (DoubleKeyFrame)value!);
    public void Remove(DoubleKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((DoubleKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(DoubleKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class ColorKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<ColorKeyFrame> _storage;
    public ColorKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(ColorKeyFrame? oldValue, ColorKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public ColorKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (ColorKeyFrame)value!; }
    public int Add(ColorKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((ColorKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(ColorKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((ColorKeyFrame)value!);
    public int IndexOf(ColorKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((ColorKeyFrame)value!);
    public void Insert(int index, ColorKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (ColorKeyFrame)value!);
    public void Remove(ColorKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((ColorKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(ColorKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class PointKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<PointKeyFrame> _storage;
    public PointKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(PointKeyFrame? oldValue, PointKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public PointKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (PointKeyFrame)value!; }
    public int Add(PointKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((PointKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(PointKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((PointKeyFrame)value!);
    public int IndexOf(PointKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((PointKeyFrame)value!);
    public void Insert(int index, PointKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (PointKeyFrame)value!);
    public void Remove(PointKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((PointKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(PointKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class ThicknessKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<ThicknessKeyFrame> _storage;
    public ThicknessKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(ThicknessKeyFrame? oldValue, ThicknessKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public ThicknessKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (ThicknessKeyFrame)value!; }
    public int Add(ThicknessKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((ThicknessKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(ThicknessKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((ThicknessKeyFrame)value!);
    public int IndexOf(ThicknessKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((ThicknessKeyFrame)value!);
    public void Insert(int index, ThicknessKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (ThicknessKeyFrame)value!);
    public void Remove(ThicknessKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((ThicknessKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(ThicknessKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}

public partial class ObjectKeyFrameCollection
{
    private readonly KeyFrameCollectionStorage<ObjectKeyFrame> _storage;
    public ObjectKeyFrameCollection() => _storage = new(ReadPreamble, WritePreamble, WritePostscript, OnChildChanged, () => IsFrozen, () => Dispatcher is not null);
    private void OnChildChanged(ObjectKeyFrame? oldValue, ObjectKeyFrame? newValue) => OnFreezablePropertyChanged(oldValue, newValue);
    public int Count => _storage.Count;
    public bool IsFixedSize => _storage.IsFixedSize;
    public bool IsReadOnly => _storage.IsReadOnly;
    public bool IsSynchronized => _storage.IsSynchronized;
    public object SyncRoot => _storage.SyncRoot;
    public ObjectKeyFrame this[int index] { get => _storage.GetItem(index); set => _storage.SetItem(index, value); }
    object? IList.this[int index] { get => this[index]; set => this[index] = (ObjectKeyFrame)value!; }
    public int Add(ObjectKeyFrame keyFrame) => _storage.Add(keyFrame);
    int IList.Add(object? value) => Add((ObjectKeyFrame)value!);
    public void Clear() => _storage.Clear();
    public bool Contains(ObjectKeyFrame keyFrame) => _storage.Contains(keyFrame);
    bool IList.Contains(object? value) => Contains((ObjectKeyFrame)value!);
    public int IndexOf(ObjectKeyFrame keyFrame) => _storage.IndexOf(keyFrame);
    int IList.IndexOf(object? value) => IndexOf((ObjectKeyFrame)value!);
    public void Insert(int index, ObjectKeyFrame keyFrame) => _storage.Insert(index, keyFrame);
    void IList.Insert(int index, object? value) => Insert(index, (ObjectKeyFrame)value!);
    public void Remove(ObjectKeyFrame keyFrame) => _storage.Remove(keyFrame);
    void IList.Remove(object? value) => Remove((ObjectKeyFrame)value!);
    public void RemoveAt(int index) => _storage.RemoveAt(index);
    public void CopyTo(ObjectKeyFrame[] array, int index) => _storage.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _storage.CopyTo(array, index);
    public IEnumerator GetEnumerator() => _storage.GetEnumerator();
}
