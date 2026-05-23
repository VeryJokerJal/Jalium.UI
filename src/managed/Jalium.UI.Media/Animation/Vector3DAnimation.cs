using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Vector3D"/> property between two target
/// values using linear interpolation.
/// </summary>
public sealed class Vector3DAnimation : AnimationTimeline
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
    /// Gets the type of value that this animation produces.
    /// </summary>
    public override Type TargetPropertyType => typeof(Vector3D);

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
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? (defaultOriginValue is Vector3D v ? v : default);
        var to = To ?? (By.HasValue
            ? from + By.Value
            : (defaultDestinationValue is Vector3D dv ? dv : default));

        return from + (to - from) * progress;
    }
}

/// <summary>
/// Animates the value of a <see cref="Vector3D"/> property using key frames.
/// </summary>
public sealed class Vector3DAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Vector3D>
{
    private readonly Vector3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Vector3D> KeyFrames => _keyFrames;
}

/// <summary>
/// A collection of <see cref="Vector3D"/> keyframes.
/// </summary>
public sealed class Vector3DKeyFrameCollection : KeyFrameCollection<Vector3D> { }

#region Vector3D KeyFrames

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with discrete interpolation.
/// </summary>
public sealed class DiscreteVector3DKeyFrame : KeyFrame<Vector3D>
{
    public DiscreteVector3DKeyFrame() { }
    public DiscreteVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public DiscreteVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;
}

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with linear interpolation.
/// </summary>
public sealed class LinearVector3DKeyFrame : KeyFrame<Vector3D>
{
    public LinearVector3DKeyFrame() { }
    public LinearVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public LinearVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress)
        => baseValue + (TypedValue - baseValue) * keyFrameProgress;
}

/// <summary>
/// A keyframe that defines a <see cref="Vector3D"/> value with spline interpolation.
/// </summary>
public sealed class SplineVector3DKeyFrame : KeyFrame<Vector3D>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    public SplineVector3DKeyFrame() { }
    public SplineVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public SplineVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineVector3DKeyFrame(Vector3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * progress;
    }
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Vector3D"/> animation.
/// </summary>
public sealed class EasingVector3DKeyFrame : KeyFrame<Vector3D>
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    public EasingVector3DKeyFrame() { }
    public EasingVector3DKeyFrame(Vector3D value) => TypedValue = value;
    public EasingVector3DKeyFrame(Vector3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingVector3DKeyFrame(Vector3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    public override Vector3D InterpolateValue(Vector3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * progress;
    }
}

#endregion
