using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Vector3D"/> property using key frames.
/// </summary>
public partial class Vector3DAnimationUsingKeyFrames : Vector3DAnimationBase, IKeyFrameAnimation, IAddChild
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
    public Vector3DAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (Vector3DKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Vector3D>.AddChildTo<Vector3DKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Vector3D>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new Vector3DAnimationUsingKeyFrames Clone() => (Vector3DAnimationUsingKeyFrames)base.Clone();
    public new Vector3DAnimationUsingKeyFrames CloneCurrentValue() => (Vector3DAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new Vector3DAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Vector3D GetCurrentValueCore(Vector3D defaultOriginValue, Vector3D defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<Vector3D>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Vector3D>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((Vector3DAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((Vector3DAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((Vector3DAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((Vector3DAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(Vector3DAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<Vector3D>.CloneKeyFrames(source._keyFrames, mode);

    // --- from Vector3DAnimation.cs ---
    private Vector3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public Vector3DKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}
