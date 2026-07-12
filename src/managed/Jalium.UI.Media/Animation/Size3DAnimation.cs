using System.Collections;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Size3D"/> property between two target
/// values using linear interpolation.
/// </summary>
public sealed class Size3DAnimation : AnimationTimeline
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="From"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Size3D?), typeof(Size3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="To"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Size3D?), typeof(Size3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="By"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Size3D?), typeof(Size3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="EasingFunction"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Size3DAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Size3D? From
    {
        get => (Size3D?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Size3D? To
    {
        get => (Size3D?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the total amount by which the animation changes its starting value.
    /// </summary>
    public Size3D? By
    {
        get => (Size3D?)GetValue(ByProperty);
        set => SetValue(ByProperty, value);
    }

    /// <summary>
    /// Gets or sets the easing function applied to this animation.
    /// </summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    #endregion

    /// <summary>
    /// Gets the type of value that this animation produces.
    /// </summary>
    public override Type TargetPropertyType => typeof(Size3D);

    /// <summary>
    /// Creates a new <see cref="Size3DAnimation"/>.
    /// </summary>
    public Size3DAnimation()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Size3DAnimation"/> with the specified To value and duration.
    /// </summary>
    public Size3DAnimation(Size3D toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new <see cref="Size3DAnimation"/> with the specified From and To values and duration.
    /// </summary>
    public Size3DAnimation(Size3D fromValue, Size3D toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? (defaultOriginValue is Size3D s ? s : default);
        var to = To ?? (By.HasValue
            ? new Size3D(from.X + By.Value.X, from.Y + By.Value.Y, from.Z + By.Value.Z)
            : (defaultDestinationValue is Size3D ds ? ds : default));

        // A 3-D size component is never negative; clamp to keep the animation
        // valid even if From/To were authored with a degenerate value.
        return new Size3D(
            Math.Max(0.0, from.X + (to.X - from.X) * progress),
            Math.Max(0.0, from.Y + (to.Y - from.Y) * progress),
            Math.Max(0.0, from.Z + (to.Z - from.Z) * progress));
    }
}

/// <summary>
/// Animates the value of a <see cref="Size3D"/> property using key frames.
/// </summary>
public sealed class Size3DAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Size3D>
{
    private Size3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public Size3DKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceKeyFrames(ref _keyFrames, value);
    }

    protected override IList KeyFramesCore => _keyFrames;
}

/// <summary>
/// A collection of <see cref="Size3D"/> keyframes.
/// </summary>
public sealed class Size3DKeyFrameCollection : KeyFrameCollectionBase<Size3DKeyFrame>
{
    private static readonly Size3DKeyFrameCollection s_empty =
        KeyFrameCollectionDefaults.CreateFrozen<Size3DKeyFrameCollection>();

    public static Size3DKeyFrameCollection Empty => s_empty;
    public new Size3DKeyFrameCollection Clone() => (Size3DKeyFrameCollection)base.Clone();
    protected override Freezable CreateInstanceCore() => new Size3DKeyFrameCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking);
    protected override void CloneCore(Freezable sourceFreezable) => base.CloneCore(sourceFreezable);
    protected override void CloneCurrentValueCore(Freezable sourceFreezable) => base.CloneCurrentValueCore(sourceFreezable);
    protected override void GetAsFrozenCore(Freezable sourceFreezable) => base.GetAsFrozenCore(sourceFreezable);
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable) => base.GetCurrentValueAsFrozenCore(sourceFreezable);
}

#region Size3D KeyFrames

/// <summary>Defines a key frame for a <see cref="Size3D"/> animation.</summary>
public abstract class Size3DKeyFrame : KeyFrame<Size3D>
{
}

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with discrete interpolation.
/// </summary>
public sealed class DiscreteSize3DKeyFrame : Size3DKeyFrame
{
    public DiscreteSize3DKeyFrame() { }
    public DiscreteSize3DKeyFrame(Size3D value) => TypedValue = value;
    public DiscreteSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Size3D InterpolateValueCore(Size3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;

    protected override Freezable CreateInstanceCore() => new DiscreteSize3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with linear interpolation.
/// </summary>
public sealed class LinearSize3DKeyFrame : Size3DKeyFrame
{
    public LinearSize3DKeyFrame() { }
    public LinearSize3DKeyFrame(Size3D value) => TypedValue = value;
    public LinearSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Size3D InterpolateValueCore(Size3D baseValue, double keyFrameProgress)
        => new(
            baseValue.X + (TypedValue.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * keyFrameProgress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * keyFrameProgress);

    protected override Freezable CreateInstanceCore() => new LinearSize3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with spline interpolation.
/// </summary>
public sealed class SplineSize3DKeyFrame : Size3DKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineSize3DKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineSize3DKeyFrame() { }
    public SplineSize3DKeyFrame(Size3D value) => TypedValue = value;
    public SplineSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineSize3DKeyFrame(Size3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Size3D InterpolateValueCore(Size3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }

    protected override Freezable CreateInstanceCore() => new SplineSize3DKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineSize3DKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Size3D"/> animation.
/// </summary>
public sealed class EasingSize3DKeyFrame : Size3DKeyFrame
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    public EasingSize3DKeyFrame() { }
    public EasingSize3DKeyFrame(Size3D value) => TypedValue = value;
    public EasingSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingSize3DKeyFrame(Size3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Size3D InterpolateValueCore(Size3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }

    protected override Freezable CreateInstanceCore() => new EasingSize3DKeyFrame();
}

#endregion
