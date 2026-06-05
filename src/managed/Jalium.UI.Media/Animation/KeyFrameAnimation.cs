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
        _type == other._type && _timeSpan == other._timeSpan && Math.Abs(_percent - other._percent) < 0.0001;

    public override bool Equals(object? obj) => obj is KeyTime other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_type, _timeSpan, _percent);
    public static bool operator ==(KeyTime left, KeyTime right) => left.Equals(right);
    public static bool operator !=(KeyTime left, KeyTime right) => !left.Equals(right);
}

/// <summary>
/// Specifies the type of a KeyTime value.
/// </summary>
public enum KeyTimeType
{
    /// <summary>
    /// The KeyTime is a specific TimeSpan value.
    /// </summary>
    TimeSpan,

    /// <summary>
    /// The KeyTime is a percentage of the animation's total duration.
    /// </summary>
    Percent,

    /// <summary>
    /// The KeyTime is distributed uniformly among all keyframes.
    /// </summary>
    Uniform,

    /// <summary>
    /// The KeyTime is paced to provide a constant rate of change.
    /// </summary>
    Paced
}

/// <summary>
/// Abstract base class for keyframes of a specific type.
/// </summary>
/// <typeparam name="T">The type of value being animated.</typeparam>
public abstract class KeyFrame<T> : DependencyObject, IKeyFrame
{
    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(T), typeof(KeyFrame<T>),
            new PropertyMetadata(default(T)));

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
    public KeyTime KeyTime { get; set; }

    /// <summary>
    /// Calculates the value of a keyframe at the specified progress.
    /// </summary>
    /// <param name="baseValue">The value to animate from.</param>
    /// <param name="keyFrameProgress">A value between 0.0 and 1.0 indicating the current progress through this keyframe.</param>
    /// <returns>The interpolated value.</returns>
    public abstract T InterpolateValue(T baseValue, double keyFrameProgress);
}

#region Double KeyFrames

/// <summary>
/// A keyframe that defines a double value at a specific time with discrete interpolation.
/// </summary>
public sealed class DiscreteDoubleKeyFrame : KeyFrame<double>
{
    public DiscreteDoubleKeyFrame() { }
    public DiscreteDoubleKeyFrame(double value) => TypedValue = value;
    public DiscreteDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override double InterpolateValue(double baseValue, double keyFrameProgress)
    {
        // Discrete: jump to target value at the end
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
}

/// <summary>
/// A keyframe that defines a double value at a specific time with linear interpolation.
/// </summary>
public sealed class LinearDoubleKeyFrame : KeyFrame<double>
{
    public LinearDoubleKeyFrame() { }
    public LinearDoubleKeyFrame(double value) => TypedValue = value;
    public LinearDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override double InterpolateValue(double baseValue, double keyFrameProgress)
    {
        return baseValue + (TypedValue - baseValue) * keyFrameProgress;
    }
}

/// <summary>
/// A keyframe that defines a double value at a specific time with spline interpolation.
/// </summary>
public sealed class SplineDoubleKeyFrame : KeyFrame<double>
{
    /// <summary>
    /// Gets or sets the spline that controls the animation.
    /// </summary>
    public KeySpline? KeySpline { get; set; }

    public SplineDoubleKeyFrame() { }
    public SplineDoubleKeyFrame(double value) => TypedValue = value;
    public SplineDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineDoubleKeyFrame(double value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override double InterpolateValue(double baseValue, double keyFrameProgress)
    {
        var splineProgress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * splineProgress;
    }
}

/// <summary>
/// A keyframe that uses an easing function for double animation.
/// </summary>
public sealed class EasingDoubleKeyFrame : KeyFrame<double>
{
    /// <summary>
    /// Gets or sets the easing function applied to this keyframe.
    /// </summary>
    public IEasingFunction? EasingFunction { get; set; }

