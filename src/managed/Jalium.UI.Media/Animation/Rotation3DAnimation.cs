using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Rotation3D"/> property between two target
/// rotations. Interpolation runs in quaternion space (SLERP) so the rotation
/// follows the shortest arc regardless of how each endpoint was authored.
/// </summary>
public sealed class Rotation3DAnimation : AnimationTimeline
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="From"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Rotation3D), typeof(Rotation3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="To"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Rotation3D), typeof(Rotation3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="By"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Rotation3D), typeof(Rotation3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="EasingFunction"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Rotation3DAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Rotation3D? From
    {
        get => (Rotation3D?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Rotation3D? To
    {
        get => (Rotation3D?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the rotation composed onto the starting value to produce
    /// the ending value when <see cref="To"/> is not specified.
    /// </summary>
    public Rotation3D? By
    {
        get => (Rotation3D?)GetValue(ByProperty);
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
    public override Type TargetPropertyType => typeof(Rotation3D);

    /// <summary>
    /// Creates a new <see cref="Rotation3DAnimation"/>.
    /// </summary>
    public Rotation3DAnimation()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Rotation3DAnimation"/> with the specified To value and duration.
    /// </summary>
    public Rotation3DAnimation(Rotation3D toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new <see cref="Rotation3DAnimation"/> with the specified From and To values and duration.
    /// </summary>
    public Rotation3DAnimation(Rotation3D fromValue, Rotation3D toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Gets the current animated value as a <see cref="QuaternionRotation3D"/>.
    /// </summary>
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var fromRotation = From ?? (defaultOriginValue as Rotation3D) ?? Rotation3D.Identity;
        var fromQuaternion = ToQuaternion(fromRotation);

        Quaternion toQuaternion;
        if (To != null)
        {
            toQuaternion = ToQuaternion(To);
        }
        else if (By != null)
        {
            // Composing rotations is a quaternion multiply.
            toQuaternion = fromQuaternion * ToQuaternion(By);
        }
        else
        {
            toQuaternion = ToQuaternion((defaultDestinationValue as Rotation3D) ?? Rotation3D.Identity);
        }

        return new QuaternionRotation3D(Quaternion.Slerp(fromQuaternion, toQuaternion, progress));
    }

    /// <summary>
    /// Reduces any <see cref="Rotation3D"/> representation to a quaternion so the
    /// two endpoints can be interpolated uniformly.
    /// </summary>
    internal static Quaternion ToQuaternion(Rotation3D rotation) => rotation switch
    {
        AxisAngleRotation3D axisAngle => new Quaternion(axisAngle.Axis, axisAngle.Angle),
        QuaternionRotation3D quaternionRotation => quaternionRotation.Quaternion,
        _ => Quaternion.Identity,
    };
}

/// <summary>
/// Animates the value of a <see cref="Rotation3D"/> property using key frames.
/// </summary>
public sealed class Rotation3DAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Rotation3D>
{
    private readonly Rotation3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Rotation3D> KeyFrames => _keyFrames;
}

/// <summary>
/// A collection of <see cref="Rotation3D"/> keyframes.
/// </summary>
public sealed class Rotation3DKeyFrameCollection : KeyFrameCollection<Rotation3D> { }

#region Rotation3D KeyFrames

/// <summary>
/// A keyframe that defines a <see cref="Rotation3D"/> value with discrete interpolation.
/// </summary>
public sealed class DiscreteRotation3DKeyFrame : KeyFrame<Rotation3D>
{
    public DiscreteRotation3DKeyFrame() { }
    public DiscreteRotation3DKeyFrame(Rotation3D value) => TypedValue = value;
    public DiscreteRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;
}

/// <summary>
/// A keyframe that defines a <see cref="Rotation3D"/> value with linear (SLERP) interpolation.
/// </summary>
public sealed class LinearRotation3DKeyFrame : KeyFrame<Rotation3D>
{
    public LinearRotation3DKeyFrame() { }
    public LinearRotation3DKeyFrame(Rotation3D value) => TypedValue = value;
    public LinearRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress)
    {
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(TypedValue);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, keyFrameProgress));
    }
}

/// <summary>
/// A keyframe that defines a <see cref="Rotation3D"/> value with spline interpolation.
/// </summary>
public sealed class SplineRotation3DKeyFrame : KeyFrame<Rotation3D>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    public SplineRotation3DKeyFrame() { }
    public SplineRotation3DKeyFrame(Rotation3D value) => TypedValue = value;
    public SplineRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineRotation3DKeyFrame(Rotation3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(TypedValue);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, progress));
    }
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Rotation3D"/> animation.
/// </summary>
public sealed class EasingRotation3DKeyFrame : KeyFrame<Rotation3D>
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    public EasingRotation3DKeyFrame() { }
    public EasingRotation3DKeyFrame(Rotation3D value) => TypedValue = value;
    public EasingRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingRotation3DKeyFrame(Rotation3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    public override Rotation3D InterpolateValue(Rotation3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(TypedValue);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, progress));
    }
}

#endregion
