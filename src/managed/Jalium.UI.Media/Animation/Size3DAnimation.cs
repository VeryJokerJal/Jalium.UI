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
    private readonly Size3DKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Size3D> KeyFrames => _keyFrames;
}

/// <summary>
/// A collection of <see cref="Size3D"/> keyframes.
/// </summary>
public sealed class Size3DKeyFrameCollection : KeyFrameCollection<Size3D> { }

#region Size3D KeyFrames

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with discrete interpolation.
/// </summary>
public sealed class DiscreteSize3DKeyFrame : KeyFrame<Size3D>
{
    public DiscreteSize3DKeyFrame() { }
    public DiscreteSize3DKeyFrame(Size3D value) => TypedValue = value;
    public DiscreteSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Size3D InterpolateValue(Size3D baseValue, double keyFrameProgress)
        => keyFrameProgress >= 1.0 ? TypedValue : baseValue;
}

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with linear interpolation.
/// </summary>
public sealed class LinearSize3DKeyFrame : KeyFrame<Size3D>
{
    public LinearSize3DKeyFrame() { }
    public LinearSize3DKeyFrame(Size3D value) => TypedValue = value;
    public LinearSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Size3D InterpolateValue(Size3D baseValue, double keyFrameProgress)
        => new(
            baseValue.X + (TypedValue.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * keyFrameProgress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * keyFrameProgress);
}

/// <summary>
/// A keyframe that defines a <see cref="Size3D"/> value with spline interpolation.
/// </summary>
public sealed class SplineSize3DKeyFrame : KeyFrame<Size3D>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    public SplineSize3DKeyFrame() { }
    public SplineSize3DKeyFrame(Size3D value) => TypedValue = value;
    public SplineSize3DKeyFrame(Size3D value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineSize3DKeyFrame(Size3D value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Size3D InterpolateValue(Size3D baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }
}

/// <summary>
/// A keyframe that uses an easing function for <see cref="Size3D"/> animation.
/// </summary>
public sealed class EasingSize3DKeyFrame : KeyFrame<Size3D>
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

    public override Size3D InterpolateValue(Size3D baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress,
            baseValue.Z + (TypedValue.Z - baseValue.Z) * progress);
    }
}

#endregion
