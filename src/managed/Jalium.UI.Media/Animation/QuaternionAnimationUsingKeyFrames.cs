using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Quaternion property using key frames.
/// </summary>
public partial class QuaternionAnimationUsingKeyFrames : QuaternionAnimationBase, IKeyFrameAnimation, IAddChild
{
    public bool IsAdditive
    {
        get => (bool)GetValue(IsAdditiveProperty)!;
        set => SetValue(IsAdditiveProperty, value);
    }

    public bool IsCumulative
    {
        get => (bool)GetValue(IsCumulativeProperty)!;
        set => SetValue(IsCumulativeProperty, value);
    }

    // --- from KeyFrameAnimationContracts.cs ---
    public QuaternionAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (QuaternionKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Quaternion>.AddChildTo<QuaternionKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Quaternion>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new QuaternionAnimationUsingKeyFrames Clone() => (QuaternionAnimationUsingKeyFrames)base.Clone();
    public new QuaternionAnimationUsingKeyFrames CloneCurrentValue() => (QuaternionAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new QuaternionAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Quaternion GetCurrentValueCore(Quaternion defaultOriginValue, Quaternion defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<Quaternion>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Quaternion>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((QuaternionAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((QuaternionAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((QuaternionAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((QuaternionAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(QuaternionAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<Quaternion>.CloneKeyFrames(source._keyFrames, mode);

    // --- from QuaternionAnimation.cs ---
    private QuaternionKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public QuaternionKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}
