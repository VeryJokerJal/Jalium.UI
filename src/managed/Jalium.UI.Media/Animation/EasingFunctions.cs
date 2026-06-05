using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies how an <see cref="EasingFunctionBase"/> interpolates.
/// </summary>
public enum EasingMode
{
    /// <summary>Easing is applied at the beginning of the animation.</summary>
    EaseIn,

    /// <summary>Easing is applied at the end of the animation.</summary>
    EaseOut,

    /// <summary>Easing is applied at both the beginning and end.</summary>
    EaseInOut
}

/// <summary>
/// Provides a mechanism for producing a value that varies non-linearly over the normalized
/// progress of an animation.
/// </summary>
public interface IEasingFunction
{
    /// <summary>Transforms the normalized progress value (0..1) into an eased value.</summary>
    double Ease(double normalizedTime);
}

/// <summary>
/// Base class for all easing functions. Mirrors WPF's
/// <c>System.Windows.Media.Animation.EasingFunctionBase</c>: a <see cref="Freezable"/> whose
/// <see cref="EasingMode"/> (and each derived function's parameters) are dependency properties,
/// so easing functions can be frozen, cloned and used as animatable values.
/// </summary>
public abstract class EasingFunctionBase : Freezable, IEasingFunction
{
    /// <summary>Identifies the <see cref="EasingMode"/> dependency property.</summary>
    public static readonly DependencyProperty EasingModeProperty =
        DependencyProperty.Register(nameof(EasingMode), typeof(EasingMode), typeof(EasingFunctionBase),
            new PropertyMetadata(EasingMode.EaseOut));

    /// <summary>Gets or sets the easing mode (<see cref="EasingMode.EaseOut"/> by default).</summary>
    public EasingMode EasingMode
    {
        get => (EasingMode)GetValue(EasingModeProperty)!;
        set => SetValue(EasingModeProperty, value);
    }

    /// <summary>
    /// Transforms <paramref name="normalizedTime"/> by applying <see cref="EaseInCore"/> according to
    /// the current <see cref="EasingMode"/>. Matches WPF's mode composition exactly.
    /// </summary>
    public double Ease(double normalizedTime)
    {
        return EasingMode switch
        {
            EasingMode.EaseIn => EaseInCore(normalizedTime),
            EasingMode.EaseOut => 1.0 - EaseInCore(1.0 - normalizedTime),
            EasingMode.EaseInOut => normalizedTime < 0.5
                ? EaseInCore(normalizedTime * 2.0) * 0.5
                : (1.0 - EaseInCore((1.0 - normalizedTime) * 2.0)) * 0.5 + 0.5,
            _ => normalizedTime
        };
    }

    /// <summary>
    /// Provides the logic for the <see cref="EasingMode.EaseIn"/> portion of the easing function;
    /// the base class derives EaseOut/EaseInOut from it.
    /// </summary>
    protected abstract double EaseInCore(double normalizedTime);

    /// <summary>WPF DoubleUtil.IsZero parity: |value| &lt; 10·DBL_EPSILON.</summary>
    private protected static bool IsZero(double value) => Math.Abs(value) < 2.2204460492503131e-015 * 10.0;
}

/// <summary>
/// An easing function that accelerates/decelerates using the power <see cref="Power"/>.
/// </summary>
public sealed class PowerEase : EasingFunctionBase
{
    /// <summary>Identifies the <see cref="Power"/> dependency property.</summary>
    public static readonly DependencyProperty PowerProperty =
        DependencyProperty.Register(nameof(Power), typeof(double), typeof(PowerEase), new PropertyMetadata(2.0));

    /// <summary>Gets or sets the exponential power of the interpolation (default 2).</summary>
    public double Power
    {
        get => (double)GetValue(PowerProperty)!;
        set => SetValue(PowerProperty, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double power = Math.Max(0.0, Power);
        return Math.Pow(normalizedTime, power);
    }

    protected override Freezable CreateInstanceCore() => new PowerEase();
}

/// <summary>An easing function that creates a quadratic (t²) curve.</summary>
public sealed class QuadraticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => normalizedTime * normalizedTime;
    protected override Freezable CreateInstanceCore() => new QuadraticEase();
}

