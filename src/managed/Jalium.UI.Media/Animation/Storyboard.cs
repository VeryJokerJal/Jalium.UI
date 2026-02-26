using System.Collections.ObjectModel;
using Jalium.UI;

namespace Jalium.UI.Media.Animation;

/// <summary>
/// A container timeline that provides object and property targeting information for its child animations.
/// </summary>
public sealed class Storyboard : Timeline, IStoryboard
{
    private static readonly HashSet<Storyboard> _activeStoryboards = new();
    private static readonly object _lock = new();

    private readonly List<AnimationClock> _clocks = new();
    private readonly List<(AnimationClock Clock, DependencyObject Target, DependencyProperty Property, object? OriginalValue)> _activeAnimations = new();
    private bool _isRunning;
    private bool _subscribedToRendering;

    /// <summary>
    /// Stops all active storyboards. Called during application shutdown.
    /// </summary>
    public static void StopAll()
    {
        Storyboard[] storyboards;
        lock (_lock)
        {
            storyboards = _activeStoryboards.ToArray();
        }

        foreach (var storyboard in storyboards)
        {
            storyboard.Stop();
        }
    }

    #region Attached Properties

    /// <summary>
    /// Identifies the TargetName attached property.
    /// </summary>
    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.RegisterAttached("TargetName", typeof(string), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Target attached property.
    /// </summary>
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(DependencyObject), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TargetProperty attached property.
    /// </summary>
    public static readonly DependencyProperty TargetPropertyProperty =
        DependencyProperty.RegisterAttached("TargetProperty", typeof(PropertyPath), typeof(Storyboard),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the target name.
    /// </summary>
    public static string? GetTargetName(DependencyObject element)
    {
        return (string?)element.GetValue(TargetNameProperty);
    }

    /// <summary>
    /// Sets the target name.
    /// </summary>
    public static void SetTargetName(DependencyObject element, string? value)
    {
        element.SetValue(TargetNameProperty, value);
    }

    /// <summary>
    /// Gets the target object.
    /// </summary>
    public static DependencyObject? GetTarget(DependencyObject element)
    {
        return (DependencyObject?)element.GetValue(TargetProperty);
    }

    /// <summary>
    /// Sets the target object.
    /// </summary>
    public static void SetTarget(DependencyObject element, DependencyObject? value)
    {
        element.SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets the target property.
    /// </summary>
    public static PropertyPath? GetTargetProperty(DependencyObject element)
    {
        return (PropertyPath?)element.GetValue(TargetPropertyProperty);
    }

    /// <summary>
    /// Sets the target property.
    /// </summary>
    public static void SetTargetProperty(DependencyObject element, PropertyPath? value)
    {
        element.SetValue(TargetPropertyProperty, value);
    }

    #endregion

    /// <summary>
    /// Gets the collection of children timelines.
    /// </summary>
    public Collection<Timeline> Children { get; } = new();

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    public void Begin()
    {
        Begin(null, false);
    }

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    /// <param name="containingObject">The object that contains the named elements to animate.</param>
    public void Begin(FrameworkElement? containingObject)
    {
        Begin(containingObject, false);
    }

    /// <summary>
    /// Applies animations to their targets and begins the storyboard.
    /// </summary>
    /// <param name="containingObject">The object that contains the named elements to animate.</param>
    /// <param name="isControllable">Whether the storyboard can be controlled.</param>
    public void Begin(FrameworkElement? containingObject, bool isControllable)
    {
        Stop();

        _clocks.Clear();
        _activeAnimations.Clear();

        foreach (var child in Children)
        {
            if (child is AnimationTimeline animationTimeline)
            {
                var target = ResolveTarget(child, containingObject);
                var propertyPath = GetTargetProperty(child);

                if (target != null && propertyPath != null)
                {
                    var property = ResolveProperty(target, propertyPath);
                    if (property != null)
                    {
                        var clock = new AnimationClock(animationTimeline);
                        var originalValue = target.GetValue(property);
                        _clocks.Add(clock);
                        _activeAnimations.Add((clock, target, property, originalValue));
                        clock.Completed += OnClockCompleted;
                        clock.Begin();
                    }
                }
            }
        }

        if (_clocks.Count > 0)
        {
            _isRunning = true;

            // Subscribe to the centralized frame timer instead of creating
            // a per-Storyboard System.Threading.Timer. This ensures all
            // storyboards tick in the same Dispatcher batch → single render.
            if (!_subscribedToRendering)
            {
                _subscribedToRendering = true;
                CompositionTarget.Rendering += OnRenderingTick;
                CompositionTarget.Subscribe();
            }

            lock (_lock)
            {
                _activeStoryboards.Add(this);
            }
        }
    }

    /// <summary>
    /// Stops the storyboard.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        UnsubscribeFromRendering();

        lock (_lock)
        {
            _activeStoryboards.Remove(this);
        }

        foreach (var clock in _clocks)
        {
            clock.Stop();
        }

        // Clear animated values - ClearAnimatedValue handles FillBehavior internally
        foreach (var (clock, target, property, originalValue) in _activeAnimations)
        {
            if (target is UIElement uiElement)
            {
                // For explicit Stop(), always clear the animation
                // The DependencyObject.ClearAnimatedValue will handle HoldEnd vs Stop behavior
                uiElement.ClearAnimatedValue(property);
            }
            else
            {
                // Fallback for non-UIElement targets: restore original value on Stop()
                target.SetValue(property, originalValue ?? property.DefaultMetadata.DefaultValue);
            }
        }

        _clocks.Clear();
        _activeAnimations.Clear();
    }

    /// <summary>
    /// Pauses the storyboard.
    /// </summary>
    public void Pause()
    {
        foreach (var clock in _clocks)
        {
            clock.Pause();
        }
    }

    /// <summary>
    /// Resumes a paused storyboard.
    /// </summary>
    public void Resume()
    {
        foreach (var clock in _clocks)
        {
            clock.Resume();
        }
    }

    /// <summary>
    /// Seeks to the specified position.
    /// </summary>
    public void Seek(TimeSpan offset)
    {
        foreach (var clock in _clocks)
        {
            clock.Controller?.Seek(offset, TimeSeekOrigin.BeginTime);
        }
    }

    /// <summary>
    /// Called by CompositionTarget.Rendering on the UI thread, once per frame.
    /// Already on UI thread — no marshaling needed.
    /// </summary>
    private void OnRenderingTick(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        foreach (var (clock, target, property, originalValue) in _activeAnimations)
        {
            clock.Tick();

            if (clock.Timeline is AnimationTimeline animationTimeline)
            {
                var currentValue = animationTimeline.GetCurrentValue(
                    originalValue ?? GetDefaultValue(property),
                    originalValue ?? GetDefaultValue(property),
                    clock);

                // Use animation layer instead of direct SetValue for UIElement targets
                if (target is UIElement uiElement)
                {
                    var holdEnd = animationTimeline.FillBehavior == FillBehavior.HoldEnd;
                    uiElement.SetAnimatedValue(property, currentValue, holdEnd);
                }
                else
                {
                    // Fallback for non-UIElement targets (e.g., Brush, Geometry)
                    target.SetValue(property, currentValue);
                }
            }
        }

        // Check completion AFTER all Tick() calls to avoid off-by-one-frame delay
        if (_activeAnimations.All(a => !a.Clock.IsRunning))
        {
            _isRunning = false;
            UnsubscribeFromRendering();
            OnCompleted();
        }
    }

    private void OnClockCompleted(object? sender, EventArgs e)
    {
        // Completion is handled by OnRenderingTick to avoid double-firing OnCompleted.
        // This callback is intentionally left empty.
    }

    private void UnsubscribeFromRendering()
    {
        if (_subscribedToRendering)
        {
            _subscribedToRendering = false;
            CompositionTarget.Rendering -= OnRenderingTick;
            CompositionTarget.Unsubscribe();
        }
    }

    private DependencyObject? ResolveTarget(Timeline timeline, FrameworkElement? containingObject)
    {
        // First check for direct target
        var directTarget = GetTarget(timeline);
        if (directTarget != null)
        {
            return directTarget;
        }

        // Then check for target name
        var targetName = GetTargetName(timeline);
        if (!string.IsNullOrEmpty(targetName) && containingObject != null)
        {
            return FindElementByName(containingObject, targetName);
        }

        // Default to containing object
        return containingObject;
    }

    private static FrameworkElement? FindElementByName(FrameworkElement root, string name)
    {
        // Check the root element
        if (root.Name == name)
        {
            return root;
        }

        // Search children recursively
        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            var child = root.GetVisualChild(i);
            if (child is FrameworkElement fe)
            {
                var found = FindElementByName(fe, name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private DependencyProperty? ResolveProperty(DependencyObject target, PropertyPath propertyPath)
    {
        // Simple property resolution - just look for the property name
        var propertyName = propertyPath.Path;

        // Handle nested property paths like "(UIElement.Opacity)"
        if (propertyName.StartsWith("(", StringComparison.Ordinal) && propertyName.EndsWith(")", StringComparison.Ordinal))
        {
            propertyName = propertyName.Substring(1, propertyName.Length - 2);
            var dotIndex = propertyName.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                propertyName = propertyName.Substring(dotIndex + 1);
            }
        }

        // Look for the dependency property in the target's type
        var targetType = target.GetType();
        var fieldInfo = targetType.GetField($"{propertyName}Property",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.FlattenHierarchy);

        return fieldInfo?.GetValue(null) as DependencyProperty;
    }

    private static object GetDefaultValue(DependencyProperty property)
    {
        return property.DefaultMetadata.DefaultValue ?? 0.0;
    }
}
