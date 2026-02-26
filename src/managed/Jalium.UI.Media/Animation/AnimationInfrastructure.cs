using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// The exception thrown when an error occurs during animation.
/// </summary>
public sealed class AnimationException : Exception
{
    public AnimationException() { }
    public AnimationException(string message) : base(message) { }
    public AnimationException(string message, Exception innerException) : base(message, innerException) { }
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
    IList KeyFrames { get; }
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

/// <summary>
/// Converts KeyTime from/to string.
/// </summary>
public sealed class KeyTimeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s.Equals("Uniform", StringComparison.OrdinalIgnoreCase))
                return KeyTime.Uniform;
            if (s.Equals("Paced", StringComparison.OrdinalIgnoreCase))
                return KeyTime.Paced;
            if (s.EndsWith('%'))
                return KeyTime.FromPercent(double.Parse(s[..^1].Trim(), CultureInfo.InvariantCulture) / 100.0);
            return KeyTime.FromTimeSpan(TimeSpan.Parse(s, CultureInfo.InvariantCulture));
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is KeyTime kt)
        {
            return kt.Type switch
            {
                KeyTimeType.Uniform => "Uniform",
                KeyTimeType.Paced => "Paced",
                KeyTimeType.Percent => $"{kt.Percent * 100}%",
                _ => kt.TimeSpan.ToString()
            };
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts Duration from/to string.
/// </summary>
public sealed class DurationConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
                return Duration.Automatic;
            if (s.Equals("Forever", StringComparison.OrdinalIgnoreCase))
                return Duration.Forever;
            return new Duration(TimeSpan.Parse(s, CultureInfo.InvariantCulture));
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is Duration d)
        {
            if (d == Duration.Automatic) return "Automatic";
            if (d == Duration.Forever) return "Forever";
            return d.TimeSpan.ToString();
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts a KeySpline to and from a string representation.
/// Parses "x1,y1 x2,y2" or "x1 y1 x2 y2" format.
/// </summary>
public sealed class KeySplineConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            s = s.Trim();
            var separator = culture?.TextInfo.ListSeparator ?? CultureInfo.InvariantCulture.TextInfo.ListSeparator;

            // Split by the list separator, spaces, or commas
            var parts = s.Split(new[] { separator[0], ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 4)
            {
                return new KeySpline(
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture),
                    double.Parse(parts[3], CultureInfo.InvariantCulture));
            }

            // Try "x1,y1 x2,y2" format
            var groups = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (groups.Length == 2)
            {
                var p1 = groups[0].Split(',');
                var p2 = groups[1].Split(',');
                if (p1.Length == 2 && p2.Length == 2)
                {
                    return new KeySpline(
                        double.Parse(p1[0], CultureInfo.InvariantCulture),
                        double.Parse(p1[1], CultureInfo.InvariantCulture),
                        double.Parse(p2[0], CultureInfo.InvariantCulture),
                        double.Parse(p2[1], CultureInfo.InvariantCulture));
                }
            }

            throw new FormatException($"Cannot parse KeySpline from '{s}'. Expected format: 'x1,y1 x2,y2' or 'x1 y1 x2 y2'.");
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is KeySpline ks)
        {
            var separator = culture?.TextInfo.ListSeparator ?? CultureInfo.InvariantCulture.TextInfo.ListSeparator;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{4}{1}{4}{2}{4}{3}",
                ks.ControlPoint1.X,
                ks.ControlPoint1.Y,
                ks.ControlPoint2.X,
                ks.ControlPoint2.Y,
                separator);
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
