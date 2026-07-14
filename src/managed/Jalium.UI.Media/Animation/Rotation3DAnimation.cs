using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Rotation3D"/> property between two target
/// rotations. Interpolation runs in quaternion space (SLERP) so the rotation
/// follows the shortest arc regardless of how each endpoint was authored.
/// </summary>
public sealed partial class Rotation3DAnimation : Rotation3DAnimationBase
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

    // --- from Media3DSimpleAnimations.WpfParity.cs ---
    public Rotation3DAnimation(Rotation3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Rotation3DAnimation(Rotation3D fromValue, Rotation3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Rotation3DAnimation Clone() => (Rotation3DAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Rotation3DAnimation();

    // --- from Rotation3DAnimation.cs ---
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
    protected override Rotation3D GetCurrentValueCore(
        Rotation3D defaultOriginValue,
        Rotation3D defaultDestinationValue,
        AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var fromRotation = From ?? defaultOriginValue ?? Rotation3D.Identity;
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
            toQuaternion = ToQuaternion(defaultDestinationValue ?? Rotation3D.Identity);
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
/// A keyframe that defines a <see cref="Rotation3D"/> value with discrete interpolation.
/// </summary>
public class DiscreteRotation3DKeyFrame : Rotation3DKeyFrame
{
    public DiscreteRotation3DKeyFrame() { }
    public DiscreteRotation3DKeyFrame(Rotation3D value) => Value = value;
    public DiscreteRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    protected override Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? Value : baseValue;

    protected override Freezable CreateInstanceCore() => new DiscreteRotation3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Rotation3D"/> value with linear (SLERP) interpolation.
/// </summary>
public class LinearRotation3DKeyFrame : Rotation3DKeyFrame
{
    public LinearRotation3DKeyFrame() { }
    public LinearRotation3DKeyFrame(Rotation3D value) => Value = value;
    public LinearRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    protected override Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress)
    {
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(Value);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, keyFrameProgress));
    }

    protected override Freezable CreateInstanceCore() => new LinearRotation3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Rotation3D"/> value with spline interpolation.
/// </summary>
public class SplineRotation3DKeyFrame : Rotation3DKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineRotation3DKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineRotation3DKeyFrame() { }
    public SplineRotation3DKeyFrame(Rotation3D value) => Value = value;
    public SplineRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public SplineRotation3DKeyFrame(Rotation3D value, KeyTime keyTime, KeySpline keySpline)
    {
        Value = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(Value);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, progress));
    }

    protected override Freezable CreateInstanceCore() => new SplineRotation3DKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Rotation3D"/> animation.
/// </summary>
public class EasingRotation3DKeyFrame : Rotation3DKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingRotation3DKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    /// <summary>Gets or sets the easing function applied to this keyframe.</summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingRotation3DKeyFrame() { }
    public EasingRotation3DKeyFrame(Rotation3D value) => Value = value;
    public EasingRotation3DKeyFrame(Rotation3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public EasingRotation3DKeyFrame(Rotation3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        Value = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Rotation3D InterpolateValueCore(Rotation3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var from = Rotation3DAnimation.ToQuaternion(baseValue ?? Rotation3D.Identity);
        var to = Rotation3DAnimation.ToQuaternion(Value);
        return new QuaternionRotation3D(Quaternion.Slerp(from, to, progress));
    }

    protected override Freezable CreateInstanceCore() => new EasingRotation3DKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}
