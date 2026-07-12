using System.Collections;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Animates the value of a Quaternion property between two target values using SLERP interpolation.
/// Used for smooth 3D rotation animations.
/// </summary>
public sealed partial class QuaternionAnimation : QuaternionAnimationBase
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
    protected override Quaternion GetCurrentValueCore(
        Quaternion defaultOriginValue,
        Quaternion defaultDestinationValue,
        AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress;

        // Apply easing function
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var fromValue = From ?? defaultOriginValue;
        var toValue = To ?? (By.HasValue ? fromValue * By.Value : defaultDestinationValue);

        return Quaternion.Slerp(fromValue, toValue, progress, UseShortestPath);
    }
}

/// <summary>
/// Animates the value of a Quaternion property using key frames.
/// </summary>
public partial class QuaternionAnimationUsingKeyFrames : QuaternionAnimationBase
{
    private QuaternionKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public QuaternionKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

#region Quaternion KeyFrames

/// <summary>
/// A keyframe that defines a Quaternion value with discrete interpolation.
/// </summary>
public class DiscreteQuaternionKeyFrame : QuaternionKeyFrame
{
    public DiscreteQuaternionKeyFrame() { }
    public DiscreteQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public DiscreteQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscreteQuaternionKeyFrame();
}

/// <summary>
/// A keyframe that defines a Quaternion value with linear (SLERP) interpolation.
/// </summary>
public class LinearQuaternionKeyFrame : QuaternionKeyFrame
{
    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public static readonly DependencyProperty UseShortestPathProperty =
        DependencyProperty.Register(nameof(UseShortestPath), typeof(bool), typeof(LinearQuaternionKeyFrame),
            new PropertyMetadata(true, static (d, _) => ((LinearQuaternionKeyFrame)d).WritePostscript()));

    public bool UseShortestPath
    {
        get => (bool)(GetValue(UseShortestPathProperty) ?? true);
        set => SetValue(UseShortestPathProperty, value);
    }

    public LinearQuaternionKeyFrame() { }
    public LinearQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public LinearQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress) =>
        Quaternion.Slerp(baseValue, TypedValue, keyFrameProgress, UseShortestPath);

    protected override Freezable CreateInstanceCore() => new LinearQuaternionKeyFrame();
}

/// <summary>
/// A keyframe that defines a Quaternion value with spline interpolation.
/// </summary>
public class SplineQuaternionKeyFrame : QuaternionKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineQuaternionKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public static readonly DependencyProperty UseShortestPathProperty =
        DependencyProperty.Register(nameof(UseShortestPath), typeof(bool), typeof(SplineQuaternionKeyFrame),
            new PropertyMetadata(true, static (d, _) => ((SplineQuaternionKeyFrame)d).WritePostscript()));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public bool UseShortestPath
    {
        get => (bool)(GetValue(UseShortestPathProperty) ?? true);
        set => SetValue(UseShortestPathProperty, value);
    }

    public SplineQuaternionKeyFrame() { }
    public SplineQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public SplineQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineQuaternionKeyFrame(Quaternion value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress)
    {
        var splineProgress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return Quaternion.Slerp(baseValue, TypedValue, splineProgress, UseShortestPath);
    }

    protected override Freezable CreateInstanceCore() => new SplineQuaternionKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineQuaternionKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for Quaternion animation.
/// </summary>
public class EasingQuaternionKeyFrame : QuaternionKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingQuaternionKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    public static readonly DependencyProperty UseShortestPathProperty =
        DependencyProperty.Register(nameof(UseShortestPath), typeof(bool), typeof(EasingQuaternionKeyFrame),
            new PropertyMetadata(true, static (d, _) => ((EasingQuaternionKeyFrame)d).WritePostscript()));

    /// <summary>Gets or sets the easing function applied to this keyframe.</summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to use the shortest path for interpolation.
    /// </summary>
    public bool UseShortestPath
    {
        get => (bool)(GetValue(UseShortestPathProperty) ?? true);
        set => SetValue(UseShortestPathProperty, value);
    }

    public EasingQuaternionKeyFrame() { }
    public EasingQuaternionKeyFrame(Quaternion value) => TypedValue = value;
    public EasingQuaternionKeyFrame(Quaternion value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingQuaternionKeyFrame(Quaternion value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Quaternion InterpolateValueCore(Quaternion baseValue, double keyFrameProgress)
    {
        var easedProgress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return Quaternion.Slerp(baseValue, TypedValue, easedProgress, UseShortestPath);
    }

    protected override Freezable CreateInstanceCore() => new EasingQuaternionKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingQuaternionKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion
