using System.Collections;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Animation;

public abstract class ByteKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<byte, ByteKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<ByteKeyFrame>();
    protected ByteKeyFrame() { }
    protected ByteKeyFrame(byte value) => Value = value;
    protected ByteKeyFrame(byte value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public byte Value { get => (byte)(GetValue(ValueProperty) ?? default(byte)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (byte)value; }
    public byte InterpolateValue(byte baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract byte InterpolateValueCore(byte baseValue, double keyFrameProgress);
}

public partial class ByteKeyFrameCollection : Freezable, IList
{
    private static readonly ByteKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<ByteKeyFrameCollection>();
    public static ByteKeyFrameCollection Empty => s_empty;
    public new ByteKeyFrameCollection Clone() => (ByteKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new ByteKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((ByteKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((ByteKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((ByteKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((ByteKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteByteKeyFrame : ByteKeyFrame
{
    public DiscreteByteKeyFrame() { }
    public DiscreteByteKeyFrame(byte value) : base(value) { }
    public DiscreteByteKeyFrame(byte value, KeyTime keyTime) : base(value, keyTime) { }
    protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteByteKeyFrame();
}

public class LinearByteKeyFrame : ByteKeyFrame
{
    public LinearByteKeyFrame() { }
    public LinearByteKeyFrame(byte value) : base(value) { }
    public LinearByteKeyFrame(byte value, KeyTime keyTime) : base(value, keyTime) { }
    protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearByteKeyFrame();
}

public class SplineByteKeyFrame : ByteKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineByteKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineByteKeyFrame() { }
    public SplineByteKeyFrame(byte value) : base(value) { }
    public SplineByteKeyFrame(byte value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineByteKeyFrame(byte value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineByteKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingByteKeyFrame : ByteKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingByteKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingByteKeyFrame() { }
    public EasingByteKeyFrame(byte value) : base(value) { }
    public EasingByteKeyFrame(byte value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingByteKeyFrame(byte value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override byte InterpolateValueCore(byte baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingByteKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class ByteAnimationUsingKeyFrames : ByteAnimationBase, IKeyFrameAnimation, IAddChild
{
    private ByteKeyFrameCollection _keyFrames = new();
    public ByteAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public ByteKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (ByteKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<byte>.AddChildTo<ByteKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<byte>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new ByteAnimationUsingKeyFrames Clone() => (ByteAnimationUsingKeyFrames)base.Clone();
    public new ByteAnimationUsingKeyFrames CloneCurrentValue() => (ByteAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ByteAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override byte GetCurrentValueCore(byte origin, byte destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<byte>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<byte>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((ByteAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((ByteAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((ByteAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((ByteAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(ByteAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<byte>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class DecimalKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<decimal, DecimalKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<DecimalKeyFrame>();
    protected DecimalKeyFrame() { }
    protected DecimalKeyFrame(decimal value) => Value = value;
    protected DecimalKeyFrame(decimal value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public decimal Value { get => (decimal)(GetValue(ValueProperty) ?? default(decimal)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (decimal)value; }
    public decimal InterpolateValue(decimal baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress);
}

public partial class DecimalKeyFrameCollection : Freezable, IList
{
    private static readonly DecimalKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<DecimalKeyFrameCollection>();
    public static DecimalKeyFrameCollection Empty => s_empty;
    public new DecimalKeyFrameCollection Clone() => (DecimalKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new DecimalKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((DecimalKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((DecimalKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((DecimalKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((DecimalKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteDecimalKeyFrame : DecimalKeyFrame
{
    public DiscreteDecimalKeyFrame() { }
    public DiscreteDecimalKeyFrame(decimal value) : base(value) { }
    public DiscreteDecimalKeyFrame(decimal value, KeyTime keyTime) : base(value, keyTime) { }
    protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteDecimalKeyFrame();
}

public class LinearDecimalKeyFrame : DecimalKeyFrame
{
    public LinearDecimalKeyFrame() { }
    public LinearDecimalKeyFrame(decimal value) : base(value) { }
    public LinearDecimalKeyFrame(decimal value, KeyTime keyTime) : base(value, keyTime) { }
    protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearDecimalKeyFrame();
}

public class SplineDecimalKeyFrame : DecimalKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineDecimalKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineDecimalKeyFrame() { }
    public SplineDecimalKeyFrame(decimal value) : base(value) { }
    public SplineDecimalKeyFrame(decimal value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineDecimalKeyFrame(decimal value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineDecimalKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingDecimalKeyFrame : DecimalKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingDecimalKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingDecimalKeyFrame() { }
    public EasingDecimalKeyFrame(decimal value) : base(value) { }
    public EasingDecimalKeyFrame(decimal value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingDecimalKeyFrame(decimal value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override decimal InterpolateValueCore(decimal baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingDecimalKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class DecimalAnimationUsingKeyFrames : DecimalAnimationBase, IKeyFrameAnimation, IAddChild
{
    private DecimalKeyFrameCollection _keyFrames = new();
    public DecimalAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public DecimalKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (DecimalKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<decimal>.AddChildTo<DecimalKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<decimal>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new DecimalAnimationUsingKeyFrames Clone() => (DecimalAnimationUsingKeyFrames)base.Clone();
    public new DecimalAnimationUsingKeyFrames CloneCurrentValue() => (DecimalAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new DecimalAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override decimal GetCurrentValueCore(decimal origin, decimal destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<decimal>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<decimal>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((DecimalAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((DecimalAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((DecimalAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((DecimalAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(DecimalAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<decimal>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class Int16KeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<short, Int16KeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Int16KeyFrame>();
    protected Int16KeyFrame() { }
    protected Int16KeyFrame(short value) => Value = value;
    protected Int16KeyFrame(short value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public short Value { get => (short)(GetValue(ValueProperty) ?? default(short)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (short)value; }
    public short InterpolateValue(short baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract short InterpolateValueCore(short baseValue, double keyFrameProgress);
}

public partial class Int16KeyFrameCollection : Freezable, IList
{
    private static readonly Int16KeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Int16KeyFrameCollection>();
    public static Int16KeyFrameCollection Empty => s_empty;
    public new Int16KeyFrameCollection Clone() => (Int16KeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Int16KeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((Int16KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((Int16KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((Int16KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((Int16KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteInt16KeyFrame : Int16KeyFrame
{
    public DiscreteInt16KeyFrame() { }
    public DiscreteInt16KeyFrame(short value) : base(value) { }
    public DiscreteInt16KeyFrame(short value, KeyTime keyTime) : base(value, keyTime) { }
    protected override short InterpolateValueCore(short baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteInt16KeyFrame();
}

public class LinearInt16KeyFrame : Int16KeyFrame
{
    public LinearInt16KeyFrame() { }
    public LinearInt16KeyFrame(short value) : base(value) { }
    public LinearInt16KeyFrame(short value, KeyTime keyTime) : base(value, keyTime) { }
    protected override short InterpolateValueCore(short baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearInt16KeyFrame();
}

public class SplineInt16KeyFrame : Int16KeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineInt16KeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineInt16KeyFrame() { }
    public SplineInt16KeyFrame(short value) : base(value) { }
    public SplineInt16KeyFrame(short value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineInt16KeyFrame(short value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override short InterpolateValueCore(short baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineInt16KeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingInt16KeyFrame : Int16KeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingInt16KeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingInt16KeyFrame() { }
    public EasingInt16KeyFrame(short value) : base(value) { }
    public EasingInt16KeyFrame(short value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingInt16KeyFrame(short value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override short InterpolateValueCore(short baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingInt16KeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class Int16AnimationUsingKeyFrames : Int16AnimationBase, IKeyFrameAnimation, IAddChild
{
    private Int16KeyFrameCollection _keyFrames = new();
    public Int16AnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public Int16KeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (Int16KeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<short>.AddChildTo<Int16KeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<short>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new Int16AnimationUsingKeyFrames Clone() => (Int16AnimationUsingKeyFrames)base.Clone();
    public new Int16AnimationUsingKeyFrames CloneCurrentValue() => (Int16AnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Int16AnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override short GetCurrentValueCore(short origin, short destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<short>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<short>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((Int16AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((Int16AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((Int16AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((Int16AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(Int16AnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<short>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class Int32KeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<int, Int32KeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Int32KeyFrame>();
    protected Int32KeyFrame() { }
    protected Int32KeyFrame(int value) => Value = value;
    protected Int32KeyFrame(int value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public int Value { get => (int)(GetValue(ValueProperty) ?? default(int)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (int)value; }
    public int InterpolateValue(int baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract int InterpolateValueCore(int baseValue, double keyFrameProgress);
}

public partial class Int32KeyFrameCollection : Freezable, IList
{
    private static readonly Int32KeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Int32KeyFrameCollection>();
    public static Int32KeyFrameCollection Empty => s_empty;
    public new Int32KeyFrameCollection Clone() => (Int32KeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Int32KeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((Int32KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((Int32KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((Int32KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((Int32KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteInt32KeyFrame : Int32KeyFrame
{
    public DiscreteInt32KeyFrame() { }
    public DiscreteInt32KeyFrame(int value) : base(value) { }
    public DiscreteInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime) { }
    protected override int InterpolateValueCore(int baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteInt32KeyFrame();
}

public class LinearInt32KeyFrame : Int32KeyFrame
{
    public LinearInt32KeyFrame() { }
    public LinearInt32KeyFrame(int value) : base(value) { }
    public LinearInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime) { }
    protected override int InterpolateValueCore(int baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearInt32KeyFrame();
}

public class SplineInt32KeyFrame : Int32KeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineInt32KeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineInt32KeyFrame() { }
    public SplineInt32KeyFrame(int value) : base(value) { }
    public SplineInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineInt32KeyFrame(int value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override int InterpolateValueCore(int baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineInt32KeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingInt32KeyFrame : Int32KeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingInt32KeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingInt32KeyFrame() { }
    public EasingInt32KeyFrame(int value) : base(value) { }
    public EasingInt32KeyFrame(int value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingInt32KeyFrame(int value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override int InterpolateValueCore(int baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingInt32KeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class Int32AnimationUsingKeyFrames : Int32AnimationBase, IKeyFrameAnimation, IAddChild
{
    private Int32KeyFrameCollection _keyFrames = new();
    public Int32AnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public Int32KeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (Int32KeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<int>.AddChildTo<Int32KeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<int>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new Int32AnimationUsingKeyFrames Clone() => (Int32AnimationUsingKeyFrames)base.Clone();
    public new Int32AnimationUsingKeyFrames CloneCurrentValue() => (Int32AnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Int32AnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override int GetCurrentValueCore(int origin, int destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<int>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<int>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((Int32AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((Int32AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((Int32AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((Int32AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(Int32AnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<int>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class Int64KeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<long, Int64KeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<Int64KeyFrame>();
    protected Int64KeyFrame() { }
    protected Int64KeyFrame(long value) => Value = value;
    protected Int64KeyFrame(long value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public long Value { get => (long)(GetValue(ValueProperty) ?? default(long)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (long)value; }
    public long InterpolateValue(long baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract long InterpolateValueCore(long baseValue, double keyFrameProgress);
}

public partial class Int64KeyFrameCollection : Freezable, IList
{
    private static readonly Int64KeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<Int64KeyFrameCollection>();
    public static Int64KeyFrameCollection Empty => s_empty;
    public new Int64KeyFrameCollection Clone() => (Int64KeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Int64KeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((Int64KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((Int64KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((Int64KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((Int64KeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteInt64KeyFrame : Int64KeyFrame
{
    public DiscreteInt64KeyFrame() { }
    public DiscreteInt64KeyFrame(long value) : base(value) { }
    public DiscreteInt64KeyFrame(long value, KeyTime keyTime) : base(value, keyTime) { }
    protected override long InterpolateValueCore(long baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteInt64KeyFrame();
}

public class LinearInt64KeyFrame : Int64KeyFrame
{
    public LinearInt64KeyFrame() { }
    public LinearInt64KeyFrame(long value) : base(value) { }
    public LinearInt64KeyFrame(long value, KeyTime keyTime) : base(value, keyTime) { }
    protected override long InterpolateValueCore(long baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearInt64KeyFrame();
}

public class SplineInt64KeyFrame : Int64KeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineInt64KeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineInt64KeyFrame() { }
    public SplineInt64KeyFrame(long value) : base(value) { }
    public SplineInt64KeyFrame(long value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineInt64KeyFrame(long value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override long InterpolateValueCore(long baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineInt64KeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingInt64KeyFrame : Int64KeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingInt64KeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingInt64KeyFrame() { }
    public EasingInt64KeyFrame(long value) : base(value) { }
    public EasingInt64KeyFrame(long value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingInt64KeyFrame(long value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override long InterpolateValueCore(long baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingInt64KeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class Int64AnimationUsingKeyFrames : Int64AnimationBase, IKeyFrameAnimation, IAddChild
{
    private Int64KeyFrameCollection _keyFrames = new();
    public Int64AnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public Int64KeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (Int64KeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<long>.AddChildTo<Int64KeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<long>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new Int64AnimationUsingKeyFrames Clone() => (Int64AnimationUsingKeyFrames)base.Clone();
    public new Int64AnimationUsingKeyFrames CloneCurrentValue() => (Int64AnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Int64AnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override long GetCurrentValueCore(long origin, long destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<long>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<long>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((Int64AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((Int64AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((Int64AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((Int64AnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(Int64AnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<long>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class SingleKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<float, SingleKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<SingleKeyFrame>();
    protected SingleKeyFrame() { }
    protected SingleKeyFrame(float value) => Value = value;
    protected SingleKeyFrame(float value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public float Value { get => (float)(GetValue(ValueProperty) ?? default(float)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (float)value; }
    public float InterpolateValue(float baseValue, double keyFrameProgress) { KeyFrameSupport.ValidateProgress(keyFrameProgress); return InterpolateValueCore(baseValue, keyFrameProgress); }
    protected abstract float InterpolateValueCore(float baseValue, double keyFrameProgress);
}

public partial class SingleKeyFrameCollection : Freezable, IList
{
    private static readonly SingleKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<SingleKeyFrameCollection>();
    public static SingleKeyFrameCollection Empty => s_empty;
    public new SingleKeyFrameCollection Clone() => (SingleKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new SingleKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _storage.CopyFrom(((SingleKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _storage.CopyFrom(((SingleKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _storage.CopyFrom(((SingleKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _storage.CopyFrom(((SingleKeyFrameCollection)source)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

public class DiscreteSingleKeyFrame : SingleKeyFrame
{
    public DiscreteSingleKeyFrame() { }
    public DiscreteSingleKeyFrame(float value) : base(value) { }
    public DiscreteSingleKeyFrame(float value, KeyTime keyTime) : base(value, keyTime) { }
    protected override float InterpolateValueCore(float baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteSingleKeyFrame();
}

public class LinearSingleKeyFrame : SingleKeyFrame
{
    public LinearSingleKeyFrame() { }
    public LinearSingleKeyFrame(float value) : base(value) { }
    public LinearSingleKeyFrame(float value, KeyTime keyTime) : base(value, keyTime) { }
    protected override float InterpolateValueCore(float baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearSingleKeyFrame();
}

public class SplineSingleKeyFrame : SingleKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineSingleKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineSingleKeyFrame() { }
    public SplineSingleKeyFrame(float value) : base(value) { }
    public SplineSingleKeyFrame(float value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineSingleKeyFrame(float value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override float InterpolateValueCore(float baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineSingleKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

public class EasingSingleKeyFrame : SingleKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingSingleKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingSingleKeyFrame() { }
    public EasingSingleKeyFrame(float value) : base(value) { }
    public EasingSingleKeyFrame(float value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingSingleKeyFrame(float value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override float InterpolateValueCore(float baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingSingleKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class SingleAnimationUsingKeyFrames : SingleAnimationBase, IKeyFrameAnimation, IAddChild
{
    private SingleKeyFrameCollection _keyFrames = new();
    public SingleAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public SingleKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (SingleKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<float>.AddChildTo<SingleKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<float>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new SingleAnimationUsingKeyFrames Clone() => (SingleAnimationUsingKeyFrames)base.Clone();
    public new SingleAnimationUsingKeyFrames CloneCurrentValue() => (SingleAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new SingleAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override float GetCurrentValueCore(float origin, float destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<float>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<float>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((SingleAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((SingleAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((SingleAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((SingleAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(SingleAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<float>.CloneKeyFrames(source._keyFrames, mode);
}
