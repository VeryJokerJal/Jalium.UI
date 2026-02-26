namespace Jalium.UI;

/// <summary>
/// Interface for storyboards used in visual state transitions.
/// Implemented by Storyboard in Jalium.UI.Media.Animation.
/// </summary>
public interface IStoryboard
{
    /// <summary>
    /// Begins the storyboard on the specified element.
    /// </summary>
    /// <param name="containingObject">The element that contains the named targets.</param>
    void Begin(FrameworkElement? containingObject);

    /// <summary>
    /// Stops the storyboard.
    /// </summary>
    void Stop();
}

/// <summary>
/// Represents a visual state that an element can be in.
/// </summary>
public sealed class VisualState
{
    private readonly List<Setter> _setters = new();

    /// <summary>
    /// Gets or sets the name of the visual state.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the collection of setters to apply when this state is active.
    /// </summary>
    public IList<Setter> Setters => _setters;

    /// <summary>
    /// Gets or sets the Storyboard that runs when the control enters this state.
    /// </summary>
    public IStoryboard? Storyboard { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualState"/> class.
    /// </summary>
    public VisualState()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualState"/> class with the specified name.
    /// </summary>
    /// <param name="name">The name of the visual state.</param>
    public VisualState(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Contains mutually exclusive visual states and manages transitions between them.
/// </summary>
public sealed class VisualStateGroup
{
    private readonly List<VisualState> _states = new();
    private readonly List<VisualTransition> _transitions = new();
    private VisualState? _currentState;
    private FrameworkElement? _attachedElement;

    /// <summary>
    /// Gets or sets the name of the visual state group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the collection of mutually exclusive visual states.
    /// </summary>
    public IList<VisualState> States => _states;

    /// <summary>
    /// Gets the collection of transitions between states.
    /// </summary>
    public IList<VisualTransition> Transitions => _transitions;

    /// <summary>
    /// Gets the current visual state in this group.
    /// </summary>
    public VisualState? CurrentState => _currentState;

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStateGroup"/> class.
    /// </summary>
    public VisualStateGroup()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualStateGroup"/> class with the specified name.
    /// </summary>
    /// <param name="name">The name of the visual state group.</param>
    public VisualStateGroup(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Attaches this visual state group to an element.
    /// </summary>
    internal void Attach(FrameworkElement element)
    {
        _attachedElement = element;
    }

    /// <summary>
    /// Detaches this visual state group from its element.
    /// </summary>
    internal void Detach()
    {
        if (_currentState != null && _attachedElement != null)
        {
            RemoveStateSetters(_currentState, _attachedElement);
        }
        _currentState = null;
        _attachedElement = null;
    }

    /// <summary>
    /// Transitions to the specified state.
    /// </summary>
    /// <param name="stateName">The name of the state to transition to.</param>
    /// <param name="useTransitions">Whether to use transitions.</param>
    /// <returns>True if the transition was successful; otherwise, false.</returns>
    internal bool GoToState(string stateName, bool useTransitions)
    {
        if (_attachedElement == null)
            return false;

        var newState = _states.FirstOrDefault(s => s.Name == stateName);
        if (newState == null)
            return false;

        if (newState == _currentState)
            return true;

        // Find transition if using transitions
        VisualTransition? transition = null;
        if (useTransitions)
        {
            transition = FindTransition(_currentState?.Name, stateName);
        }

        // Check if we should use animated transition
        if (transition != null && transition.GeneratedDuration > TimeSpan.Zero)
        {
            ApplyAnimatedTransition(_currentState, newState, transition);
        }
        else
        {
            // Immediate transition (no animation)
            if (_currentState != null)
            {
                // Stop previous state's storyboard if running
                _currentState.Storyboard?.Stop();
                RemoveStateSetters(_currentState, _attachedElement);
            }

            _currentState = newState;
            ApplyStateSetters(newState, _attachedElement);

            // Begin new state's storyboard if present
            newState.Storyboard?.Begin(_attachedElement);
        }

        _attachedElement.InvalidateVisual();
        return true;
    }

    private void ApplyAnimatedTransition(VisualState? fromState, VisualState toState, VisualTransition transition)
    {
        if (_attachedElement == null) return;

        // Collect property changes
        var fromSetters = fromState?.Setters.ToDictionary(
            s => (s.TargetName, s.Property),
            s => s.Value) ?? new Dictionary<(string?, DependencyProperty?), object?>();

        var toSetters = toState.Setters.ToDictionary(
            s => (s.TargetName, s.Property),
            s => s.Value);

        // Apply animations for changing properties
        foreach (var (key, toValue) in toSetters)
        {
            var (targetName, property) = key;
            if (property == null) continue;

            // Resolve target element
            var target = string.IsNullOrEmpty(targetName)
                ? _attachedElement
                : _attachedElement.FindName(targetName) as FrameworkElement;

            if (target == null) continue;

            // Get from value
            object? fromValue;
            if (fromSetters.TryGetValue(key, out var fv))
            {
                fromValue = fv;
            }
            else
            {
                fromValue = target.GetValue(property);
            }

            // Try to create animation
            IAnimationTimeline? animation = null;

            // Use custom factory if provided
            if (transition.AnimationFactory != null)
            {
                animation = transition.AnimationFactory(property.PropertyType, fromValue, toValue, transition.GeneratedDuration);
            }

            // Fallback: generate default animation for common types
            animation ??= CreateDefaultAnimation(property.PropertyType, fromValue, toValue, transition.GeneratedDuration);

            // Apply animation or immediate value
            if (animation != null)
            {
                target.BeginAnimation(property, animation);
            }
            else
            {
                // No animation available, apply value immediately
                target.SetValue(property, toValue);
            }
        }

        // Remove properties that are no longer in the new state
        foreach (var (key, _) in fromSetters)
        {
            if (!toSetters.ContainsKey(key))
            {
                var (targetName, property) = key;
                if (property == null) continue;

                var target = string.IsNullOrEmpty(targetName)
                    ? _attachedElement
                    : _attachedElement.FindName(targetName) as FrameworkElement;

                if (target != null)
                {
                    // Stop any animation and clear the value
                    target.BeginAnimation(property, null);
                }
            }
        }

        // Stop old state's storyboard
        fromState?.Storyboard?.Stop();

        // Begin transition storyboard if present
        transition.Storyboard?.Begin(_attachedElement);

        // Begin new state's storyboard
        toState.Storyboard?.Begin(_attachedElement);

        _currentState = toState;
    }

    private VisualTransition? FindTransition(string? from, string to)
    {
        // First try to find an exact match
        var exactMatch = _transitions.FirstOrDefault(t =>
            t.From == from && t.To == to);
        if (exactMatch != null)
            return exactMatch;

        // Then try to find a transition from the current state to any state
        var fromMatch = _transitions.FirstOrDefault(t =>
            t.From == from && string.IsNullOrEmpty(t.To));
        if (fromMatch != null)
            return fromMatch;

        // Then try to find a transition from any state to the target
        var toMatch = _transitions.FirstOrDefault(t =>
            string.IsNullOrEmpty(t.From) && t.To == to);
        if (toMatch != null)
            return toMatch;

        // Finally, try to find a default transition
        return _transitions.FirstOrDefault(t =>
            string.IsNullOrEmpty(t.From) && string.IsNullOrEmpty(t.To));
    }

    private static void ApplyStateSetters(VisualState state, FrameworkElement element)
    {
        foreach (var setter in state.Setters)
        {
            setter.Apply(element);
        }
    }

    private static void RemoveStateSetters(VisualState state, FrameworkElement element)
    {
        // Remove setters in reverse order
        for (int i = state.Setters.Count - 1; i >= 0; i--)
        {
            state.Setters[i].Remove(element);
        }
    }

    /// <summary>
    /// Gets or sets the global default animation factory used when no custom factory is provided.
    /// This is typically set by the animation layer (Jalium.UI.Media.Animation) at startup.
    /// </summary>
    public static Func<Type, object?, object?, TimeSpan, IAnimationTimeline?>? DefaultAnimationFactory { get; set; }

    private static IAnimationTimeline? CreateDefaultAnimation(Type propertyType, object? fromValue, object? toValue, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return null;

        // Use the registered default animation factory if available
        if (DefaultAnimationFactory != null)
        {
            return DefaultAnimationFactory(propertyType, fromValue, toValue, duration);
        }

        // No default factory registered, cannot auto-generate animations
        return null;
    }
}

/// <summary>
/// Defines a transition between visual states.
/// </summary>
public sealed class VisualTransition
{
    /// <summary>
    /// Gets or sets the name of the state to transition from.
    /// Empty string means any state.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the state to transition to.
    /// Empty string means any state.
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the duration of the transition.
    /// When greater than zero, animated transitions are generated automatically.
    /// </summary>
    public TimeSpan GeneratedDuration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets an optional animation timeline factory for custom animations.
    /// This is called for each property that changes during the transition.
    /// </summary>
    public Func<Type, object?, object?, TimeSpan, IAnimationTimeline?>? AnimationFactory { get; set; }

    /// <summary>
    /// Gets or sets the Storyboard that runs during the transition.
    /// </summary>
    public IStoryboard? Storyboard { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VisualTransition"/> class.
    /// </summary>
    public VisualTransition()
    {
    }
}

/// <summary>
/// Manages visual states for controls.
/// </summary>
public static class VisualStateManager
{
    /// <summary>
    /// Identifies the VisualStateGroups attached property.
    /// </summary>
    public static readonly DependencyProperty VisualStateGroupsProperty =
        DependencyProperty.RegisterAttached(
            "VisualStateGroups",
            typeof(IList<VisualStateGroup>),
            typeof(VisualStateManager),
            new PropertyMetadata(null, OnVisualStateGroupsChanged));

    /// <summary>
    /// Gets the visual state groups for the specified element.
    /// </summary>
    /// <param name="element">The element to get the visual state groups from.</param>
    /// <returns>The collection of visual state groups.</returns>
    public static IList<VisualStateGroup>? GetVisualStateGroups(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(VisualStateGroupsProperty) as IList<VisualStateGroup>;
    }

    /// <summary>
    /// Sets the visual state groups for the specified element.
    /// </summary>
    /// <param name="element">The element to set the visual state groups on.</param>
    /// <param name="value">The collection of visual state groups.</param>
    public static void SetVisualStateGroups(FrameworkElement element, IList<VisualStateGroup>? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VisualStateGroupsProperty, value);
    }

    /// <summary>
    /// Transitions the control to the specified state.
    /// </summary>
    /// <param name="control">The control to transition.</param>
    /// <param name="stateName">The name of the state to transition to.</param>
    /// <param name="useTransitions">Whether to use transitions.</param>
    /// <returns>True if the transition was successful; otherwise, false.</returns>
    public static bool GoToState(FrameworkElement control, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrEmpty(stateName))
            return false;

        var groups = GetVisualStateGroups(control);
        if (groups == null || groups.Count == 0)
            return false;

        // Find the group containing the state
        foreach (var group in groups)
        {
            if (group.States.Any(s => s.Name == stateName))
            {
                return group.GoToState(stateName, useTransitions);
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the current state name for the specified group.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="groupName">The name of the state group.</param>
    /// <returns>The current state name, or null if not found.</returns>
    public static string? GetCurrentStateName(FrameworkElement control, string groupName)
    {
        ArgumentNullException.ThrowIfNull(control);

        var groups = GetVisualStateGroups(control);
        if (groups == null)
            return null;

        var group = groups.FirstOrDefault(g => g.Name == groupName);
        return group?.CurrentState?.Name;
    }

    private static void OnVisualStateGroupsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        // Detach old groups
        if (e.OldValue is IList<VisualStateGroup> oldGroups)
        {
            foreach (var group in oldGroups)
            {
                group.Detach();
            }
        }

        // Attach new groups
        if (e.NewValue is IList<VisualStateGroup> newGroups)
        {
            foreach (var group in newGroups)
            {
                group.Attach(element);
            }
        }
    }
}

/// <summary>
/// Common visual state names.
/// </summary>
public static class VisualStateNames
{
    /// <summary>
    /// Common states group name.
    /// </summary>
    public const string CommonStatesGroup = "CommonStates";

    /// <summary>
    /// Focus states group name.
    /// </summary>
    public const string FocusStatesGroup = "FocusStates";

    /// <summary>
    /// Selection states group name.
    /// </summary>
    public const string SelectionStatesGroup = "SelectionStates";

    /// <summary>
    /// Expansion states group name.
    /// </summary>
    public const string ExpansionStatesGroup = "ExpansionStates";

    /// <summary>
    /// Check states group name.
    /// </summary>
    public const string CheckStatesGroup = "CheckStates";

    /// <summary>
    /// Normal state.
    /// </summary>
    public const string Normal = "Normal";

    /// <summary>
    /// MouseOver state.
    /// </summary>
    public const string MouseOver = "MouseOver";

    /// <summary>
    /// Pressed state.
    /// </summary>
    public const string Pressed = "Pressed";

    /// <summary>
    /// Disabled state.
    /// </summary>
    public const string Disabled = "Disabled";

    /// <summary>
    /// Focused state.
    /// </summary>
    public const string Focused = "Focused";

    /// <summary>
    /// Unfocused state.
    /// </summary>
    public const string Unfocused = "Unfocused";

    /// <summary>
    /// Selected state.
    /// </summary>
    public const string Selected = "Selected";

    /// <summary>
    /// Unselected state.
    /// </summary>
    public const string Unselected = "Unselected";

    /// <summary>
    /// Expanded state.
    /// </summary>
    public const string Expanded = "Expanded";

    /// <summary>
    /// Collapsed state.
    /// </summary>
    public const string Collapsed = "Collapsed";

    /// <summary>
    /// Checked state.
    /// </summary>
    public const string Checked = "Checked";

    /// <summary>
    /// Unchecked state.
    /// </summary>
    public const string Unchecked = "Unchecked";

    /// <summary>
    /// Indeterminate state.
    /// </summary>
    public const string Indeterminate = "Indeterminate";
}
