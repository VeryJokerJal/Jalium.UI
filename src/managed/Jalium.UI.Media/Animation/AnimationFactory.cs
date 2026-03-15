using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Factory for creating animations for common property types.
/// </summary>
public static class AnimationFactory
{
    /// <summary>
    /// Creates an animation for the specified property type.
    /// </summary>
    /// <param name="propertyType">The type of property to animate.</param>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="duration">The animation duration.</param>
    /// <returns>An animation timeline, or null if the type is not supported.</returns>
    public static IAnimationTimeline? CreateAnimation(Type propertyType, object? from, object? to, TimeSpan duration)
    {
        return CreateAnimation(propertyType, from, to, duration, null);
    }

    /// <summary>
    /// Creates an animation for the specified property type with an optional easing function.
    /// </summary>
    /// <param name="propertyType">The type of property to animate.</param>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="duration">The animation duration.</param>
    /// <param name="easingFunction">Optional easing function.</param>
    /// <returns>An animation timeline, or null if the type is not supported.</returns>
    public static IAnimationTimeline? CreateAnimation(Type propertyType, object? from, object? to, TimeSpan duration, IEasingFunction? easingFunction)
    {
        var targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (targetType == typeof(double))
        {
            return new DoubleAnimation
            {
                From = TryGetValue<double>(from),
                To = TryGetValue<double>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(float))
        {
            return new SingleAnimation
            {
                From = TryGetValue<float>(from),
                To = TryGetValue<float>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(int))
        {
            return new Int32Animation
            {
                From = TryGetValue<int>(from),
                To = TryGetValue<int>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(short))
        {
            return new Int16Animation
            {
                From = TryGetValue<short>(from),
                To = TryGetValue<short>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(long))
        {
            return new Int64Animation
            {
                From = TryGetValue<long>(from),
                To = TryGetValue<long>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(byte))
        {
            return new ByteAnimation
            {
                From = TryGetValue<byte>(from),
                To = TryGetValue<byte>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(decimal))
        {
            return new DecimalAnimation
            {
                From = TryGetValue<decimal>(from),
                To = TryGetValue<decimal>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Color))
        {
            return new ColorAnimation
            {
                From = TryGetValue<Color>(from),
                To = TryGetValue<Color>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Thickness))
        {
            return new ThicknessAnimation
            {
                From = TryGetValue<Thickness>(from),
                To = TryGetValue<Thickness>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Point))
        {
            return new PointAnimation
            {
                From = TryGetValue<Point>(from),
                To = TryGetValue<Point>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Size))
        {
            return new SizeAnimation
            {
                From = TryGetValue<Size>(from),
                To = TryGetValue<Size>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Rect))
        {
            return new RectAnimation
            {
                From = TryGetValue<Rect>(from),
                To = TryGetValue<Rect>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(Vector))
        {
            return new VectorAnimation
            {
                From = TryGetValue<Vector>(from),
                To = TryGetValue<Vector>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (targetType == typeof(CornerRadius))
        {
            return new CornerRadiusAnimation
            {
                From = TryGetValue<CornerRadius>(from),
                To = TryGetValue<CornerRadius>(to),
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        if (typeof(Brush).IsAssignableFrom(targetType))
        {
            var fromBrush = from as Brush;
            var toBrush = to as Brush;
            if (!BrushAnimation.SupportsTransition(fromBrush, toBrush))
                return null;

            return new BrushAnimation
            {
                From = fromBrush,
                To = toBrush,
                Duration = new Duration(duration),
                EasingFunction = easingFunction
            };
        }

        // Not a type we can animate
        return null;
    }

    /// <summary>
    /// Creates an automatic transition animation for a specific dependency property.
    /// Property metadata can supply a custom transition factory for user-defined dependency properties.
    /// </summary>
    public static IAnimationTimeline? CreateTransitionAnimation(
        DependencyProperty property,
        object? from,
        object? to,
        TimeSpan duration,
        TransitionTimingFunction timingFunction)
    {
        ArgumentNullException.ThrowIfNull(property);

        var metadataAnimation = property.DefaultMetadata.AutomaticTransitionFactory?.Invoke(
            property,
            from,
            to,
            duration,
            timingFunction);

        if (metadataAnimation != null)
        {
            return ApplyTransitionFillBehavior(metadataAnimation);
        }

        return CreateTransitionAnimation(property.PropertyType, from, to, duration, timingFunction);
    }

    /// <summary>
    /// Creates an automatic transition animation using the framework timing presets.
    /// </summary>
    public static IAnimationTimeline? CreateTransitionAnimation(
        Type propertyType,
        object? from,
        object? to,
        TimeSpan duration,
        TransitionTimingFunction timingFunction)
    {
        var animation = CreateAnimation(propertyType, from, to, duration, GetEasingFunction(timingFunction));
        return ApplyTransitionFillBehavior(animation);
    }

    /// <summary>
    /// Gets a default animation factory delegate that can be used with VisualTransition.
    /// </summary>
    public static Func<Type, object?, object?, TimeSpan, IAnimationTimeline?> DefaultFactory { get; } =
        (propertyType, from, to, duration) => CreateAnimation(propertyType, from, to, duration);

    /// <summary>
    /// Gets an animation factory delegate with the specified easing function.
    /// </summary>
    /// <param name="easingFunction">The easing function to use.</param>
    /// <returns>An animation factory delegate.</returns>
    public static Func<Type, object?, object?, TimeSpan, IAnimationTimeline?> WithEasing(IEasingFunction easingFunction)
    {
        return (propertyType, from, to, duration) => CreateAnimation(propertyType, from, to, duration, easingFunction);
    }

    private static IEasingFunction GetEasingFunction(TransitionTimingFunction timingFunction)
    {
        return timingFunction switch
        {
            TransitionTimingFunction.Linear => new LinearEase(),
            TransitionTimingFunction.EaseIn => new CubicEase { EasingMode = EasingMode.EaseIn },
            TransitionTimingFunction.EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut },
            TransitionTimingFunction.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
            _ => new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }

    private static IAnimationTimeline? ApplyTransitionFillBehavior(IAnimationTimeline? animation)
    {
        if (animation is Timeline timeline)
        {
            timeline.FillBehavior = FillBehavior.Stop;
        }

        return animation;
    }

    private static T? TryGetValue<T>(object? value)
        where T : struct
    {
        if (value is T typedValue)
            return typedValue;

        return null;
    }
}