/// <summary>An easing function that creates a cubic (t³) curve.</summary>
public class CubicEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => normalizedTime * normalizedTime * normalizedTime;
    protected override Freezable CreateInstanceCore() => new CubicEase();
}

/// <summary>An easing function that creates a quartic (t⁴) curve.</summary>
public sealed class QuarticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        double t = normalizedTime;
        return t * t * t * t;
    }

    protected override Freezable CreateInstanceCore() => new QuarticEase();
}

/// <summary>An easing function that creates a quintic (t⁵) curve.</summary>
public sealed class QuinticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        double t = normalizedTime;
        return t * t * t * t * t;
    }

    protected override Freezable CreateInstanceCore() => new QuinticEase();
}

/// <summary>
/// An easing function that resembles a spring oscillating back and forth.
/// </summary>
public sealed class ElasticEase : EasingFunctionBase
{
    /// <summary>Identifies the <see cref="Oscillations"/> dependency property.</summary>
    public static readonly DependencyProperty OscillationsProperty =
        DependencyProperty.Register(nameof(Oscillations), typeof(int), typeof(ElasticEase), new PropertyMetadata(3));

    /// <summary>Identifies the <see cref="Springiness"/> dependency property.</summary>
    public static readonly DependencyProperty SpringinessProperty =
        DependencyProperty.Register(nameof(Springiness), typeof(double), typeof(ElasticEase), new PropertyMetadata(3.0));

    /// <summary>Gets or sets the number of oscillations (default 3).</summary>
    public int Oscillations
    {
        get => (int)GetValue(OscillationsProperty)!;
        set => SetValue(OscillationsProperty, value);
    }

    /// <summary>Gets or sets the springiness/stiffness (default 3).</summary>
    public double Springiness
    {
        get => (double)GetValue(SpringinessProperty)!;
        set => SetValue(SpringinessProperty, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double oscillations = Math.Max(0.0, (double)Oscillations);
        double springiness = Math.Max(0.0, Springiness);
        double expo = IsZero(springiness)
            ? normalizedTime
            : (Math.Exp(springiness * normalizedTime) - 1.0) / (Math.Exp(springiness) - 1.0);

        return expo * Math.Sin((Math.PI * 2.0 * oscillations + Math.PI * 0.5) * normalizedTime);
    }

    protected override Freezable CreateInstanceCore() => new ElasticEase();
}

/// <summary>
/// An easing function that creates a bouncing effect, parameterized by <see cref="Bounces"/> and
/// <see cref="Bounciness"/> (matches WPF's geometric-series bounce, not the fixed CSS bounce).
/// </summary>
public sealed class BounceEase : EasingFunctionBase
{
    /// <summary>Identifies the <see cref="Bounces"/> dependency property.</summary>
    public static readonly DependencyProperty BouncesProperty =
        DependencyProperty.Register(nameof(Bounces), typeof(int), typeof(BounceEase), new PropertyMetadata(3));

    /// <summary>Identifies the <see cref="Bounciness"/> dependency property.</summary>
    public static readonly DependencyProperty BouncinessProperty =
        DependencyProperty.Register(nameof(Bounciness), typeof(double), typeof(BounceEase), new PropertyMetadata(2.0));

    /// <summary>Gets or sets the number of bounces (default 3).</summary>
    public int Bounces
    {
        get => (int)GetValue(BouncesProperty)!;
        set => SetValue(BouncesProperty, value);
    }

