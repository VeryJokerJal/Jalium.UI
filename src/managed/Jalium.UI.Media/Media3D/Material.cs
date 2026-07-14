using System.Collections;
using System.Globalization;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Media3D;

/// <summary>Abstract base class for materials.</summary>
public abstract class Material : Animatable, IFormattable
{
    public new Material Clone() => (Material)base.Clone();

    public new Material CloneCurrentValue() => (Material)base.CloneCurrentValue();

    public override string ToString() => ToString(CultureInfo.CurrentCulture);

    public string ToString(IFormatProvider? provider) => GetType().FullName ?? GetType().Name;

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) =>
        ToString(formatProvider);
}

/// <summary>Applies a brush as a diffuse material to a 3-D model.</summary>
public sealed class DiffuseMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(DiffuseMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty AmbientColorProperty =
        DependencyProperty.Register(
            nameof(AmbientColor),
            typeof(Color),
            typeof(DiffuseMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(DiffuseMaterial),
            new PropertyMetadata(null));

    public DiffuseMaterial()
    {
    }

    public DiffuseMaterial(Brush brush)
    {
        Brush = brush;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Color AmbientColor
    {
        get => (Color)(GetValue(AmbientColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(AmbientColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public new DiffuseMaterial Clone() => (DiffuseMaterial)base.Clone();

    public new DiffuseMaterial CloneCurrentValue() => (DiffuseMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new DiffuseMaterial();
}

/// <summary>Applies a specular highlight to a 3-D model.</summary>
public sealed class SpecularMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(SpecularMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(SpecularMaterial),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SpecularPowerProperty =
        DependencyProperty.Register(
            nameof(SpecularPower),
            typeof(double),
            typeof(SpecularMaterial),
            new PropertyMetadata(40d));

    public SpecularMaterial()
    {
    }

    public SpecularMaterial(Brush brush, double specularPower)
    {
        Brush = brush;
        SpecularPower = specularPower;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public double SpecularPower
    {
        get => (double)(GetValue(SpecularPowerProperty) ?? 40d);
        set => SetValue(SpecularPowerProperty, value);
    }

    public new SpecularMaterial Clone() => (SpecularMaterial)base.Clone();

    public new SpecularMaterial CloneCurrentValue() => (SpecularMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new SpecularMaterial();
}

/// <summary>Applies a brush as if it were emitting light.</summary>
public sealed class EmissiveMaterial : Material
{
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(EmissiveMaterial),
            new PropertyMetadata(Color.FromRgb(255, 255, 255)));

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(
            nameof(Brush),
            typeof(Brush),
            typeof(EmissiveMaterial),
            new PropertyMetadata(null));

    public EmissiveMaterial()
    {
    }

    public EmissiveMaterial(Brush brush)
    {
        Brush = brush;
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Color.FromRgb(255, 255, 255));
        set => SetValue(ColorProperty, value);
    }

    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public new EmissiveMaterial Clone() => (EmissiveMaterial)base.Clone();

    public new EmissiveMaterial CloneCurrentValue() => (EmissiveMaterial)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new EmissiveMaterial();
}

/// <summary>Represents an ordered collection of materials.</summary>
public sealed class MaterialCollection : Animatable, IList<Material>, IList
{
    private readonly AnimatableListStorage<Material> _items;

    public MaterialCollection() => _items = CreateStorage();

    public MaterialCollection(int capacity) => _items = CreateStorage(capacity);

    public MaterialCollection(IEnumerable<Material> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<Material> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public Material this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<Material>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<Material>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(Material value) => _items.Add(value);
    int IList.Add(object? value) { Add(AnimatableListStorage<Material>.Cast(value)); return Count - 1; }
    public void Clear() => _items.Clear();
    public bool Contains(Material value) => _items.Contains(value);
    bool IList.Contains(object? value) => value is Material material && Contains(material);
    public void CopyTo(Material[] array, int index) => _items.CopyTo(array, index);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    public Enumerator GetEnumerator() => new(_items.GetEnumerator());
    IEnumerator<Material> IEnumerable<Material>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Material value) => _items.IndexOf(value);
    int IList.IndexOf(object? value) => value is Material material ? IndexOf(material) : -1;
    public void Insert(int index, Material value) => _items.Insert(index, value);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<Material>.Cast(value));
    public bool Remove(Material value) => _items.Remove(value);
    void IList.Remove(object? value) { if (value is Material material) Remove(material); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new MaterialCollection Clone() => (MaterialCollection)base.Clone();

    public new MaterialCollection CloneCurrentValue() => (MaterialCollection)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MaterialCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((MaterialCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((MaterialCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((MaterialCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((MaterialCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    private AnimatableListStorage<Material> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);

    public struct Enumerator : IEnumerator<Material>
    {
        private List<Material>.Enumerator _inner;
        internal Enumerator(List<Material>.Enumerator inner) => _inner = inner;
        public Material Current => _inner.Current;
        object IEnumerator.Current => Current;
        public bool MoveNext() => _inner.MoveNext();
        public void Reset() => ((IEnumerator)_inner).Reset();
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>Represents materials that are applied together.</summary>
public sealed class MaterialGroup : Material
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(MaterialCollection),
            typeof(MaterialGroup),
            new PropertyMetadata(null));

    public MaterialGroup()
    {
        Children = new MaterialCollection();
    }

    public MaterialCollection Children
    {
        get => (MaterialCollection?)GetValue(ChildrenProperty)!;
        set => SetValue(ChildrenProperty, value);
    }

    public new MaterialGroup Clone() => (MaterialGroup)base.Clone();

    public new MaterialGroup CloneCurrentValue() => (MaterialGroup)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new MaterialGroup();
}
