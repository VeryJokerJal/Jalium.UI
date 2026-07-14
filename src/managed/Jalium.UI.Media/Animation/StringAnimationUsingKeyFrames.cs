using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a String property using key frames.
/// </summary>
public partial class StringAnimationUsingKeyFrames : StringAnimationBase, IKeyFrameAnimation, IAddChild
{
    public StringAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (StringKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<string>.AddChildTo<StringKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<string>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new StringAnimationUsingKeyFrames Clone() => (StringAnimationUsingKeyFrames)base.Clone();
    public new StringAnimationUsingKeyFrames CloneCurrentValue() => (StringAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new StringAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override string GetCurrentValueCore(string defaultOriginValue, string defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<string>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<string>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((StringAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((StringAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((StringAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((StringAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(StringAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<string>.CloneKeyFrames(source._keyFrames, mode);

    // --- from KeyFrameAnimations.cs ---
    private StringKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of key frames.
    /// </summary>
    public StringKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}