    /// <summary>Gets or sets how bouncy the animation is — controls both amplitude and period (default 2).</summary>
    public double Bounciness
    {
        get => (double)GetValue(BouncinessProperty)!;
        set => SetValue(BouncinessProperty, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        // Faithful port of WPF BounceEase: symmetric bounces whose amplitude and period are both
        // governed by Bounciness, with Bounces controlling the count (excluding the final half bounce).
        double bounces = Math.Max(0.0, (double)Bounces);
        double bounciness = Bounciness;

        // Clamp to avoid a divide-by-zero around 1.0.
        if (bounciness < 1.0 || IsOne(bounciness))
            bounciness = 1.001;

        double pow = Math.Pow(bounciness, bounces);
        double oneMinusBounciness = 1.0 - bounciness;

        // Total number of 'units' via a geometric series (last bounce counts as half).
        double sumOfUnits = (1.0 - pow) / oneMinusBounciness + pow * 0.5;
        double unitAtT = normalizedTime * sumOfUnits;

        // Which bounce we are in.
        double bounceAtT = Math.Log(-unitAtT * (1.0 - bounciness) + 1.0, bounciness);
        double start = Math.Floor(bounceAtT);
        double end = start + 1.0;

        // Project bounce start/end back into time space.
        double startTime = (1.0 - Math.Pow(bounciness, start)) / (oneMinusBounciness * sumOfUnits);
        double endTime = (1.0 - Math.Pow(bounciness, end)) / (oneMinusBounciness * sumOfUnits);

        // Fit a quadratic through (startTime,0),(endTime,0) peaking at amplitude.
        double midTime = (startTime + endTime) * 0.5;
        double timeRelativeToPeak = normalizedTime - midTime;
        double radius = midTime - startTime;
        double amplitude = Math.Pow(1.0 / bounciness, bounces - start);

        return (-amplitude / (radius * radius)) * (timeRelativeToPeak - radius) * (timeRelativeToPeak + radius);
    }

    private static bool IsOne(double value) => Math.Abs(value - 1.0) < 2.2204460492503131e-015 * 10.0;

    protected override Freezable CreateInstanceCore() => new BounceEase();
}

/// <summary>
/// An easing function that retracts slightly before proceeding (WPF's sine-based back, not the
/// classic 1.70158 overshoot).
/// </summary>
public class BackEase : EasingFunctionBase
{
    /// <summary>Identifies the <see cref="Amplitude"/> dependency property.</summary>
    public static readonly DependencyProperty AmplitudeProperty =
        DependencyProperty.Register(nameof(Amplitude), typeof(double), typeof(BackEase), new PropertyMetadata(1.0));

    /// <summary>Gets or sets the amount of retraction; must be non-negative (default 1).</summary>
    public double Amplitude
    {
        get => (double)GetValue(AmplitudeProperty)!;
        set => SetValue(AmplitudeProperty, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double amp = Math.Max(0.0, Amplitude);
        return Math.Pow(normalizedTime, 3.0) - normalizedTime * amp * Math.Sin(Math.PI * normalizedTime);
    }

    protected override Freezable CreateInstanceCore() => new BackEase();
}

/// <summary>An easing function that creates a circular (1 - √(1-t²)) curve.</summary>
public sealed class CircleEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        normalizedTime = Math.Max(0.0, Math.Min(1.0, normalizedTime));
        return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
    }

    protected override Freezable CreateInstanceCore() => new CircleEase();
}

/// <summary>An easing function that creates an exponential curve controlled by <see cref="Exponent"/>.</summary>
public sealed class ExponentialEase : EasingFunctionBase
{
    /// <summary>Identifies the <see cref="Exponent"/> dependency property.</summary>
    public static readonly DependencyProperty ExponentProperty =
        DependencyProperty.Register(nameof(Exponent), typeof(double), typeof(ExponentialEase), new PropertyMetadata(2.0));

    /// <summary>Gets or sets the exponent; 0 produces a linear curve (default 2).</summary>
    public double Exponent
    {
        get => (double)GetValue(ExponentProperty)!;
        set => SetValue(ExponentProperty, value);
    }

    protected override double EaseInCore(double normalizedTime)
    {
        double factor = Exponent;
        return IsZero(factor)
            ? normalizedTime
            : (Math.Exp(factor * normalizedTime) - 1.0) / (Math.Exp(factor) - 1.0);
    }

    protected override Freezable CreateInstanceCore() => new ExponentialEase();
}

/// <summary>An easing function that creates a sinusoidal curve.</summary>
public sealed class SineEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime) => 1.0 - Math.Sin(Math.PI / 2.0 * (1.0 - normalizedTime));
    protected override Freezable CreateInstanceCore() => new SineEase();
}

/// <summary>
/// A simple linear easing function (identity). Not present in WPF; retained as a Jalium convenience.
/// </summary>
public sealed class LinearEase : IEasingFunction
{
    /// <summary>Returns <paramref name="normalizedTime"/> unchanged (linear progression).</summary>
    public double Ease(double normalizedTime) => normalizedTime;
}
