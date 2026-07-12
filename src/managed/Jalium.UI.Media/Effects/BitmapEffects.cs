using System.Collections;
using System.Runtime.InteropServices;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Media.Effects;

#pragma warning disable CS0618 // These types intentionally implement WPF's retired BitmapEffect API.

[Obsolete("Use BlurEffect instead.")]
public sealed class BlurBitmapEffect : BitmapEffect
{
    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.Register(
            nameof(Radius),
            typeof(double),
            typeof(BlurBitmapEffect),
            new PropertyMetadata(5.0, BitmapEffectPropertyChanged));

    public static readonly DependencyProperty KernelTypeProperty =
        DependencyProperty.Register(
            nameof(KernelType),
            typeof(KernelType),
            typeof(BlurBitmapEffect),
            new PropertyMetadata(KernelType.Gaussian, BitmapEffectPropertyChanged),
            value => value is KernelType type && (type == KernelType.Gaussian || type == KernelType.Box));

    public double Radius
    {
        get => (double)(GetValue(RadiusProperty) ?? 5.0);
        set => SetValue(RadiusProperty, value);
    }

    public KernelType KernelType
    {
        get => (KernelType)(GetValue(KernelTypeProperty) ?? KernelType.Gaussian);
        set => SetValue(KernelTypeProperty, value);
    }

    public new BlurBitmapEffect Clone() => (BlurBitmapEffect)base.Clone();

    public new BlurBitmapEffect CloneCurrentValue() => (BlurBitmapEffect)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new BlurBitmapEffect();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(Radius), Radius);
        SetValue(unmanagedEffect, nameof(KernelType), KernelType);
    }
#pragma warning restore CS0628
}

[Obsolete("Use DropShadowEffect instead.")]
public sealed class DropShadowBitmapEffect : BitmapEffect
{
    public static readonly DependencyProperty ShadowDepthProperty = RegisterDouble(nameof(ShadowDepth), 5.0);
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(Color),
            typeof(DropShadowBitmapEffect),
            new PropertyMetadata(Colors.Black, BitmapEffectPropertyChanged));
    public static readonly DependencyProperty DirectionProperty = RegisterDouble(nameof(Direction), 315.0);
    public static readonly DependencyProperty NoiseProperty = RegisterDouble(nameof(Noise), 0.0);
    public static readonly DependencyProperty OpacityProperty = RegisterDouble(nameof(Opacity), 1.0);
    public static readonly DependencyProperty SoftnessProperty = RegisterDouble(nameof(Softness), 0.5);

    public double ShadowDepth
    {
        get => (double)(GetValue(ShadowDepthProperty) ?? 5.0);
        set => SetValue(ShadowDepthProperty, value);
    }

    public Color Color
    {
        get => (Color)(GetValue(ColorProperty) ?? Colors.Black);
        set => SetValue(ColorProperty, value);
    }

    public double Direction
    {
        get => (double)(GetValue(DirectionProperty) ?? 315.0);
        set => SetValue(DirectionProperty, value);
    }

    public double Noise
    {
        get => (double)(GetValue(NoiseProperty) ?? 0.0);
        set => SetValue(NoiseProperty, value);
    }

    public double Opacity
    {
        get => (double)(GetValue(OpacityProperty) ?? 1.0);
        set => SetValue(OpacityProperty, value);
    }

    public double Softness
    {
        get => (double)(GetValue(SoftnessProperty) ?? 0.5);
        set => SetValue(SoftnessProperty, value);
    }

    public new DropShadowBitmapEffect Clone() => (DropShadowBitmapEffect)base.Clone();

    public new DropShadowBitmapEffect CloneCurrentValue() => (DropShadowBitmapEffect)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new DropShadowBitmapEffect();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(ShadowDepth), ShadowDepth);
        SetValue(unmanagedEffect, nameof(Color), Color);
        SetValue(unmanagedEffect, nameof(Direction), Direction);
        SetValue(unmanagedEffect, nameof(Noise), Noise);
        SetValue(unmanagedEffect, nameof(Opacity), Opacity);
        SetValue(unmanagedEffect, nameof(Softness), Softness);
    }
#pragma warning restore CS0628

    private static DependencyProperty RegisterDouble(string name, double defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(double),
            typeof(DropShadowBitmapEffect),
            new PropertyMetadata(defaultValue, BitmapEffectPropertyChanged));
}

