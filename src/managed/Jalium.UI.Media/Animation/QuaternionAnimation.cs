using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Quaternion property between two target values using SLERP interpolation.
/// Used for smooth 3D rotation animations.
/// </summary>
public sealed class QuaternionAnimation : AnimationTimeline
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the From dependency property.
    /// </summary>
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(Quaternion?), typeof(QuaternionAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the To dependency property.
    /// </summary>
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(Quaternion?), typeof(QuaternionAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the By dependency property.
    /// </summary>
    public static readonly DependencyProperty ByProperty =
        DependencyProperty.Register(nameof(By), typeof(Quaternion?), typeof(QuaternionAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the EasingFunction dependency property.
    /// </summary>
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(QuaternionAnimation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the UseShortestPath dependency property.
    /// </summary>
    public static readonly DependencyProperty UseShortestPathProperty =
        DependencyProperty.Register(nameof(UseShortestPath), typeof(bool), typeof(QuaternionAnimation),
            new PropertyMetadata(true));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the animation's starting value.
    /// </summary>
    public Quaternion? From
    {
        get => (Quaternion?)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation's ending value.
    /// </summary>
    public Quaternion? To
    {
        get => (Quaternion?)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    /// <summary>
    /// Gets or sets the total amount by which the animation changes its starting value.
    /// </summary>
    public Quaternion? By
    {
        get => (Quaternion?)GetValue(ByProperty);
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

    /// <summary>
    /// If true, the animation will automatically flip the sign of the destination
    /// Quaternion to ensure the shortest path is taken.
    /// </summary>
    public bool UseShortestPath
    {
        get => (bool)GetValue(UseShortestPathProperty)!;
        set => SetValue(UseShortestPathProperty, value);
    }

    #endregion

    /// <summary>
    /// Gets the type of value that this animation produces.
    /// </summary>
    public override Type TargetPropertyType => typeof(Quaternion);

    /// <summary>
    /// Creates a new QuaternionAnimation.
    /// </summary>
    public QuaternionAnimation()
    {
    }

    /// <summary>
    /// Creates a new QuaternionAnimation with the specified To value and duration.
    /// </summary>
    public QuaternionAnimation(Quaternion toValue, Duration duration)
    {
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new QuaternionAnimation with the specified From and To values and duration.
    /// </summary>
    public QuaternionAnimation(Quaternion fromValue, Quaternion toValue, Duration duration)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
    }

    /// <summary>
    /// Creates a new QuaternionAnimation with the specified From, To values, duration, and shortest path flag.
    /// </summary>
    public QuaternionAnimation(Quaternion fromValue, Quaternion toValue, Duration duration, bool useShortestPath)
    {
        From = fromValue;
        To = toValue;
        Duration = duration;
        UseShortestPath = useShortestPath;
    }

    /// <summary>
    /// Gets the current animated value.
    /// </summary>
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        // Apply easing function
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var fromValue = From ?? (defaultOriginValue is Quaternion q ? q : Quaternion.Identity);
        var toValue = To ?? (By.HasValue ? fromValue * By.Value : (defaultDestinationValue is Quaternion dq ? dq : Quaternion.Identity));

        // Use SLERP for smooth quaternion interpolation
        if (UseShortestPath)
        {
            // Check if the quaternions point in opposite directions, flip if needed
            var dot = fromValue.X * toValue.X + fromValue.Y * toValue.Y +
                      fromValue.Z * toValue.Z + fromValue.W * toValue.W;
            if (dot < 0)
            {
                toValue = new Quaternion(-toValue.X, -toValue.Y, -toValue.Z, -toValue.W);
            }
        }

        return Quaternion.Slerp(fromValue, toValue, progress);
    }
}

/// <summary>
/// Animates the value of a Quaternion property using key frames.
/// </summary>
public sealed class QuaternionAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Quaternion>
{
    private readonly QuaternionKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Quaternion> KeyFrames => _keyFrames;
}

/// <summary>
/// A collection of Quaternion keyframes.
/// </summary>
public sealed class QuaternionKeyFrameCollection : KeyFrameCollection<Quaternion> { }

#region Quaternion KeyFrames

/// <summary>
/// A keyframe that defines a Quaternion value with discrete interpolation.
/// </summary>
public sealed class DiscreteQuaternionKeyFrame : KeyFrame<Quaternion>
{
    public DiscreteQuaternionKeyFrame() { }
    public DiscreteQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public DiscreteQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
}

/// <summary>
/// A keyframe that defines a Quaternion value with linear (SLERP) interpolation.
/// </summary>
public sealed class LinearQuaternionKeyFrame : KeyFrame<Quaternion>
{
    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public bool UseShortestPath { get; set; } = true;

    public LinearQuaternionKeyFrame() { }
    public LinearQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public LinearQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress)
    {
        var target = TypedValue;

        if (UseShortestPath)
        {
            var dot = baseValue.X * target.X + baseValue.Y * target.Y +
                      baseValue.Z * target.Z + baseValue.W * target.W;
            if (dot < 0)
            {
                target = new Quaternion(-target.X, -target.Y, -target.Z, -target.W);
            }
        }

        return Quaternion.Slerp(baseValue, target, keyFrameProgress);
    }
}

/// <summary>
/// A keyframe that defines a Quaternion value with spline interpolation.
/// </summary>
public sealed class SplineQuaternionKeyFrame : KeyFrame<Quaternion>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public bool UseShortestPath { get; set; } = true;

    public SplineQuaternionKeyFrame() { }
    public SplineQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public SplineQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineQuaternionKeyFrame(Quaternion value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress)
    {
        var splineProgress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        var target = TypedValue;

        if (UseShortestPath)
        {
            var dot = baseValue.X * target.X + baseValue.Y * target.Y +
                      baseValue.Z * target.Z + baseValue.W * target.W;
            if (dot < 0)
            {
                target = new Quaternion(-target.X, -target.Y, -target.Z, -target.W);
            }
        }

        return Quaternion.Slerp(baseValue, target, splineProgress);
    }
}

/// <summary>
/// A keyframe that uses an easing function for Quaternion animation.
/// </summary>
public sealed class EasingQuaternionKeyFrame : KeyFrame<Quaternion>
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public bool UseShortestPath { get; set; } = true;

    public EasingQuaternionKeyFrame() { }
    public EasingQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public EasingQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingQuaternionKeyFrame(Quaternion value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    public override Quaternion InterpolateValue(Quaternion baseValue, double keyFrameProgress)
    {
        var easedProgress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var target = TypedValue;

        if (UseShortestPath)
        {
            var dot = baseValue.X * target.X + baseValue.Y * target.Y +
                      baseValue.Z * target.Z + baseValue.W * target.W;
            if (dot < 0)
            {
                target = new Quaternion(-target.X, -target.Y, -target.Z, -target.W);
            }
        }

        return Quaternion.Slerp(baseValue, target, easedProgress);
    }
}

#endregion
