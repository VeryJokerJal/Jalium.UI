using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Defines methods and properties for objects that can be animated.
/// </summary>
public interface IAnimatable
{
    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty.
    /// The existing animations are replaced by the new animation.
    /// </summary>
    void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock);

    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock, HandoffBehavior handoffBehavior);

    /// <summary>
    /// Starts an animation for the specified DependencyProperty.
    /// </summary>
    void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation);

    /// <summary>
    /// Starts an animation for the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior);

    /// <summary>
    /// Gets a value indicating whether one or more AnimationClock objects are associated
    /// with any of this object's dependency properties.
    /// </summary>
    bool HasAnimatedProperties { get; }

    /// <summary>
    /// Retrieves the base value of the specified DependencyProperty, disregarding any animated value.
    /// </summary>
    object? GetAnimationBaseValue(DependencyProperty dp);
}