[Obsolete("BitmapEffect is deprecated.")]
public sealed class BevelBitmapEffect : BitmapEffect
{
    public static readonly DependencyProperty BevelWidthProperty = RegisterDouble(nameof(BevelWidth), 5.0);
    public static readonly DependencyProperty ReliefProperty = RegisterDouble(nameof(Relief), 0.3);
    public static readonly DependencyProperty LightAngleProperty = RegisterDouble(nameof(LightAngle), 135.0);
    public static readonly DependencyProperty SmoothnessProperty = RegisterDouble(nameof(Smoothness), 0.2);
    public static readonly DependencyProperty EdgeProfileProperty =
        DependencyProperty.Register(
            nameof(EdgeProfile),
            typeof(EdgeProfile),
            typeof(BevelBitmapEffect),
            new PropertyMetadata(EdgeProfile.Linear, BitmapEffectPropertyChanged),
            value => value is EdgeProfile profile && profile >= EdgeProfile.Linear && profile <= EdgeProfile.BulgedUp);

    public double BevelWidth
    {
        get => (double)(GetValue(BevelWidthProperty) ?? 5.0);
        set => SetValue(BevelWidthProperty, value);
    }

    public double Relief
    {
        get => (double)(GetValue(ReliefProperty) ?? 0.3);
        set => SetValue(ReliefProperty, value);
    }

    public double LightAngle
    {
        get => (double)(GetValue(LightAngleProperty) ?? 135.0);
        set => SetValue(LightAngleProperty, value);
    }

    public double Smoothness
    {
        get => (double)(GetValue(SmoothnessProperty) ?? 0.2);
        set => SetValue(SmoothnessProperty, value);
    }

    public EdgeProfile EdgeProfile
    {
        get => (EdgeProfile)(GetValue(EdgeProfileProperty) ?? EdgeProfile.Linear);
        set => SetValue(EdgeProfileProperty, value);
    }

    public new BevelBitmapEffect Clone() => (BevelBitmapEffect)base.Clone();

    public new BevelBitmapEffect CloneCurrentValue() => (BevelBitmapEffect)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new BevelBitmapEffect();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(BevelWidth), BevelWidth);
        SetValue(unmanagedEffect, nameof(Relief), Relief);
        SetValue(unmanagedEffect, nameof(LightAngle), LightAngle);
        SetValue(unmanagedEffect, nameof(Smoothness), Smoothness);
        SetValue(unmanagedEffect, nameof(EdgeProfile), EdgeProfile);
    }
#pragma warning restore CS0628

    private static DependencyProperty RegisterDouble(string name, double defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(double),
            typeof(BevelBitmapEffect),
            new PropertyMetadata(defaultValue, BitmapEffectPropertyChanged));
}

[Obsolete("BitmapEffect is deprecated.")]
public sealed class EmbossBitmapEffect : BitmapEffect
{
    public static readonly DependencyProperty LightAngleProperty =
        DependencyProperty.Register(
            nameof(LightAngle),
            typeof(double),
            typeof(EmbossBitmapEffect),
            new PropertyMetadata(45.0, BitmapEffectPropertyChanged));

    public static readonly DependencyProperty ReliefProperty =
        DependencyProperty.Register(
            nameof(Relief),
            typeof(double),
            typeof(EmbossBitmapEffect),
            new PropertyMetadata(0.44, BitmapEffectPropertyChanged));

    public double LightAngle
    {
        get => (double)(GetValue(LightAngleProperty) ?? 45.0);
        set => SetValue(LightAngleProperty, value);
    }

    public double Relief
    {
        get => (double)(GetValue(ReliefProperty) ?? 0.44);
        set => SetValue(ReliefProperty, value);
    }

    public new EmbossBitmapEffect Clone() => (EmbossBitmapEffect)base.Clone();

    public new EmbossBitmapEffect CloneCurrentValue() => (EmbossBitmapEffect)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new EmbossBitmapEffect();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(LightAngle), LightAngle);
        SetValue(unmanagedEffect, nameof(Relief), Relief);
    }
#pragma warning restore CS0628
}

[Obsolete("BitmapEffect is deprecated.")]
public sealed class OuterGlowBitmapEffect : BitmapEffect
{
    public static readonly DependencyProperty GlowColorProperty =
        DependencyProperty.Register(
            nameof(GlowColor),
            typeof(Color),
            typeof(OuterGlowBitmapEffect),
            new PropertyMetadata(Colors.Gold, BitmapEffectPropertyChanged));
    public static readonly DependencyProperty GlowSizeProperty = RegisterDouble(nameof(GlowSize), 5.0);
    public static readonly DependencyProperty NoiseProperty = RegisterDouble(nameof(Noise), 0.0);
    public static readonly DependencyProperty OpacityProperty = RegisterDouble(nameof(Opacity), 1.0);

    public Color GlowColor
    {
        get => (Color)(GetValue(GlowColorProperty) ?? Colors.Gold);
        set => SetValue(GlowColorProperty, value);
    }

    public double GlowSize
    {
        get => (double)(GetValue(GlowSizeProperty) ?? 5.0);
        set => SetValue(GlowSizeProperty, value);
    }

