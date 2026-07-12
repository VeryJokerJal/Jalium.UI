using System.Collections;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Vector3D"/> property between two target
/// values using linear interpolation.
/// </summary>
public partial class Vector3DAnimation : Vector3DAnimationBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="From"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Vector3D?), typeof(Vector3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="To"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Vector3D?), typeof(Vector3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="By"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Vector3D?), typeof(Vector3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="EasingFunction"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Vector3DAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Vector3D? From
    {
        get => (Vector3D?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Vector3D? To
    {
        get => (Vector3D?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the total amount by which the animation changes its starting value.
    /// </summary>
    public Vector3D? By
    {
        get => (Vector3D?)GetValue(ByProperty);
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
    /// Creates a new <see cref="Vector3DAnimation"/>.
    /// </summary>
    public Vector3DAnimation()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Vector3DAnimation"/> with the specified To value and duration.
    /// </summary>
    public Vector3DAnimation(Vector3D toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new <see cref="Vector3DAnimation"/> with the specified From and To values and duration.
    /// </summary>
    public Vector3DAnimation(Vector3D fromValue, Vector3D toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    protected override Vector3D GetCurrentValueCore(
        Vector3D defaultOriginValue,
        Vector3D defaultDestinationValue,
        AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        return AnimationValueOperations.EvaluateFromToBy(
            defaultOriginValue,
            defaultDestinationValue,
            From,
            To,
            By,
            progress,
            animationClock.CurrentIteration ?? 1,
            IsAdditive,
            IsCumulative);
    }
}

/// <summary>
/// Animates the value of a <see cref="Vector3D"/> property using key frames.
/// </summary>
public partial class Vector3DAnimationUsingKeyFrames : Vector3DAnimationBase
{
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

#region Vector3D KeyFrames

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with discrete interpolation.
/// </summary>
public class DiscreteVector3DKeyFrame : Vector3DKeyFrame
{
    public DiscreteVector3DKeyFrame() { }
    public DiscreteVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public DiscreteVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;

    protected override Freezable CreateInstanceCore() => new DiscreteVector3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with linear interpolation.
/// </summary>
public class LinearVector3DKeyFrame : Vector3DKeyFrame
{
    public LinearVector3DKeyFrame() { }
    public LinearVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public LinearVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress)
        => baseValue + (TypedValue - baseValue) * keyFrameProgress;

    protected override Freezable CreateInstanceCore() => new LinearVector3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with spline interpolation.
/// </summary>
public class SplineVector3DKeyFrame : Vector3DKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineVector3DKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineVector3DKeyFrame() { }
    public SplineVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public SplineVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineVector3DKeyFrame(Vector3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * progress;
    }

    protected override Freezable CreateInstanceCore() => new SplineVector3DKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineVector3DKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Vector3D"/> animation.
/// </summary>
public class EasingVector3DKeyFrame : Vector3DKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingVector3DKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    /// <summary>Gets or sets the easing function applied to this keyframe.</summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingVector3DKeyFrame() { }
    public EasingVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public EasingVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingVector3DKeyFrame(Vector3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Vector3D InterpolateValueCore(Vector3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * progress;
    }

    protected override Freezable CreateInstanceCore() => new EasingVector3DKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingVector3DKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion
