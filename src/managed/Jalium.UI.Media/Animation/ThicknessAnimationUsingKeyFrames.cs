using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Thickness property using keyframes.
/// </summary>
public partial class ThicknessAnimationUsingKeyFrames : ThicknessAnimationBase, IKeyFrameAnimation, IAddChild
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

    // --- from KeyFrameAnimation.cs ---
    private ThicknessKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public ThicknessKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }

    // --- from KeyFrameAnimationContracts.cs ---
    public ThicknessAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (ThicknessKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Thickness>.AddChildTo<ThicknessKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Thickness>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new ThicknessAnimationUsingKeyFrames Clone() => (ThicknessAnimationUsingKeyFrames)base.Clone();
    public new ThicknessAnimationUsingKeyFrames CloneCurrentValue() => (ThicknessAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ThicknessAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Thickness GetCurrentValueCore(Thickness defaultOriginValue, Thickness defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<Thickness>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Thickness>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((ThicknessAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((ThicknessAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((ThicknessAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((ThicknessAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(ThicknessAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<Thickness>.CloneKeyFrames(source._keyFrames, mode);
}
