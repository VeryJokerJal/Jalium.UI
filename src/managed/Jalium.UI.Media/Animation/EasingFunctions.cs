namespace Jalium.UI.Media.Animation;

/// <summary>
/// Specifies the mode of an easing function.
/// </summary>
public enum EasingMode
{
    /// <summary>
    /// Easing is applied at the beginning of the animation.
    /// </summary>
    EaseIn,

    /// <summary>
    /// Easing is applied at the end of the animation.
    /// </summary>
    EaseOut,

    /// <summary>
    /// Easing is applied at both the beginning and end.
    /// </summary>
    EaseInOut
}

/// <summary>
/// Provides a mechanism for producing a value that varies over time.
/// </summary>
public interface IEasingFunction
{
    /// <summary>
    /// Transforms the normalized progress value.
    /// </summary>
    double Ease(double normalizedTime);
}

/// <summary>
/// Base class for all easing functions.
/// </summary>
public abstract class EasingFunctionBase : IEasingFunction
{
    /// <summary>
    /// Gets or sets the easing mode.
    /// </summary>
    public EasingMode EasingMode { get; set; } = EasingMode.EaseOut;

    /// <summary>
    /// Transforms the normalized progress value.
    /// </summary>
    public double Ease(double normalizedTime)
    {
        return EasingMode switch
        {
            EasingMode.EaseIn => EaseInCore(normalizedTime),
            EasingMode.EaseOut => 1.0 - EaseInCore(1.0 - normalizedTime),
            EasingMode.EaseInOut => normalizedTime < 0.5
                ? EaseInCore(normalizedTime * 2) / 2
                : 1.0 - EaseInCore((1.0 - normalizedTime) * 2) / 2,
            _ => normalizedTime
        };
    }

    /// <summary>
    /// Provides the logic for the EaseIn portion of the easing function.
    /// </summary>
    protected abstract double EaseInCore(double normalizedTime);
}

/// <summary>
/// Represents an easing function that creates an animation that accelerates and/or decelerates using a polynomial formula.
/// </summary>
public sealed class PowerEase : EasingFunctionBase
{
    /// <summary>
    /// Gets or sets the exponential power of the animation interpolation.
    /// </summary>
    public double Power { get; set; } = 2.0;

    protected override double EaseInCore(double normalizedTime)
    {
        return Math.Pow(normalizedTime, Power);
    }
}

/// <summary>
/// Represents an easing function that creates a quadratic animation curve.
/// </summary>
public sealed class QuadraticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        return normalizedTime * normalizedTime;
    }
}

/// <summary>
/// Represents an easing function that creates a cubic animation curve.
/// </summary>
public class CubicEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        return normalizedTime * normalizedTime * normalizedTime;
    }
}

/// <summary>
/// Represents an easing function that creates a quartic animation curve.
/// </summary>
public sealed class QuarticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        var t = normalizedTime;
        return t * t * t * t;
    }
}

/// <summary>
/// Represents an easing function that creates a quintic animation curve.
/// </summary>
public sealed class QuinticEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        var t = normalizedTime;
        return t * t * t * t * t;
    }
}

/// <summary>
/// Represents an easing function that creates an animation that resembles a spring oscillating.
/// </summary>
public sealed class ElasticEase : EasingFunctionBase
{
    /// <summary>
    /// Gets or sets the number of oscillations.
    /// </summary>
    public int Oscillations { get; set; } = 3;

    /// <summary>
    /// Gets or sets the springiness.
    /// </summary>
    public double Springiness { get; set; } = 3.0;

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime == 0 || normalizedTime == 1)
            return normalizedTime;

        var oscillations = Math.Max(0, Oscillations);
        var springiness = Math.Max(0, Springiness);

        var exp = Math.Exp(springiness * normalizedTime) - 1;
        return exp * Math.Sin((Math.PI * 2 * oscillations + Math.PI * 0.5) * normalizedTime);
    }
}

/// <summary>
/// Represents an easing function that creates a bouncing effect.
/// </summary>
public sealed class BounceEase : EasingFunctionBase
{
    /// <summary>
    /// Gets or sets the number of bounces.
    /// </summary>
    public int Bounces { get; set; } = 3;

    /// <summary>
    /// Gets or sets the bounciness.
    /// </summary>
    public double Bounciness { get; set; } = 2.0;

    protected override double EaseInCore(double normalizedTime)
    {
        // Invert the time for EaseIn (bouncing effect works naturally as EaseOut)
        var t = 1.0 - normalizedTime;
        return 1.0 - BounceOut(t);
    }

    private double BounceOut(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;

        if (t < 1 / d1)
        {
            return n1 * t * t;
        }
        else if (t < 2 / d1)
        {
            t -= 1.5 / d1;
            return n1 * t * t + 0.75;
        }
        else if (t < 2.5 / d1)
        {
            t -= 2.25 / d1;
            return n1 * t * t + 0.9375;
        }
        else
        {
            t -= 2.625 / d1;
            return n1 * t * t + 0.984375;
        }
    }
}

/// <summary>
/// Represents an easing function that creates an animation that retracts slightly before proceeding.
/// </summary>
public class BackEase : EasingFunctionBase
{
    /// <summary>
    /// Gets or sets the amplitude of retraction.
    /// </summary>
    public double Amplitude { get; set; } = 1.0;

    protected override double EaseInCore(double normalizedTime)
    {
        var s = Amplitude * 1.70158;
        return normalizedTime * normalizedTime * ((s + 1) * normalizedTime - s);
    }
}

/// <summary>
/// Represents an easing function that creates a circular animation curve.
/// </summary>
public sealed class CircleEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        return 1.0 - Math.Sqrt(1.0 - normalizedTime * normalizedTime);
    }
}

/// <summary>
/// Represents an easing function that creates an exponential animation curve.
/// </summary>
public sealed class ExponentialEase : EasingFunctionBase
{
    /// <summary>
    /// Gets or sets the exponent.
    /// </summary>
    public double Exponent { get; set; } = 2.0;

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime == 0)
            return 0;

        return (Math.Exp(Exponent * normalizedTime) - 1) / (Math.Exp(Exponent) - 1);
    }
}

/// <summary>
/// Represents an easing function that creates a sine animation curve.
/// </summary>
public sealed class SineEase : EasingFunctionBase
{
    protected override double EaseInCore(double normalizedTime)
    {
        return 1.0 - Math.Sin(Math.PI / 2 * (1.0 - normalizedTime));
    }
}

/// <summary>
/// A simple linear easing function (no easing).
/// </summary>
public sealed class LinearEase : IEasingFunction
{
    /// <summary>
    /// Returns the same value (linear progression).
    /// </summary>
    public double Ease(double normalizedTime) => normalizedTime;
}
