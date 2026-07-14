using System.Collections;
using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>Provides functionality shared by 3-D models.</summary>
public abstract class Model3D : Animatable, IFormattable
{
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(
            nameof(Transform),
            typeof(Transform3D),
            typeof(Model3D),
            new PropertyMetadata(Transform3D.Identity));

    /// <summary>Gets the model's axis-aligned bounds in its parent coordinate space.</summary>
    public abstract Rect3D Bounds { get; }

    /// <summary>Gets or sets the transform applied to the model.</summary>
    public Transform3D? Transform
    {
        get => (Transform3D?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    public new Model3D Clone() => (Model3D)base.Clone();

    public new Model3D CloneCurrentValue() => (Model3D)base.CloneCurrentValue();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider) => GetType().FullName ?? GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? provider) =>
        ToString(provider);

    internal Rect3D TransformBounds(Rect3D bounds) =>
        SceneMath.TransformBounds(bounds, Transform?.Value ?? Matrix3D.Identity);
}

/// <summary>Represents a 3-D model constructed from geometry and material.</summary>
public sealed class GeometryModel3D : Model3D
{
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(
            nameof(Geometry),
            typeof(Geometry3D),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty MaterialProperty =
        DependencyProperty.Register(
            nameof(Material),
            typeof(Material),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public static readonly DependencyProperty BackMaterialProperty =
        DependencyProperty.Register(
            nameof(BackMaterial),
            typeof(Material),
            typeof(GeometryModel3D),
            new PropertyMetadata(null));

    public GeometryModel3D()
    {
    }

    public GeometryModel3D(Geometry3D geometry, Material material)
    {
        Geometry = geometry;
        Material = material;
    }

    public Geometry3D? Geometry
    {
        get => (Geometry3D?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public Material? Material
    {
        get => (Material?)GetValue(MaterialProperty);
        set => SetValue(MaterialProperty, value);
    }

    public Material? BackMaterial
    {
        get => (Material?)GetValue(BackMaterialProperty);
        set => SetValue(BackMaterialProperty, value);
    }

    public override Rect3D Bounds =>
        TransformBounds(Geometry?.Bounds ?? Rect3D.Empty);

    public new GeometryModel3D Clone() => (GeometryModel3D)base.Clone();

    public new GeometryModel3D CloneCurrentValue() => (GeometryModel3D)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new GeometryModel3D();
}

/// <summary>Represents an ordered collection of 3-D models.</summary>
public sealed class Model3DCollection : Animatable, IList<Model3D>, IList
{
    private readonly AnimatableListStorage<Model3D> _items;

    public Model3DCollection() => _items = CreateStorage();

    public Model3DCollection(int capacity) => _items = CreateStorage(capacity);

    public Model3DCollection(IEnumerable<Model3D> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<Model3D> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public Model3D this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Model3D>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Model3D>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Model3D value) => _items.Add(value);
    int IList.Add(object? value) { Add(AnimatableListStorage<Model3D>.Cast(value)); return Count - 1; }
    public void Clear() => _items.Clear();
    public bool Contains(Model3D value) => _items.Contains(value);
    bool IList.Contains(object? value) => value is Model3D model && Contains(model);
    public void CopyTo(Model3D[] array, int index) => _items.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public Enumerator GetEnumerator() => new(_items.GetEnumerator());
    IEnumerator<Model3D> IEnumerable<Model3D>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Model3D value) => _items.IndexOf(value);
    int IList.IndexOf(object? value) => value is Model3D model ? IndexOf(model) : -1;
    public void Insert(int index, Model3D value) => _items.Insert(index, value);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Model3D>.Cast(value));
    public bool Remove(Model3D value) => _items.Remove(value);
    void IList.Remove(object? value) { if (value is Model3D model) Remove(model); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new Model3DCollection Clone() => (Model3DCollection)base.Clone();

    public new Model3DCollection CloneCurrentValue() => (Model3DCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Model3DCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((Model3DCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((Model3DCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((Model3DCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((Model3DCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    private AnimatableListStorage<Model3D> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);

    public struct Enumerator : IEnumerator<Model3D>
    {
        private List<Model3D>.Enumerator _inner;
        internal Enumerator(List<Model3D>.Enumerator inner) => _inner = inner;
        public Model3D Current => _inner.Current;
        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => ((IEnumerator)_inner).Reset();
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>Uses a collection of models as one model.</summary>
public sealed class Model3DGroup : Model3D
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(Model3DCollection),
            typeof(Model3DGroup),
            new PropertyMetadata(null));

    public Model3DGroup()
    {
        Children = new Model3DCollection();
    }

    public Model3DCollection Children
    {
        get => (Model3DCollection?)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public override Rect3D Bounds
    {
        get
        {
            Model3DCollection? children = (Model3DCollection?)GetValue(ChildrenProperty);
            if (children is null || children.Count == 0)
            {
                return Rect3D.Empty;
            }

            Rect3D result = Rect3D.Empty;
            foreach (Model3D child in children)
            {
                result.Union(child.Bounds);
            }

            return TransformBounds(result);
        }
    }

    public new Model3DGroup Clone() => (Model3DGroup)base.Clone();

    public new Model3DGroup CloneCurrentValue() => (Model3DGroup)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new Model3DGroup();
}
