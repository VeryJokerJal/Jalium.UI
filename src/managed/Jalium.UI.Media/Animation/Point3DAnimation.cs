using Jalium.UI.Media.Media3D;
using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Point3D"/> property between two target
/// values using linear interpolation.
/// </summary>
public sealed partial class Point3DAnimation : Point3DAnimationBase
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
    public Point3DAnimation(Point3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(toValue, duration) => FillBehavior = fillBehavior;

    public Point3DAnimation(Point3D fromValue, Point3D toValue, Duration duration, FillBehavior fillBehavior)
        : this(fromValue, toValue, duration) => FillBehavior = fillBehavior;

    public new Point3DAnimation Clone() => (Point3DAnimation)base.Clone();

    protected override Freezable CreateInstanceCore() => new Point3DAnimation();

    // --- from Point3DAnimation.cs ---
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="From"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Point3D?), typeof(Point3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="To"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Point3D?), typeof(Point3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="By"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Point3D?), typeof(Point3DAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="EasingFunction"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(Point3DAnimation),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Point3D? From
    {
        get => (Point3D?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Point3D? To
    {
        get => (Point3D?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the total amount by which the animation changes its starting value.
    /// </summary>
    public Point3D? By
    {
        get => (Point3D?)GetValue(ByProperty);
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
    /// Creates a new <see cref="Point3DAnimation"/>.
    /// </summary>
    public Point3DAnimation()
    {
    }

    /// <summary>
    /// Creates a new <see cref="Point3DAnimation"/> with the specified To value and duration.
    /// </summary>
    public Point3DAnimation(Point3D toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new <see cref="Point3DAnimation"/> with the specified From and To values and duration.
    /// </summary>
    public Point3DAnimation(Point3D fromValue, Point3D toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    protected override Point3D GetCurrentValueCore(
        Point3D defaultOriginValue,
        Point3D defaultDestinationValue,
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
/// A keyframe that defines a <see cref="Point3D"/> value with discrete interpolation.
/// </summary>
public class DiscretePoint3DKeyFrame : Point3DKeyFrame
{
    public DiscretePoint3DKeyFrame() { }
    public DiscretePoint3DKeyFrame(Point3D value) => Value = value;
    public DiscretePoint3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    protected override Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? Value : baseValue;

    protected override Freezable CreateInstanceCore() => new DiscretePoint3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Point3D"/> value with linear interpolation.
/// </summary>
public class LinearPoint3DKeyFrame : Point3DKeyFrame
{
    public LinearPoint3DKeyFrame() { }
    public LinearPoint3DKeyFrame(Point3D value) => Value = value;
    public LinearPoint3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    protected override Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress)
        => new(
            baseValue.X + (Value.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (Value.Y - baseValue.Y) * keyFrameProgress,
            baseValue.Z + (Value.Z - baseValue.Z) * keyFrameProgress);

    protected override Freezable CreateInstanceCore() => new LinearPoint3DKeyFrame();
}

/// <summary>
/// A keyframe that defines a <see cref="Point3D"/> value with spline interpolation.
/// </summary>
public class SplinePoint3DKeyFrame : Point3DKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplinePoint3DKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplinePoint3DKeyFrame() { }
    public SplinePoint3DKeyFrame(Point3D value) => Value = value;
    public SplinePoint3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public SplinePoint3DKeyFrame(Point3D value, KeyTime keyTime, KeySpline keySpline)
    {
        Value = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (Value.X - baseValue.X) * progress,
            baseValue.Y + (Value.Y - baseValue.Y) * progress,
            baseValue.Z + (Value.Z - baseValue.Z) * progress);
    }

    protected override Freezable CreateInstanceCore() => new SplinePoint3DKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Point3D"/> animation.
/// </summary>
public class EasingPoint3DKeyFrame : Point3DKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingPoint3DKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    /// <summary>Gets or sets the easing function applied to this keyframe.</summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingPoint3DKeyFrame() { }
    public EasingPoint3DKeyFrame(Point3D value) => Value = value;
    public EasingPoint3DKeyFrame(Point3D value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }
    public EasingPoint3DKeyFrame(Point3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        Value = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Point3D InterpolateValueCore(Point3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (Value.X - baseValue.X) * progress,
            baseValue.Y + (Value.Y - baseValue.Y) * progress,
            baseValue.Z + (Value.Z - baseValue.Z) * progress);
    }

    protected override Freezable CreateInstanceCore() => new EasingPoint3DKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        KeyFrameSupport.OnChildPropertyChanged((Freezable)d, e, EasingFunctionProperty);
}
