using System.Collections;
using Jalium.UI.Media.Animation;

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
[Markup.ContentProperty(nameof(Storyboard))]
public class VisualState : DependencyObject
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
    public Storyboard? Storyboard { get; set; }

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
/// Provides data for visual-state change events.
/// </summary>
public sealed class VisualStateChangedEventArgs : EventArgs
{
    internal VisualStateChangedEventArgs(
        VisualState? oldState,
        VisualState newState,
        FrameworkElement? control,
        FrameworkElement stateGroupsRoot)
    {
        OldState = oldState;
        NewState = newState;
        Control = control;
        StateGroupsRoot = stateGroupsRoot;
    }

    /// <summary>
    /// Gets the state that the element is leaving, or <see langword="null"/> for its first state.
    /// </summary>
    public VisualState? OldState { get; }

    /// <summary>
    /// Gets the state that the element is entering.
    /// </summary>
    public VisualState NewState { get; }

    /// <summary>
    /// Gets the control whose state is changing, or <see langword="null"/> for an element state change.
    /// </summary>
    public FrameworkElement? Control { get; }

    /// <summary>
    /// Gets the element that owns the visual-state groups.
    /// </summary>
    public FrameworkElement StateGroupsRoot { get; }
}

/// <summary>
/// Contains mutually exclusive visual states and manages transitions between them.
/// </summary>
[Markup.ContentProperty(nameof(States))]
public class VisualStateGroup : DependencyObject
{
    private readonly List<VisualState> _states = new();
    private readonly List<VisualTransition> _transitions = new();
    private VisualState? _currentState;
    private FrameworkElement? _attachedElement;
    private readonly List<(FrameworkElement Target, DependencyProperty Property)> _transitionTargets = new();
    private Storyboard? _transitionStoryboard;

    /// <summary>
    /// Gets or sets the name of the visual state group.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the collection of mutually exclusive visual states.
    /// </summary>
    public IList States => _states;

    /// <summary>
    /// Gets the collection of transitions between states.
    /// </summary>
    public IList Transitions => _transitions;

    /// <summary>
    /// Gets the current visual state in this group.
    /// </summary>
    public VisualState? CurrentState => _currentState;

    /// <summary>
    /// Occurs when this group begins changing visual states.
    /// </summary>
    public event EventHandler<VisualStateChangedEventArgs>? CurrentStateChanging;

    /// <summary>
    /// Occurs after this group has changed visual states.
    /// </summary>
    public event EventHandler<VisualStateChangedEventArgs>? CurrentStateChanged;

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
        StopTransition();
        _currentState?.Storyboard?.Stop();
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
    /// <param name="control">The control whose state is changing.</param>
    /// <param name="stateGroupsRoot">The element that owns the visual-state groups.</param>
    /// <param name="newState">The state to transition to.</param>
    /// <param name="useTransitions">Whether to use transitions.</param>
    /// <returns>True if the transition was successful; otherwise, false.</returns>
    internal bool GoToState(
        FrameworkElement? control,
        FrameworkElement stateGroupsRoot,
        VisualState newState,
        bool useTransitions)
    {
        if (_attachedElement == null)
            return false;

        if (newState == _currentState)
            return true;

        var oldState = _currentState;

        RaiseCurrentStateChanging(stateGroupsRoot, oldState, newState, control);

        // Find transition if using transitions
        VisualTransition? transition = null;
        if (useTransitions)
        {
            transition = FindTransition(oldState?.Name, newState.Name);
        }

        var fromValues = transition?.GeneratedDuration is { HasTimeSpan: true } transitionDuration &&
            transitionDuration.TimeSpan > TimeSpan.Zero ? CaptureTransitionValues(oldState, newState) : null;
        StopTransition();
        oldState?.Storyboard?.Stop();
        if (oldState != null) RemoveStateSetters(oldState, _attachedElement);
        _currentState = newState;
        VisualStateManager.ApplyCurrentStateSetters(stateGroupsRoot);

        // The state supplies the resting values even when an animation is used.
        // Removing its animation must reveal that state, rather than a stale style.
        if (transition?.GeneratedDuration is { HasTimeSpan: true } duration &&
            duration.TimeSpan > TimeSpan.Zero)
        {
            ApplyAnimatedTransition(fromValues!, transition);
        }
        else if (transition?.Storyboard is { } storyboard)
        {
            _transitionStoryboard = storyboard;
            storyboard.Begin(_attachedElement);
        }
        newState.Storyboard?.Begin(_attachedElement);
        _attachedElement.InvalidateVisual();
        RaiseCurrentStateChanged(stateGroupsRoot, oldState, newState, control);
        return true;
    }

