using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// The exception thrown when an error occurs during animation.
/// </summary>
public sealed class AnimationException : SystemException
{
    public AnimationException() { }
    public AnimationException(string message) : base(message) { }
    public AnimationException(string message, Exception innerException) : base(message, innerException) { }

    internal AnimationException(
        AnimationClock clock,
        DependencyProperty property,
        IAnimatable target,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Clock = clock;
        Property = property;
        Target = target;
    }

    public AnimationClock Clock { get; } = null!;
    public DependencyProperty Property { get; } = null!;
    public IAnimatable Target { get; } = null!;
}

/// <summary>
/// Defines the interface for animation objects.
/// </summary>
public interface IAnimation
{
    object? GetCurrentValue(object? defaultOriginValue, object? defaultDestinationValue, AnimationClock animationClock);
}

/// <summary>
/// Defines the interface for clock objects.
/// </summary>
public interface IClock
{
    ClockState CurrentState { get; }
    double? CurrentProgress { get; }
    TimeSpan? CurrentTime { get; }
    Timeline Timeline { get; }
}

/// <summary>
/// Defines the interface for keyframe-based animations.
/// </summary>
public interface IKeyFrameAnimation
{
    IList KeyFrames { get; set; }
}

/// <summary>
/// Specifies the type of animation.
/// </summary>
public enum AnimationType
{
    Automatic,
    From,
    To,
    By,
    FromTo,
    FromBy
}

/// <summary>
/// Converts RepeatBehavior from/to string.
/// </summary>
public sealed class RepeatBehaviorConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s.Equals("Forever", StringComparison.OrdinalIgnoreCase))
                return RepeatBehavior.Forever;
            if (s.EndsWith('x'))
                return new RepeatBehavior(double.Parse(s[..^1].Trim(), CultureInfo.InvariantCulture));
            return new RepeatBehavior(TimeSpan.Parse(s, CultureInfo.InvariantCulture));
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is RepeatBehavior rb)
        {
            return rb.ToString();
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