    public EasingDoubleKeyFrame() { }
    public EasingDoubleKeyFrame(double value) => TypedValue = value;
    public EasingDoubleKeyFrame(double value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public EasingDoubleKeyFrame(double value, KeyTime keyTime, IEasingFunction easingFunction)
    {
        TypedValue = value;
        KeyTime = keyTime;
        EasingFunction = easingFunction;
    }

    public override double InterpolateValue(double baseValue, double keyFrameProgress)
    {
        var easedProgress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return baseValue + (TypedValue - baseValue) * easedProgress;
    }
}

#endregion

#region Color KeyFrames

/// <summary>
/// A keyframe that defines a Color value with discrete interpolation.
/// </summary>
public sealed class DiscreteColorKeyFrame : KeyFrame<Color>
{
    public DiscreteColorKeyFrame() { }
    public DiscreteColorKeyFrame(Color value) => TypedValue = value;
    public DiscreteColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Color InterpolateValue(Color baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
}

/// <summary>
/// A keyframe that defines a Color value with linear interpolation.
/// </summary>
public sealed class LinearColorKeyFrame : KeyFrame<Color>
{
    public LinearColorKeyFrame() { }
    public LinearColorKeyFrame(Color value) => TypedValue = value;
    public LinearColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Color InterpolateValue(Color baseValue, double keyFrameProgress)
    {
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * keyFrameProgress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * keyFrameProgress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * keyFrameProgress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * keyFrameProgress);
        return Color.FromArgb(a, r, g, b);
    }
}

/// <summary>
/// A keyframe that defines a Color value with spline interpolation.
/// </summary>
public sealed class SplineColorKeyFrame : KeyFrame<Color>
{
    public KeySpline? KeySpline { get; set; }

    public SplineColorKeyFrame() { }
    public SplineColorKeyFrame(Color value) => TypedValue = value;
    public SplineColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }
    public SplineColorKeyFrame(Color value, KeyTime keyTime, KeySpline keySpline)
    {
        TypedValue = value;
        KeyTime = keyTime;
        KeySpline = keySpline;
    }

    public override Color InterpolateValue(Color baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * progress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * progress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * progress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * progress);
        return Color.FromArgb(a, r, g, b);
    }
}

/// <summary>
/// A keyframe that uses an easing function for Color animation.
/// </summary>
public sealed class EasingColorKeyFrame : KeyFrame<Color>
{
    public IEasingFunction? EasingFunction { get; set; }

    public EasingColorKeyFrame() { }
    public EasingColorKeyFrame(Color value) => TypedValue = value;
    public EasingColorKeyFrame(Color value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Color InterpolateValue(Color baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        var a = (byte)(baseValue.A + (TypedValue.A - baseValue.A) * progress);
        var r = (byte)(baseValue.R + (TypedValue.R - baseValue.R) * progress);
        var g = (byte)(baseValue.G + (TypedValue.G - baseValue.G) * progress);
        var b = (byte)(baseValue.B + (TypedValue.B - baseValue.B) * progress);
        return Color.FromArgb(a, r, g, b);
    }
}

#endregion

#region Point KeyFrames

/// <summary>
/// A keyframe that defines a Point value with discrete interpolation.
/// </summary>
public sealed class DiscretePointKeyFrame : KeyFrame<Point>
{
    public DiscretePointKeyFrame() { }
    public DiscretePointKeyFrame(Point value) => TypedValue = value;
    public DiscretePointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point InterpolateValue(Point baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
}

/// <summary>
/// A keyframe that defines a Point value with linear interpolation.
/// </summary>
public sealed class LinearPointKeyFrame : KeyFrame<Point>
{
    public LinearPointKeyFrame() { }
    public LinearPointKeyFrame(Point value) => TypedValue = value;
    public LinearPointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point InterpolateValue(Point baseValue, double keyFrameProgress)
    {
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * keyFrameProgress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * keyFrameProgress);
    }
}

/// <summary>
/// A keyframe that defines a Point value with spline interpolation.
/// </summary>
public sealed class SplinePointKeyFrame : KeyFrame<Point>
{
    public KeySpline? KeySpline { get; set; }

    public SplinePointKeyFrame() { }
    public SplinePointKeyFrame(Point value) => TypedValue = value;
    public SplinePointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point InterpolateValue(Point baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress);
    }
}

/// <summary>
/// A keyframe that uses an easing function for Point animation.
/// </summary>
public sealed class EasingPointKeyFrame : KeyFrame<Point>
{
    public IEasingFunction? EasingFunction { get; set; }

