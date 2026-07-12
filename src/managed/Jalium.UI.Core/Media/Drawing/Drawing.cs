namespace Jalium.UI.Media;

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Jalium.UI.Media.Animation;

/// <summary>
/// Abstract base class for objects that describe 2-D drawing operations.
/// </summary>
public abstract class Drawing : Animatable
{
    /// <summary>
    /// Gets the axis-aligned bounding box of this Drawing's contents.
    /// </summary>
    public abstract Rect Bounds { get; }

    /// <summary>
    /// Renders this Drawing to the specified DrawingContext.
    /// </summary>
    /// <param name="context">The DrawingContext to render to.</param>
    public abstract void RenderTo(DrawingContext context);

    public new Drawing Clone() => (Drawing)base.Clone();
    public new Drawing CloneCurrentValue() => (Drawing)base.CloneCurrentValue();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072",
        Justification = "Custom Drawing implementations preserve a parameterless constructor as part of the Freezable clone contract.")]
    protected override Freezable CreateInstanceCore() =>
        (Freezable)(Activator.CreateInstance(GetType(), nonPublic: true)
            ?? throw new InvalidOperationException($"Drawing type '{GetType().FullName}' must have a parameterless constructor."));
}

/// <summary>
/// Represents a collection of Drawing objects.
/// </summary>
public sealed class DrawingCollection : Animatable, IList<Drawing>, IList
{
    private readonly AnimatableListStorage<Drawing> _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCollection"/> class.
    /// </summary>
    public DrawingCollection()
    {
        _items = CreateStorage();
    }

    public DrawingCollection(int capacity)
    {
        _items = CreateStorage(capacity);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCollection"/> class
    /// with the specified drawings.
    /// </summary>
    public DrawingCollection(IEnumerable<Drawing> drawings)
    {
        ArgumentNullException.ThrowIfNull(drawings);
        _items = CreateStorage(drawings is ICollection<Drawing> source ? source.Count : 0);
        _items.AddRange(drawings);
    }

    public Drawing this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Drawing>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Drawing>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Drawing item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<Drawing>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<Drawing> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(Drawing item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is Drawing item && Contains(item);
    public void CopyTo(Drawing[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    IEnumerator<Drawing> IEnumerable<Drawing>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(Drawing item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is Drawing item ? IndexOf(item) : -1;
    public void Insert(int index, Drawing item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Drawing>.Cast(value));
    public bool Remove(Drawing item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is Drawing item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new DrawingCollection Clone() => (DrawingCollection)base.Clone();
    public new DrawingCollection CloneCurrentValue() => (DrawingCollection)base.CloneCurrentValue();
    public Enumerator GetEnumerator() => new(this);

    protected override Freezable CreateInstanceCore() => new DrawingCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((DrawingCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((DrawingCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((DrawingCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((DrawingCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    public struct Enumerator : IEnumerator<Drawing>
    {
        private IEnumerator<Drawing>? _inner;

        internal Enumerator(DrawingCollection collection)
        {
            _inner = ((IEnumerable<Drawing>)collection).GetEnumerator();
        }

        public Drawing Current =>
            _inner?.Current ?? throw new InvalidOperationException("The enumerator is not positioned on an item.");
        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner?.MoveNext() ?? false;
        public void Reset() => _inner?.Reset();
        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }
    }

    private AnimatableListStorage<Drawing> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}
