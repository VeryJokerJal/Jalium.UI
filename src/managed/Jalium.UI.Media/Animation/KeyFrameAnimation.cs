using System.Collections;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Represents a single keyframe in a keyframe animation.
/// </summary>
public interface IKeyFrame
{
    /// <summary>
    /// Gets or sets the time at which the keyframe's target value should be reached.
    /// </summary>
    KeyTime KeyTime { get; set; }

    /// <summary>
    /// Gets or sets the keyframe's target value.
    /// </summary>
    object Value { get; set; }
}

/// <summary>
/// Represents a time value for a keyframe.
/// </summary>
public readonly struct KeyTime : IEquatable<KeyTime>
{
    private readonly TimeSpan _timeSpan;
    private readonly double _percent;
    private readonly KeyTimeType _type;

    private KeyTime(TimeSpan timeSpan)
    {
        _timeSpan = timeSpan;
        _percent = 0;
        _type = KeyTimeType.TimeSpan;
    }

    private KeyTime(double percent)
    {
        _timeSpan = TimeSpan.Zero;
        _percent = percent;
        _type = KeyTimeType.Percent;
    }

    private KeyTime(KeyTimeType type)
    {
        _timeSpan = TimeSpan.Zero;
        _percent = 0;
        _type = type;
    }

    /// <summary>
    /// Gets the TimeSpan value of this KeyTime.
    /// </summary>
    public TimeSpan TimeSpan => _timeSpan;

    /// <summary>
    /// Gets the percentage value of this KeyTime (0.0 to 1.0).
    /// </summary>
    public double Percent => _percent;

    /// <summary>
    /// Gets the type of this KeyTime.
    /// </summary>
    public KeyTimeType Type => _type;

    /// <summary>
    /// Creates a KeyTime from a TimeSpan.
    /// </summary>
    public static KeyTime FromTimeSpan(TimeSpan timeSpan) => new(timeSpan);

    /// <summary>
    /// Creates a KeyTime from a percentage (0.0 to 1.0).
    /// </summary>
    public static KeyTime FromPercent(double percent)
    {
        if (percent < 0 || percent > 1)
            throw new ArgumentOutOfRangeException(nameof(percent), "Percent must be between 0.0 and 1.0.");
        return new KeyTime(percent);
    }

    /// <summary>
    /// Gets a KeyTime that represents uniform distribution.
    /// </summary>
    public static KeyTime Uniform { get; } = new(KeyTimeType.Uniform);

    /// <summary>
    /// Gets a KeyTime that represents paced distribution.
    /// </summary>
    public static KeyTime Paced { get; } = new(KeyTimeType.Paced);

    /// <summary>
    /// Implicitly converts a TimeSpan to a KeyTime.
    /// </summary>
    public static implicit operator KeyTime(TimeSpan timeSpan) => FromTimeSpan(timeSpan);

    public bool Equals(KeyTime other) =>
        _type == other._type && _timeSpan == other._timeSpan && _percent.Equals(other._percent);

    /// <summary>Determines whether two key times are equal.</summary>
    public static bool Equals(KeyTime keyTime1, KeyTime keyTime2) => keyTime1.Equals(keyTime2);

    public override bool Equals(object? obj) => obj is KeyTime other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_type, _timeSpan, _percent);
    public static bool operator ==(KeyTime left, KeyTime right) => left.Equals(right);
    public static bool operator !=(KeyTime left, KeyTime right) => !left.Equals(right);
}

/// <summary>
/// Specifies the type of a KeyTime value.
/// </summary>
public enum KeyTimeType : byte
{
    /// <summary>
    /// The KeyTime is a specific TimeSpan value.
    /// </summary>
    TimeSpan = 2,

    /// <summary>
    /// The KeyTime is a percentage of the animation's total duration.
    /// </summary>
    Percent = 3,

    /// <summary>
    /// The KeyTime is distributed uniformly among all keyframes.
    /// </summary>
    Uniform = 0,

    /// <summary>
    /// The KeyTime is paced to provide a constant rate of change.
    /// </summary>
    Paced = 1
}