    internal VisualState? GetState(string stateName)
    {
        return _states.FirstOrDefault(state => state.Name == stateName);
    }

    internal void RaiseCurrentStateChanging(
        FrameworkElement stateGroupsRoot,
        VisualState? oldState,
        VisualState newState,
        FrameworkElement? control)
    {
        CurrentStateChanging?.Invoke(
            stateGroupsRoot,
            new VisualStateChangedEventArgs(oldState, newState, control, stateGroupsRoot));
    }

    internal void RaiseCurrentStateChanged(
        FrameworkElement stateGroupsRoot,
        VisualState? oldState,
        VisualState newState,
        FrameworkElement? control)
    {
        CurrentStateChanged?.Invoke(
            stateGroupsRoot,
            new VisualStateChangedEventArgs(oldState, newState, control, stateGroupsRoot));
    }

    private Dictionary<(FrameworkElement Target, DependencyProperty Property), object?> CaptureTransitionValues(
        VisualState? fromState, VisualState toState)
    {
        var values = new Dictionary<(FrameworkElement, DependencyProperty), object?>();
        foreach (var setter in (fromState?.Setters ?? Array.Empty<Setter>()).Concat(toState.Setters))
            if (_attachedElement != null && setter.ResolveTargetProperty(_attachedElement) is { } key)
                values[key] = key.Target.GetValue(key.Property);
        return values;
    }

    private void ApplyAnimatedTransition(
        Dictionary<(FrameworkElement Target, DependencyProperty Property), object?> fromValues,
        VisualTransition transition)
    {
        if (_attachedElement == null) return;
        var duration = transition.GeneratedDuration.TimeSpan;
        foreach (var ((target, property), fromValue) in fromValues)
        {
            var toValue = target.GetValue(property);
            if (Equals(fromValue, toValue)) continue;
            var animation = transition.AnimationFactory?.Invoke(property.PropertyType, fromValue, toValue, duration)
                ?? CreateDefaultAnimation(property.PropertyType, fromValue, toValue, duration);
            if (animation == null) continue;
            target.BeginAnimation(property, animation);
            _transitionTargets.Add((target, property));
        }
        _transitionStoryboard = transition.Storyboard;
        _transitionStoryboard?.Begin(_attachedElement);
    }

    private void StopTransition()
    {
        _transitionStoryboard?.Stop();
        _transitionStoryboard = null;
        foreach (var (target, property) in _transitionTargets) target.BeginAnimation(property, null);
        _transitionTargets.Clear();
    }