    public double Noise
    {
        get => (double)(GetValue(NoiseProperty) ?? 0.0);
        set => SetValue(NoiseProperty, value);
    }

    public double Opacity
    {
        get => (double)(GetValue(OpacityProperty) ?? 1.0);
        set => SetValue(OpacityProperty, value);
    }

    public new OuterGlowBitmapEffect Clone() => (OuterGlowBitmapEffect)base.Clone();

    public new OuterGlowBitmapEffect CloneCurrentValue() => (OuterGlowBitmapEffect)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new OuterGlowBitmapEffect();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(GlowColor), GlowColor);
        SetValue(unmanagedEffect, nameof(GlowSize), GlowSize);
        SetValue(unmanagedEffect, nameof(Noise), Noise);
        SetValue(unmanagedEffect, nameof(Opacity), Opacity);
    }
#pragma warning restore CS0628

    private static DependencyProperty RegisterDouble(string name, double defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(double),
            typeof(OuterGlowBitmapEffect),
            new PropertyMetadata(defaultValue, BitmapEffectPropertyChanged));
}

[Obsolete("Use Effect-derived classes instead.")]
public sealed class BitmapEffectGroup : BitmapEffect
{
    public static readonly DependencyProperty ChildrenProperty =
        DependencyProperty.Register(
            nameof(Children),
            typeof(BitmapEffectCollection),
            typeof(BitmapEffectGroup),
            new PropertyMetadata(null, OnChildrenChanged));

    public BitmapEffectGroup()
    {
        Children = new BitmapEffectCollection();
    }

    public BitmapEffectCollection? Children
    {
        get => (BitmapEffectCollection?)GetValue(ChildrenProperty);
        set => SetValue(ChildrenProperty, value);
    }

    public new BitmapEffectGroup Clone() => (BitmapEffectGroup)base.Clone();

    public new BitmapEffectGroup CloneCurrentValue() => (BitmapEffectGroup)base.CloneCurrentValue();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new BitmapEffectGroup();

    protected override SafeHandle CreateUnmanagedEffect() => CreateBitmapEffectOuter();

    protected override void UpdateUnmanagedPropertyState(SafeHandle unmanagedEffect)
    {
        SetValue(unmanagedEffect, nameof(Children), Children?.Count ?? 0);
    }
#pragma warning restore CS0628

    private static void OnChildrenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapEffectGroup group)
        {
            group.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject);
            BitmapEffectPropertyChanged(d, e);
        }
    }
}

/// <summary>A mutable, Freezable collection of legacy bitmap effects.</summary>
[Obsolete("BitmapEffect is deprecated.")]
public sealed class BitmapEffectCollection : Animatable, IList<BitmapEffect>, IList
{
    private List<BitmapEffect> _items;
    private int _version;

    public BitmapEffectCollection()
    {
        _items = new List<BitmapEffect>();
    }

    public BitmapEffectCollection(int capacity)
    {
        _items = new List<BitmapEffect>(capacity);
    }

    public BitmapEffectCollection(IEnumerable<BitmapEffect> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = new List<BitmapEffect>();
        foreach (BitmapEffect effect in collection)
        {
            Add(effect);
        }
    }

    public new BitmapEffectCollection Clone() => (BitmapEffectCollection)base.Clone();

    public new BitmapEffectCollection CloneCurrentValue() => (BitmapEffectCollection)base.CloneCurrentValue();

    public BitmapEffect this[int index]
    {
        get
        {
            ReadPreamble();
            return _items[index];
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            WritePreamble();
            BitmapEffect oldValue = _items[index];
            OnFreezablePropertyChanged(oldValue, value);
            _items[index] = value;
            _version++;
            WritePostscript();
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public int Count
    {
        get
        {
            ReadPreamble();
            return _items.Count;
        }
    }

    bool ICollection<BitmapEffect>.IsReadOnly => IsFrozen;
    bool IList.IsReadOnly => IsFrozen;
    bool IList.IsFixedSize => IsFrozen;
    bool ICollection.IsSynchronized => IsFrozen;
    object ICollection.SyncRoot => this;

    public void Add(BitmapEffect item)
    {
        ArgumentNullException.ThrowIfNull(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Add(item);
        _version++;
        WritePostscript();
    }

    int IList.Add(object? value)
    {
        Add(Cast(value));
        return Count - 1;
    }

    public void Clear()
    {
        WritePreamble();
        foreach (BitmapEffect effect in _items)
        {
            OnFreezablePropertyChanged(effect, null);
        }

        _items.Clear();
        _version++;
        WritePostscript();
    }

    public bool Contains(BitmapEffect item)
    {
        ReadPreamble();
        return _items.Contains(item);
    }

    bool IList.Contains(object? value) => value is BitmapEffect effect && Contains(effect);

    public int IndexOf(BitmapEffect item)
    {
        ReadPreamble();
        return _items.IndexOf(item);
    }

    int IList.IndexOf(object? value) => value is BitmapEffect effect ? IndexOf(effect) : -1;

    public void Insert(int index, BitmapEffect item)
    {
        ArgumentNullException.ThrowIfNull(item);
        WritePreamble();
        OnFreezablePropertyChanged(null, item);
        _items.Insert(index, item);
        _version++;
        WritePostscript();
    }

    void IList.Insert(int index, object? value) => Insert(index, Cast(value));

    public bool Remove(BitmapEffect item)
    {
        WritePreamble();
        int index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        BitmapEffect oldValue = _items[index];
        OnFreezablePropertyChanged(oldValue, null);
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
        return true;
    }

    void IList.Remove(object? value)
    {
        if (value is BitmapEffect effect)
        {
            Remove(effect);
        }
    }

    public void RemoveAt(int index)
    {
        WritePreamble();
        BitmapEffect oldValue = _items[index];
        OnFreezablePropertyChanged(oldValue, null);
        _items.RemoveAt(index);
        _version++;
        WritePostscript();
    }

    public void CopyTo(BitmapEffect[] array, int arrayIndex)
    {
        ReadPreamble();
        _items.CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ReadPreamble();
        ((ICollection)_items).CopyTo(array, index);
    }

    public Enumerator GetEnumerator()
    {
        ReadPreamble();
        return new Enumerator(this);
    }

    IEnumerator<BitmapEffect> IEnumerable<BitmapEffect>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

#pragma warning disable CS0628
    protected override Freezable CreateInstanceCore() => new BitmapEffectCollection();

    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyFrom((BitmapEffectCollection)sourceFreezable, CloneKind.Clone);
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyFrom((BitmapEffectCollection)sourceFreezable, CloneKind.CloneCurrentValue);
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyFrom((BitmapEffectCollection)sourceFreezable, CloneKind.GetAsFrozen);
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyFrom((BitmapEffectCollection)sourceFreezable, CloneKind.GetCurrentValueAsFrozen);
    }

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
        {
            return false;
        }

        foreach (BitmapEffect effect in _items)
        {
            if (isChecking)
            {
                if (!effect.CanFreeze)
                {
                    return false;
                }
            }
            else
            {
                effect.Freeze();
            }
        }

        return true;
    }
#pragma warning restore CS0628

    private void CopyFrom(BitmapEffectCollection source, CloneKind kind)
    {
        _items = new List<BitmapEffect>(source._items.Count);
        foreach (BitmapEffect effect in source._items)
        {
            BitmapEffect copy = kind switch
            {
                CloneKind.Clone => effect.Clone(),
                CloneKind.CloneCurrentValue => effect.CloneCurrentValue(),
                CloneKind.GetAsFrozen => (BitmapEffect)effect.GetAsFrozen(),
                _ => (BitmapEffect)effect.GetCurrentValueAsFrozen(),
            };
            OnFreezablePropertyChanged(null, copy);
            _items.Add(copy);
        }
    }

    private static BitmapEffect Cast(object? value) => value as BitmapEffect ??
        throw new ArgumentException($"Value must be a {nameof(BitmapEffect)}.", nameof(value));

    private enum CloneKind
    {
        Clone,
        CloneCurrentValue,
        GetAsFrozen,
        GetCurrentValueAsFrozen,
    }

    public struct Enumerator : IEnumerator<BitmapEffect>
    {
        private readonly BitmapEffectCollection _collection;
        private readonly int _version;
        private int _index;

        internal Enumerator(BitmapEffectCollection collection)
        {
            _collection = collection;
            _version = collection._version;
            _index = -1;
        }

        public BitmapEffect Current
        {
            get
            {
                VerifyState();
                if (_index < 0 || _index >= _collection._items.Count)
                {
                    throw new InvalidOperationException("The enumerator is not positioned on an item.");
                }

                return _collection._items[_index];
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            VerifyState();
            int next = _index + 1;
            if (next >= _collection._items.Count)
            {
                _index = _collection._items.Count;
                return false;
            }

            _index = next;
            return true;
        }

        public void Reset()
        {
            VerifyState();
            _index = -1;
        }

        public void Dispose()
        {
        }

        private readonly void VerifyState()
        {
            if (_version != _collection._version)
            {
                throw new InvalidOperationException("The collection was modified during enumeration.");
            }
        }
    }
}

public enum EdgeProfile
{
    Linear = 0,
    CurvedIn = 1,
    CurvedOut = 2,
    BulgedUp = 3,
}

/// <summary>Compatibility brush representing implicit shader input.</summary>
public sealed class ImplicitInputBrush : Brush
{
    protected override Freezable CreateInstanceCore() => new ImplicitInputBrush();
}

#pragma warning restore CS0618