/// <summary>
/// Abstract base class for keyframes of a specific type.
/// </summary>
/// <typeparam name="T">The type of value being animated.</typeparam>
public abstract class KeyFrame<T> : Freezable, IKeyFrame
{
    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(T), typeof(KeyFrame<T>),
            new PropertyMetadata(default(T), OnKeyFramePropertyChanged));

    /// <summary>
    /// Identifies the <see cref="KeyTime"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty KeyTimeProperty =
        DependencyProperty.Register(nameof(KeyTime), typeof(KeyTime), typeof(KeyFrame<T>),
            new PropertyMetadata(KeyTime.Uniform, OnKeyFramePropertyChanged));

    /// <summary>
    /// Gets or sets the keyframe's target value.
    /// </summary>
    public T Value
    {
        get => (T)(GetValue(ValueProperty) ?? default(T)!);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets the typed value (alias for Value).
    /// </summary>
    public T TypedValue
    {
        get => Value;
        set => Value = value;
    }

    object IKeyFrame.Value
    {
        get => Value!;
        set => Value = (T)value;
    }

    /// <summary>
    /// Gets or sets the time at which the keyframe's target value should be reached.
    /// </summary>
    public KeyTime KeyTime
    {
        get => (KeyTime)(GetValue(KeyTimeProperty) ?? KeyTime.Uniform);
        set => SetValue(KeyTimeProperty, value);
    }

    /// <summary>
    /// Calculates the value of a keyframe at the specified progress.
    /// </summary>
    /// <param name="baseValue">The value to animate from.</param>
    /// <param name="keyFrameProgress">A value between 0.0 and 1.0 indicating the current progress through this keyframe.</param>
    /// <returns>The interpolated value.</returns>
    public T InterpolateValue(T baseValue, double keyFrameProgress)
    {
        if (keyFrameProgress < 0.0 || keyFrameProgress > 1.0)
            throw new ArgumentOutOfRangeException(nameof(keyFrameProgress));

        if (keyFrameProgress == 0.0)
            return baseValue;
        if (keyFrameProgress == 1.0)
            return Value;

        return InterpolateValueCore(baseValue, keyFrameProgress);
    }

    /// <summary>
    /// Calculates the value produced by the key frame after argument validation.
    /// </summary>
    protected abstract T InterpolateValueCore(T baseValue, double keyFrameProgress);

    /// <summary>
    /// Updates a child Freezable relationship stored in a key-frame dependency property.
    /// </summary>
    protected void OnFreezableChildPropertyChanged(
        DependencyPropertyChangedEventArgs e,
        DependencyProperty property)
    {
        OnFreezablePropertyChanged(
            e.OldValue as DependencyObject,
            e.NewValue as DependencyObject,
            property);
        WritePostscript();
    }

    private static void OnKeyFramePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var keyFrame = (KeyFrame<T>)dependencyObject;
        keyFrame.OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject);
        keyFrame.WritePostscript();
    }
}

