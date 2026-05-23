using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a <see cref="Point3D"/> property between two target
/// values using linear interpolation.
/// </summary>
public sealed class Point3DAnimation : AnimationTimeline
{
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
    /// Gets the type of value that this animation produces.
    /// </summary>
    public override Type TargetPropertyType => typeof(Point3D);

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
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var from = From ?? (defaultOriginValue is Point3D p ? p : default);
        var to = To ?? (By.HasValue
            ? new Point3D(from.X + By.Value.X, from.Y + By.Value.Y, from.Z + By.Value.Z)
            : (defaultDestinationValue is Point3D dp ? dp : default));

        return new Point3D(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress,
            from.Z + (to.Z - from.Z) * progress);
    }
}

/// <summary>
/// Animates the value of a <see cref="Point3D"/> property using key frames.
/// </summary>
public sealed class Point3DAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Point3D>
{
    private readonly Point3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Point3D> KeyFrames => _keyFrames;
}

/// <summary>
/// A collection of <see cref="Point3D"/> keyframes.
/// </summary>
public sealed class Point3DKeyFrameCollection : KeyFrameCollection<Point3D> { }

#region Point3D KeyFrames

/// <summary>
/// A keyframe that defines a <see cref="Point3D"/> value with discrete interpolation.
/// </summary>
public sealed class DiscretePoint3DKeyFrame : KeyFrame<Point3D>
{
    public DiscretePoint3DKeyFrame() { }
    public DiscretePoint3DKeyFrame(Point3D value) => TypedValue = value;
    public DiscretePoint3DKeyFrame(Point3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;
}

/// <summary>
/// A keyframe that defines a <see cref="Point3D"/> value with linear interpolation.
/// </summary>
public sealed class LinearPoint3DKeyFrame : KeyFrame<Point3D>
{
    public LinearPoint3DKeyFrame() { }
    public LinearPoint3DKeyFrame(Point3D value) => TypedValue = value;
    public LinearPoint3DKeyFrame(Point3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress)
        => new(
            baseValue.X + (TypedValue.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * keyFrameProgress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * keyFrameProgress);
}

/// <summary>
/// A keyframe that defines a <see cref="Point3D"/> value with spline interpolation.
/// </summary>
public sealed class SplinePoint3DKeyFrame : KeyFrame<Point3D>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    public SplinePoint3DKeyFrame() { }
    public SplinePoint3DKeyFrame(Point3D value) => TypedValue = value;
    public SplinePoint3DKeyFrame(Point3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplinePoint3DKeyFrame(Point3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Point3D"/> animation.
/// </summary>
public sealed class EasingPoint3DKeyFrame : KeyFrame<Point3D>
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    public EasingPoint3DKeyFrame() { }
    public EasingPoint3DKeyFrame(Point3D value) => TypedValue = value;
    public EasingPoint3DKeyFrame(Point3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingPoint3DKeyFrame(Point3D value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    public override Point3D InterpolateValue(Point3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }
}

#endregion