    internal void ApplyCurrentSetters()
    {
        if (_currentState != null && _attachedElement != null) ApplyStateSetters(_currentState, _attachedElement);
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
            setter.Apply(element, DependencyObject.LayerValueSource.VisualState);
        }
    }

    private static void RemoveStateSetters(VisualState state, FrameworkElement element)
    {
        // Remove setters in reverse order
        for (int i = state.Setters.Count - 1; i >= 0; i--)
        {
            state.Setters[i].Remove(element, DependencyObject.LayerValueSource.VisualState);
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
[Markup.ContentProperty(nameof(Storyboard))]
public class VisualTransition : DependencyObject
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
    public Duration GeneratedDuration { get; set; } = new(TimeSpan.Zero);

    /// <summary>Gets or sets the easing function used for generated transition animations.</summary>
    public IEasingFunction? GeneratedEasingFunction { get; set; }

    /// <summary>
    /// Gets or sets an optional animation timeline factory for custom animations.
    /// This is called for each property that changes during the transition.
    /// </summary>
    public Func<Type, object?, object?, TimeSpan, IAnimationTimeline?>? AnimationFactory { get; set; }

    /// <summary>
    /// Gets or sets the Storyboard that runs during the transition.
    /// </summary>
    public Storyboard? Storyboard { get; set; }

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
public class VisualStateManager : DependencyObject
{
    /// <summary>
    /// Identifies the VisualStateGroups attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty VisualStateGroupsProperty =
        DependencyProperty.RegisterAttached(
            "VisualStateGroups",
            typeof(IList),
            typeof(VisualStateManager),
            new PropertyMetadata(null, OnVisualStateGroupsChanged));

    /// <summary>
    /// Identifies the CustomVisualStateManager attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CustomVisualStateManagerProperty =
        DependencyProperty.RegisterAttached(
            "CustomVisualStateManager",
            typeof(VisualStateManager),
            typeof(VisualStateManager),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the visual state groups for the specified element.
    /// </summary>
    /// <param name="element">The element to get the visual state groups from.</param>
    /// <returns>The collection of visual state groups.</returns>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static IList? GetVisualStateGroups(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(VisualStateGroupsProperty) as IList;
    }

    /// <summary>
    /// Sets the visual state groups for the specified element.
    /// This compatibility API preserves Jalium's existing explicit collection setup.
    /// </summary>
    /// <param name="element">The element to set the visual state groups on.</param>
    /// <param name="value">The collection of visual state groups.</param>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static void SetVisualStateGroups(FrameworkElement element, IList? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VisualStateGroupsProperty, value);
    }

    /// <summary>
    /// Gets the custom manager associated with an element.
    /// </summary>
    public static VisualStateManager? GetCustomVisualStateManager(FrameworkElement obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return obj.GetValue(CustomVisualStateManagerProperty) as VisualStateManager;
    }

    /// <summary>
    /// Associates a custom manager with an element.
    /// </summary>
    public static void SetCustomVisualStateManager(FrameworkElement obj, VisualStateManager? value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(CustomVisualStateManagerProperty, value);
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
        ArgumentNullException.ThrowIfNull(stateName);
        control.ApplyTemplate();
        return GoToStateCommon(control, GetStateGroupsRoot(control), stateName, useTransitions);
    }

    /// <summary>
    /// Transitions an element that directly owns visual-state groups to the specified state.
    /// </summary>
    public static bool GoToElementState(FrameworkElement stateGroupsRoot, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateGroupsRoot);
        return GoToStateCommon(null, stateGroupsRoot, stateName, useTransitions);
    }

    /// <summary>
    /// Allows a custom visual-state manager to override state-transition behavior.
    /// </summary>
    protected virtual bool GoToStateCore(
        FrameworkElement? control,
        FrameworkElement stateGroupsRoot,
        string stateName,
        VisualStateGroup? group,
        VisualState? state,
        bool useTransitions)
    {
        return GoToStateInternal(control, stateGroupsRoot, group, state, useTransitions);
    }

    /// <summary>
    /// Raises the CurrentStateChanging event for a custom visual-state manager.
    /// </summary>
    protected void RaiseCurrentStateChanging(
        VisualStateGroup stateGroup,
        VisualState? oldState,
        VisualState newState,
        FrameworkElement? control,
        FrameworkElement? stateGroupsRoot)
    {
        ArgumentNullException.ThrowIfNull(stateGroup);
        ArgumentNullException.ThrowIfNull(newState);

        if (stateGroupsRoot == null)
            return;

        stateGroup.RaiseCurrentStateChanging(stateGroupsRoot, oldState, newState, control);
    }

    /// <summary>
    /// Raises the CurrentStateChanged event for a custom visual-state manager.
    /// </summary>
    protected void RaiseCurrentStateChanged(
        VisualStateGroup stateGroup,
        VisualState? oldState,
        VisualState newState,
        FrameworkElement? control,
        FrameworkElement? stateGroupsRoot)
    {
        ArgumentNullException.ThrowIfNull(stateGroup);
        ArgumentNullException.ThrowIfNull(newState);

        if (stateGroupsRoot == null)
            return;

        stateGroup.RaiseCurrentStateChanged(stateGroupsRoot, oldState, newState, control);
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

        var groups = GetVisualStateGroups(GetStateGroupsRoot(control));
        if (groups == null)
            return null;

        foreach (var item in groups)
        {
            if (item is VisualStateGroup group && group.Name == groupName)
                return group.CurrentState?.Name;
        }

        return null;
    }

    internal static void ApplyCurrentStateSetters(FrameworkElement root)
    {
        if (GetVisualStateGroups(root) is not { } groups) return;
        foreach (var item in groups)
            if (item is VisualStateGroup group) group.ApplyCurrentSetters();
    }

    internal static void DetachVisualStateGroups(FrameworkElement element)
    {
        if (GetVisualStateGroups(element) is not { } groups) return;
        foreach (var item in groups)
            if (item is VisualStateGroup group) group.Detach();
    }

    private static FrameworkElement GetStateGroupsRoot(FrameworkElement control)
    {
        var root = control.StateGroupsRoot;
        // Preserve explicitly attached states on a standalone element. Template
        // states take precedence once a control has expanded its template.
        return root != null &&
            (GetVisualStateGroups(root) != null || GetCustomVisualStateManager(root) != null)
            ? root : control;
    }

    private static bool GoToStateCommon(
        FrameworkElement? control,
        FrameworkElement stateGroupsRoot,
        string stateName,
        bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateName);

        var groups = GetVisualStateGroups(stateGroupsRoot);
        if (groups == null)
            return false;

        TryGetState(groups, stateName, out var group, out var state);

        var customManager = GetCustomVisualStateManager(stateGroupsRoot);
        if (customManager != null)
        {
            // WPF gives a custom manager the request even when no registered state matched.
            return customManager.GoToStateCore(
                control,
                stateGroupsRoot,
                stateName,
                group,
                state,
                useTransitions);
        }

        return state != null
            && GoToStateInternal(control, stateGroupsRoot, group, state, useTransitions);
    }

    private static bool TryGetState(
        IList groups,
        string stateName,
        out VisualStateGroup? group,
        out VisualState? state)
    {
        foreach (var item in groups)
        {
            if (item is not VisualStateGroup candidateGroup)
                continue;

            var candidateState = candidateGroup.GetState(stateName);
            if (candidateState != null)
            {
                group = candidateGroup;
                state = candidateState;
                return true;
            }
        }

        group = null;
        state = null;
        return false;
    }

    private static bool GoToStateInternal(
        FrameworkElement? control,
        FrameworkElement stateGroupsRoot,
        VisualStateGroup? group,
        VisualState? state,
        bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(stateGroupsRoot);
        ArgumentNullException.ThrowIfNull(state);

        if (group == null)
            throw new InvalidOperationException("The visual state is not associated with a visual state group.");

        return group.GoToState(control, stateGroupsRoot, state, useTransitions);
    }

    private static void OnVisualStateGroupsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (e.OldValue is IList oldGroups)
        {
            foreach (var item in oldGroups)
            {
                if (item is VisualStateGroup group)
                    group.Detach();
            }
        }

        if (e.NewValue is IList newGroups)
        {
            foreach (var item in newGroups)
            {
                if (item is VisualStateGroup group)
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