/// <summary>Defines the common contract for key frames that animate <see cref="double"/> values.</summary>
public abstract class DoubleKeyFrame : KeyFrame<double>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<double>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<double>.KeyTimeProperty;

    protected DoubleKeyFrame() { }
    protected DoubleKeyFrame(double value) => Value = value;
    protected DoubleKeyFrame(double value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new double Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new double InterpolateValue(double baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override double InterpolateValueCore(double baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Color"/> values.</summary>
public abstract class ColorKeyFrame : KeyFrame<Color>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Color>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Color>.KeyTimeProperty;

    protected ColorKeyFrame() { }
    protected ColorKeyFrame(Color value) => Value = value;
    protected ColorKeyFrame(Color value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Color Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Color InterpolateValue(Color baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Color InterpolateValueCore(Color baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Point"/> values.</summary>
public abstract class PointKeyFrame : KeyFrame<Point>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Point>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Point>.KeyTimeProperty;

    protected PointKeyFrame() { }
    protected PointKeyFrame(Point value) => Value = value;
    protected PointKeyFrame(Point value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Point Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Point InterpolateValue(Point baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Point InterpolateValueCore(Point baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate <see cref="Thickness"/> values.</summary>
public abstract class ThicknessKeyFrame : KeyFrame<Thickness>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<Thickness>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<Thickness>.KeyTimeProperty;

    protected ThicknessKeyFrame() { }
    protected ThicknessKeyFrame(Thickness value) => Value = value;
    protected ThicknessKeyFrame(Thickness value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new Thickness Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override Thickness InterpolateValueCore(Thickness baseValue, double keyFrameProgress);
}

/// <summary>Defines the common contract for key frames that animate object values.</summary>
public abstract class ObjectKeyFrame : KeyFrame<object>
{
    public new static readonly DependencyProperty ValueProperty = KeyFrame<object>.ValueProperty;
    public new static readonly DependencyProperty KeyTimeProperty = KeyFrame<object>.KeyTimeProperty;

    protected ObjectKeyFrame() { }
    protected ObjectKeyFrame(object value) => Value = value;
    protected ObjectKeyFrame(object value, KeyTime keyTime) { Value = value; KeyTime = keyTime; }

    public new object Value { get => base.Value; set => base.Value = value; }
    public new KeyTime KeyTime { get => base.KeyTime; set => base.KeyTime = value; }
    public new object InterpolateValue(object baseValue, double keyFrameProgress) =>
        base.InterpolateValue(baseValue, keyFrameProgress);

    protected abstract override object InterpolateValueCore(object baseValue, double keyFrameProgress);
}

#region Double KeyFrames

/// <summary>
/// A keyframe that defines a double value at a specific time with discrete interpolation.
/// </summary>
public class DiscreteDoubleKeyFrame : DoubleKeyFrame
{
    public DiscreteDoubleKeyFrame() { }
    public DiscreteDoubleKeyFrame(double value) => TypedValue = value;
    public DiscreteDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override double InterpolateValueCore(double baseValue, double keyFrameProgress)
    {
        // Discrete: jump to target value at the end
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscreteDoubleKeyFrame();
}

/// <summary>
/// A keyframe that defines a double value at a specific time with linear interpolation.
/// </summary>
public class LinearDoubleKeyFrame : DoubleKeyFrame
{
    public LinearDoubleKeyFrame() { }
    public LinearDoubleKeyFrame(double value) => TypedValue = value;
    public LinearDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override double InterpolateValueCore(double baseValue, double keyFrameProgress)
    {
        return baseValue + (TypedValue - baseValue) * keyFrameProgress;
    }

    protected override Freezable CreateInstanceCore() => new LinearDoubleKeyFrame();
}

/// <summary>
/// A keyframe that defines a double value at a specific time with spline interpolation.
/// </summary>
public class SplineDoubleKeyFrame : DoubleKeyFrame
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineDoubleKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineDoubleKeyFrame() { }
    public SplineDoubleKeyFrame(double value) => TypedValue = value;
    public SplineDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineDoubleKeyFrame(double value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override double InterpolateValueCore(double baseValue, double keyFrameProgress)
    {
        var splineProgress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * splineProgress;
    }

    protected override Freezable CreateInstanceCore() => new SplineDoubleKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineDoubleKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for double animation.
/// </summary>
public class EasingDoubleKeyFrame : DoubleKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingDoubleKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    /// <summary>Gets or sets the easing function applied to this keyframe.</summary>
    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingDoubleKeyFrame() { }
    public EasingDoubleKeyFrame(double value) => TypedValue = value;
    public EasingDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override double InterpolateValueCore(double baseValue, double keyFrameProgress)
    {
        var easedProgress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * easedProgress;
    }

    protected override Freezable CreateInstanceCore() => new EasingDoubleKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingDoubleKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion

#region Color KeyFrames

/// <summary>
/// A keyframe that defines a Color value with discrete interpolation.
/// </summary>
public class DiscreteColorKeyFrame : ColorKeyFrame
{
    public DiscreteColorKeyFrame() { }
    public DiscreteColorKeyFrame(Color value) => TypedValue = value;
    public DiscreteColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Color InterpolateValueCore(Color baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscreteColorKeyFrame();
}

/// <summary>
/// A keyframe that defines a Color value with linear interpolation.
/// </summary>
public class LinearColorKeyFrame : ColorKeyFrame
{
    public LinearColorKeyFrame() { }
    public LinearColorKeyFrame(Color value) => TypedValue = value;
    public LinearColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Color InterpolateValueCore(Color baseValue, double keyFrameProgress)
    {
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * keyFrameProgress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * keyFrameProgress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * keyFrameProgress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * keyFrameProgress);
        return Color.FromArgb(a, r, g, b);
    }

    protected override Freezable CreateInstanceCore() => new LinearColorKeyFrame();
}

/// <summary>
/// A keyframe that defines a Color value with spline interpolation.
/// </summary>
public class SplineColorKeyFrame : ColorKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineColorKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineColorKeyFrame() { }
    public SplineColorKeyFrame(Color value) => TypedValue = value;
    public SplineColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineColorKeyFrame(Color value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Color InterpolateValueCore(Color baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * progress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * progress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * progress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * progress);
        return Color.FromArgb(a, r, g, b);
    }

    protected override Freezable CreateInstanceCore() => new SplineColorKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineColorKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for Color animation.
/// </summary>
public class EasingColorKeyFrame : ColorKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingColorKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingColorKeyFrame() { }
    public EasingColorKeyFrame(Color value) => TypedValue = value;
    public EasingColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingColorKeyFrame(Color value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Color InterpolateValueCore(Color baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * progress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * progress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * progress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * progress);
        return Color.FromArgb(a, r, g, b);
    }

    protected override Freezable CreateInstanceCore() => new EasingColorKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingColorKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion

#region Point KeyFrames

/// <summary>
/// A keyframe that defines a Point value with discrete interpolation.
/// </summary>
public class DiscretePointKeyFrame : PointKeyFrame
{
    public DiscretePointKeyFrame() { }
    public DiscretePointKeyFrame(Point value) => TypedValue = value;
    public DiscretePointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Point InterpolateValueCore(Point baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscretePointKeyFrame();
}

/// <summary>
/// A keyframe that defines a Point value with linear interpolation.
/// </summary>
public class LinearPointKeyFrame : PointKeyFrame
{
    public LinearPointKeyFrame() { }
    public LinearPointKeyFrame(Point value) => TypedValue = value;
    public LinearPointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Point InterpolateValueCore(Point baseValue, double keyFrameProgress)
    {
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * keyFrameProgress);
    }

    protected override Freezable CreateInstanceCore() => new LinearPointKeyFrame();
}

/// <summary>
/// A keyframe that defines a Point value with spline interpolation.
/// </summary>
public class SplinePointKeyFrame : PointKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplinePointKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplinePointKeyFrame() { }
    public SplinePointKeyFrame(Point value) => TypedValue = value;
    public SplinePointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplinePointKeyFrame(Point value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Point InterpolateValueCore(Point baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress);
    }

    protected override Freezable CreateInstanceCore() => new SplinePointKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplinePointKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>
/// A keyframe that uses an easing function for Point animation.
/// </summary>
public class EasingPointKeyFrame : PointKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingPointKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingPointKeyFrame() { }
    public EasingPointKeyFrame(Point value) => TypedValue = value;
    public EasingPointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingPointKeyFrame(Point value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Point InterpolateValueCore(Point baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress);
    }

    protected override Freezable CreateInstanceCore() => new EasingPointKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingPointKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion

#region Thickness KeyFrames

/// <summary>
/// A keyframe that defines a Thickness value with discrete interpolation.
/// </summary>
public class DiscreteThicknessKeyFrame : ThicknessKeyFrame
{
    public DiscreteThicknessKeyFrame() { }
    public DiscreteThicknessKeyFrame(Thickness value) => TypedValue = value;
    public DiscreteThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Thickness InterpolateValueCore(Thickness baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscreteThicknessKeyFrame();
}

/// <summary>
/// A keyframe that defines a Thickness value with linear interpolation.
/// </summary>
public class LinearThicknessKeyFrame : ThicknessKeyFrame
{
    public LinearThicknessKeyFrame() { }
    public LinearThicknessKeyFrame(Thickness value) => TypedValue = value;
    public LinearThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override Thickness InterpolateValueCore(Thickness baseValue, double keyFrameProgress)
    {
        return new Thickness(
            baseValue.Left + (TypedValue.Left - baseValue.Left) * keyFrameProgress,
            baseValue.Top + (TypedValue.Top - baseValue.Top) * keyFrameProgress,
            baseValue.Right + (TypedValue.Right - baseValue.Right) * keyFrameProgress,
            baseValue.Bottom + (TypedValue.Bottom - baseValue.Bottom) * keyFrameProgress);
    }

    protected override Freezable CreateInstanceCore() => new LinearThicknessKeyFrame();
}

/// <summary>
/// A keyframe that defines a Thickness value with spline interpolation.
/// </summary>
public class SplineThicknessKeyFrame : ThicknessKeyFrame
{
    public static readonly DependencyProperty KeySplineProperty =
        DependencyProperty.Register(nameof(KeySpline), typeof(KeySpline), typeof(SplineThicknessKeyFrame),
            new PropertyMetadata(null, OnKeySplineChanged));

    public KeySpline? KeySpline
    {
        get => (KeySpline?)GetValue(KeySplineProperty);
        set => SetValue(KeySplineProperty, value);
    }

    public SplineThicknessKeyFrame() { }
    public SplineThicknessKeyFrame(Thickness value) => TypedValue = value;
    public SplineThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineThicknessKeyFrame(Thickness value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    protected override Thickness InterpolateValueCore(Thickness baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new Thickness(
            baseValue.Left + (TypedValue.Left - baseValue.Left) * progress,
            baseValue.Top + (TypedValue.Top - baseValue.Top) * progress,
            baseValue.Right + (TypedValue.Right - baseValue.Right) * progress,
            baseValue.Bottom + (TypedValue.Bottom - baseValue.Bottom) * progress);
    }

    protected override Freezable CreateInstanceCore() => new SplineThicknessKeyFrame();

    private static void OnKeySplineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SplineThicknessKeyFrame)d).OnFreezableChildPropertyChanged(e, KeySplineProperty);
}

/// <summary>A keyframe that applies an easing function to Thickness interpolation.</summary>
public class EasingThicknessKeyFrame : ThicknessKeyFrame
{
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(EasingThicknessKeyFrame),
            new PropertyMetadata(null, OnEasingFunctionChanged));

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    public EasingThicknessKeyFrame() { }
    public EasingThicknessKeyFrame(Thickness value) => TypedValue = value;
    public EasingThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingThicknessKeyFrame(Thickness value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    protected override Thickness InterpolateValueCore(Thickness baseValue, double keyFrameProgress)
    {
        double progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new Thickness(
            baseValue.Left + (TypedValue.Left - baseValue.Left) * progress,
            baseValue.Top + (TypedValue.Top - baseValue.Top) * progress,
            baseValue.Right + (TypedValue.Right - baseValue.Right) * progress,
            baseValue.Bottom + (TypedValue.Bottom - baseValue.Bottom) * progress);
    }

    protected override Freezable CreateInstanceCore() => new EasingThicknessKeyFrame();

    private static void OnEasingFunctionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((EasingThicknessKeyFrame)d).OnFreezableChildPropertyChanged(e, EasingFunctionProperty);
}

#endregion

#region Object KeyFrames

/// <summary>
/// A keyframe that defines an Object value with discrete interpolation.
/// </summary>
public class DiscreteObjectKeyFrame : ObjectKeyFrame
{
    public DiscreteObjectKeyFrame() { }
    public DiscreteObjectKeyFrame(object value) => TypedValue = value;
    public DiscreteObjectKeyFrame(object value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    protected override object InterpolateValueCore(object baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }

    protected override Freezable CreateInstanceCore() => new DiscreteObjectKeyFrame();
}

#endregion

/// <summary>
/// Represents a cubic Bezier curve used for spline keyframes.
/// </summary>
/// <summary>
/// Defines the two Bézier control points of an animation's spline, used by the
/// <c>*UsingKeyFrames</c> spline key-frame types. Mirrors WPF's
/// <c>System.Windows.Media.Animation.KeySpline</c>: a <see cref="Freezable"/> whose control-point
/// X coordinates are constrained to [0,1] (so X(t) is monotonic), with the exact same
/// Newton-Raphson-plus-bisection parameter solver so spline easing matches WPF bit-for-bit.
/// </summary>
public partial class KeySpline : Freezable, IFormattable
{
    // 1/3 of the desired X accuracy, and a computational zero, matching WPF exactly.
    private const double Accuracy = 0.001;
    private const double Fuzz = 0.000001;

    private Point _controlPoint1 = new(0.0, 0.0);
    private Point _controlPoint2 = new(1.0, 1.0);

    // Cached Bézier power-basis coefficients (rebuilt lazily when control points change).
    private bool _isDirty = true;
    private bool _isSpecified;
    private double _parameter;
    private double _Bx, _Cx, _Cx_Bx, _three_Cx, _By, _Cy;

    /// <summary>Creates a new KeySpline with default control points (0,0)–(1,1) (linear).</summary>
    public KeySpline()
    {
    }

    /// <summary>Creates a new KeySpline with the specified control-point coordinates.</summary>
    public KeySpline(double x1, double y1, double x2, double y2)
        : this(new Point(x1, y1), new Point(x2, y2))
    {
    }

    /// <summary>Creates a new KeySpline with the specified control points.</summary>
    public KeySpline(Point controlPoint1, Point controlPoint2)
    {
        if (!IsValidControlPoint(controlPoint1))
            throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(controlPoint1));
        if (!IsValidControlPoint(controlPoint2))
            throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(controlPoint2));

        _controlPoint1 = controlPoint1;
        _controlPoint2 = controlPoint2;
    }

    /// <summary>
    /// Gets or sets the first control point. Its X coordinate must be in [0,1].
    /// </summary>
    public Point ControlPoint1
    {
        get
        {
            ReadPreamble();
            return _controlPoint1;
        }
        set
        {
            WritePreamble();
            if (value != _controlPoint1)
            {
                if (!IsValidControlPoint(value))
                    throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(value));
                _controlPoint1 = value;
                WritePostscript();
            }
        }
    }

    /// <summary>
    /// Gets or sets the second control point. Its X coordinate must be in [0,1].
    /// </summary>
    public Point ControlPoint2
    {
        get
        {
            ReadPreamble();
            return _controlPoint2;
        }
        set
        {
            WritePreamble();
            if (value != _controlPoint2)
            {
                if (!IsValidControlPoint(value))
                    throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(value));
                _controlPoint2 = value;
                WritePostscript();
            }
        }
    }

    /// <summary>
    /// Maps a linear progress value (0..1) through the spline, returning the eased Y progress.
    /// </summary>
    public double GetSplineProgress(double linearProgress)
    {
        ReadPreamble();
        if (_isDirty)
            Build();

        // (0,0)-(1,1) control points are the identity; skip the solve.
        if (!_isSpecified)
            return linearProgress;

        SetParameterFromX(linearProgress);
        return GetBezierValue(_By, _Cy, _parameter);
    }

    #region Freezable

    /// <summary>Creates a modifiable clone of this KeySpline.</summary>
    public new KeySpline Clone() => (KeySpline)base.Clone();

    /// <inheritdoc />
    protected override Freezable CreateInstanceCore() => new KeySpline();

    /// <inheritdoc />
    protected override void OnChanged()
    {
        _isDirty = true;
        base.OnChanged();
    }

    /// <inheritdoc />
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        CopyControlPoints((KeySpline)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        CopyControlPoints((KeySpline)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        CopyControlPoints((KeySpline)sourceFreezable);
    }

    /// <inheritdoc />
    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        CopyControlPoints((KeySpline)sourceFreezable);
    }

    private void CopyControlPoints(KeySpline source)
    {
        // Control points are plain fields (not DPs), so the base Freezable clone won't carry them.
        _controlPoint1 = source._controlPoint1;
        _controlPoint2 = source._controlPoint2;
        _isDirty = true;
    }

    #endregion

    /// <inheritdoc />
    public override string ToString()
    {
        ReadPreamble();
        return ConvertToString(null, null);
    }

    /// <summary>Formats the two control points using the supplied culture.</summary>
    public string ToString(IFormatProvider? formatProvider)
    {
        ReadPreamble();
        return ConvertToString(null, formatProvider);
    }

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
    {
        ReadPreamble();
        return ConvertToString(format, formatProvider);
    }

    private string ConvertToString(string? format, IFormatProvider? formatProvider)
    {
        var numberFormat = System.Globalization.NumberFormatInfo.GetInstance(formatProvider);
        char separator = numberFormat.NumberDecimalSeparator.Length > 0
            && numberFormat.NumberDecimalSeparator[0] == ','
            ? ';'
            : ',';
        return string.Format(
            formatProvider,
            "{1}{0}{2}",
            separator,
            ((IFormattable)_controlPoint1).ToString(format, formatProvider),
            ((IFormattable)_controlPoint2).ToString(format, formatProvider));
    }

    private static bool IsValidControlPoint(Point point) => point.X >= 0.0 && point.X <= 1.0;

    // Precompute the power-basis coefficients of the cubic Bézier with endpoints (0,0)-(1,1).
    private void Build()
    {
        if (_controlPoint1 == new Point(0, 0) && _controlPoint2 == new Point(1, 1))
        {
            // Identity spline: GetSplineProgress returns its input unchanged.
            _isSpecified = false;
        }
        else
        {
            _isSpecified = true;
            _parameter = 0;

            // X coefficients
            _Bx = 3 * _controlPoint1.X;
            _Cx = 3 * _controlPoint2.X;
            _Cx_Bx = 2 * (_Cx - _Bx);
            _three_Cx = 3 - _Cx;

            // Y coefficients
            _By = 3 * _controlPoint1.Y;
            _Cy = 3 * _controlPoint2.Y;
        }

        _isDirty = false;
    }

    // b·t·(1-t)² + c·t²·(1-t) + t³  — cubic Bézier component with endpoints 0 and 1.
    private static double GetBezierValue(double b, double c, double t)
    {
        double s = 1.0 - t;
        double t2 = t * t;
        return b * t * s * s + c * t2 * s + t2 * t;
    }

    private void GetXAndDx(double t, out double x, out double dx)
    {
        double s = 1.0 - t;
        double t2 = t * t;
        double s2 = s * s;

        x = _Bx * t * s2 + _Cx * t2 * s + t2 * t;
        dx = _Bx * s2 + _Cx_Bx * s * t + _three_Cx * t2;
    }

    // Solve X(t) = time for t, using Newton-Raphson clamped to a shrinking bisection interval.
    private void SetParameterFromX(double time)
    {
        double bottom = 0;
        double top = 1;

        if (time == 0)
        {
            _parameter = 0;
        }
        else if (time == 1)
        {
            _parameter = 1;
        }
        else
        {
            while (top - bottom > Fuzz)
            {
                GetXAndDx(_parameter, out double x, out double dx);
                double absdx = Math.Abs(dx);

                // Rely on monotonicity of X(t) to clamp the interval.
                if (x > time)
                    top = _parameter;
                else
                    bottom = _parameter;

                // Desired accuracy is ultimately in Y; scale by dx (dy/dt ≤ 3 is omitted).
                if (Math.Abs(x - time) < Accuracy * absdx)
                    break;

                if (absdx > Fuzz)
                {
                    // Newton-Raphson guess.
                    double next = _parameter - (x - time) / dx;

                    if (next >= top)
                        _parameter = (_parameter + top) / 2;
                    else if (next <= bottom)
                        _parameter = (_parameter + bottom) / 2;
                    else
                        _parameter = next;
                }
                else
                {
                    // Zero derivative: fall back to bisection.
                    _parameter = (bottom + top) / 2;
                }
            }
        }
    }
}

#region KeyFrame Animation Timelines

internal static class KeyFrameDefaults
{
    internal static KeySpline CreateFrozenKeySpline()
    {
        var keySpline = new KeySpline();
        keySpline.Freeze();
        return keySpline;
    }
}

/// <summary>
/// Base class for keyframe-based animations.
/// </summary>
/// <typeparam name="T">The type of value being animated.</typeparam>
public abstract class KeyFrameAnimationTimeline<T> : AnimationTimeline<T> where T : notnull
{
    protected abstract IList KeyFramesCore { get; }

    protected override T GetCurrentValueCore(T defaultOriginValue, T defaultDestinationValue, AnimationClock animationClock) =>
        GetCurrentValueCoreImpl(defaultOriginValue, defaultDestinationValue, animationClock);

    protected override Duration GetNaturalDurationCore(Clock clock) => GetNaturalDurationCoreImpl();

    protected T GetCurrentValueCoreImpl(T defaultOriginValue, T defaultDestinationValue, AnimationClock animationClock) =>
        Evaluate(this, KeyFramesCore, defaultOriginValue, defaultDestinationValue, animationClock);

    internal static T Evaluate(
        AnimationTimeline animation,
        IList keyFrames,
        T defaultOriginValue,
        T defaultDestinationValue,
        AnimationClock animationClock)
    {
        if (keyFrames.Count == 0)
            return defaultDestinationValue;

        var duration = animation.Duration.HasTimeSpan ? animation.Duration.TimeSpan : TimeSpan.FromSeconds(1);

        // Resolve keyframe times
        var resolvedKeyFrames = ResolveKeyFrameTimes(keyFrames, duration);

        // AnimationClock.CurrentTime is the time within the current iteration;
        // unlike CurrentProgress it also moves backward during AutoReverse.
        var currentTime = animationClock.CurrentTime ??
            TimeSpan.FromTicks((long)(duration.Ticks * animationClock.CurrentProgress));
        bool supportsComposition = AnimationValueOperations.IsSupported<T>();
        bool isAdditive = (bool)animation.GetValue(IsAdditiveProperty)!;
        bool isCumulative = (bool)animation.GetValue(IsCumulativeProperty)!;

        // Find the two keyframes we're between
        KeyFrame<T>? prevFrame = null;
        KeyFrame<T>? nextFrame = null;
        TimeSpan prevTime = TimeSpan.Zero;
        TimeSpan nextTime = duration;

        for (var i = 0; i < resolvedKeyFrames.Count; i++)
        {
            var (frame, time) = resolvedKeyFrames[i];
            if (time <= currentTime)
            {
                prevFrame = frame;
                prevTime = time;
            }
            else
            {
                nextFrame = frame;
                nextTime = time;
                break;
            }
        }

        T currentIterationValue;

        // If we haven't reached the first keyframe yet
        if (prevFrame == null)
        {
            if (nextFrame != null && resolvedKeyFrames.Count > 0)
            {
                var frameProgress = currentTime.TotalMilliseconds / nextTime.TotalMilliseconds;
                var firstSegmentOrigin = isAdditive && supportsComposition
                    ? AnimationValueOperations.GetZero<T>()
                    : defaultOriginValue;
                currentIterationValue = nextFrame.InterpolateValue(firstSegmentOrigin, frameProgress);
            }
            else
            {
                currentIterationValue = defaultOriginValue;
            }
        }
        else if (nextFrame == null)
        {
            // Past all keyframes: hold the final resolved value.
            currentIterationValue = prevFrame.TypedValue;
        }
        else
        {
            // Interpolate between the two keyframes.
            var segmentDuration = nextTime - prevTime;
            var segmentProgress = segmentDuration.TotalMilliseconds > 0
                ? (currentTime - prevTime).TotalMilliseconds / segmentDuration.TotalMilliseconds
                : 1.0;

            currentIterationValue = nextFrame.InterpolateValue(prevFrame.TypedValue, segmentProgress);
        }

        if (supportsComposition)
        {
            int currentRepeat = (animationClock.CurrentIteration ?? 1) - 1;
            if (isCumulative && currentRepeat > 0)
            {
                // WPF key-frame animations accumulate the final resolved key
                // frame value, rather than the last-minus-first delta used by
                // From/To/By animations.
                var finalValue = resolvedKeyFrames[^1].Frame.TypedValue;
                currentIterationValue = AnimationValueOperations.Add(
                    currentIterationValue,
                    AnimationValueOperations.Scale(finalValue, currentRepeat));
            }

            if (isAdditive)
            {
                currentIterationValue = AnimationValueOperations.Add(defaultOriginValue, currentIterationValue);
            }
        }

        return currentIterationValue;
    }

    private static List<(KeyFrame<T> Frame, TimeSpan Time)> ResolveKeyFrameTimes(
        IList keyFrames,
        TimeSpan totalDuration)
    {
        var result = new List<(KeyFrame<T> Frame, TimeSpan Time)>();
        var uniformFrames = new List<KeyFrame<T>>();

        foreach (object? item in keyFrames)
        {
            var frame = (KeyFrame<T>)item!;
            switch (frame.KeyTime.Type)
            {
                case KeyTimeType.TimeSpan:
                    result.Add((frame, frame.KeyTime.TimeSpan));
                    break;
                case KeyTimeType.Percent:
                    result.Add((frame, TimeSpan.FromTicks((long)(totalDuration.Ticks * frame.KeyTime.Percent))));
                    break;
                case KeyTimeType.Uniform:
                    uniformFrames.Add(frame);
                    break;
                case KeyTimeType.Paced:
                    // For simplicity, treat paced as uniform
                    uniformFrames.Add(frame);
                    break;
            }
        }

        // Distribute uniform keyframes evenly
        if (uniformFrames.Count > 0)
        {
            var interval = totalDuration.Ticks / (uniformFrames.Count + 1);
            for (var i = 0; i < uniformFrames.Count; i++)
            {
                result.Add((uniformFrames[i], TimeSpan.FromTicks(interval * (i + 1))));
            }
        }

        // Sort by time
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    protected Duration GetNaturalDurationCoreImpl() => GetNaturalDuration(KeyFramesCore);

    internal static Duration GetNaturalDuration(IList keyFrames)
    {
        bool hasTimeSpanKeyTime = false;
        var maxTime = TimeSpan.Zero;
        foreach (object? item in keyFrames)
        {
            var frame = (KeyFrame<T>)item!;
            if (frame.KeyTime.Type == KeyTimeType.TimeSpan)
            {
                hasTimeSpanKeyTime = true;
                if (frame.KeyTime.TimeSpan > maxTime)
                {
                    maxTime = frame.KeyTime.TimeSpan;
                }
            }
        }

        return new Duration(hasTimeSpanKeyTime ? maxTime : TimeSpan.FromSeconds(1));
    }

    protected internal override Duration GetNaturalDuration() => GetNaturalDurationCoreImpl();

    protected void ReplaceKeyFrames<TCollection>(ref TCollection storage, TCollection value)
        where TCollection : Freezable, IList
        => ReplaceAnimationChild(ref storage, value);

    internal static TCollection CloneKeyFrames<TCollection>(TCollection source, KeyFrameCollectionCloneMode mode)
        where TCollection : Freezable
    {
        return mode switch
        {
            KeyFrameCollectionCloneMode.BaseValue => (TCollection)source.Clone(),
            KeyFrameCollectionCloneMode.CurrentValue => (TCollection)source.CloneCurrentValue(),
            KeyFrameCollectionCloneMode.AsFrozen => (TCollection)source.GetAsFrozen(),
            KeyFrameCollectionCloneMode.CurrentValueAsFrozen => (TCollection)source.GetCurrentValueAsFrozen(),
            _ => throw new InvalidOperationException(),
        };
    }

    internal static void AddChildTo<TFrame>(KeyFrameCollectionBase<TFrame> collection, object child)
        where TFrame : Freezable, IKeyFrame
    {
        if (child is not TFrame keyFrame)
        {
            throw new ArgumentException("A child of a key-frame animation must be a compatible key frame.", nameof(child));
        }

        collection.Add(keyFrame);
    }

    internal static void RejectTextChild(string childText) =>
        throw new InvalidOperationException("Key-frame animations cannot have text children.");

    protected override bool FreezeCore(bool isChecking)
    {
        if (!base.FreezeCore(isChecking))
        {
            return false;
        }

        return KeyFramesCore is not Freezable collection || Freeze(collection, isChecking);
    }
}

/// <summary>Compatibility collection retained for legacy strongly typed key-frame timelines.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public class KeyFrameCollection<T> : List<KeyFrame<T>> where T : notnull
{
}

internal enum KeyFrameCollectionCloneMode
{
    BaseValue,
    CurrentValue,
    AsFrozen,
    CurrentValueAsFrozen,
}

/// <summary>
/// Animates the value of a double property using keyframes.
/// </summary>
public partial class DoubleAnimationUsingKeyFrames : DoubleAnimationBase
{
    private DoubleKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public DoubleKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

/// <summary>
/// Animates the value of a Color property using keyframes.
/// </summary>
public partial class ColorAnimationUsingKeyFrames : ColorAnimationBase
{
    private ColorKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public ColorKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

/// <summary>
/// Animates the value of a Point property using keyframes.
/// </summary>
public partial class PointAnimationUsingKeyFrames : PointAnimationBase
{
    private PointKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public PointKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

/// <summary>
/// Animates the value of a Thickness property using keyframes.
/// </summary>
public partial class ThicknessAnimationUsingKeyFrames : ThicknessAnimationBase
{
    private ThicknessKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public ThicknessKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

/// <summary>
/// Animates the value of an Object property using discrete keyframes.
/// </summary>
public partial class ObjectAnimationUsingKeyFrames : ObjectAnimationBase
{
    private ObjectKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public ObjectKeyFrameCollection KeyFrames
    {
        get => _keyFrames;
        set => ReplaceAnimationChild(ref _keyFrames, value);
    }
}

#endregion
