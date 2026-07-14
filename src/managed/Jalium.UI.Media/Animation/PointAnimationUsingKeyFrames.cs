using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Point property using keyframes.
/// </summary>
public partial class PointAnimationUsingKeyFrames : PointAnimationBase, IKeyFrameAnimation, IAddChild
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
    private PointKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public PointKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }

    // --- from KeyFrameAnimationContracts.cs ---
    public PointAnimationUsingKeyFrames() => OnFreezablePropertyChanged(null, _keyFrames);
    IList IKeyFrameAnimation.KeyFrames { get => KeyFrames; set => KeyFrames = (PointKeyFrameCollection)value; }
    protected virtual void AddChild(object child) => KeyFrameAnimationTimeline<Point>.AddChildTo<PointKeyFrame>(KeyFrames, child);
    protected virtual void AddText(string childText) => KeyFrameAnimationTimeline<Point>.RejectTextChild(childText);
    void IAddChild.AddChild(object child) => AddChild(child);
    void IAddChild.AddText(string childText) => AddText(childText);
    public bool ShouldSerializeKeyFrames() => KeyFrames.Count > 0;
    public new PointAnimationUsingKeyFrames Clone() => (PointAnimationUsingKeyFrames)base.Clone();
    public new PointAnimationUsingKeyFrames CloneCurrentValue() => (PointAnimationUsingKeyFrames)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new PointAnimationUsingKeyFrames();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && Freeze(_keyFrames, isChecking);
    protected override void OnChanged() => base.OnChanged();
    protected sealed override Point GetCurrentValueCore(Point defaultOriginValue, Point defaultDestinationValue, AnimationClock animationClock) => KeyFrameAnimationTimeline<Point>.Evaluate(this, KeyFrames, defaultOriginValue, defaultDestinationValue, animationClock);
    protected sealed override Duration GetNaturalDurationCore(Clock clock) => KeyFrameAnimationTimeline<Point>.GetNaturalDuration(KeyFrames);
    protected override void CloneCore(Freezable sourceFreezable) { base.CloneCore(sourceFreezable); CopyFrames((PointAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.BaseValue); }
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) { base.CloneCurrentValueCore(sourceFreezable); CopyFrames((PointAnimationUsingKeyFrames)sourceFreezable, KeyFrameCollectionCloneMode.CurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); CopyFrames((PointAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.AsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); CopyFrames((PointAnimationUsingKeyFrames)source, KeyFrameCollectionCloneMode.CurrentValueAsFrozen); }
    private void CopyFrames(PointAnimationUsingKeyFrames source, KeyFrameCollectionCloneMode mode) => KeyFrames = KeyFrameAnimationTimeline<Point>.CloneKeyFrames(source._keyFrames, mode);
}
