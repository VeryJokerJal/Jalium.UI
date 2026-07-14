using System.Collections;
using Jalium.UI.Markup;

namespace Jalium.UI.Media.Animation;

public abstract class BooleanKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<bool, BooleanKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<BooleanKeyFrame>();
    protected BooleanKeyFrame() { }
    protected BooleanKeyFrame(bool value) => Value = value;
    protected BooleanKeyFrame(bool value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public bool Value { get => (bool)(GetValue(ValueProperty) ?? false); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (bool)value; }
    public bool InterpolateValue(bool baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract bool InterpolateValueCore(bool baseValue, double keyFrameProgress);
}

public class DiscreteBooleanKeyFrame : BooleanKeyFrame
{
    public DiscreteBooleanKeyFrame() { }
    public DiscreteBooleanKeyFrame(bool value) : base(value) { }
    public DiscreteBooleanKeyFrame(bool value, KeyTime keyTime) : base(value, keyTime) { }
    protected override bool InterpolateValueCore(bool baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteBooleanKeyFrame();
}

public partial class BooleanKeyFrameCollection : Freezable, IList
{
    private static readonly BooleanKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<BooleanKeyFrameCollection>();
    public static BooleanKeyFrameCollection Empty => s_empty;
    public new BooleanKeyFrameCollection Clone() => (BooleanKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new BooleanKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((BooleanKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((BooleanKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((BooleanKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((BooleanKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class BooleanAnimationUsingKeyFrames : BooleanAnimationBase, IKeyFrameAnimation, IAddChild
{
    private BooleanKeyFrameCollection _keyFrames = new();
    public BooleanAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public BooleanKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (BooleanKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<bool>.AddChildTo<BooleanKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<bool>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new BooleanAnimationUsingKeyFrames Clone() => (BooleanAnimationUsingKeyFrames)base.Clone();
    public new BooleanAnimationUsingKeyFrames CloneCurrentValue() => (BooleanAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new BooleanAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override bool GetCurrentValueCore(bool defaultOriginValue, bool defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<bool>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<bool>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((BooleanAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((BooleanAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((BooleanAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((BooleanAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(BooleanAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<bool>.CloneKeyFrames(source._keyFrames, mode);
}

public abstract class CharKeyFrame : Freezable, IKeyFrame
{
    public static readonly DependencyProperty ValueProperty = KeyFrameSupport.RegisterValue<char, CharKeyFrame>();
    public static readonly DependencyProperty KeyTimeProperty = KeyFrameSupport.RegisterKeyTime<CharKeyFrame>();
    protected CharKeyFrame() { }
    protected CharKeyFrame(char value) => Value = value;
    protected CharKeyFrame(char value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public char Value { get => (char)(GetValue(ValueProperty) ?? default(char)); set => SetValue(ValueProperty, value); }
    public KeyTime KeyTime { get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform); set => SetValue(KeyTimeProperty, value); }
    object IKeyFrame.Value { get => Value; set => Value = (char)value; }
    public char InterpolateValue(char baseValue, double keyFrameProgress)
    {
        KeyFrameSupport.ValidateProgress(keyFrameProgress);
        return InterpolateValueCore(baseValue, keyFrameProgress);
    }
    protected abstract char InterpolateValueCore(char baseValue, double keyFrameProgress);
}

public class DiscreteCharKeyFrame : CharKeyFrame
{
    public DiscreteCharKeyFrame() { }
    public DiscreteCharKeyFrame(char value) : base(value) { }
    public DiscreteCharKeyFrame(char value, KeyTime keyTime) : base(value, keyTime) { }
    protected override char InterpolateValueCore(char baseValue, double keyFrameProgress) => keyFrameProgress >= 1d ? Value : baseValue;
    protected override Freezable CreateInstanceCore() => new DiscreteCharKeyFrame();
}

public partial class CharKeyFrameCollection : Freezable, IList
{
    private static readonly CharKeyFrameCollection s_empty = KeyFrameCollectionDefaults.CreateFrozen<CharKeyFrameCollection>();
    public static CharKeyFrameCollection Empty => s_empty;
    public new CharKeyFrameCollection Clone() => (CharKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new CharKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _storage.Freeze(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); _storage.CopyFrom(((CharKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); _storage.CopyFrom(((CharKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable sourceFreezable) { base.GetAsFrozenCore(sourceFreezable); _storage.CopyFrom(((CharKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) { base.GetCurrentValueAsFrozenCore(sourceFreezable); _storage.CopyFrom(((CharKeyFrameCollection)sourceFreezable)._storage, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
}

[Jalium.UI.Markup.ContentProperty("KeyFrames")]
public class CharAnimationUsingKeyFrames : CharAnimationBase, IKeyFrameAnimation, IAddChild
{
    private CharKeyFrameCollection _keyFrames = new();
    public CharAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    public CharKeyFrameCollection KeyFrames { get => _keyFrames; set => ReplaceAnimationChild(ref _keyFrames, value); }
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (CharKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<char>.AddChildTo<CharKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<char>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new CharAnimationUsingKeyFrames Clone() => (CharAnimationUsingKeyFrames)base.Clone();
    public new CharAnimationUsingKeyFrames CloneCurrentValue() => (CharAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new CharAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override char GetCurrentValueCore(char defaultOriginValue, char defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<char>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<char>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((CharAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((CharAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((CharAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((CharAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(CharAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<char>.CloneKeyFrames(source._keyFrames, mode);
}