    public EasingPointKeyFrame() { }
    public EasingPointKeyFrame(Point value) => TypedValue = value;
    public EasingPointKeyFrame(Point value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Point InterpolateValue(Point baseValue, double keyFrameProgress)
    {
        var progress = EasingFunction?.Ease(keyFrameProgress) ?? keyFrameProgress;
        return new Point(
            baseValue.X + (TypedValue.X - baseValue.X) * progress,
            baseValue.Y + (TypedValue.Y - baseValue.Y) * progress);
    }
}

#endregion

#region Thickness KeyFrames

/// <summary>
/// A keyframe that defines a Thickness value with discrete interpolation.
/// </summary>
public sealed class DiscreteThicknessKeyFrame : KeyFrame<Thickness>
{
    public DiscreteThicknessKeyFrame() { }
    public DiscreteThicknessKeyFrame(Thickness value) => TypedValue = value;
    public DiscreteThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
}

/// <summary>
/// A keyframe that defines a Thickness value with linear interpolation.
/// </summary>
public sealed class LinearThicknessKeyFrame : KeyFrame<Thickness>
{
    public LinearThicknessKeyFrame() { }
    public LinearThicknessKeyFrame(Thickness value) => TypedValue = value;
    public LinearThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress)
    {
        return new Thickness(
            baseValue.Left + (TypedValue.Left - baseValue.Left) * keyFrameProgress,
            baseValue.Top + (TypedValue.Top - baseValue.Top) * keyFrameProgress,
            baseValue.Right + (TypedValue.Right - baseValue.Right) * keyFrameProgress,
            baseValue.Bottom + (TypedValue.Bottom - baseValue.Bottom) * keyFrameProgress);
    }
}

/// <summary>
/// A keyframe that defines a Thickness value with spline interpolation.
/// </summary>
public sealed class SplineThicknessKeyFrame : KeyFrame<Thickness>
{
    public KeySpline? KeySpline { get; set; }

    public SplineThicknessKeyFrame() { }
    public SplineThicknessKeyFrame(Thickness value) => TypedValue = value;
    public SplineThicknessKeyFrame(Thickness value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override Thickness InterpolateValue(Thickness baseValue, double keyFrameProgress)
    {
        var progress = KeySpline?.GetSplineProgress(keyFrameProgress) ?? keyFrameProgress;
        return new Thickness(
            baseValue.Left + (TypedValue.Left - baseValue.Left) * progress,
            baseValue.Top + (TypedValue.Top - baseValue.Top) * progress,
            baseValue.Right + (TypedValue.Right - baseValue.Right) * progress,
            baseValue.Bottom + (TypedValue.Bottom - baseValue.Bottom) * progress);
    }
}

#endregion

#region Object KeyFrames

/// <summary>
/// A keyframe that defines an Object value with discrete interpolation.
/// </summary>
public sealed class DiscreteObjectKeyFrame : KeyFrame<object>
{
    public DiscreteObjectKeyFrame() { }
    public DiscreteObjectKeyFrame(object value) => TypedValue = value;
    public DiscreteObjectKeyFrame(object value, KeyTime keyTime) { TypedValue = value; KeyTime = keyTime; }

    public override object InterpolateValue(object baseValue, double keyFrameProgress)
    {
        return keyFrameProgress >= 1.0 ? TypedValue : baseValue;
    }
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
public sealed class KeySpline : Freezable
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
        get => _controlPoint1;
        set
        {
            WritePreamble();
            if (!IsValidControlPoint(value))
                throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(value));
            _controlPoint1 = value;
            _isDirty = true;
            WritePostscript();
        }
    }

    /// <summary>
    /// Gets or sets the second control point. Its X coordinate must be in [0,1].
    /// </summary>
    public Point ControlPoint2
    {
        get => _controlPoint2;
        set
        {
            WritePreamble();
            if (!IsValidControlPoint(value))
                throw new ArgumentException("KeySpline control point X must be between 0 and 1.", nameof(value));
            _controlPoint2 = value;
            _isDirty = true;
            WritePostscript();
        }
    }

