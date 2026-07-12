using System.Collections;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Animation;

public abstract class MatrixKeyFrame : KeyFrame<Matrix>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Matrix>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Matrix>.KeyTimeProperty;
    protected MatrixKeyFrame() { }
    protected MatrixKeyFrame(Matrix value) => Value = value;
    protected MatrixKeyFrame(Matrix value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public new Matrix Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Matrix InterpolateValue(Matrix baseValue, double keyFrameProgress) => base.InterpolateValue(baseValue, keyFrameProgress);
    protected abstract override Matrix InterpolateValueCore(Matrix baseValue, double keyFrameProgress);
}

public class MatrixKeyFrameCollection : KeyFrameCollectionBase<MatrixKeyFrame>
{
    private static readonly MatrixKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<MatrixKeyFrameCollection>();
    public static MatrixKeyFrameCollection Empty => s_empty;
    public new MatrixKeyFrameCollection Clone() => (MatrixKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new MatrixKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
    protected override void CloneCore(Freezable source) => base.CloneCore(source);
    protected override void CloneCurrentValueCore(Freezable source) => base.CloneCurrentValueCore(source);
    protected override void GetAsFrozenCore(Freezable source) => base.GetAsFrozenCore(source);
    protected override void GetCurrentValueAsFrozenCore(Freezable source) => base.GetCurrentValueAsFrozenCore(source);
}

public class DiscreteMatrixKeyFrame : MatrixKeyFrame
{
    public DiscreteMatrixKeyFrame() { }
    public DiscreteMatrixKeyFrame(Matrix value) : base(value) { }
    public DiscreteMatrixKeyFrame(Matrix value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Matrix InterpolateValueCore(Matrix baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteMatrixKeyFrame();
}

[ContentProperty("KeyFrames")]
public class MatrixAnimationUsingKeyFrames : MatrixAnimationBase, IKeyFrameAnimation, IAddChild
{
    private MatrixKeyFrameCollection _keyFrames = new();
    public MatrixAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public MatrixKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (MatrixKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Matrix>.AddChildTo(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Matrix>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new MatrixAnimationUsingKeyFrames Clone() => (MatrixAnimationUsingKeyFrames)base.Clone();
    public new MatrixAnimationUsingKeyFrames CloneCurrentValue() => (MatrixAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new MatrixAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Matrix GetCurrentValueCore(Matrix origin, Matrix destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<Matrix>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Matrix>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((MatrixAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((MatrixAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((MatrixAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((MatrixAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(MatrixAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<Matrix>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class RectKeyFrame : KeyFrame<Rect>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Rect>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Rect>.KeyTimeProperty;
    protected RectKeyFrame() { }
    protected RectKeyFrame(Rect value) => Value = value;
    protected RectKeyFrame(Rect value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public new Rect Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Rect InterpolateValue(Rect baseValue, double keyFrameProgress) => base.InterpolateValue(baseValue, keyFrameProgress);
    protected abstract override Rect InterpolateValueCore(Rect baseValue, double keyFrameProgress);
}

public class RectKeyFrameCollection : KeyFrameCollectionBase<RectKeyFrame>
{
    private static readonly RectKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<RectKeyFrameCollection>();
    public static RectKeyFrameCollection Empty => s_empty;
    public new RectKeyFrameCollection Clone() => (RectKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new RectKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
    protected override void CloneCore(Freezable source) => base.CloneCore(source);
    protected override void CloneCurrentValueCore(Freezable source) => base.CloneCurrentValueCore(source);
    protected override void GetAsFrozenCore(Freezable source) => base.GetAsFrozenCore(source);
    protected override void GetCurrentValueAsFrozenCore(Freezable source) => base.GetCurrentValueAsFrozenCore(source);
}

public class DiscreteRectKeyFrame : RectKeyFrame
{
    public DiscreteRectKeyFrame() { }
    public DiscreteRectKeyFrame(Rect value) : base(value) { }
    public DiscreteRectKeyFrame(Rect value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Rect InterpolateValueCore(Rect baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteRectKeyFrame();
}

public class LinearRectKeyFrame : RectKeyFrame
{
    public LinearRectKeyFrame() { }
    public LinearRectKeyFrame(Rect value) : base(value) { }
    public LinearRectKeyFrame(Rect value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Rect InterpolateValueCore(Rect baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearRectKeyFrame();
}

public class SplineRectKeyFrame : RectKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineRectKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineRectKeyFrame() { }
    public SplineRectKeyFrame(Rect value) : base(value) { }
    public SplineRectKeyFrame(Rect value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineRectKeyFrame(Rect value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override Rect InterpolateValueCore(Rect baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineRectKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineRectKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

public class EasingRectKeyFrame : RectKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingRectKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingRectKeyFrame() { }
    public EasingRectKeyFrame(Rect value) : base(value) { }
    public EasingRectKeyFrame(Rect value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingRectKeyFrame(Rect value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override Rect InterpolateValueCore(Rect baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingRectKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingRectKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

[ContentProperty("KeyFrames")]
public class RectAnimationUsingKeyFrames : RectAnimationBase, IKeyFrameAnimation, IAddChild
{
    private RectKeyFrameCollection _keyFrames = new();
    public RectAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public RectKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (RectKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Rect>.AddChildTo(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Rect>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new RectAnimationUsingKeyFrames Clone() => (RectAnimationUsingKeyFrames)base.Clone();
    public new RectAnimationUsingKeyFrames CloneCurrentValue() => (RectAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new RectAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Rect GetCurrentValueCore(Rect origin, Rect destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<Rect>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Rect>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((RectAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((RectAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((RectAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((RectAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(RectAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<Rect>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class SizeKeyFrame : KeyFrame<Size>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Size>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Size>.KeyTimeProperty;
    protected SizeKeyFrame() { }
    protected SizeKeyFrame(Size value) => Value = value;
    protected SizeKeyFrame(Size value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public new Size Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Size InterpolateValue(Size baseValue, double keyFrameProgress) => base.InterpolateValue(baseValue, keyFrameProgress);
    protected abstract override Size InterpolateValueCore(Size baseValue, double keyFrameProgress);
}

public class SizeKeyFrameCollection : KeyFrameCollectionBase<SizeKeyFrame>
{
    private static readonly SizeKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<SizeKeyFrameCollection>();
    public static SizeKeyFrameCollection Empty => s_empty;
    public new SizeKeyFrameCollection Clone() => (SizeKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new SizeKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
    protected override void CloneCore(Freezable source) => base.CloneCore(source);
    protected override void CloneCurrentValueCore(Freezable source) => base.CloneCurrentValueCore(source);
    protected override void GetAsFrozenCore(Freezable source) => base.GetAsFrozenCore(source);
    protected override void GetCurrentValueAsFrozenCore(Freezable source) => base.GetCurrentValueAsFrozenCore(source);
}

public class DiscreteSizeKeyFrame : SizeKeyFrame
{
    public DiscreteSizeKeyFrame() { }
    public DiscreteSizeKeyFrame(Size value) : base(value) { }
    public DiscreteSizeKeyFrame(Size value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Size InterpolateValueCore(Size baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteSizeKeyFrame();
}

public class LinearSizeKeyFrame : SizeKeyFrame
{
    public LinearSizeKeyFrame() { }
    public LinearSizeKeyFrame(Size value) : base(value) { }
    public LinearSizeKeyFrame(Size value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Size InterpolateValueCore(Size baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearSizeKeyFrame();
}

public class SplineSizeKeyFrame : SizeKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineSizeKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineSizeKeyFrame() { }
    public SplineSizeKeyFrame(Size value) : base(value) { }
    public SplineSizeKeyFrame(Size value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineSizeKeyFrame(Size value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override Size InterpolateValueCore(Size baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineSizeKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineSizeKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

public class EasingSizeKeyFrame : SizeKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingSizeKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingSizeKeyFrame() { }
    public EasingSizeKeyFrame(Size value) : base(value) { }
    public EasingSizeKeyFrame(Size value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingSizeKeyFrame(Size value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override Size InterpolateValueCore(Size baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingSizeKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingSizeKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

[ContentProperty("KeyFrames")]
public class SizeAnimationUsingKeyFrames : SizeAnimationBase, IKeyFrameAnimation, IAddChild
{
    private SizeKeyFrameCollection _keyFrames = new();
    public SizeAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public SizeKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (SizeKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Size>.AddChildTo(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Size>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new SizeAnimationUsingKeyFrames Clone() => (SizeAnimationUsingKeyFrames)base.Clone();
    public new SizeAnimationUsingKeyFrames CloneCurrentValue() => (SizeAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new SizeAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Size GetCurrentValueCore(Size origin, Size destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<Size>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Size>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((SizeAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((SizeAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((SizeAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((SizeAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(SizeAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<Size>.CloneKeyFrames(source._keyFrames, mode);
}


public abstract class VectorKeyFrame : KeyFrame<Vector>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Vector>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Vector>.KeyTimeProperty;
    protected VectorKeyFrame() { }
    protected VectorKeyFrame(Vector value) => Value = value;
    protected VectorKeyFrame(Vector value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public new Vector Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Vector InterpolateValue(Vector baseValue, double keyFrameProgress) => base.InterpolateValue(baseValue, keyFrameProgress);
    protected abstract override Vector InterpolateValueCore(Vector baseValue, double keyFrameProgress);
}

public class VectorKeyFrameCollection : KeyFrameCollectionBase<VectorKeyFrame>
{
    private static readonly VectorKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<VectorKeyFrameCollection>();
    public static VectorKeyFrameCollection Empty => s_empty;
    public new VectorKeyFrameCollection Clone() => (VectorKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new VectorKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
    protected override void CloneCore(Freezable source) => base.CloneCore(source);
    protected override void CloneCurrentValueCore(Freezable source) => base.CloneCurrentValueCore(source);
    protected override void GetAsFrozenCore(Freezable source) => base.GetAsFrozenCore(source);
    protected override void GetCurrentValueAsFrozenCore(Freezable source) => base.GetCurrentValueAsFrozenCore(source);
}

public class DiscreteVectorKeyFrame : VectorKeyFrame
{
    public DiscreteVectorKeyFrame() { }
    public DiscreteVectorKeyFrame(Vector value) : base(value) { }
    public DiscreteVectorKeyFrame(Vector value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Vector InterpolateValueCore(Vector baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteVectorKeyFrame();
}

public class LinearVectorKeyFrame : VectorKeyFrame
{
    public LinearVectorKeyFrame() { }
    public LinearVectorKeyFrame(Vector value) : base(value) { }
    public LinearVectorKeyFrame(Vector value, KeyTime keyTime) : base(value, keyTime) { }
    protected override Vector InterpolateValueCore(Vector baseValue, double keyFrameProgress) => AnimationValueOperations.Interpolate(baseValue, Value, keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new LinearVectorKeyFrame();
}

public class SplineVectorKeyFrame : VectorKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineVectorKeyFrame), new PropertyMetadata(KeyFrameDefaults.CreateFrozenKeySpline(), OnKeySplineChanged));
    public KeySpline KeySpline { get => (KeySpline)GetValue(KeySplineProperty)!; set { ArgumentNullException.ThrowIfNull(value); SetValue(KeySplineProperty, value); } }
    public SplineVectorKeyFrame() { }
    public SplineVectorKeyFrame(Vector value) : base(value) { }
    public SplineVectorKeyFrame(Vector value, KeyTime keyTime) : base(value, keyTime) { }
    public SplineVectorKeyFrame(Vector value, KeyTime keyTime, KeySpline keySpline) : base(value, keyTime) => KeySpline = keySpline;
    protected override Vector InterpolateValueCore(Vector baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, KeySpline.GetSplineProgress(keyFrameProgress));
    protected override Freezable CreateInstanceCore() => new SplineVectorKeyFrame();
    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineVectorKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

public class EasingVectorKeyFrame : VectorKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingVectorKeyFrame), new PropertyMetadata(null, OnEasingFunctionChanged));
    public IEasingFunction? EasingFunction { get => (IEasingFunction?)GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }
    public EasingVectorKeyFrame() { }
    public EasingVectorKeyFrame(Vector value) : base(value) { }
    public EasingVectorKeyFrame(Vector value, KeyTime keyTime) : base(value, keyTime) { }
    public EasingVectorKeyFrame(Vector value, KeyTime keyTime, IEasingFunction easingFunction) : base(value, keyTime) => EasingFunction = easingFunction;
    protected override Vector InterpolateValueCore(Vector baseValue, double keyFrameProgress) =>
        AnimationValueOperations.Interpolate(baseValue, Value, EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress);
    protected override Freezable CreateInstanceCore() => new EasingVectorKeyFrame();
    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingVectorKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

[ContentProperty("KeyFrames")]
public class VectorAnimationUsingKeyFrames : VectorAnimationBase, IKeyFrameAnimation, IAddChild
{
    private VectorKeyFrameCollection _keyFrames = new();
    public VectorAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public bool IsAdditive { get => (bool)GetValue(IsAdditiveProperty)!; set => SetValue(IsAdditiveProperty, value); }
    public bool IsCumulative { get => (bool)GetValue(IsCumulativeProperty)!; set => SetValue(IsCumulativeProperty, value); }
    public VectorKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (VectorKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Vector>.AddChildTo(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Vector>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new VectorAnimationUsingKeyFrames Clone() => (VectorAnimationUsingKeyFrames)base.Clone();
    public new VectorAnimationUsingKeyFrames CloneCurrentValue() => (VectorAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new VectorAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Vector GetCurrentValueCore(Vector origin, Vector destination, AnimationClock clock) =>
        KeyFrameAnimationTimeline<Vector>.Evaluate(this, KeyFrames, origin, destination, clock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Vector>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); CopyFrames((VectorAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); CopyFrames((VectorAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((VectorAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((VectorAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(VectorAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) =>
        KeyFrames = KeyFrameAnimationTimeline<Vector>.CloneKeyFrames(source._keyFrames, mode);
}
