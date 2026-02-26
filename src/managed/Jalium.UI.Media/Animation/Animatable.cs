using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// Abstract class that provides animation support.
/// This is the base class for objects that can have animated dependency properties.
/// </summary>
public abstract class Animatable : Freezable, IAnimatable
{
    private readonly Dictionary<DependencyProperty, AnimationClock> _animationClocks = new();

    /// <summary>
    /// Gets a value indicating whether one or more AnimationClock objects are associated
    /// with any of this object's dependency properties.
    /// </summary>
    public bool HasAnimatedProperties => _animationClocks.Count > 0;

    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty, replacing existing animations.
    /// </summary>
    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock)
    {
        ApplyAnimationClock(dp, clock, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Applies an AnimationClock to the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock, HandoffBehavior handoffBehavior)
    {
        if (clock == null)
        {
            _animationClocks.Remove(dp);
        }
        else
        {
            if (handoffBehavior == HandoffBehavior.SnapshotAndReplace)
            {
                _animationClocks[dp] = clock;
            }
            else
            {
                _animationClocks[dp] = clock;
            }
        }
    }

    /// <summary>
    /// Starts an animation for the specified DependencyProperty.
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation)
    {
        BeginAnimation(dp, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Starts an animation for the specified DependencyProperty with the specified HandoffBehavior.
    /// </summary>
    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation, HandoffBehavior handoffBehavior)
    {
        if (animation == null)
        {
            _animationClocks.Remove(dp);
            return;
        }

        var clock = (AnimationClock)animation.CreateClock();
        ApplyAnimationClock(dp, clock, handoffBehavior);
        clock.Begin();
    }

    /// <summary>
    /// Retrieves the base value of the specified DependencyProperty, disregarding any animated value.
    /// </summary>
    public object? GetAnimationBaseValue(DependencyProperty dp)
    {
        return GetValue(dp);
    }

    /// <inheritdoc/>
    protected override bool FreezeCore(bool isChecking)
    {
        if (HasAnimatedProperties)
            return false;

        return base.FreezeCore(isChecking);
    }
}