    /// <summary>
    /// Maps a linear progress value (0..1) through the spline, returning the eased Y progress.
    /// </summary>
    public double GetSplineProgress(double linearProgress)
    {
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

#region KeyFrame Collections

/// <summary>
/// A collection of keyframes for a specific type.
/// </summary>
/// <typeparam name="T">The type of keyframe.</typeparam>
public class KeyFrameCollection<T> : List<KeyFrame<T>> where T : notnull
{
}

/// <summary>
/// A collection of double keyframes.
/// </summary>
public sealed class DoubleKeyFrameCollection : KeyFrameCollection<double> { }

/// <summary>
/// A collection of Color keyframes.
/// </summary>
public sealed class ColorKeyFrameCollection : KeyFrameCollection<Color> { }

/// <summary>
/// A collection of Point keyframes.
/// </summary>
public sealed class PointKeyFrameCollection : KeyFrameCollection<Point> { }

/// <summary>
/// A collection of Thickness keyframes.
/// </summary>
public sealed class ThicknessKeyFrameCollection : KeyFrameCollection<Thickness> { }

/// <summary>
/// A collection of Object keyframes.
/// </summary>
public sealed class ObjectKeyFrameCollection : KeyFrameCollection<object> { }

#endregion

#region KeyFrame Animation Timelines

/// <summary>
/// Base class for keyframe-based animations.
/// </summary>
/// <typeparam name="T">The type of value being animated.</typeparam>
public abstract class KeyFrameAnimationTimeline<T> : AnimationTimeline<T> where T : notnull
{
    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public abstract KeyFrameCollection<T> KeyFrames { get; }

    /// <inheritdoc />
    protected override T GetCurrentValueCore(T defaultOriginValue, T defaultDestinationValue, AnimationClock animationClock)
    {
        if (KeyFrames.Count == 0)
            return defaultOriginValue;

        var progress = animationClock.CurrentProgress;
        var duration = Duration.HasTimeSpan ? Duration.TimeSpan : TimeSpan.FromSeconds(1);

        // Resolve keyframe times
        var resolvedKeyFrames = ResolveKeyFrameTimes(duration);

        // Find the current keyframe
        var currentTime = TimeSpan.FromTicks((long)(duration.Ticks * progress));

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

        // If we haven't reached the first keyframe yet
        if (prevFrame == null)
        {
            if (nextFrame != null && resolvedKeyFrames.Count > 0)
            {
                var frameProgress = currentTime.TotalMilliseconds / nextTime.TotalMilliseconds;
                return nextFrame.InterpolateValue(defaultOriginValue, frameProgress);
            }
            return defaultOriginValue;
        }

        // If we're past all keyframes
        if (nextFrame == null)
        {
            return prevFrame.TypedValue;
        }

        // Interpolate between the two keyframes
        var segmentDuration = nextTime - prevTime;
        var segmentProgress = segmentDuration.TotalMilliseconds > 0
            ? (currentTime - prevTime).TotalMilliseconds / segmentDuration.TotalMilliseconds
            : 1.0;

        return nextFrame.InterpolateValue(prevFrame.TypedValue, segmentProgress);
    }

    private List<(KeyFrame<T> Frame, TimeSpan Time)> ResolveKeyFrameTimes(TimeSpan totalDuration)
    {
        var result = new List<(KeyFrame<T> Frame, TimeSpan Time)>();
        var uniformFrames = new List<KeyFrame<T>>();

        foreach (var frame in KeyFrames)
        {
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

    /// <inheritdoc />
    protected internal override Duration GetNaturalDuration()
    {
        if (KeyFrames.Count == 0)
            return Duration.Automatic;

        var maxTime = TimeSpan.Zero;
        foreach (var frame in KeyFrames)
        {
            if (frame.KeyTime.Type == KeyTimeType.TimeSpan && frame.KeyTime.TimeSpan > maxTime)
            {
                maxTime = frame.KeyTime.TimeSpan;
            }
        }

        return maxTime > TimeSpan.Zero ? new Duration(maxTime) : Duration.Automatic;
    }
}

/// <summary>
/// Animates the value of a double property using keyframes.
/// </summary>
public sealed class DoubleAnimationUsingKeyFrames : KeyFrameAnimationTimeline<double>
{
    private readonly DoubleKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<double> KeyFrames => _keyFrames;
}

/// <summary>
/// Animates the value of a Color property using keyframes.
/// </summary>
public sealed class ColorAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Color>
{
    private readonly ColorKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Color> KeyFrames => _keyFrames;
}

/// <summary>
/// Animates the value of a Point property using keyframes.
/// </summary>
public sealed class PointAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Point>
{
    private readonly PointKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Point> KeyFrames => _keyFrames;
}

/// <summary>
/// Animates the value of a Thickness property using keyframes.
/// </summary>
public sealed class ThicknessAnimationUsingKeyFrames : KeyFrameAnimationTimeline<Thickness>
{
    private readonly ThicknessKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<Thickness> KeyFrames => _keyFrames;
}

/// <summary>
/// Animates the value of an Object property using discrete keyframes.
/// </summary>
public sealed class ObjectAnimationUsingKeyFrames : KeyFrameAnimationTimeline<object>
{
    private readonly ObjectKeyFrameCollection _keyFrames = new();

    /// <summary>
    /// Gets the collection of keyframes.
    /// </summary>
    public override KeyFrameCollection<object> KeyFrames => _keyFrames;
}

#endregion
